using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
    private readonly ChartGeometry _cellGeometry = new();
    private readonly ChartGeometry _pickGeometry = new();
    private ChartSelection? _gridSelection;
    private ChartDataSeries[]? _gridSelectionData;
    private long _gridSelectionVersion;
    private (ChartRange Range, ChartAxis Axis)? _heatmapScaleKey;
    private string _heatmapScale = "";
    private string _kagiKey = "";
    private double _kagiKeyReversal;

    private void RenderKagiKey(CellBuffer buffer) {
        if (_kagiKeyReversal != ReversalAmount) {
            _kagiKey = $"Kagi reversal {ReversalAmount:G4} | thick yang / thin yin";
            _kagiKeyReversal = ReversalAmount;
        }
        var bounds = PlotBounds;
        WriteFeature(buffer, bounds.X, bounds.Bottom + (ShowAxes ? 1 : 0), _kagiKey, Palette.Body.WithForeground(Palette.ChartLabel), bounds.Width);
    }

    private void RenderMarketCategories(CellBuffer buffer, ChartDataSeries[] data, int row) {
        var bounds = PlotBounds;
        _pickGeometry.Build(data, RenderRange(data), GraphKind, bounds.Width, bounds.Height, reversalAmount: ReversalAmount,
            flameReverse: FlameReverse, flameFocus: GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
            flameFrameHeight: GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1, taskOffset: TaskOffset);
        int previousPoint = -1, occupied = -1;
        foreach (var shape in _pickGeometry.Primitives) {
            if (shape.Point < 0 || shape.Point == previousPoint || shape.X0 <= occupied) {
                continue;
            }
            previousPoint = shape.Point;
            int end = GraphKind == GraphKind.Marimekko ? shape.X1 + 1 : bounds.Width;
            int width = Math.Min(end - shape.X0, Math.Max(1, bounds.Width / XAxis.TickCount));
            string label = data[shape.Series].Points[shape.Point].Label;
            WriteFeature(buffer, bounds.X + shape.X0, row, label, Palette.Body.WithForeground(Palette.ChartLabel), width);
            occupied = shape.X0 + Math.Min(width, label.Length);
        }
    }
    private static ChartPoint GridPoint(ChartDataSeries[] data, int index) {
        foreach (var series in data) {
            if (index < series.Count) {
                return series.Points[index];
            }
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private void RenderHeatmapScale(CellBuffer buffer, ChartRange range) {
        if (_heatmapScaleKey != (range, YAxis)) {
            _heatmapScale = " " + YAxis.FormatValue(range.MinColor) + " .. " + YAxis.FormatValue(range.MaxColor) + " (blank = missing)";
            _heatmapScaleKey = (range, YAxis);
        }
        var bounds = PlotBounds;
        int y = bounds.Bottom + (ShowAxes ? 1 : 0);
        for (int index = 0; index < Math.Min(5, bounds.Width); index++) {
            buffer.WriteText(bounds.X + index, y, "█", Palette.Body.WithForeground(GridColor((byte)(index + 2))));
        }
        WriteFeature(buffer, bounds.X + Math.Min(5, bounds.Width), y, _heatmapScale, Palette.Body.WithForeground(Palette.ChartLabel), Math.Max(0, bounds.Width - 5));
    }

    private void RenderWaterfallKey(CellBuffer buffer) {
        var bounds = PlotBounds;
        int y = bounds.Bottom + (ShowAxes ? 1 : 0);
        WriteFeature(buffer, bounds.X, y, "+ increase", Palette.Body.WithForeground(Palette.Success), bounds.Width);
        WriteFeature(buffer, bounds.X + 12, y, "- decrease", Palette.Body.WithForeground(Palette.Error), Math.Max(0, bounds.Width - 12));
        WriteFeature(buffer, bounds.X + 24, y, "0 unchanged", Palette.Body.WithForeground(PixelInk), Math.Max(0, bounds.Width - 24));
    }

    private Color GridColor(byte shade) => GraphKind == GraphKind.Waterfall
        ? shade == 3 ? Palette.Success : shade == 6 ? Palette.Error : shade == 1 ? Color.Mix(PixelBackground, PixelInk, .1f) : PixelInk
        : Palette.SequentialColor((Math.Clamp(shade, (byte)2, (byte)6) - 2) / 4f);

    private ChartSelection? PickGrid(ChartDataSeries[] data, int x, int y) {
        var bounds = PlotBounds;
        _pickGeometry.Build(data, RenderRange(data), GraphKind, bounds.Width, bounds.Height, reversalAmount: ReversalAmount,
            flameReverse: FlameReverse, flameFocus: GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
            flameFrameHeight: GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1, taskOffset: TaskOffset);
        foreach (var shape in _pickGeometry.Primitives) {
            if (shape.Point < 0 || shape.Kind == ChartPrimitiveKind.Text
                || x - bounds.X < shape.X0 || x - bounds.X > shape.X1
                || y - bounds.Y < Math.Min(shape.Y0, shape.Y1) || y - bounds.Y > Math.Max(shape.Y0, shape.Y1)) {
                continue;
            }
            var series = data[shape.Series];
            var point = series.Points[shape.Point];
            int sourceIndex = series.SourceIndex < 0 ? shape.Series : series.SourceIndex;
            long version = DataVersion;
            if (_gridSelectionData == data && _gridSelectionVersion == version && _gridSelection is { } selected
                && selected.SeriesIndex == sourceIndex && selected.PointIndex == point.Index) {
                return selected;
            }
            _gridSelectionData = data;
            _gridSelectionVersion = version;
            string details = GraphKind == GraphKind.Flamegraph && series.FlameTree is { } tree
                ? $"self {tree.Nodes[shape.Point].Self:G6}; inclusive {tree.Nodes[shape.Point].Inclusive:G6}; {tree.Nodes[shape.Point].Sources.Count} frame row(s)"
                : GraphKind == GraphKind.Gantt && series.Schedule is { } schedule
                    ? $"{schedule.Tasks[shape.Point].Start.UtcDateTime:u} .. {schedule.Tasks[shape.Point].End.UtcDateTime:u} (UTC); progress {(schedule.Tasks[shape.Point].Progress is { } completed ? completed.ToString("P0", System.Globalization.CultureInfo.InvariantCulture) : "unknown")}"
                    : series.Property;
            return _gridSelection = new(sourceIndex, details, point.Index, point.X, point.Y!.Value, point.Label, point.Source);
        }
        return null;
    }
    private IEnumerable<ChartThreshold>? RenderThresholds => SelectedRange is { } range ? [
        ..Thresholds,
        new(range.MinimumX, Vertical: true),
        new(range.MaximumX, Vertical: true)
    ] : Thresholds.Count == 0 ? null : Thresholds;

    private void DrawGeometryCells(CellBuffer buffer, ChartDataSeries[] data, bool overlaysOnly = false) {
        var plotBounds = PlotBounds;
        _cellGeometry.Build(
            data,
            RenderRange(data),
            overlaysOnly ? GraphKind.Line : GraphKind,
            plotBounds.Width,
            plotBounds.Height,
            RenderThresholds,
            Annotations.Count == 0 ? null : Annotations,
            XAxis,
            YAxis,
            ReversalAmount,
            FlameReverse,
            GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
            flameFrameHeight: GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1,
            taskOffset: TaskOffset
        );
        foreach (var shape in _cellGeometry.Primitives) {
            if (shape.Kind == ChartPrimitiveKind.Text) {
                continue;
            }

            Color color = shape.Color ?? (GraphKind == GraphKind.Flamegraph ? FlameColor(shape.Shade)
                : GraphKind == GraphKind.Gantt ? shape.Shade == 3 ? Palette.Success : PixelInk
                : GraphKind == GraphKind.Kagi ? shape.Shade == 3 ? Palette.Success : Palette.Error
                : ChartGrid.Supports(GraphKind) ? GridColor(shape.Shade) : shape.Shade > 0 ? Color.Mix(PixelBackground, PixelInk, (shape.Shade - 1) / 5f) : data.ElementAtOrDefault(shape.Series)?.Color ?? DefaultSeriesColor(shape.Series));
            if (ChartTopology.Supports(GraphKind) && shape.Point >= 0 && data[shape.Series].Points[shape.Point].Index == SelectedIndex) {
                color = Color.Mix(PixelBackground, PixelInk, .8f);
            }
            var ink = Palette.Body.WithForeground(color);
            if (GraphKind == GraphKind.Kagi && shape.Point == SelectedIndex) {
                ink = Palette.AccentStyle;
            }
            if (shape.Kind == ChartPrimitiveKind.Rectangle) {
                for (int y = shape.Y0; y <= shape.Y1; y++) {
                    for (int x = shape.X0; x <= shape.X1; x++) {
                        bool selected = shape.Point >= 0 && (ChartTopology.Supports(GraphKind) || ChartStackedArea.Supports(GraphKind) ? data[shape.Series].Points[shape.Point].Index : GraphKind is GraphKind.Flamegraph or GraphKind.Gantt ? data[0].Points[shape.Point].Index : shape.Point) == SelectedIndex;
                        bool border = GraphKind == GraphKind.Flamegraph && (x == shape.X0 || x == shape.X1);
                        string glyph = ChartTopology.Supports(GraphKind) ? selected ? "▓" : " " : GraphKind == GraphKind.Flamegraph ? border ? "│" : " " : selected ? "▓" : "█";
                        var style = ChartTopology.Supports(GraphKind) ? ink.WithBackground(color)
                            : GraphKind == GraphKind.Flamegraph
                            ? ink.WithBackground(color).WithForeground(selected ? Palette.Accent : PixelBackground) : ink;
                        buffer.WriteText(plotBounds.X + x, plotBounds.Y + y, glyph, style);
                    }
                }
            }
            else {
                int steps = Math.Max(1, Math.Max(Math.Abs(shape.X1 - shape.X0), Math.Abs(shape.Y1 - shape.Y0)));
                for (int index = 0; index <= steps; index++) {
                    int x = (int)Math.Round(shape.X0 + (shape.X1 - shape.X0) * index / (double)steps), y = (int)Math.Round(shape.Y0 + (shape.Y1 - shape.Y0) * index / (double)steps);
                    buffer.WriteText(
                        plotBounds.X + x,
                        plotBounds.Y + y,
                        shape.X0 == shape.X1 ? shape.Stroke > 1 ? "┃" : "│" : shape.Y0 == shape.Y1 ? shape.Stroke > 1 ? "━" : "─" : "•",
                        ink
                    );
                }
            }
        }
    }
}

internal sealed partial class SixelPlot {
    private readonly ChartGeometry _geometry = new();
    private void DrawGeometry(
        ChartDataSeries[] data,
        ChartRange range,
        GraphKind kind,
        int width,
        int height,
        IEnumerable<ChartThreshold>? thresholds,
        IEnumerable<ChartAnnotation>? annotations,
        ChartAxis? xAxis,
        ChartAxis? yAxis,
        int selected = -1,
        double reversalAmount = 1,
        bool flameReverse = false,
        int flameFocus = -1,
        int flameFrameHeight = 1,
        int taskOffset = 0
    ) {
        _geometry.Build(data, range, kind, width, height, thresholds, annotations, xAxis, yAxis, reversalAmount, flameReverse, flameFocus, flameFrameHeight, taskOffset);
        foreach (var shape in _geometry.Primitives) {
            if (shape.Kind == ChartPrimitiveKind.Text) {
                continue;
            }

            byte color = shape.Shade > 0 ? shape.Shade : data.Length > 1 ? (byte)(2 + shape.Series % Themes.ThemePalette.SeriesColorCount) : (byte)6;
            if (ChartTopology.Supports(kind) && shape.Point >= 0 && data[shape.Series].Points[shape.Point].Index == selected) {
                color = 5;
            }
            if (kind == GraphKind.Kagi && shape.Point == selected) {
                color = 2;
            }
            if (shape.Color is { } requested) {
                var rgb = requested.ToRgb();
                int nearest = int.MaxValue;
                for (byte index = 2; index <= ColorCount; index++) {
                    var candidate = Palette[index].ToRgb();
                    int distance = (candidate.R - rgb.R) * (candidate.R - rgb.R) + (candidate.G - rgb.G) * (candidate.G - rgb.G) + (candidate.B - rgb.B) * (candidate.B - rgb.B);
                    if (distance < nearest) {
                        nearest = distance;
                        color = index;
                    }
                }
            }

            if (shape.Kind == ChartPrimitiveKind.Rectangle) {
                int radius = kind == GraphKind.Flamegraph ? Math.Min(2, Math.Min((shape.X1 - shape.X0) / 2, (shape.Y1 - shape.Y0) / 2)) : 0;
                int source = (ChartTopology.Supports(kind) || ChartStackedArea.Supports(kind)) && shape.Point >= 0 ? data[shape.Series].Points[shape.Point].Index
                    : kind is GraphKind.Flamegraph or GraphKind.Gantt && shape.Point >= 0 ? data[0].Points[shape.Point].Index : shape.Point;
                bool highlight = source >= 0 && source == selected;
                if (kind != GraphKind.Flamegraph && (!highlight || ChartTopology.Supports(kind))) {
                    FillRectangle(shape.X0, shape.Y0, shape.X1, shape.Y1, color);
                    continue;
                }

                for (int y = shape.Y0; y <= shape.Y1; y++) {
                    for (int x = shape.X0; x <= shape.X1; x++) {
                        if (radius > 0 && (x < shape.X0 + radius || x > shape.X1 - radius)
                            && (y < shape.Y0 + radius || y > shape.Y1 - radius)) {
                            int dx = x < shape.X0 + radius ? shape.X0 + radius - x : x - shape.X1 + radius;
                            int dy = y < shape.Y0 + radius ? shape.Y0 + radius - y : y - shape.Y1 + radius;
                            if (dx * dx + dy * dy > radius * radius) {
                                continue;
                            }
                        }
                        bool edge = x == shape.X0 || x == shape.X1 || y == shape.Y0 || y == shape.Y1;
                        SetPixel(x, y, !ChartTopology.Supports(kind) && highlight && edge
                            ? color >= 4 ? (byte)2 : (byte)6
                            : kind == GraphKind.Flamegraph && edge && shape.X1 - shape.X0 >= 2 ? (byte)1 : color);
                    }
                }
            }
            else {
                int steps = Math.Max(1, Math.Max(Math.Abs(shape.X1 - shape.X0), Math.Abs(shape.Y1 - shape.Y0)));
                for (int index = 0; index <= steps; index++) {
                    int x = (int)Math.Round(shape.X0 + (shape.X1 - shape.X0) * index / (double)steps), y = (int)Math.Round(shape.Y0 + (shape.Y1 - shape.Y0) * index / (double)steps);
                    if ((uint)x < (uint)width && (uint)y < (uint)height) {
                        SetPixel(x, y, color);
                        if (shape.Stroke > 1) {
                            if (shape.X0 == shape.X1 && x + 1 < width) {
                                SetPixel(x + 1, y, color);
                            }
                            else if (shape.Y0 == shape.Y1 && y + 1 < height) {
                                SetPixel(x, y + 1, color);
                            }
                        }
                    }
                }
            }
        }
    }
}
