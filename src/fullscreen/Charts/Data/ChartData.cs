using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

internal readonly record struct ChartPoint(int Index, double X, double? Y, string Label) {
    internal object? Source { get; init; }
    internal int[]? SourceIndices { get; init; }
    internal double? Lower { get; init; }
    internal double? Upper { get; init; }
}

internal sealed class ChartDataSeries(string property, ChartPoint[] points) {
    internal string Property { get; } = property;
    internal ChartPoint[] Points { get; } = points;
    internal int Count { get; set; } = points.Length;
    internal Color? Color { get; set; }
    internal bool Visible { get; set; } = true;
    internal ChartLineStyle LineStyle { get; set; }
    internal ChartMarker Marker { get; set; }
    internal ChartSeriesRole Role { get; set; }
    internal int SourceIndex { get; set; } = -1;
    internal ChartFlameTree? FlameTree { get; init; }
    internal ChartSchedule? Schedule { get; init; }
    internal ChartTopology? Topology { get; init; }
    internal ReadOnlySpan<ChartPoint> Span => Points.AsSpan(0, Count);
}

internal readonly record struct ChartSeriesVersion(
    int Values,
    int X,
    int Labels,
    int Sources,
    int Lower,
    int Upper,
    Color? Color,
    bool Visible,
    ChartLineStyle LineStyle,
    ChartMarker Marker,
    ChartSeriesRole Role
);
