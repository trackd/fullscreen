using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;
using MetricHistory = fullscreen.Charts.TimeSeries;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    internal static readonly MonitorView[] NavigationViews = [
        MonitorView.Processes,
        MonitorView.Services,
        MonitorView.Network,
        MonitorView.Gpu,
        MonitorView.Disks,
        MonitorView.Users,
        MonitorView.Dashboard
    ];
    private readonly List<Charts.BuiltInTrend> _plots = [.. Enumerable.Range(0, 8).Select(_ => new Charts.BuiltInTrend { Interactive = false })];
    private XenoAtom.Terminal.UI.Geometry.Rectangle[] _chartBounds = new XenoAtom.Terminal.UI.Geometry.Rectangle[8];
    private int _chartCount;
    private readonly record struct PlotHit(
        int X,
        int Y,
        int Width,
        int Height,
        MetricHistory History,
        MetricHistory? Secondary,
        bool Rate,
        bool Bytes
    );
    private PlotHit[] _plotHits = new PlotHit[8];
    private int _plotHitCount;
    private int _inspectX = -1, _inspectY = -1;
    private static readonly TimeSpan PlotWindow = TimeSpan.FromSeconds(120);
    private void InspectPlot(
        CellBuffer buffer,
        int x,
        int y,
        int width,
        int height,
        MetricHistory history,
        Style style,
        MetricHistory? secondary = null,
        bool rate = false,
        bool bytes = false
    ) {
        if (width <= 0 || height <= 0) {
            return;
        }

        DateTimeOffset end = secondary is not null && secondary.Latest > history.Latest ? secondary.Latest : history.Latest;
        if (_plotHitCount == _plotHits.Length) { Array.Resize(ref _plotHits, _plotHits.Length * 2); }
        _plotHits[_plotHitCount++] = new(x, y, width, height, history, secondary, rate, bytes);

        if (_inspectX >= x && _inspectX < x + width && _inspectY >= y && _inspectY < y + height) {
            double seconds = PlotWindow.TotalSeconds * (1 - (double)(_inspectX - x) / Math.Max(1, width - 1));
            DateTimeOffset time = end.AddSeconds(-Math.Min(seconds, (end - DateTimeOffset.MinValue).TotalSeconds));
            var inspected = secondary is not null && _inspectY >= y + (height + 1) / 2 ? secondary : history;
            var inspectedStyle = inspected == secondary ? Purple : style;
            int index = inspected.Nearest(time);
            if (index >= 0) {
                Span<char> label = stackalloc char[64];
                if (label.TryWrite(
                    CultureInfo.InvariantCulture,
                    $"{inspected.TimeAt(index).ToLocalTime():HH:mm:ss} · ",
                    out int length
                )) {
                    Write(buffer, x, y - 1, label[..length], Accent, width);
                }

                if (rate || bytes) {
                    Quantity(buffer, x + 11, y - 1, inspected.ValueAt(index), rate, inspectedStyle, width - 11);
                }
                else {
                    Metric(buffer, x + 11, y - 1, inspected.ValueAt(index), inspectedStyle, width - 11);
                }
            }
        }
    }

    private readonly MetricHistory _receiveHistory = new(), _sendHistory = new(), _vramHistory = new();
    private int TabWidth => Bounds.Width >= 130 ? 14 : Bounds.Width >= 92 ? 12 : 6;

    private void RenderNavigation(CellBuffer buffer) {
        ReadOnlySpan<string> labels = TabWidth == 6 ? [
            "Pr",
            "Svc",
            "Net",
            "GPU",
            "Dsk",
            "Usr",
            "Da"
        ] : [
            "Processes",
            "Services",
            "Network",
            "GPU",
            "Disks",
            "Users",
            "Dashboard"
        ];
        Span<char> tabLabel = stackalloc char[24];
        for (int i = 0; i < NavigationViews.Length; i++) {
            int x = 1 + i * TabWidth;
            bool selected = View == NavigationViews[i];
            Fill(buffer, x, 0, TabWidth - 1, 1, selected ? Selected : Body);
            if (tabLabel.TryWrite(CultureInfo.InvariantCulture, $"{i + 1} {labels[i]}", out int n)) {
                Write(buffer, x, 0, tabLabel[..n], selected ? Selected : Muted, TabWidth - 1);
            }
        }

        int left = 1 + NavigationViews.Length * TabWidth, space = Bounds.Width - left - 4;
        if (space >= 18) {
            string machine = Frame.MachineName.Length > 0 ? Frame.MachineName : SourceLabel;
            Span<char> label = stackalloc char[256];
            string status = Paused ? "PAUSED" : SourceError is not null ? "STALE" : SnapshotAgeSeconds < 0 ? "WAIT" : "LIVE";
            bool written = space < 36 ? label.TryWrite(
                CultureInfo.InvariantCulture,
                $"{(IsRemote ? "R" : "L")} / {machine} · {status}",
                out int length
            ) : label.TryWrite(
                CultureInfo.InvariantCulture,
                $"FULLSCREEN {(IsRemote ? "remote" : "local")} / {machine} · {status}",
                out length
            );
            if (written) {
                Write(
                    buffer,
                    Math.Max(left, Bounds.Width - length - 4),
                    0,
                    label[..length],
                    Paused || SourceError is not null ? Hot : Accent,
                    space
                );
            }
        }
    }

    private void CpuTime(CellBuffer buffer, int x, int y, long ticks, Style style, int width) {
        var time = TimeSpan.FromTicks(Math.Max(0, ticks));
        Span<char> label = stackalloc char[40];
        if (label.TryWrite(
            CultureInfo.InvariantCulture,
            $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}",
            out int length
        )) {
            Write(buffer, x, y, label[..length], style, width);
        }
    }

    private void RenderDashboard(CellBuffer buffer) {
        // Replace summary cards with four generous history panels.
        int top = Minimal ? 0 : 2;
        Fill(buffer, 0, top, Bounds.Width, Bounds.Height - top, Body);
        int width = Math.Max(12, (Bounds.Width - 3) / 2), right = Bounds.Width - 3 - width;
        var (upper, lower) = DashboardHeights(Bounds.Height);
        int detailRows = ResourceFooterRows(width, upper);
        string cpuName = Frame.CpuDetails?.Name.Replace("(R)", "", StringComparison.Ordinal).Replace("(TM)", "", StringComparison.Ordinal).Replace(" CPU @", " @", StringComparison.Ordinal) ?? "0—100%";
        GraphPanel(buffer, 1, top, width, upper, "CPU · " + cpuName, _cpuHistory, 100, CpuPercent, Accent, false, footerRows: detailRows);
        GraphPanel(
            buffer,
            width + 2,
            top,
            right,
            upper,
            "MEMORY · physical RAM %",
            _memoryHistory,
            100,
            MemoryPercent,
            Green,
            false,
            footerRows: detailRows
        );
        if (detailRows > 0) { RenderResourceSummary(buffer, width, upper, top); }
        GraphPanel(
            buffer,
            1,
            upper + top,
            width,
            lower,
            "NETWORK · ↓ receive / ↑ send",
            _receiveHistory,
            0,
            Frame.ReceiveBytesPerSecond,
            Green,
            true,
            secondary: _sendHistory
        );
        GraphPanel(
            buffer,
            width + 2,
            upper + top,
            right,
            lower,
            "GPU · " + GpuDeviceName + " %",
            _gpuHistory,
            100,
            GpuPercent,
            Purple,
            false
        );
        if (Minimal) {
            return;
        }

        RenderCompactFooter(buffer);
    }

    private void GraphPanel(
        CellBuffer buffer,
        int x,
        int y,
        int width,
        int height,
        string label,
        MetricHistory history,
        double scale,
        double? current,
        Style style,
        bool rate,
        bool bytes = false,
        MetricHistory? secondary = null,
        int footerRows = 0,
        bool scalar = false,
        string primaryLabel = "↓ RX",
        string secondaryLabel = "↑ TX"
    ) {
        Box(buffer, x, y, width, height, label, style);
        height -= footerRows;
        if (rate || bytes) {
            Quantity(buffer, x + 2, y + 1, current, rate, style, width - 4);
        }
        else {
            Metric(buffer, x + 2, y + 1, current, style, 8);
            Write(buffer, x + 10, y + 1, scalar ? "IOPS" : "%", Muted);
        }

        if (height < 7) {
            if (height >= 5) {
                InspectPlot(buffer, x + 2, y + 2, width - 4, height - 3, history, style, secondary, rate, bytes);
            }

            return;
        }

        int axis = width >= 36 ? 8 : 0;
        int plotHeight = Math.Max(1, height - 5);
        InspectPlot(buffer, x + 2 + axis, y + 2, width - 4 - axis, plotHeight, history, style, secondary, rate, bytes);
        if (axis > 0) {
            double maximum = scale > 0 ? scale : history.Scale;
            if (secondary is not null) {
                Write(buffer, x + 2, y + 2, primaryLabel, Green, axis);
                Write(buffer, x + 2, y + 2 + (plotHeight + 1) / 2, secondaryLabel, Purple, axis);
                if (plotHeight >= 6) {
                    if (scalar) { Metric(buffer, x + 1, y + 3, maximum, Muted, axis, "F0"); }
                    else { Quantity(buffer, x + 1, y + 3, maximum, false, Muted, axis); }
                    if (scalar) { Metric(buffer, x + 1, y + 3 + (plotHeight + 1) / 2, scale > 0 ? scale : secondary.Scale, Muted, axis, "F0"); }
                    else {
                        Quantity(
                        buffer,
                        x + 1,
                        y + 3 + (plotHeight + 1) / 2,
                        scale > 0 ? scale : secondary.Scale,
                        false,
                        Muted,
                        axis
                    );
                    }
                }
            }
            else if (rate || bytes) {
                Quantity(buffer, x + 1, y + 2, maximum, false, Muted, axis);
            }
            else {
                Number(buffer, x + 2, y + 2, maximum, "F0", Muted);
            }

            if (plotHeight >= 5 && !rate && !bytes) {
                Number(buffer, x + 2, y + 2 + plotHeight / 2, maximum / 2, "F0", Muted);
            }

            if (secondary is null) {
                Write(buffer, x + 2, y + 1 + plotHeight, "0", Muted, axis);
            }

            Write(buffer, x + 2 + axis, y + height - 3, "−2m", Muted);
            Write(buffer, x + width - 6, y + height - 3, "now", Muted);
        }

        if (secondary is not null) {
            Write(buffer, x + width / 2, y + 1, secondaryLabel, Purple, 7);
            double? latest = secondary.Count > 0 ? secondary.ValueAt(secondary.Count - 1) : null;
            if (scalar) { Metric(buffer, x + width / 2 + 8, y + 1, latest, Purple, width / 2 - 10); }
            else { Quantity(buffer, x + width / 2 + 8, y + 1, latest, true, Purple, width / 2 - 10); }
        }

        Write(buffer, x + 2, y + height - 2, "peak", Muted);
        double? peak = history.HasValues ? history.Maximum : null;
        double? average = history.HasValues ? history.Average : null;
        if (rate || bytes) {
            Quantity(buffer, x + 8, y + height - 2, peak, rate, style, width - 10);
        }
        else {
            Metric(buffer, x + 8, y + height - 2, peak, style, width - 10);
        }

        if (width >= 48) {
            Write(buffer, x + width / 2, y + height - 2, "avg", Muted);
            if (rate || bytes) {
                Quantity(buffer, x + width / 2 + 4, y + height - 2, average, rate, style, width / 2 - 6);
            }
            else {
                Metric(buffer, x + width / 2 + 4, y + height - 2, average, style, width / 2 - 6);
            }
        }
    }

    private void RenderDetailCharts(CellBuffer buffer) {
        int width = Math.Max(12, (Bounds.Width - 3) / 2);
        int height = DetailChartHeight(Bounds.Height);
        if (View == MonitorView.Network) {
            var network = GraphInterface;
            var history = network.HasValue && _interfaceHistory.TryGetValue(network.Value.Id, out var selected) ? selected : (_receiveHistory, _sendHistory);
            GraphPanel(
                buffer,
                1,
                2,
                width,
                height,
                "RECEIVE · " + (network?.Name ?? "all interfaces") + " · [ / ]",
                history.Item1,
                0,
                network.HasValue ? network.Value.ReceiveBytesPerSecond : Frame.ReceiveBytesPerSecond,
                Green,
                true
            );
            GraphPanel(
                buffer,
                width + 2,
                2,
                width,
                height,
                "SEND · " + (network?.Name ?? "all interfaces") + " · [ / ]",
                history.Item2,
                0,
                network.HasValue ? network.Value.SendBytesPerSecond : Frame.SendBytesPerSecond,
                Purple,
                true
            );
        }
        else {
            GraphPanel(buffer, 1, 2, width, height, "GPU · utilization %", _gpuHistory, 100, GpuPercent, Purple, false);
            double? used = null;
            foreach (ref readonly var gpu in Frame.Gpus.AsSpan(0, Frame.GpuCount)) {
                if (gpu.UsedMemory.HasValue) {
                    used = (used ?? 0) + gpu.UsedMemory.Value;
                }
            }

            GraphPanel(
                buffer,
                width + 2,
                2,
                width,
                height,
                "GPU memory · bytes in use",
                _vramHistory,
                0,
                used,
                Green,
                false,
                true
            );
        }
    }
}
