using System.Runtime.CompilerServices;
using fullscreen.Charts;
using XenoAtom.Terminal.UI;

namespace fullscreen.Tests;

// Existing timestamped fixtures exercise the production adapter and numeric raster together.
internal static class TimeSeriesRasterFixture {
    private static readonly ConditionalWeakTable<SixelPlot, TimeSeriesChartAdapter> Adapters = [];
    internal static void Draw(this SixelPlot plot, TimeSeries history, DateTimeOffset end, double maximum, int width, int height,
        Color ink, Color background, GraphKind kind, int inspection = -1) {
        var adapter = Adapters.GetOrCreateValue(plot);
        var data = adapter.Project(history, end, kind, width);
        plot.Draw(data, new(0, kind is GraphKind.Histogram or GraphKind.BoxPlot ? maximum : 120, 0, maximum),
            width, height, ink, background, null, kind, -1, inspection);
    }
}
