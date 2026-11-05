using System.Reflection;
using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartPerformanceTests {
    public TestContext TestContext { get; set; }

    private static long Allocations(Action action) {
        for (int index = 0; index < 10; index++) {
            action();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            action();
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [TestMethod]
    public void SvgPixelMaskingDoesNotAllocatePredicatesPerPixel() {
        var chart = new Chart { GraphKind = GraphKind.PercentStackedArea, ShowAxes = true, ShowLegend = true };
        chart.SetData([.. Enumerable.Range(0, 3).Select(series => new ChartDataSeries($"Series {series}",
            [.. Enumerable.Range(0, 1000).Select(index => new ChartPoint(index, index, 10 + series + index % 7,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture)))]))]);
        chart.Annotations.Add(new(333, 80, "source note"));
        for (int index = 0; index < 3; index++) {
            _ = chart.ExportSvg(100, 32);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        string svg = chart.ExportSvg(100, 32);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsLessThan(32L * 1024 * 1024, allocated,
            "Export may allocate its SVG document, but not a captured predicate for every scanned pixel.");
        Assert.Contains("source note", svg);
    }

    [TestMethod]
    public void RasterRedrawsAndBoxStatisticsKeepBoundedScratchAllocations() {
        var chart = new Chart(Enumerable.Range(0, 100).Select(index => (double)(index % 17))) {
            GraphKind = GraphKind.BoxPlot
        };
        var raster = new SixelPlot();
        long redraws = Allocations(() => chart.DrawPixels(raster, 320, 160));
        double[] observations = [100, 1, 8, 3, 7, 2, 6, 4, 5];
        long summaries = Allocations(() => _ = ChartStatistics.Summarize(observations));
        TestContext.WriteLine($"100 raster redraws: {redraws} bytes; 100 box summaries: {summaries} bytes.");
        Assert.AreEqual(0L, redraws);
        Assert.AreEqual(0L, summaries);
    }

    [TestMethod]
    public void SharedCalculationsAndGeometryDoNotAllocateAfterWarmup() {
        double[] sorted = [1, 2, 3, 4, 5, 6, 7, 8, 100];
        var geometry = new ChartGeometry();
        ChartDataSeries[] data = [new("First", [new(0, 0, -2, ""), new(1, 1, 5, "")])];
        Rectangle plot = new(0, 0, 320, 160), viewport = new(10, 10, 100, 100);
        Assert.AreEqual(0L, Allocations(() => {
            _ = ChartMath.Fraction(0, -double.MaxValue, double.MaxValue);
            _ = ChartMath.BarCenter(1, 0, 2, 320, 3);
            _ = ChartStatistics.SummarizeSorted(sorted);
            _ = ChartClipping.Intersect(plot, viewport);
            geometry.Build(data, new(0, 1, -2, 5), GraphKind.StackedBars, 320, 160);
        }));
        Assert.HasCount(2, geometry.Primitives);
    }
    [TestMethod]
    public void CategoryLabelsAndGapNavigationReadExistingPointsWithoutAllocatingCopies() {
        var chart = new Chart(Enumerable.Range(0, 10000).Select(value => (double)value));
        var data = chart.GetData(120);
        var tick = typeof(Chart).GetMethod("FormatTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Chart, ChartAxis, double, ChartDataSeries[], bool, string>>();
        var axis = new ChartAxis { Scale = ChartScale.Category };
        Assert.AreEqual("4100", tick(chart, axis, 4100.5, data, true), "Equal distances choose the first category.");
        Assert.AreEqual("9999", tick(chart, axis, 20000, data, true));
        Assert.AreEqual(0L, Allocations(() => _ = tick(chart, axis, 4100.5, data, true)));
        ChartPoint[] points = [new(7, 0, null, "gap"), new(20, 1, 1, "a"), new(30, 2, null, "gap"), new(40, 3, 2, "b"), new(50, 4, null, "gap")];
        Assert.AreEqual(1, Chart.NavigationIndex(points, 40, TerminalKey.Left));
        Assert.AreEqual(3, Chart.NavigationIndex(points, 20, TerminalKey.Right));
        Assert.AreEqual(1, Chart.NavigationIndex(points, 99, TerminalKey.Left));
        Assert.AreEqual(1, Chart.NavigationIndex(points, 99, TerminalKey.Right));
        Assert.AreEqual(1, Chart.NavigationIndex(points, 40, TerminalKey.Home));
        Assert.AreEqual(3, Chart.NavigationIndex(points, 20, TerminalKey.End));
        Assert.AreEqual(3, Chart.NavigationIndex(points, 40, TerminalKey.Right));
        Assert.AreEqual(1, Chart.NavigationIndex(points, 20, TerminalKey.Left));
        Assert.AreEqual(-1, Chart.NavigationIndex([], 0, TerminalKey.Right));
        Assert.AreEqual(0L, Allocations(() => _ = Chart.NavigationIndex(points, 40, TerminalKey.Left)));
    }
    [TestMethod]
    public void DerivedViewsReuseCachedDataAndInvalidateForValuesAxesBoundsStylesAndWidth() {
        var source = new object();
        var chart = new Chart([1, 10, 100]);
        chart.Sources.Add(source);
        chart.SetView(0, 1);
        var first = chart.GetRenderData(120);
        Assert.AreSame(first, chart.GetRenderData(120));
        Assert.AreSame(source, first[0].Points[0].Source);
        Assert.IsNull(first[0].Points[2].Y);
        Assert.AreEqual(0L, Allocations(() => _ = chart.GetRenderData(120)));
        chart.Values[1] = 20;
        var changed = chart.GetRenderData(120);
        Assert.AreNotSame(first, changed);
        Assert.AreEqual(20d, changed[0].Points[1].Y);
        Assert.AreEqual(10d, first[0].Points[1].Y, "Previously returned projections must not be rewritten.");
        chart.SetView(0, 2);
        Assert.AreEqual(100d, chart.GetRenderData(120)[0].Points[2].Y);
        chart.YAxis = new() {
            Scale = ChartScale.Logarithmic
        };
        Assert.AreEqual(2d, chart.GetRenderData(120)[0].Points[2].Y);
        changed = chart.GetRenderData(120);
        Assert.AreSame(changed, chart.GetRenderData(240), "Cell and pixel widths share width-independent numeric projections.");
        chart.SetColors(Color.Rgb(200, 0, 0), Color.Rgb(0, 0, 0));
        Assert.AreEqual(Color.Rgb(200, 0, 0), chart.GetRenderData(120)[0].Color);
        chart.SetSeriesStyle(0, new(Color: Color.Rgb(0, 200, 0), LineStyle: ChartLineStyle.Dashed));
        Assert.AreEqual(Color.Rgb(0, 200, 0), chart.GetRenderData(120)[0].Color);
        chart.SetSeriesVisible(0, false);
        Assert.IsEmpty(chart.GetRenderData(120));
        chart.SetSeriesVisible(0, true);
        Assert.HasCount(1, chart.GetRenderData(120));
        chart.Minimum = 1;
        chart.Maximum = 1000;
        Assert.AreEqual(3d, chart.RenderRange(chart.GetRenderData(120)).MaxY);
        chart.Maximum = 10000;
        Assert.AreEqual(4d, chart.RenderRange(chart.GetRenderData(120)).MaxY);
    }
    [TestMethod]
    public void DerivedViewsInvalidateForCoordinatesLabelsAdaptedDataAndLogXLimits() {
        object firstSource = new(), nextSource = new();
        var chart = new Chart([2, 4]) { XAxis = new() { Scale = ChartScale.Logarithmic } };
        chart.XValues.Add(1);
        chart.XValues.Add(10);
        chart.Labels.Add("first");
        chart.Labels.Add("last");
        chart.Sources.Add(firstSource);
        var first = chart.GetRenderData(120);
        Assert.AreEqual(1d, first[0].Points[1].X);
        chart.XValues[1] = 100;
        chart.Labels[1] = "changed";
        chart.Sources[0] = nextSource;
        var changed = chart.GetRenderData(120);
        Assert.AreEqual(2d, changed[0].Points[1].X);
        Assert.AreEqual("changed", changed[0].Points[1].Label);
        Assert.AreSame(nextSource, changed[0].Points[0].Source);
        Assert.AreSame(firstSource, first[0].Points[0].Source);
        chart.SetView(1, 10);
        Assert.IsNull(chart.GetRenderData(120)[0].Points[1].Y);
        chart.SetView(null, null);
        Assert.AreEqual(4d, chart.GetRenderData(120)[0].Points[1].Y);
        chart.GraphKind = GraphKind.HorizontalBars;
        Assert.AreEqual(Math.Log10(2), chart.GetRenderData(120)[0].Points[0].Y!.Value, 1e-12);
        chart.SetData([new("replacement", [new(9, 8, 7, "adapted") { Source = firstSource }])]);
        chart.XAxis = new();
        chart.GraphKind = GraphKind.Scatter;
        var adapted = chart.GetRenderData(120)[0].Points[0];
        Assert.AreEqual(9, adapted.Index);
        Assert.AreEqual("adapted", adapted.Label);
        Assert.AreSame(firstSource, adapted.Source);
        Assert.AreEqual(new ChartRange(8, 8, 7, 7), chart.RenderRange(chart.GetRenderData(120)));
    }
    [TestMethod]
    public void StackedRangesAndCellDistributionsCacheResultsWithoutChangingTheirStatistics() {
        var chart = new Chart([10, -2]) { GraphKind = GraphKind.StackedBars };
        chart.Series.Add(new("Other", [5, -3]));
        var data = chart.GetRenderData(120);
        Assert.AreEqual(new ChartRange(0, 1, -5, 15), chart.RenderRange(data));
        Assert.AreEqual(0L, Allocations(() => _ = chart.RenderRange(data)));
        chart.Series[0].Values[0] = 20;
        Assert.AreEqual(30d, chart.RenderRange(chart.GetRenderData(120)).MaxY);
        chart.GraphKind = GraphKind.Histogram;
        data = chart.GetRenderData(120);
        var range = chart.RenderRange(data);
        var bins = chart.GetDistributions(data, range, 16);
        CollectionAssert.AreEqual(new[] { 1, 0, 1, 0 }, bins[0].Bins);
        Assert.AreSame(bins, chart.GetDistributions(data, range, 16));
        Assert.AreEqual(0L, Allocations(() => _ = chart.GetDistributions(data, range, 16)));
        Assert.HasCount(8, chart.GetDistributions(data, range, 32)[0].Bins);
        chart.GraphKind = GraphKind.BoxPlot;
        var sorted = chart.GetDistributions(data, chart.RenderRange(data), 16);
        CollectionAssert.AreEqual(new[] { -2d, 10d }, sorted[0].Sorted);
        chart.Values.Add(100);
        data = chart.GetRenderData(120);
        CollectionAssert.AreEqual(new[] { -2d, 10d, 100d }, chart.GetDistributions(data, chart.RenderRange(data), 16)[0].Sorted);
    }
    [TestMethod]
    public void NativeAdapterReadsStayAllocationFreeAndStillObserveInPlaceChangesAndEmptyLists() {
        var label = new TextBlock("A");
        var native = new BarChart([new(label, 1), new(new TextBlock("B"), 2)]);
        var chart = ChartAdapters.Enhance(native);
        var data = chart.GetData(120);
        Assert.AreSame(data, chart.GetData(120));
        Assert.AreEqual(0L, Allocations(() => _ = chart.GetData(120)));
        native.Items[0].Value = 9;
        label.Text = "Changed";
        Assert.AreEqual(9d, chart.GetData(120)[0].Points[0].Y);
        Assert.AreEqual("Changed", chart.GetData(120)[0].Points[0].Label);
        Assert.AreSame(native.Items[0], chart.GetData(120)[0].Points[0].Source);
        native.Items.RemoveAt(0);
        Assert.AreEqual("B", chart.GetData(120)[0].Points[0].Label);
        native.Items.Clear();
        Assert.AreEqual(0, chart.GetData(120)[0].Count);
        var breakdown = new BreakdownChart();
        var empty = ChartAdapters.Enhance(breakdown);
        long version = empty.DataVersion;
        Assert.IsEmpty(empty.GetData(120));
        Assert.AreEqual(version, empty.DataVersion);
        Assert.AreEqual(0L, Allocations(() => _ = empty.GetData(120)));
        breakdown.Segments.Add(new(3, new TextBlock("New")));
        Assert.AreEqual(3d, empty.GetData(120)[0].Points[0].Y);
        breakdown.Segments[0].Value = 7;
        Assert.AreEqual(7d, empty.GetData(120)[0].Points[0].Y);
    }
    [TestMethod]
    public void HistoryViewsInvalidateWhenSamplesOrProjectionWidthChange() {
        var series = new TimeSeries();
        var time = DateTimeOffset.UnixEpoch;
        series.Add(1, time);
        series.Add(10, time.AddSeconds(10));
        var chart = new BuiltInTrend();
        chart.Update(series, series.Latest, 100);
        chart.SetView(0, 120);
        var first = chart.GetRenderData(20);
        Assert.AreSame(first, chart.GetRenderData(20));
        Assert.AreEqual(20, first[0].Count);
        Assert.AreEqual(40, chart.GetRenderData(40)[0].Count);
        series.Add(20, time.AddSeconds(20));
        chart.Update(series, series.Latest, 100);
        Assert.AreEqual(20d, chart.GetRenderData(20)[0].Span[^1].Y);
    }
}
