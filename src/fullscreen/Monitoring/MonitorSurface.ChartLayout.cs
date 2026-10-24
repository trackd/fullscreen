using fullscreen.Charts;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    internal static readonly GraphKind[] TimeGraphTypes = [
        GraphKind.Line,
        GraphKind.Area,
        GraphKind.Bars,
        GraphKind.Step,
        GraphKind.Scatter,
        GraphKind.Stem
    ];
    private GraphKind _graphType = GraphKind.Area;
    private (int Upper, int Lower) DashboardHeights(int height) {
        int available = Math.Max(8, height - (Minimal ? 0 : 3));
        return (available / 2, available - available / 2);
    }

    /// <summary>Gets or sets the graphics rendering mode reported by the host.</summary>
    public ChartRenderer Renderer { get; set; }
    internal string GraphLabel => App?.IsGraphicsPresentationEnabled == true ? $"{GraphType} · {(Renderer == ChartRenderer.Regis ? "ReGIS prototype" : "Sixel")}" : $"native line (requested: {GraphType})";

    /// <summary>Gets or sets the chart representation to display.</summary>
    public GraphKind GraphType {
        get => _graphType;
        set {
            if (!TimeGraphTypes.Contains(value)) {
                throw new ArgumentException("Monitor graphs must use a time-based graph type.", nameof(value));
            }

            if (_graphType == value) {
                return;
            }

            _graphType = value;
            Refresh();
        }
    }

    private void PrepareCharts(int width, int height) {
        _chartCount = 0;
        Array.Clear(_chartBounds);
        foreach (var chart in _plots) {
            chart.InspectionColumn = -1;
        }

        if (ColumnsOpen || View is MonitorView.Services or MonitorView.Users || width < 35 || height < 12) {
            return;
        }

        if (View == MonitorView.Dashboard) {
            int top = Minimal ? 0 : 2;
            int w = Math.Max(12, (width - 3) / 2), right = width - 3 - w;
            var (upper, lower) = DashboardHeights(height);
            PanelChart(1, top, w, upper, _cpuHistory, 100, Accent);
            PanelChart(w + 2, top, right, upper, _memoryHistory, 100, Green);
            PanelChart(1, upper + top, w, lower, _receiveHistory, 0, Green, _sendHistory);
            PanelChart(w + 2, upper + top, right, lower, _gpuHistory, 100, Purple);
        }
        else if (!Minimal && width >= 76 && height >= 20) {
            if (View is MonitorView.Network or MonitorView.Gpu) {
                int w = Math.Max(12, (width - 3) / 2), h = DetailChartHeight(height);
                PanelChart(
                    1,
                    2,
                    w,
                    h,
                    View == MonitorView.Network ? _receiveHistory : _gpuHistory,
                    View == MonitorView.Network ? 0 : 100,
                    View == MonitorView.Network ? Green : Purple
                );
                PanelChart(
                    w + 2,
                    2,
                    w,
                    h,
                    View == MonitorView.Network ? _sendHistory : _vramHistory,
                    0,
                    View == MonitorView.Network ? Purple : Green
                );
            }
            else {
                int w = (width - 5) / 4, h = SummaryHeight(height);
                AddChart(new(3, 5, w - 4, Math.Max(1, h - 4)), _cpuHistory, 100, Accent);
                AddChart(new(4 + w, 5, w - 4, Math.Max(1, h - 4)), _memoryHistory, 100, Green);
                AddChart(new(5 + 2 * w, 5, w - 4, Math.Max(1, h - 4)), _gpuHistory, 100, Purple);
                AddChart(new(6 + 3 * w, 6, w - 4, Math.Max(1, h - 5)), _receiveHistory, 0, Green, _sendHistory);
            }
        }
    }

    private void PanelChart(
        int x,
        int y,
        int width,
        int height,
        TimeSeries history,
        double maximum,
        Style style,
        TimeSeries? secondary = null
    ) {
        if (height < 7) {
            if (height >= 5) {
                AddChart(new(x + 2, y + 2, width - 4, height - 3), history, maximum, style, secondary);
            }

            return;
        }

        int axis = width >= 36 ? 8 : 0;
        AddChart(
            new(x + 2 + axis, y + 2, width - 4 - axis, Math.Max(1, height - 5)),
            history,
            maximum,
            style,
            secondary
        );
    }

    private void AddChart(Rectangle rect, TimeSeries history, double maximum, Style ink, TimeSeries? secondary = null) {
        if (rect.Width <= 0 || rect.Height <= 0) {
            return;
        }

        DateTimeOffset end = secondary is not null && secondary.Latest > history.Latest ? secondary.Latest : history.Latest;
        void Lane(Rectangle bounds, TimeSeries source, Style style) {
            if (_chartCount >= _plots.Length) {
                return;
            }

            _chartBounds[_chartCount] = bounds;
            _plots[_chartCount].GraphKind = GraphType;
            _plots[_chartCount].PixelInk = style == Green ? Palette.Success : style == Purple ? Palette.Purple : Palette.Accent;
            _plots[_chartCount].PixelBackground = Palette.Background;
            _plots[_chartCount++].Update(source, end, maximum > 0 ? maximum : source.Scale);
            if (_inspectX >= bounds.X && _inspectX < bounds.X + bounds.Width && _inspectY >= bounds.Y
                && _inspectY < bounds.Y + bounds.Height) {
                _plots[_chartCount - 1].InspectionColumn = _inspectX - bounds.X;
            }
        }

        if (secondary is null) {
            Lane(rect, history, ink);
        }
        else {
            int upper = (rect.Height + 1) / 2;
            Lane(new(rect.X, rect.Y, rect.Width, upper), history, ink);
            Lane(new(rect.X, rect.Y + upper, rect.Width, rect.Height - upper), secondary, Purple);
        }
    }
}
