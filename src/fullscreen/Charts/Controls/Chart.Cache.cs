using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

public partial class Chart {
    private readonly record struct ProjectionKey(
        ChartDataSeries[] Data,
        long Version,
        int Width,
        GraphKind Kind,
        ChartAxis X,
        ChartAxis Y,
        double? MinimumX,
        double? MaximumX,
        Themes.ThemePalette Palette,
        Color Ink
    );
    private readonly record struct RangeKey(
        ChartDataSeries[] Data,
        long Version,
        int Count,
        GraphKind Kind,
        double? Minimum,
        double? Maximum,
        double? MinimumX,
        double? MaximumX,
        ChartAxis X,
        ChartAxis Y
    );
    private ProjectionKey? _projectionKey;
    private ChartDataSeries[] _projection = [];
    private RangeKey? _rangeKey;
    private ChartRange _range;
    private readonly record struct DistributionKey(
        ChartDataSeries[] Data,
        long Version,
        ChartRange Range,
        GraphKind Kind,
        int Bins
    );
    private DistributionKey? _distributionKey;
    private ChartDistribution[] _distributions = [];
    internal sealed record ChartDistribution(int[] Bins, double[] Sorted);
    internal void InvalidateDerivedData() {
        _projectionKey = null;
        _projection = [];
        _rangeKey = null;
        _distributionKey = null;
        _distributions = [];
    }

    internal ChartDistribution[] GetDistributions(ChartDataSeries[] data, ChartRange range, int width) {
        int bins = Math.Clamp(width / 4, 1, 32);
        var key = new DistributionKey(data, DataVersion, range, GraphKind, bins);
        if (_distributionKey == key) {
            return _distributions;
        }

        var results = new ChartDistribution[data.Length];
        for (int index = 0; index < data.Length; index++) {
            var series = data[index];
            if (GraphKind == GraphKind.Histogram) {
                results[index] = new(ChartStatistics.Histogram(series.Span, range.MinX, range.MaxX, bins), []);
            }
            else {
                int count = 0;
                foreach (var point in series.Span) {
                    if (point.Y.HasValue) {
                        count++;
                    }
                }

                var values = new double[count];
                int position = 0;
                foreach (var point in series.Span) {
                    if (point.Y is { } value) {
                        values[position++] = value;
                    }
                }

                Array.Sort(values);
                results[index] = new([], values);
            }
        }

        _distributions = results;
        _distributionKey = key;
        return results;
    }

    internal static int NavigationIndex(ReadOnlySpan<ChartPoint> points, int selected, TerminalKey key) {
        int first = -1, last = -1, current = -1;
        for (int index = 0; index < points.Length; index++) {
            if (!points[index].Y.HasValue) {
                continue;
            }

            if (first < 0) {
                first = index;
            }

            last = index;
            if (current < 0 && points[index].Index == selected) {
                current = index;
            }
        }

        if (first < 0 || key == TerminalKey.Home) {
            return first;
        }

        if (key == TerminalKey.End) {
            return last;
        }

        if (current < 0) {
            return first;
        }

        if (key == TerminalKey.Left) {
            for (int index = current - 1; index >= first; index--) {
                if (points[index].Y.HasValue) {
                    return index;
                }
            }

            return first;
        }

        for (int index = current + 1; index <= last; index++) {
            if (points[index].Y.HasValue) {
                return index;
            }
        }

        return last;
    }
}
