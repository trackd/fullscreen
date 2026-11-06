using System.Collections;
using System.Management.Automation;
using System.Security.Cryptography;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartMarketTests {
    public TestContext TestContext { get; set; }

    private static Chart Example(GraphKind kind) {
        var chart = new Chart { GraphKind = kind, Title = kind.ToString(), ShowAxes = true, ShowLegend = true, ReversalAmount = 5 };
        if (kind == GraphKind.Marimekko) {
            chart.Series.Add(new("Alpha", [30, 10, 0, double.NaN]));
            chart.Series.Add(new("Beta", [10, 10, 0]));
            foreach (var series in chart.Series) {
                series.Labels.AddRange(["Large", "Small", "Zero", "Missing"]);
            }
        }
        else {
            chart.Series.Add(new("Close", [100, 110, 108, 104, 95, 100, 111, 106, 90, double.NaN, 101, 101]));
            for (int index = 0; index < chart.Series[0].Values.Count; index++) {
                chart.Series[0].Labels.Add($"D{index}");
            }
        }
        return chart;
    }

    [TestMethod]
    public void MekkoWidthsAndStacksEncodeCategoryTotalsWithoutOverlap() {
        var chart = Example(GraphKind.Marimekko);
        var data = chart.GetRenderData(60);
        var geometry = new ChartGeometry();
        geometry.Build(data, chart.RenderRange(data), chart.GraphKind, 60, 20);
        var shapes = geometry.Primitives;
        Assert.HasCount(4, shapes);
        var alphaLarge = shapes.Single(shape => shape.Series == 0 && shape.Point == 0);
        var betaLarge = shapes.Single(shape => shape.Series == 1 && shape.Point == 0);
        var alphaSmall = shapes.Single(shape => shape.Series == 0 && shape.Point == 1);
        Assert.AreEqual(40, alphaLarge.X1 + 1);
        Assert.AreEqual(40, alphaSmall.X0);
        Assert.AreEqual(59, alphaSmall.X1);
        Assert.AreEqual(15, alphaLarge.Y1 - alphaLarge.Y0 + 1);
        Assert.AreEqual(5, betaLarge.Y1 - betaLarge.Y0 + 1);
        Assert.AreEqual(betaLarge.Y1 + 1, alphaLarge.Y0);
        Assert.AreEqual(0d, chart.RenderRange(data).MinY);
        Assert.AreEqual(100d, chart.RenderRange(data).MaxY);
        chart.Series[0].Values[0] = double.MaxValue;
        chart.Series[1].Values[0] = double.MaxValue;
        geometry.Build(chart.GetRenderData(60), chart.RenderRange(chart.GetRenderData(60)), chart.GraphKind, 60, 20);
        Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X1 < 60 && shape.Y0 >= 0 && shape.Y1 < 20));
        chart.SetSeriesVisible(0, false);
        Assert.AreEqual(1, chart.GetRenderData(60)[0].SourceIndex);
    }

    [TestMethod]
    public void KagiReversesAtFixedAmountAndSplitsAtShouldersAndWaists() {
        var chart = Example(GraphKind.Kagi);
        var segments = new List<ChartMarket.PriceSegment>();
        int columns = ChartMarket.BuildKagi(segments, chart.GetRenderData(80), 5);
        Assert.AreEqual(6, columns, "Three reversals plus a two-column missing gap produce six event columns.");
        Assert.IsFalse(segments.Any(segment => segment.Point == 2), "The two-point pullback is ignored.");
        Assert.AreEqual(3, segments.Count(segment => segment.Connector));
        Assert.IsTrue(segments.Any(segment => segment.Point == 6 && segment.From == 100 && segment.To == 110 && !segment.Yang));
        Assert.IsTrue(segments.Any(segment => segment.Point == 6 && segment.From == 110 && segment.To == 111 && segment.Yang));
        Assert.IsTrue(segments.Any(segment => segment.Point == 8 && segment.From == 106 && segment.To == 95 && segment.Yang));
        Assert.IsTrue(segments.Any(segment => segment.Point == 8 && segment.From == 95 && segment.To == 90 && !segment.Yang));
        Assert.IsFalse(segments.Any(segment => segment.Point == 9));
        chart.ReversalAmount = 6;
        _ = ChartMarket.BuildKagi(segments, chart.GetRenderData(80), chart.ReversalAmount);
        Assert.IsFalse(segments.Any(segment => segment.Point == 5), "A five-unit bounce does not reverse a six-unit Kagi.");
        chart.Values.Clear();
        var exact = new Chart([100, 110, 105]) { GraphKind = GraphKind.Kagi, ReversalAmount = 5 };
        Assert.AreEqual(2, ChartMarket.BuildKagi(segments, exact.GetRenderData(80), 5), "Equality triggers reversal.");
    }

    [TestMethod]
    public void MarketInputsRejectMisleadingShapesAndOptions() {
        var mekko = Example(GraphKind.Marimekko);
        mekko.Series[0].Values[0] = -1;
        Assert.ThrowsExactly<ArgumentException>(() => mekko.GetRenderData(60));
        mekko.Series[0].Values[0] = 1;
        mekko.Series[1].XValues.AddRange([1, 2, 3]);
        Assert.ThrowsExactly<ArgumentException>(() => mekko.GetRenderData(60));
        var kagi = new Chart([100, 0]) { GraphKind = GraphKind.Kagi };
        Assert.ThrowsExactly<ArgumentException>(() => kagi.GetRenderData(60));
        kagi.Values[1] = 110;
        kagi.ReversalAmount = 0;
        Assert.ThrowsExactly<ArgumentException>(() => kagi.GetRenderData(60));
        kagi.ReversalAmount = 5;
        kagi.Series.Add(new("Other", [100]));
        Assert.ThrowsExactly<ArgumentException>(() => kagi.GetRenderData(60));
        foreach (var kind in new[] { GraphKind.Marimekko, GraphKind.Kagi }) {
            var chart = Example(kind);
            chart.MinimumX = 1;
            Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
            chart.MinimumX = null;
            chart.YAxis = chart.YAxis with { Scale = ChartScale.Logarithmic };
            Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
            Assert.IsFalse(RegisChartPresenterPrototype.Supports(kind));
        }
        Assert.ThrowsExactly<ArgumentException>(() => new Chart().ApplySettings(new() { ReversalAmount = double.NaN }));
        Assert.ThrowsExactly<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"plot","Type":"Chart","GraphKind":"Kagi","ReversalAmount":0}}"""));
        var scalar = TuiDocument.FromJson("""{"Root":{"Id":"plot","Type":"Chart","GraphKind":"Kagi","Bind":"price"}}""");
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(scalar, new Hashtable { ["price"] = 100d }));
    }

    [TestMethod]
    public void EmptyAndDenseMarketInputsRemainBoundedAndPreserveMissingGaps() {
        foreach (var kind in new[] { GraphKind.Marimekko, GraphKind.Kagi }) {
            var chart = new Chart([double.NaN, double.NaN]) { GraphKind = kind };
            var geometry = new ChartGeometry();
            geometry.Build(chart.GetRenderData(80), chart.RenderRange(chart.GetRenderData(80)), kind, 3, 2);
            Assert.IsEmpty(geometry.Primitives);
            chart.Values.Clear();
            for (int index = 0; index < 512; index++) {
                chart.Values.Add(index % 11 == 0 ? double.NaN : index % 2 == 0 ? 100 : 110);
            }
            foreach (var size in new[] { (1, 1), (3, 2), (32, 8) }) {
                var data = chart.GetRenderData(size.Item1);
                Assert.AreEqual(512, data[0].Count);
                geometry.Build(data, chart.RenderRange(data), kind, size.Item1, size.Item2, reversalAmount: 5);
                Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X1 < size.Item1
                    && shape.Y0 >= 0 && shape.Y0 < size.Item2 && shape.Y1 >= 0 && shape.Y1 < size.Item2));
            }
        }
        var zero = new Chart([0, 0]) { GraphKind = GraphKind.Marimekko };
        var empty = new ChartGeometry();
        empty.Build(zero.GetRenderData(60), zero.RenderRange(zero.GetRenderData(60)), zero.GraphKind, 60, 20);
        Assert.IsEmpty(empty.Primitives, "Zero categories must not acquire artificial equal widths.");
    }

    [TestMethod]
    [DataRow(GraphKind.Marimekko, "DF3CE55C6800B5A0516B08815434C5304E5EB68D97BCDFF51A66F944A1834F8E")]
    [DataRow(GraphKind.Kagi, "EFCAAB1BBFF233550B5B25F89D3BBC1B653F8A0A0E9C97F6E73712AE686C2D9E")]
    public void MarketOutputsReuseGeometryAndPreserveSettings(GraphKind kind, string expected) {
        var chart = Example(kind);
        var cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 80, 24).ToMarkupLines());
        Assert.Contains(kind.ToString(), cells);
        Assert.Contains(kind == GraphKind.Marimekko ? "Large" : "Kagi reversal 5", cells);
        if (kind == GraphKind.Marimekko) {
            Assert.Contains("66.67%", cells);
        }
        var plot = new SixelPlot();
        for (int index = 0; index < 10; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        Assert.AreEqual(expected, hash);
        TestContext.WriteLine($"{kind} pixel fixture {hash}");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(plot.Pixels)));
        chart.SelectedIndex = 1;
        chart.DrawPixels(plot, 240, 120);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        TestContext.WriteLine($"{kind}: 100 warm raster draws and 100 selected redraws each allocated 0 bytes.");
        var copy = Example(kind);
        copy.ImportSettings(chart.ExportSettings());
        Assert.AreEqual(5d, copy.ReversalAmount);
        Assert.AreEqual(kind, copy.GraphKind);
        Assert.Contains("<svg", chart.ExportSvg(80, 24));
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(320, 160)[..4]);
        foreach (var size in new[] { (1, 1), (3, 2), (11, 7) }) {
            chart.DrawPixels(plot, size.Item1, size.Item2);
            _ = VisualSnapshotRenderer.Render(chart, size.Item1, size.Item2);
        }
        var document = TuiDocument.FromJson($$$"""{"Root":{"Id":"plot","Type":"Chart","Bind":"prices","GraphKind":"{{{kind}}}","ReversalAmount":5}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["prices"] = new[] { 100d, 110d, 105d } });
        var custom = Assert.IsInstanceOfType<Chart>(app.Controls["plot"]);
        Assert.AreEqual(5d, custom.ReversalAmount);
        app.State["prices"] = new[] { 110d, 100d, 120d };
        app.Tick();
        CollectionAssert.AreEqual(new[] { 110d, 100d, 120d }, custom.Values.ToArray());
    }

    [TestMethod]
    [DataRow(GraphKind.Marimekko)]
    [DataRow(GraphKind.Kagi)]
    public async Task PaintedGeometrySelectsOriginalRowsAndWarmPicksReuseSelection(GraphKind kind) {
        PSObject[] rows = [
            PSObject.AsPSObject(new { Category = "A", Value = 100d, Other = 30d }),
            PSObject.AsPSObject(new { Category = "B", Value = 110d, Other = 10d }),
            PSObject.AsPSObject(new { Category = "C", Value = 105d, Other = 0d }),
            PSObject.AsPSObject(new { Category = "D", Value = 120d, Other = 20d })
        ];
        using var viewer = new ObjectChartApp(rows, "Category", kind == GraphKind.Marimekko ? ["Value", "Other"] : ["Value"], kind, "", CancellationToken.None);
        viewer.Surface.Plot.ReversalAmount = 5;
        await using var ui = new DesignerUxTests.UiSession(viewer.Root, () => { }, 80, 30);
        var chart = viewer.Surface.Plot;
        var geometry = new ChartGeometry();
        var data = chart.GetRenderData(80);
        geometry.Build(data, chart.RenderRange(data), kind, chart.PlotBounds.Width, chart.PlotBounds.Height, reversalAmount: 5);
        var shape = geometry.Primitives.First(shape => shape.Point == 1 && shape.Series == 0 && shape.Y0 != shape.Y1);
        int x = chart.PlotBounds.X + (shape.X0 + shape.X1) / 2;
        int y = chart.PlotBounds.Y + (shape.Y0 + shape.Y1) / 2;
        ChartSelection? invoked = null;
        chart.PointInvoked += selection => invoked = selection;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => chart.Selection is not null);
        Assert.AreSame(rows[1], chart.Selection!.Source);
        if (kind == GraphKind.Kagi) {
            Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X, chart.PlotBounds.Y), "Blank event-space regions must not choose the nearest original price.");
        }
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => invoked is not null);
        Assert.AreSame(rows[1], invoked!.Source);
        _ = chart.Pick(x, y);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            _ = chart.Pick(x, y);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        var copy = new ObjectChartApp(rows, "Category", kind == GraphKind.Marimekko ? ["Value", "Other"] : ["Value"], kind, "", CancellationToken.None);
        using (copy) {
            copy.ImportConfiguration(viewer.ExportConfiguration());
            Assert.AreEqual(5d, copy.Surface.Plot.ReversalAmount);
            Assert.AreSame(rows[1], copy.Surface.Series[0].Points[1].Source);
        }
    }
}
