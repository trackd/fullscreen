using MetricHistory = fullscreen.Charts.TimeSeries;
using System.Globalization;
using System.Text;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    /// <summary>Gets or sets the reusable metric snapshot to read or fill.</summary>
    public CollectorFrame Frame { get; set; } = new();
    /// <summary>Gets or sets the display label identifying the collection source.</summary>
    public string SourceLabel { get; set; } = Environment.MachineName;
    /// <summary>Gets or sets the latest source error, or null when collection is healthy.</summary>
    public string? SourceError { get; set; }
    /// <summary>Gets or sets whether collection targets a remote computer.</summary>
    public bool IsRemote { get; set; }
    /// <summary>Gets or sets the elapsed age of the current snapshot in seconds.</summary>
    public int SnapshotAgeSeconds { get; set; } = -1;
    /// <summary>Gets or sets the observed remote collection latency in milliseconds.</summary>
    public double LatencyMilliseconds { get; set; }

    private readonly MetricHistory _cpuHistory = new(), _memoryHistory = new(), _gpuHistory = new();
    private int _detailOffset;
    private bool DetailMatches(
        string name,
        string id
    ) =>
        Model.Filter.Length == 0 || name.Contains(Model.Filter, StringComparison.OrdinalIgnoreCase)
        || id.Contains(Model.Filter, StringComparison.OrdinalIgnoreCase);
    private int DetailCount {
        get {
            int count = 0;
            if (View == MonitorView.Network) {
                foreach (ref readonly var row in Frame.Networks.AsSpan(0, Frame.NetworkCount)) {
                    if (NetworkMatches(row)) {
                        count++;
                    }
                }
            }
            else {
                foreach (ref readonly var row in Frame.Gpus.AsSpan(0, Frame.GpuCount)) {
                    if (GpuMatches(row)) {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    private void MoveDetail(int delta) =>
        _detailOffset = (int)Math.Clamp((long)_detailOffset + delta, 0, Math.Max(0, DetailCount - PageSize));
    private double? MemoryPercent => TotalMemory is > 0 && AvailableMemory is ulong available ? Math.Clamp(100.0 * (1 - (double)available / TotalMemory.Value), 0, 100) : null;

    private double? GpuPercent {
        get {
            double? result = null;
            foreach (ref readonly var gpu in Frame.Gpus.AsSpan(0, Frame.GpuCount)) {
                if (gpu.Utilization.HasValue) {
                    result = Math.Max(result ?? 0, gpu.Utilization.Value);
                }
            }

            return result;
        }
    }

    /// <summary>Applies a collected frame and appends its values to chart history.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    public void SetFrame(CollectorFrame frame) {
        _columnLayoutWidth = -1;
        Frame = frame;
        if (ColumnsOpen && _pickerCapabilities != frame.Capabilities) {
            UpdatePickerCapabilities();
        }
        CpuPercent = frame.CpuPercent;
        TotalMemory = frame.TotalMemory;
        AvailableMemory = frame.AvailableMemory;
        Model.TotalMemory = frame.TotalMemory;
        ProcessCount = frame.ProcessCount;
        SampleMilliseconds = frame.SampleMilliseconds;
        SampleAllocation = frame.SampleAllocation;
        DateTimeOffset timestamp = frame.CapturedAt == default ? DateTimeOffset.UtcNow : frame.CapturedAt;
        bool all = BackgroundSampling || View == MonitorView.Dashboard || !Minimal && ProcessTable;
        if (all) {
            _cpuHistory.Add(CpuPercent, timestamp);
            _memoryHistory.Add(MemoryPercent, timestamp);
        }

        if (all || !Minimal && View == MonitorView.Gpu) {
            _gpuHistory.Add(GpuPercent, timestamp);
            double? vram = null;
            foreach (ref readonly var gpu in frame.Gpus.AsSpan(0, frame.GpuCount)) {
                if (gpu.UsedMemory.HasValue) {
                    vram = (vram ?? 0) + gpu.UsedMemory.Value;
                }
            }

            _vramHistory.Add(vram, timestamp);
        }

        if (all || !Minimal && View == MonitorView.Network) {
            _receiveHistory.Add(frame.ReceiveBytesPerSecond, timestamp);
            _sendHistory.Add(frame.SendBytesPerSecond, timestamp);
        }
    }

    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        _plotHitCount = 0;
        Fill(buffer, 0, 0, Bounds.Width, Bounds.Height, Body);
        if (Bounds.Width < 35 || Bounds.Height < 12) {
            Write(buffer, 1, 0, "FULLSCREEN · resize to 35 × 12", Accent);
            return;
        }

        if (View == MonitorView.Users) {
            RenderUsers(buffer);
            return;
        }

        if (View == MonitorView.Services) {
            RenderServices(buffer);
            return;
        }

        if (Minimal) {
            if (View == MonitorView.Dashboard) {
                RenderDashboard(buffer);
            }
            else {
                if (View == MonitorView.Network) {
                    RenderNetwork(buffer);
                }
                else if (View == MonitorView.Gpu) {
                    RenderGpu(buffer);
                }
                else {
                    RenderProcesses(buffer);
                }

                if (_editingFilter) {
                    Write(buffer, 1, 0, "FILTER › " + Model.Filter, Accent, Bounds.Width - 2);
                }

                if (ColumnsOpen) {
                    RenderColumnPicker(buffer);
                }
            }

            if (SourceError is not null) {
                Write(buffer, 1, Bounds.Height - 1, "ERROR · " + SourceError, Hot, Bounds.Width - 2);
            }
            else if (ActionMessage.Length > 0) {
                Write(buffer, 1, Bounds.Height - 1, ActionMessage, Accent, Bounds.Width - 2);
            }

            return;
        }

        RenderNavigation(buffer);
        Rule(buffer, 1);
        if (View == MonitorView.Dashboard) {
            RenderDashboard(buffer);
            return;
        }

        if (Bounds.Width >= 76 && Bounds.Height >= 20) {
            if (View is MonitorView.Network or MonitorView.Gpu) {
                RenderDetailCharts(buffer);
            }
            else {
                RenderCards(buffer);
            }
        }
        else {
            Write(buffer, 1, 3, "CPU", Accent);
            Metric(buffer, 6, 3, CpuPercent, Accent, 7);
            Write(buffer, 17, 3, "RAM", Green);
            Metric(buffer, 22, 3, MemoryPercent, Green, 7);
            Write(buffer, 1, 4, "GPU", Purple);
            Metric(buffer, 6, 4, GpuPercent, Purple, 7);
            Write(buffer, 17, 4, "↓", Green);
            Quantity(buffer, 20, 4, Frame.ReceiveBytesPerSecond, true, Green, 14);
            Write(buffer, 1, 5, "NET ↑", Purple);
            Quantity(buffer, 8, 5, Frame.SendBytesPerSecond, true, Purple, 18);
        }

        if (View == MonitorView.Dashboard) {
            RenderDashboard(buffer);
            return;
        }

        int title = TableTop - 2;
        Write(buffer, 1, title, View switch {
            MonitorView.Network => "NETWORK INTERFACES",
            MonitorView.Gpu => "GRAPHICS DEVICES",
            _ => "PROCESSES"
        }, Accent);
        if (Bounds.Width > 70 && View is not (MonitorView.Network or MonitorView.Gpu)) {
            Number(buffer, 24, title, Model.Count, "F0", Body);
            Write(buffer, 31, title, "shown", Muted);
            Write(buffer, 40, title, "sort", Muted);
            Write(buffer, 45, title, Model.Sort.ToString(), Green);
            Write(buffer, 58, title, Model.Ascending ? "↑" : "↓", Green);
        }

        if (View == MonitorView.Network) {
            RenderNetwork(buffer);
        }
        else if (View == MonitorView.Gpu) {
            RenderGpu(buffer);
        }
        else {
            RenderProcesses(buffer);
        }

        RenderCompactFooter(buffer);
        if (ColumnsOpen) {
            RenderColumnPicker(buffer);
        }
    }

    private void RenderCards(CellBuffer buffer) {
        int height = CardHeight;
        int width = (Bounds.Width - 5) / 4;
        Card(buffer, 1, width, height, "CPU", CpuPercent, Accent, _cpuHistory, "system load");
        Card(buffer, 2 + width, width, height, "MEMORY", MemoryPercent, Green, _memoryHistory, "physical RAM");
        Card(
            buffer,
            3 + 2 * width,
            width,
            height,
            "GPU",
            GpuPercent,
            Purple,
            _gpuHistory,
            Frame.GpuCount > 0 ? Frame.Gpus[0].Name : "no provider"
        );
        int netX = 4 + 3 * width;
        Box(buffer, netX, 2, width, height, "NETWORK", Accent);
        Write(buffer, netX + 2, 4, "↓", Green);
        Quantity(buffer, netX + 4, 4, Frame.ReceiveBytesPerSecond, true, Green, width - 6);
        Write(buffer, netX + 2, 5, "↑", Purple);
        Quantity(buffer, netX + 4, 5, Frame.SendBytesPerSecond, true, Purple, width - 6);
        if (height > 4) {
            InspectPlot(
                buffer,
                netX + 2,
                6,
                width - 4,
                Math.Max(1, height - 5),
                _receiveHistory,
                Green,
                _sendHistory,
                true
            );
            Write(buffer, netX + 2, 3, "120s · RX / TX", Muted, width - 4);
            if (TotalMemory is ulong total && AvailableMemory is ulong available) {
                int x = 2 + width;
                Fill(buffer, x + 2, 3, width - 4, 1, Panel);
                if (width < 26) {
                    Quantity(buffer, x + 2, 3, total - Math.Min(total, available), false, Green, width - 4);
                }
                else {
                    Quantity(buffer, x + 2, 3, total - Math.Min(total, available), false, Green, width / 2 - 2);
                    Write(buffer, x + width / 2, 3, "/", Muted);
                    Quantity(buffer, x + width / 2 + 2, 3, total, false, Muted, width / 2 - 3);
                }
            }
        }
    }

    private void Card(
        CellBuffer buffer,
        int x,
        int width,
        int height,
        string label,
        double? percent,
        Style style,
        MetricHistory history,
        string detail
    ) {
        Box(buffer, x, 2, width, height, label, style);
        Metric(buffer, x + 2, 4, percent, style, 6);
        if (percent.HasValue) {
            Write(buffer, x + 8, 4, "%", Muted);
        }

        if (width > 18 && percent.HasValue) {
            Meter(buffer, x + 11, 4, percent.Value, width - 13);
        }

        if (height > 4) {
            InspectPlot(buffer, x + 2, 5, width - 4, Math.Max(1, height - 4), history, style);
            Write(buffer, x + 2, 3, detail, Muted, width - 4);
        }
    }

    private void RenderProcesses(CellBuffer buffer) {
        int top = TableTop;
        Fill(buffer, 1, top - 1, Bounds.Width - 2, 1, Panel);
        foreach (ref readonly var column in Columns) {
            Write(buffer, column.X, top - 1, column.Header, column.Sort == Model.Sort ? Accent : Muted, column.Width);
            Write(
                buffer,
                column.X + column.Width - 2,
                top - 1,
                Model.ColumnFilters.ContainsKey(column.Kind) ? "◆" : "⌄",
                Accent
            );
            if (column.Sort == Model.Sort && column.Width < 10000) {
                Write(buffer, column.X + column.Width - 1, top - 1, Model.Ascending ? "↑" : "↓", Accent);
            }
        }
        Write(buffer, 0, top - 1, _firstProcessColumn > 0 ? "‹" : "", Accent, 1);
        bool overflow = _firstProcessColumn + _columnCount < AvailableProcessColumns.Count
            || _columnCount > 0 && _columns[_columnCount - 1].Width < DescribeColumn(_columns[_columnCount - 1].Kind).Width;
        Write(buffer, Bounds.Width - 1, top - 1, overflow ? "›" : "", Accent, 1);
        if (AvailableProcessColumns.Count == 0) {
            Write(buffer, 2, top - 1, "Selected columns unsupported on target · C: choose columns", Muted, Bounds.Width - 4);
        }

        int visible = Math.Max(0, Bounds.Height - top - Footer);
        int offset = Math.Clamp(Model.Offset, 0, Math.Max(0, Model.Count - visible));
        var rows = Model.Rows;
        for (int i = offset; i < Math.Min(rows.Length, offset + visible); i++) {
            ref readonly var row = ref rows[i];
            int y = top + i - offset;
            Style style = row.Id == Model.SelectedId ? Selected : (i & 1) == 0 ? Body : Panel;
            Fill(buffer, 1, y, Bounds.Width - 2, 1, style);
            foreach (ref readonly var column in Columns) {
                Style metric = row.Id == Model.SelectedId ? style : column.Sort == ProcessSort.Cpu && row.CpuPercent > 50 ? Hot : style;
                switch (column.Header) {
                    case "PID":
                        Metric(buffer, column.X, y, row.Id, metric, column.Width, "F0", rightAlign: true);
                        break;
                    case "PPID":
                        Metric(buffer, column.X, y, row.ParentId, metric, column.Width, "F0", rightAlign: true);
                        break;
                    case "USER":
                        Write(buffer, column.X, y, row.UserName ?? "—", metric, column.Width);
                        break;
                    case "CPU%":
                        Metric(buffer, column.X, y, row.CpuPercent, metric, column.Width, rightAlign: true);
                        break;
                    case "MEM%":
                        Metric(
                            buffer,
                            column.X,
                            y,
                            TotalMemory is > 0 ? row.WorkingSet * 100.0 / TotalMemory.Value : null,
                            metric,
                            column.Width,
                            rightAlign: true
                        );
                        break;
                    case "RSS":
                        Quantity(buffer, column.X, y, row.WorkingSet, false, metric, column.Width, rightAlign: true);
                        break;
                    case "GPU%":
                        Metric(buffer, column.X, y, row.GpuPercent, metric, column.Width, rightAlign: true);
                        break;
                    case "VRAM":
                        Quantity(buffer, column.X, y, row.GpuMemory, false, metric, column.Width, rightAlign: true);
                        break;
                    case "TIME":
                        CpuTime(buffer, column.X, y, row.CpuTimeTicks, metric, column.Width);
                        break;
                    case "STATUS":
                        Write(buffer, column.X, y, row.Status ?? "—", metric, column.Width);
                        break;
                    case "DISK/s":
                        Quantity(
                            buffer,
                            column.X,
                            y,
                            row.DiskBytesPerSecond,
                            true,
                            metric,
                            column.Width,
                            rightAlign: true
                        );
                        break;
                    case "NET/s":
                        Quantity(
                            buffer,
                            column.X,
                            y,
                            row.NetworkBytesPerSecond,
                            true,
                            metric,
                            column.Width,
                            rightAlign: true
                        );
                        break;
                    case "THR":
                        Metric(buffer, column.X, y, row.Threads, metric, column.Width, "F0", rightAlign: true);
                        break;
                    default:
                        if (Model.Tree) {
                            TreeName(buffer, column.X, y, column.Width, i, metric);
                        }
                        else {
                            Write(buffer, column.X, y, row.Name.AsSpan(), metric, column.Width);
                        }

                        break;
                }
            }
        }

        if (rows.IsEmpty) {
            Write(
                buffer,
                2,
                top,
                SnapshotAgeSeconds < 0 ? "Waiting for first snapshot…" : "No matching processes",
                Muted
            );
        }
    }

    private void RenderNetwork(CellBuffer buffer) =>
        RenderDetailTable(buffer, true);
    private void RenderGpu(CellBuffer buffer) =>
        RenderDetailTable(buffer, false);
    private void Metric(
        CellBuffer buffer,
        int x,
        int y,
        double? value,
        Style style,
        int width,
        string format = "F1",
        bool rightAlign = false
    ) {
        if (!value.HasValue) {
            Write(buffer, x, y, "—", style, width);
            return;
        }

        Span<char> chars = stackalloc char[32];
        if (value.Value.TryFormat(chars, out int written, format, CultureInfo.InvariantCulture)) {
            int offset = rightAlign ? Math.Max(0, width - written) : 0;
            Write(buffer, x + offset, y, chars[..written], style, width - offset);
        }
    }

    private void LinkSpeed(CellBuffer buffer, int x, int y, long speed) {
        if (speed <= 0) {
            Write(buffer, x, y, "—", Muted);
            return;
        }

        double value = speed;
        int unit = 0;
        while (value >= 1000 && unit < 3) {
            value /= 1000;
            unit++;
        }

        ReadOnlySpan<string> units = ["bps", "Kbps", "Mbps", "Gbps"];
        Span<char> text = stackalloc char[24];
        if (text.TryWrite(CultureInfo.InvariantCulture, $"{value:F1} {units[unit]}", out int written)) {
            Write(buffer, x, y, text[..written], Muted, 13);
        }
    }

    private void Quantity(
        CellBuffer buffer,
        int x,
        int y,
        double? value,
        bool rate,
        Style style,
        int width,
        bool rightAlign = false
    ) {
        if (!value.HasValue) {
            Write(buffer, x, y, "—", style, width);
            return;
        }

        double number = Math.Max(0, value.Value);
        int unit = 0;
        while (number >= 1024 && unit < 4) {
            number /= 1024;
            unit++;
        }

        ReadOnlySpan<string> units = ["B", "KiB", "MiB", "GiB", "TiB"];
        Span<char> chars = stackalloc char[32];
        if (chars.TryWrite(
            CultureInfo.InvariantCulture,
            $"{number:F1} {units[unit]}{(rate ? "/s" : "")}",
            out int written
        )) {
            int offset = rightAlign ? Math.Max(0, width - written) : 0;
            Write(buffer, x + offset, y, chars[..written], style, width - offset);
        }
    }

    private void Box(CellBuffer buffer, int x, int y, int width, int height, string label, Style style) {
        Fill(buffer, x, y, width, height, Panel);
        for (int i = 1; i < width - 1; i++) {
            Cell(buffer, x + i, y, '─', Muted);
            Cell(buffer, x + i, y + height - 1, '─', Muted);
        }

        for (int i = 1; i < height - 1; i++) {
            Cell(buffer, x, y + i, '│', Muted);
            Cell(buffer, x + width - 1, y + i, '│', Muted);
        }

        Cell(buffer, x, y, '╭', Muted);
        Cell(buffer, x + width - 1, y, '╮', Muted);
        Cell(buffer, x, y + height - 1, '╰', Muted);
        Cell(buffer, x + width - 1, y + height - 1, '╯', Muted);
        Write(buffer, x + 2, y, label, style, width - 4);
    }

    private void Rule(CellBuffer buffer, int y) {
        for (int x = 1; x < Bounds.Width - 1; x++) {
            Cell(buffer, x, y, '─', Muted);
        }
    }

    private void Fill(CellBuffer buffer, int x, int y, int width, int height, Style style) {
        for (int row = Math.Max(0, y); row < Math.Min(Bounds.Height, y + height); row++) {
            for (int col = Math.Max(0, x); col < Math.Min(Bounds.Width, x + width); col++) {
                Cell(buffer, col, row, ' ', style);
            }
        }
    }

    private void Cell(CellBuffer buffer, int x, int y, char glyph, Style style) {
        if ((uint)x < (uint)Bounds.Width && (uint)y < (uint)Bounds.Height) {
            buffer.SetCell(Bounds.X + x, Bounds.Y + y, new Rune(glyph), style);
        }
    }
}
