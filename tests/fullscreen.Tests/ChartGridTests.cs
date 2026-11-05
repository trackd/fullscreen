using System.Diagnostics;
using System.Collections;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Globalization;
using System.Xml.Linq;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartGridTests {
    [TestMethod]
    public void SkippedWaterfallColumnsBreakConnectorsWithoutResettingTotals() {
        var chart = new Chart([1, 2, 3, 4]) { GraphKind = GraphKind.Waterfall };
        var data = chart.GetRenderData(2);
        var geometry = new ChartGeometry();
        geometry.Build(data, chart.RenderRange(data), GraphKind.Waterfall, 2, 11);
        Assert.IsFalse(geometry.Primitives.Any(shape => shape.Kind == ChartPrimitiveKind.Line));
        var bars = geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 3 }, bars.Select(shape => shape.Point).ToArray());
        Assert.AreEqual(4, bars[1].Y1, "The final bar still starts at cumulative total six, not three.");
        geometry.Build(data, chart.RenderRange(data), GraphKind.Waterfall, 4, 11);
        Assert.AreEqual(3, geometry.Primitives.Count(shape => shape.Kind == ChartPrimitiveKind.Line));
    }

    public TestContext TestContext { get; set; }

    private static Chart Example(GraphKind kind) {
        var chart = new Chart { GraphKind = kind, ShowLegend = true, ShowAxes = true };
        chart.Series.Add(new("Change", [20, -5, 0, double.NaN, -25, 15]));
        chart.Series[0].Labels.AddRange(["Sales", "Costs", "Flat", "Missing", "Refunds", "Other"]);
        if (kind == GraphKind.Heatmap) {
            chart.Series.Add(new("Target", [-20, 0, 15, 25]));
            chart.Series[1].Labels.AddRange(["Sales", "Costs", "Flat", "Missing"]);
        }
        return chart;
    }

    [TestMethod]
    [DataRow(GraphKind.Waterfall, 8, 16)]
    [DataRow(GraphKind.Heatmap, 8, 16)]
    [DataRow(GraphKind.Waterfall, 10, 23)]
    [DataRow(GraphKind.Heatmap, 9, 17)]
    public void SvgFitsTextToRowsAndCompositesTextAboveTheRaster(GraphKind kind, int width, int height) {
        var chart = Example(kind);
        chart.Title = kind.ToString();
        var root = XDocument.Parse(chart.ExportSvg(80, 24, width, height)).Root!;
        var ns = root.Name.Namespace;
        var elements = root.Elements().ToArray();
        var raster = elements.Single(element => element.Name == ns + "g" && element.Attribute("shape-rendering")?.Value == "crispEdges");
        Assert.IsTrue(raster.Descendants().All(element => element.Name.Namespace == ns), "Raster rectangles must remain in the SVG namespace.");
        Assert.IsTrue(raster.Descendants(ns + "rect").Any());
        string style = root.Element(ns + "style")!.Value;
        Assert.Contains($"font-size:{(height * .75).ToString(CultureInfo.InvariantCulture)}px", style);
        Assert.Contains("dominant-baseline:central", style);
        foreach (var text in root.Elements(ns + "text")) {
            Assert.IsGreaterThan(Array.IndexOf(elements, raster), Array.IndexOf(elements, text));
            Assert.AreEqual("spacingAndGlyphs", text.Attribute("lengthAdjust")!.Value, "Box-drawing glyphs must still fill their logical cell widths.");
            string id = text.Attribute("clip-path")!.Value[5..^1];
            var clip = root.Descendants(ns + "clipPath").Single(element => element.Attribute("id")!.Value == id).Element(ns + "rect")!;
            double y = double.Parse(text.Attribute("y")!.Value, CultureInfo.InvariantCulture);
            double top = double.Parse(clip.Attribute("y")!.Value, CultureInfo.InvariantCulture);
            Assert.AreEqual(height, int.Parse(clip.Attribute("height")!.Value, CultureInfo.InvariantCulture));
            Assert.AreEqual(top + height / 2d, y);
        }
        Assert.Contains(kind.ToString(), string.Join(' ', root.Elements(ns + "text").Select(text => text.Value)));
    }

    [TestMethod]
    public void WaterfallRangeUsesCumulativeEndpointsAndKeepsMissingGaps() {
        var chart = Example(GraphKind.Waterfall);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        Assert.AreEqual(-10d, range.MinY);
        Assert.AreEqual(20d, range.MaxY);
        var geometry = new ChartGeometry();
        geometry.Build(data, range, chart.GraphKind, 60, 31);
        var bars = geometry.Primitives.Where(shape => shape.Point >= 0).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 4, 5 }, bars.Select(shape => shape.Point).ToArray());
        Assert.AreEqual(1, bars[1].Shade == 6 ? 1 : 0);
        Assert.AreEqual(bars[2].Y0, bars[2].Y1, "Zero is a selectable flat bar, not missing.");
        Assert.AreEqual(3, geometry.Primitives.Count(shape => shape.Kind == ChartPrimitiveKind.Line), "Missing rows break connectors.");
        chart.SetView(4, 5);
        data = chart.GetRenderData(60);
        geometry.Build(data, chart.RenderRange(data), chart.GraphKind, 60, 31);
        Assert.AreEqual(5, geometry.Primitives.Last().Point);
        Assert.AreEqual(15d, data[0].Points[0].Y + data[0].Points[1].Y, "Viewport filtering must not reset cumulative history.");
        Assert.ThrowsExactly<ArgumentException>(() => ChartRange.From([new("", [new(0, 0, double.MaxValue, ""), new(1, 1, double.MaxValue, "")])], GraphKind.Waterfall, null, null));
        chart.Series.Add(new("Ambiguous", [1, 2]));
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
    }

    [TestMethod]
    public void HeatmapHasAlignedCellsAndSharedSignedColorScaleWithBlankMissingSlots() {
        var chart = Example(GraphKind.Heatmap);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        Assert.AreEqual(-25d, range.MinColor);
        Assert.AreEqual(25d, range.MaxColor);
        var geometry = new ChartGeometry();
        geometry.Build(data, range, chart.GraphKind, 60, 20);
        Assert.HasCount(9, geometry.Primitives);
        Assert.AreEqual(10, geometry.Primitives.Single(shape => shape.Series == 1 && shape.Point == 1).X0);
        Assert.IsFalse(geometry.Primitives.Any(shape => shape.Series == 0 && shape.Point == 3));
        chart.Minimum = -10;
        chart.Maximum = 10;
        range = chart.RenderRange(chart.GetRenderData(60));
        geometry.Build(data, range, chart.GraphKind, 60, 20);
        Assert.AreEqual((byte)2, geometry.Primitives.Single(shape => shape.Series == 0 && shape.Point == 4).Shade);
        Assert.AreEqual((byte)6, geometry.Primitives.Single(shape => shape.Series == 0 && shape.Point == 0).Shade);
        chart.Series[1].XValues.AddRange([0, 2, 3, 4]);
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
    }

    [TestMethod]
    [DataRow(GraphKind.Waterfall)]
    [DataRow(GraphKind.Heatmap)]
    public void CustomNumericBindingsRefreshTheNewGridGeometry(GraphKind kind) {
        var document = TuiDocument.FromJson($$$"""{"Root":{"Id":"plot","Type":"Chart","Bind":"samples","GraphKind":"{{{kind}}}","ShowLegend":true}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["samples"] = new object?[] { 20d, null, -5d, 0d } });
        var chart = Assert.IsInstanceOfType<Chart>(app.Controls["plot"]);
        Assert.AreEqual(kind, chart.GraphKind);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 120, 60);
        var first = plot.Pixels.ToArray();
        app.State["samples"] = new[] { -5d, 0d, 15d };
        app.Tick();
        CollectionAssert.AreEqual(new[] { -5d, 0d, 15d }, chart.Values.ToArray());
        chart.DrawPixels(plot, 120, 60);
        Assert.IsFalse(first.AsSpan().SequenceEqual(plot.Pixels));
        Assert.Contains("█", string.Join('\n', VisualSnapshotRenderer.Render(app.Root, 60, 18).ToMarkupLines()));
    }

    [TestMethod]
    public void EmptyExtremeAndManyCategoryInputsRemainBoundedAndDoNotInventValues() {
        foreach (var kind in new[] { GraphKind.Waterfall, GraphKind.Heatmap }) {
            var chart = new Chart([double.NaN, double.NaN, double.NaN]) { GraphKind = kind };
            var data = chart.GetRenderData(60);
            var geometry = new ChartGeometry();
            geometry.Build(data, chart.RenderRange(data), kind, 60, 20);
            Assert.IsEmpty(geometry.Primitives);
            chart.Values.Clear();
            chart.Values.AddRange([double.MaxValue, -double.MaxValue, 0]);
            var plot = new SixelPlot();
            chart.DrawPixels(plot, 120, 60);
            Assert.Contains((byte)2, plot.Pixels.ToArray());
            chart.Values.Clear();
            for (int index = 0; index < 512; index++) {
                chart.Values.Add(index % 2 == 0 ? 1 : -1);
            }
            data = chart.GetRenderData(32);
            geometry.Build(data, chart.RenderRange(data), kind, 32, 8);
            Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X1 < 32 && shape.Y0 >= 0 && shape.Y1 < 8));
            Assert.AreEqual(512, data[0].Count, "Pixel resolution must not truncate the data model.");
            chart.XAxis = chart.XAxis with { Scale = ChartScale.Logarithmic };
            Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
        }
    }

    [TestMethod]
    public void HeatmapSelectionKeepsHiddenSeriesIdentityAndColorRangeIgnoresHiddenValues() {
        var chart = Example(GraphKind.Heatmap);
        chart.SetSeriesVisible(0, false);
        _ = VisualSnapshotRenderer.Render(chart, 80, 24);
        var data = chart.GetRenderData(80);
        Assert.AreEqual(1, data[0].SourceIndex);
        Assert.AreEqual(-20d, chart.RenderRange(data).MinColor);
        int point = chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y + 1);
        Assert.AreEqual(0, point);
        chart.Series[1].Values[0] = double.NaN;
        Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y + 1), "Cached source selection must not resurrect a missing cell.");
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(GraphKind.Waterfall));
        Assert.IsTrue(RegisChartPresenterPrototype.Supports(GraphKind.Heatmap));
    }

    [TestMethod]
    [DataRow(GraphKind.Waterfall)]
    [DataRow(GraphKind.Heatmap)]
    public async Task ExactGeometryClicksSelectSourceAndDoNotSelectGaps(GraphKind kind) {
        PSObject[] rows = [
            PSObject.AsPSObject(new { Category = "A", Change = 20d, Target = 0d }),
            PSObject.AsPSObject(new { Category = "B", Change = -5d, Target = 15d }),
            PSObject.AsPSObject(new { Category = "C", Change = double.NaN, Target = -15d }),
            PSObject.AsPSObject(new { Category = "D", Change = 0d, Target = 25d })
        ];
        using var viewer = new ObjectChartApp(rows, "Category", kind == GraphKind.Heatmap ? ["Change", "Target"] : ["Change"], kind, "", CancellationToken.None);
        await using var ui = new DesignerUxTests.UiSession(viewer.Root, () => { }, 80, 30);
        var chart = viewer.Surface.Plot;
        ChartSelection? invoked = null;
        chart.PointInvoked += selection => invoked = selection;
        var geometry = new ChartGeometry();
        var data = chart.GetRenderData(80);
        geometry.Build(data, chart.RenderRange(data), kind, chart.PlotBounds.Width, chart.PlotBounds.Height);
        var target = geometry.Primitives.Single(shape => shape.Point == 1 && shape.Series == 0);
        int x = chart.PlotBounds.X + (target.X0 + target.X1) / 2, y = chart.PlotBounds.Y + (target.Y0 + target.Y1) / 2;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => chart.Selection is not null);
        Assert.AreSame(rows[1], chart.Selection!.Source);
        Assert.AreEqual(0, chart.Selection.SeriesIndex);
        Assert.AreEqual(1, viewer.Surface.SelectedIndex);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => invoked is not null);
        Assert.AreSame(rows[1], invoked!.Source);
        int gapX = chart.PlotBounds.X + 2 * chart.PlotBounds.Width / 4 + 1;
        Assert.AreEqual(-1, chart.Pick(gapX, chart.PlotBounds.Y + 1), "Missing cells and empty bar regions must not choose nearest data.");
        using var copy = new ObjectChartApp(rows, "Category", kind == GraphKind.Heatmap ? ["Change", "Target"] : ["Change"], kind, "", CancellationToken.None);
        copy.ImportConfiguration(viewer.ExportConfiguration());
        Assert.AreEqual(kind, copy.Surface.GraphType);
        Assert.AreSame(rows[1], copy.Surface.Series[0].Points[1].Source);
    }

    [TestMethod]
    [DataRow(GraphKind.Waterfall, "9F0A27FA8648FC462DC6C4FAAA2AED91661B64DAAF34B6E8392723E0F8F06705")]
    [DataRow(GraphKind.Heatmap, "2023BAFBB270872C7C282AC45C04B509DAB3520CA24105991C248CB02993634B")]
    public void OutputsAndWarmRasterPickingReuseBuffers(GraphKind kind, string expectedHash) {
        var chart = Example(kind);
        var cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 80, 24).ToMarkupLines());
        Assert.Contains("Sales", cells);
        if (kind == GraphKind.Heatmap) {
            Assert.Contains("blank = missing", cells);
        }
        var plot = new SixelPlot();
        for (int index = 0; index < 10; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2);
        }
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        Assert.AreEqual(expectedHash, hash);
        TestContext.WriteLine($"{kind} raster hash {hash}");
        long started = Stopwatch.GetTimestamp(), before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"{kind}: 100 warm raster draws and picks: {allocated} bytes, {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}ms.");
        Assert.AreEqual(0L, allocated);
        Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(plot.Pixels)));
        chart.SelectedIndex = 1;
        chart.DrawPixels(plot, 240, 120);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Contains("<svg", chart.ExportSvg(80, 24));
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(320, 160)[..4]);
        var copy = Example(kind);
        copy.ImportSettings(chart.ExportSettings());
        Assert.AreEqual(kind, copy.GraphKind);
        foreach (var size in new[] { (1, 1), (3, 2), (11, 7) }) {
            chart.DrawPixels(plot, size.Item1, size.Item2);
            _ = VisualSnapshotRenderer.Render(chart, size.Item1, size.Item2);
        }
        var document = TuiDocument.FromJson($$$"""{"Root":{"Id":"chart","Type":"Chart","GraphKind":"{{{kind}}}"}}""");
        Assert.AreEqual(kind.ToString(), TuiDocument.FromJson(document.ToJson()).Root.GraphKind);
    }
}
