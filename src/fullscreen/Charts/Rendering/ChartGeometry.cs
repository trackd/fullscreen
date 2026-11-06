using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

internal enum ChartPrimitiveKind {
    Line,
    Rectangle,
    Text
}

internal readonly record struct ChartPrimitive(
    ChartPrimitiveKind Kind,
    int X0,
    int Y0,
    int X1,
    int Y1,
    int Series = 0,
    byte Shade = 0,
    string? Text = null,
    Color? Color = null,
    int Point = -1,
    int Stroke = 1
);
/// <summary>Shared geometry for additional plot styles and overlays, independent of terminal protocol.</summary>
internal sealed class ChartGeometry {
    internal List<ChartPrimitive> Primitives { get; } = [];

    private readonly Dictionary<double, (double Positive, double Negative)> _stacks = [];
    private readonly List<ChartMarket.PriceSegment> _prices = [];
    private readonly ChartFlame _flame = new();
    internal static bool IsAdditional(GraphKind kind) =>
        kind is GraphKind.HorizontalBars or GraphKind.StackedBars or GraphKind.Heatmap
        or GraphKind.ErrorBars or GraphKind.Waterfall or GraphKind.Marimekko or GraphKind.Kagi or GraphKind.Flamegraph or GraphKind.Gantt
        or GraphKind.FlowChart or GraphKind.NetworkDiagram or GraphKind.StackedArea or GraphKind.PercentStackedArea;
    internal void Build(
        ChartDataSeries[] data,
        ChartRange range,
        GraphKind kind,
        int width,
        int height,
        IEnumerable<ChartThreshold>? thresholds = null,
        IEnumerable<ChartAnnotation>? annotations = null,
        ChartAxis? xAxis = null,
        ChartAxis? yAxis = null,
        double reversalAmount = 1,
        bool flameReverse = false,
        int flameFocus = -1,
        int flameFrameHeight = 1,
        int taskOffset = 0
    ) {
        Primitives.Clear();
        _stacks.Clear();
        if (width <= 0 || height <= 0) {
            return;
        }
        if (ChartTopology.Supports(kind)) {
            if (data.Length != 2 || data[0].Topology is not { } graph) {
                throw new ArgumentException("Relationship diagrams require separate node and edge collections.");
            }
            ChartDiagram.Build(Primitives, graph, kind, width, height);
            return;
        }
        if (kind == GraphKind.Gantt) {
            ChartGantt.Build(Primitives, data, range, width, height, flameFrameHeight, taskOffset);
            return;
        }
        if (ChartGrid.Supports(kind)) {
            ChartGrid.Build(Primitives, data, range, kind, width, height);
        }
        if (ChartStackedArea.Supports(kind)) {
            ChartStackedArea.Build(Primitives, data, range, kind, width, height);
        }
        if (ChartMarket.Supports(kind)) {
            ChartMarket.Validate(data, kind);
            if (kind == GraphKind.Marimekko) {
                ChartMarket.BuildMekko(Primitives, data, width, height);
            }
            else {
                int columns = ChartMarket.BuildKagi(_prices, data, reversalAmount);
                ChartMarket.MapKagi(Primitives, _prices, columns, range, width, height);
            }
        }
        if (kind == GraphKind.Flamegraph) {
            if (data.Length != 1 || data[0].FlameTree is not { } tree) {
                throw new ArgumentException("Flamegraph requires hierarchical frames and exclusive self weights.");
            }
            if (data[0].Visible) {
                _flame.Build(Primitives, tree, width, height, flameReverse, flameFocus, flameFrameHeight);
            }
        }

        int X(double value) =>
            (int)Math.Round(ChartMath.Fraction(value, range.MinX, range.MaxX) * (width - 1));
        int Y(double value) =>
            height - 1 - (int)Math.Round(ChartMath.Fraction(value, range.MinY, range.MaxY) * (height - 1));
        void Line(
            int x0,
            int y0,
            int x1,
            int y1,
            int series,
            Color? color = null
        ) =>
            Primitives.Add(new(ChartPrimitiveKind.Line, x0, y0, x1, y1, series, Color: color));
        void Rect(
            int x0,
            int y0,
            int x1,
            int y1,
            int series,
            byte shade = 0,
            int point = -1
        ) =>
            Primitives.Add(new(
                ChartPrimitiveKind.Rectangle,
                Math.Max(0, Math.Min(x0, x1)),
                Math.Max(0, Math.Min(y0, y1)),
                Math.Min(width - 1, Math.Max(x0, x1)),
                Math.Min(height - 1, Math.Max(y0, y1)),
                series,
                shade,
                Point: point
            ));
        for (int seriesIndex = 0; !ChartGrid.Supports(kind) && !ChartStackedArea.Supports(kind) && seriesIndex < data.Length; seriesIndex++) {
            var series = data[seriesIndex];
            for (int position = 0; position < series.Count; position++) {
                var point = series.Points[position];
                if (point.Y is not { } value) {
                    continue;
                }

                if (kind == GraphKind.HorizontalBars) {
                    int span = Math.Max(1, height / Math.Max(1, series.Count) - 1);
                    int center = ChartMath.BarCenter(point.X, 0, Math.Max(0, series.Count - 1), height, series.Count);
                    int top = Math.Clamp(center - span / 2, 0, height - span);
                    int from = top + seriesIndex * span / Math.Max(1, data.Length), to = top + (seriesIndex + 1) * span / Math.Max(1, data.Length) - 1;
                    int inset = data.Length > 1 ? Math.Min(Math.Max(0, (to - from) / 2), Math.Max(1, (to - from + 1) / 8)) : 0;
                    Rect(X(0), from + inset, X(value), Math.Max(from + inset, to - inset), seriesIndex, point: position);
                }
                else if (kind == GraphKind.StackedBars) {
                    _stacks.TryGetValue(point.X, out var stack);
                    double start = value >= 0 ? stack.Positive : stack.Negative;
                    double end = start + value;
                    _stacks[point.X] = value >= 0 ? (end, stack.Negative) : (stack.Positive, end);
                    int span = Math.Max(1, width / Math.Max(1, series.Count) - 1);
                    int center = ChartMath.BarCenter(point.X, range.MinX, range.MaxX, width, series.Count);
                    int left = Math.Clamp(center - span / 2, 0, width - span);
                    Rect(left, Y(start), left + span - 1, Y(end), seriesIndex);
                }
                else if (kind == GraphKind.ErrorBars) {
                    double lower = point.Lower ?? value, upper = point.Upper ?? value;
                    if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper) {
                        continue;
                    }

                    int x = X(point.X), low = Y(lower), high = Y(upper), cap = Math.Max(1, Math.Min(4, width / Math.Max(1, series.Count * 8)));
                    Line(x, low, x, high, seriesIndex);
                    Line(Math.Max(0, x - cap), low, Math.Min(width - 1, x + cap), low, seriesIndex);
                    Line(Math.Max(0, x - cap), high, Math.Min(width - 1, x + cap), high, seriesIndex);
                    Line(Math.Max(0, x - 1), Y(value), Math.Min(width - 1, x + 1), Y(value), seriesIndex);
                }
            }
        }

