using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using fullscreen.Charts;
using fullscreen.Custom;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartRadialTests {
    public TestContext TestContext { get; set; }

    private static Chart Example(GraphKind kind) {
        var chart = new Chart { GraphKind = kind, ShowLegend = true, ShowAxes = true };
        chart.Series.Add(new("Actual", [25, 50, 75, 100]));
        chart.Series[0].Labels.AddRange(["CPU", "Memory", "Disk", "Network"]);
        if (kind == GraphKind.Radar) {
            chart.Series.Add(new("Target", [50, 60, double.NaN, 90]));
            chart.Series[1].Labels.AddRange(["CPU", "Memory", "Disk", "Network"]);
        }
        ThemeCatalog.Load("Monokai").Apply(chart);
        return chart;
    }

    [TestMethod]
    [DataRow(GraphKind.Pie, "68EBBEFADF674785200F60A5A039ADE566149DA18C9336B233AF7CDD7DF970A6")]
    [DataRow(GraphKind.Gauge, "4E980039FE6716CB6E20EA028E2128CD65ADB96B762FBC875D160660AF1C33DF")]
    [DataRow(GraphKind.RadialBars, "01F78EAA0C9963FE624DA1738B10558E8F9F1FFF5981E6FF8EA47E6AC12B832E")]
    [DataRow(GraphKind.Radar, "A466BF46A8CE55ECD9BCFF707E5DA67CFD970145E9961849DEFFFA29A649B70F")]
    public void RadialOutputsExportAndWarmPathsReuseBuffers(GraphKind kind, string expectedPixels) {
        var chart = Example(kind);
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 60, 24).ToMarkupLines());
        Assert.Contains(kind == GraphKind.Radar ? "Target" : "CPU: 25", cells);
        Assert.IsLessThanOrEqualTo(60, chart.PlotBounds.Width);
        var plot = new SixelPlot();
        for (int index = 0; index < 10; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.PlotBounds;
        }
        Assert.AreEqual((byte)0, plot.Pixels[0], "Radial output must not retain Cartesian grid lines.");
        Assert.Contains((byte)2, plot.Pixels.ToArray());
        Assert.Contains((byte)3, plot.Pixels.ToArray());
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        TestContext.WriteLine($"{kind} raster hash: {hash}");
        Assert.AreEqual(expectedPixels, hash);
        long started = Stopwatch.GetTimestamp();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.PlotBounds;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var elapsed = Stopwatch.GetElapsedTime(started);
        TestContext.WriteLine($"{kind}: 100 warm 240x120 raster redraws/legend reads: {allocated} bytes, {elapsed.TotalMilliseconds:F2} ms; pixel hash {hash}.");
        Assert.AreEqual(0L, allocated);
        Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(plot.Pixels)));
        chart.SelectedIndex = 0;
        chart.DrawPixels(plot, 240, 120);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "Selection markers must not allocate on redraw.");
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(60, 24)[..4]);
        Assert.Contains("<svg", chart.ExportSvg(60, 24));
        if (kind == GraphKind.Radar) {
            var snapshot = new Chart { MinHeight = 24, MaxHeight = 24, Palette = chart.Palette };
            snapshot.SetData(chart.GetData(60));
            snapshot.ApplySettings(chart.CaptureSettings());
            snapshot.SelectedIndex = chart.SelectedIndex;
            _ = VisualSnapshotRenderer.Render(snapshot, 60, 24);
            var exclusions = snapshot.AnnotationBounds.ToArray();
            Assert.IsNotEmpty(exclusions, "Radar category labels must exclude raster painting.");
            XNamespace ns = "http://www.w3.org/2000/svg";
            var raster = XDocument.Parse(chart.ExportSvg(60, 24)).Descendants(ns + "g")
                .Single(element => element.Attribute("shape-rendering")?.Value == "crispEdges");
            foreach (var pixel in raster.Elements(ns + "rect")) {
                int x = int.Parse(pixel.Attribute("x")!.Value, CultureInfo.InvariantCulture), y = int.Parse(pixel.Attribute("y")!.Value, CultureInfo.InvariantCulture);
                int width = int.Parse(pixel.Attribute("width")!.Value, CultureInfo.InvariantCulture);
                foreach (var area in exclusions) {
                    int left = (area.X - snapshot.PlotBounds.X) * 8, top = (area.Y - snapshot.PlotBounds.Y) * 16;
                    Assert.IsFalse(y >= top && y < top + 16 && x < left + area.Width * 8 && x + width > left,
                        $"SVG raster strokes must not cover label {area}; pixel {x},{y},{width}.");
                }
            }
        }
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(kind));
        var copy = Example(kind);
        copy.ImportSettings(chart.ExportSettings());
        Assert.AreEqual(kind, copy.GraphKind);
    }

    [TestMethod]
    public void PieUsesOverflowSafePositiveTotalAndRetainsZeroAndMissingRowIdentity() {
        var chart = new Chart([double.MaxValue, 0, double.NaN, double.MaxValue]) { GraphKind = GraphKind.Pie };
        var data = chart.GetRenderData(60);
        var radial = new ChartRadial();
        radial.Build(data, chart.RenderRange(data), chart.GraphKind);
        radial.Sample(.5, 0, out var right, out _);
        radial.Sample(-.5, 0, out var left, out _);
        Assert.AreEqual(new RadialHit(0, 0), right);
        Assert.AreEqual(new RadialHit(0, 3), left);
        Assert.AreEqual(new RadialHit(-1, -1), radial.Pick(0, 0, 60, 20, true));
        chart.Values.Clear();
        chart.Values.AddRange([0, double.NaN, 0]);
        radial.Build(chart.GetRenderData(60), chart.RenderRange(chart.GetRenderData(60)), chart.GraphKind);
        Assert.IsTrue(radial.Empty);
        Assert.Contains("No positive values", string.Join('\n', VisualSnapshotRenderer.Render(chart, 40, 12).ToMarkupLines()));
    }

    [TestMethod]
    public void RadialSamplerCannotPaintOrSelectOutsideItsFittedRadius() {
        foreach (var kind in new[] { GraphKind.Pie, GraphKind.Gauge, GraphKind.RadialBars }) {
            var chart = Example(kind);
            var data = chart.GetRenderData(80);
            var radial = new ChartRadial();
            radial.Build(data, chart.RenderRange(data), kind);
            foreach (double angle in new[] { -Math.PI / 2, -Math.PI / 4, -Math.PI, 0d }) {
                Assert.AreEqual(0, radial.Sample(Math.Cos(angle) * 1.01, Math.Sin(angle) * 1.01, out var hit, out _), kind.ToString());
                Assert.AreEqual(new RadialHit(-1, -1), hit);
            }
        }
    }

    [TestMethod]
    [DataRow(80, 35, 8, 16)]
    [DataRow(144, 35, 10, 23)]
    [DataRow(73, 22, 9, 17)]
    public void PieBoundaryIsCircularRatherThanAClippedOversizedRim(int columns, int rows, int cellWidth, int cellHeight) {
        var chart = Example(GraphKind.Pie);
        var plot = new SixelPlot();
        int width = columns * cellWidth, height = rows * cellHeight;
        chart.DrawPixels(plot, width, height);
        var (CenterX, CenterY, RadiusX, RadiusY) = ChartRadial.Layout(GraphKind.Pie, width, height, false);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                double dx = (x - CenterX) / RadiusX, dy = (y - CenterY) / RadiusY;
                Assert.AreEqual(dx * dx + dy * dy <= 1, plot.Pixels[y * width + x] != 0, $"Circle boundary at {x},{y}, {width}x{height}.");
            }
        }
        Assert.IsLessThanOrEqualTo(2, plot.Pixels[..width].ToArray().Count(pixel => pixel != 0), "A tangent is not a broad flattened cap.");
        Assert.IsLessThanOrEqualTo(2, plot.Pixels.Slice((height - 1) * width, width).ToArray().Count(pixel => pixel != 0));
    }

    [TestMethod]
    [DataRow(GraphKind.Gauge)]
    [DataRow(GraphKind.RadialBars)]
    public void LanesHaveBoundedGeometryGradientAndExplicitOverflow(GraphKind kind) {
        var chart = new Chart([100, 150, double.NaN, 0]) { GraphKind = kind, ShowLegend = true };
        chart.Labels.AddRange(["full", "over", "missing", "zero"]);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        Assert.AreEqual(0d, range.MinY);
        Assert.AreEqual(100d, range.MaxY);
        var radial = new ChartRadial();
        radial.Build(data, range, kind);
        double angle = kind == GraphKind.Gauge ? -.01 : Math.Tau * .99 - Math.PI / 2;
        int color = radial.Sample(Math.Cos(angle) * .72, Math.Sin(angle) * .72, out var hit, out bool overflow);
        Assert.AreEqual(new RadialHit(0, 1), hit);
        Assert.IsTrue(overflow);
        Assert.AreEqual(6, color);
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 60, 24).ToMarkupLines());
        Assert.Contains("over: 150 !", cells);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 240, 120);
        if (kind == GraphKind.Gauge) {
            Assert.AreEqual(ThemePalette.Default.Success, plot.Palette[2]);
            Assert.AreEqual(ThemePalette.Default.Error, plot.Palette[6]);
        }
        chart.Maximum = 200;
        Assert.AreEqual(200d, chart.RenderRange(chart.GetRenderData(60)).MaxY);
        radial.Build(chart.GetRenderData(60), chart.RenderRange(chart.GetRenderData(60)), kind);
        radial.Sample(Math.Cos(angle) * .72, Math.Sin(angle) * .72, out _, out overflow);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    [DataRow(GraphKind.Pie)]
    [DataRow(GraphKind.Gauge)]
    [DataRow(GraphKind.RadialBars)]
    public void RowModesRejectAmbiguousSeriesNegativeDataAndInvalidScale(GraphKind kind) {
        var chart = new Chart([-1, 2, 3]) { GraphKind = kind };
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
        chart.Values[0] = 1;
        chart.Series.Add(new("Other", [1, 2, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
        chart.Series.Clear();
        chart.YAxis = new() { Scale = ChartScale.Logarithmic };
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
        chart.YAxis = new();
        chart.Minimum = chart.Maximum = 10;
        Assert.ThrowsExactly<ArgumentException>(() => chart.RenderRange(chart.GetRenderData(60)));
    }

    [TestMethod]
    public void RadarSharesSignedScaleLeavesMissingSegmentsAndMapsNearestEndpoint() {
        var chart = new Chart { GraphKind = GraphKind.Radar };
        chart.Series.Add(new("Signed", [-10, 0, 10, double.NaN]));
        chart.Series.Add(new("Short", [5, 10, -5]));
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        Assert.AreEqual(-10d, range.MinY);
        Assert.AreEqual(10d, range.MaxY);
        var radial = new ChartRadial();
        radial.Build(data, range, chart.GraphKind);
        Assert.AreEqual(8, radial.Vertices.Length);
        Assert.AreEqual(4, radial.Segments.ToArray().Count(segment => !segment.Grid));
        var vertex = radial.Vertices[2];
        var (X, Y) = ChartRadial.Position(vertex, chart.GraphKind, 60, 24, true);
        Assert.AreEqual(new RadialHit(0, 2), radial.Pick((int)Math.Round(X), (int)Math.Round(Y), 60, 24, true));
        chart.Minimum = 0;
        radial.Build(chart.GetRenderData(60), chart.RenderRange(chart.GetRenderData(60)), chart.GraphKind);
        Assert.IsTrue(radial.Vertices[0].Overflow);
        chart.Series[1].XValues.AddRange([1, 2, 3]);
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(60));
        var tooFew = new Chart([1, 2]) { GraphKind = GraphKind.Radar };
        Assert.ThrowsExactly<ArgumentException>(() => tooFew.GetRenderData(60));
    }

    [TestMethod]
    [DataRow(GraphKind.Pie)]
    [DataRow(GraphKind.Gauge)]
    [DataRow(GraphKind.RadialBars)]
    [DataRow(GraphKind.Radar)]
    public async Task ObjectProjectionConfigurationAndClicksRetainOriginalRows(GraphKind kind) {
        PSObject[] objects = [
            PSObject.AsPSObject(new { Category = "CPU", Load = 25, Target = 50 }),
            PSObject.AsPSObject(new { Category = "Memory", Load = 50, Target = 60 }),
            PSObject.AsPSObject(new { Category = "Disk", Load = 75, Target = 70 }),
            PSObject.AsPSObject(new { Category = "Network", Load = 100, Target = 80 })
        ];
        object[] y = kind == GraphKind.Radar ? ["Load", "Target"] : ["Load"];
        using var app = new ObjectChartApp(objects, "Category", y, kind, "Resources", CancellationToken.None);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => { }, 80, 30);
        var chart = app.Surface.Plot;
        var radial = new ChartRadial();
        radial.Build(chart.GetRenderData(80), chart.RenderRange(chart.GetRenderData(80)), kind);
        var vertex = kind == GraphKind.Radar ? radial.Vertices[0] : radial.Anchor(0);
        var (X, Y) = ChartRadial.Position(vertex, kind, chart.PlotBounds.Width, chart.PlotBounds.Height, true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left,
            chart.PlotBounds.X + (int)Math.Round(X), chart.PlotBounds.Y + (int)Math.Round(Y),
            () => chart.Selection is not null);
        Assert.AreSame(objects[0], chart.Selection!.Source);
        Assert.AreEqual(0, app.Surface.SelectedIndex);
        using var copy = new ObjectChartApp(objects, "Category", y, kind, "", CancellationToken.None);
        copy.ImportConfiguration(app.ExportConfiguration());
        Assert.AreEqual(kind, copy.Surface.GraphType);
        Assert.AreSame(objects[0], copy.Surface.Series[0].Points[0].Source);
        Assert.ThrowsExactly<ArgumentException>(() => app.Configure("Category", ["Load", "Target"], GraphKind.Pie));
        Assert.AreEqual(kind, app.Surface.GraphType, "Invalid radial reconfiguration must not replace the active chart.");
    }

    [TestMethod]
    [DataRow(GraphKind.Pie)]
    [DataRow(GraphKind.Gauge)]
    [DataRow(GraphKind.RadialBars)]
    [DataRow(GraphKind.Radar)]
    public async Task GeometryAndLegendClicksInspectOriginalObjects(GraphKind kind) {
        var chart = Example(kind);
        object source = new();
        chart.Series[0].Sources.AddRange([source, new object(), new object(), new object()]);
        chart.ShowAxes = false;
        chart.ShowLegend = false;
        ChartSelection? selection = null, invoked = null;
        chart.SelectionChanged += value => selection = value;
        chart.PointInvoked += value => invoked = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 60, 24);
        var data = chart.GetRenderData(60);
        var radial = new ChartRadial();
        radial.Build(data, chart.RenderRange(data), kind);
        var vertex = kind == GraphKind.Radar ? radial.Vertices[0] : radial.Anchor(0);
        var (X, Y) = ChartRadial.Position(vertex, kind, chart.PlotBounds.Width, chart.PlotBounds.Height, true);
        int x = chart.PlotBounds.X + (int)Math.Round(X), y = chart.PlotBounds.Y + (int)Math.Round(Y);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => selection is not null);
        Assert.AreSame(source, selection!.Source);
        Assert.AreEqual(0, selection.PointIndex);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => invoked is not null);
        Assert.AreSame(source, invoked!.Source);
        if (kind != GraphKind.Radar) {
            selection = null;
            chart.ShowLegend = true;
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 2, 0, () => true);
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 2, 0, () => selection is not null);
            Assert.AreSame(source, selection!.Source);
            Assert.IsTrue(chart.Series[0].Visible, "Row legends select data rather than hiding the entire single series.");
        }
    }

    [TestMethod]
    [DataRow(GraphKind.Gauge)]
    [DataRow(GraphKind.RadialBars)]
    [DataRow(GraphKind.Radar)]
    public void ManyCategoriesNarrowLayoutsAndCustomDefinitionsKeepAllData(GraphKind kind) {
        var chart = new Chart(Enumerable.Range(0, 32).Select(index => (double)(index * 4))) { GraphKind = kind, ShowAxes = true, ShowLegend = true };
        chart.Labels.AddRange(Enumerable.Range(0, 32).Select(index => "Category " + index));
        string text = string.Join('\n', VisualSnapshotRenderer.Render(chart, 8, 5).ToMarkupLines());
        Assert.AreEqual(32, chart.GetRenderData(8)[0].Count);
        Assert.IsGreaterThanOrEqualTo(1, chart.PlotBounds.Height);
        Assert.IsNotEmpty(text);
        var document = TuiDocument.FromJson($$$"""{"Root":{"Id":"radial","Type":"Chart","GraphKind":"{{{kind}}}","Minimum":0,"Maximum":100,"ShowLegend":true,"Bind":"samples"}}""");
        Assert.AreEqual(kind.ToString(), document.Root.GraphKind);
        Assert.IsTrue(document.Root.ShowLegend);
    }
}