        if (thresholds is not null) {
            foreach (var threshold in thresholds) {
                double value = threshold.Vertical ? xAxis?.Transform(threshold.Value) ?? threshold.Value : yAxis?.Transform(threshold.Value) ?? threshold.Value;
                if (!double.IsFinite(value)) {
                    continue;
                }

                if (threshold.Vertical && value >= range.MinX && value <= range.MaxX) {
                    Line(X(value), 0, X(value), height - 1, 0, threshold.Color);
                }
                else if (!threshold.Vertical && value >= range.MinY && value <= range.MaxY) {
                    Line(0, Y(value), width - 1, Y(value), 0, threshold.Color);
                }
                else {
                    continue;
                }

                if (threshold.Label.Length > 0) {
                    Primitives.Add(new(
                        ChartPrimitiveKind.Text,
                        threshold.Vertical ? X(value) : 0,
                        threshold.Vertical ? 0 : Y(value),
                        0,
                        0,
                        Text: threshold.Label,
                        Color: threshold.Color
                    ));
                }
            }
        }

        if (annotations is not null) {
            foreach (var annotation in annotations) {
                double x = xAxis?.Transform(annotation.X) ?? annotation.X, y = yAxis?.Transform(annotation.Y) ?? annotation.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y) || x < range.MinX || x > range.MaxX || y < range.MinY
                    || y > range.MaxY) {
                    continue;
                }

                Primitives.Add(new(
                    ChartPrimitiveKind.Text,
                    X(x),
                    Y(y),
                    0,
                    0,
                    Text: annotation.Text,
                    Color: annotation.Color
                ));
            }
        }
    }
}
