using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml.Linq;
using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartFeatureTests {
    [TestMethod]
    public async Task WheelScrollsParentUnlessZoomIsExplicitlyEnabledAndSettingsPreserveOptIn() {
        var chart = new Chart([10, 30, 20]) { MinHeight = 12, MaxHeight = 12, Title = "Chart" };
        var scroll = new ScrollViewer(new VStack(chart, new TextBlock("More content") { MinHeight = 40, MaxHeight = 40 })) {
            HorizontalScrollEnabled = false
        };
        await using var ui = new DesignerUxTests.UiSession(scroll, () => { }, 90, 24);
        Assert.IsFalse(chart.EnableZoom);
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2,
            () => host.TooltipContent is not null);
        ui.Wheel(chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2, -1, () => scroll.VerticalOffset > 0);
        ui.Tick();
        Assert.IsNull(host.TooltipContent, "Scrolling must invalidate a tooltip anchored to the old chart position.");
        Assert.IsNull(chart.MinimumX);
        Assert.IsNull(chart.MaximumX);
        ui.App.Focus(chart);
        ui.Text("+", () => true);
        ui.Tick();
        Assert.IsNull(chart.MinimumX, "Keyboard zoom must also be opt-in.");
        chart.EnableZoom = true;
        ui.Wheel(chart.PlotBounds.X + 2, Math.Max(0, chart.PlotBounds.Y) + 2, 1, () => chart.MinimumX.HasValue);
        Assert.IsLessThan(2d, (chart.MaximumX - chart.MinimumX)!.Value);
        var copy = new Chart();
        copy.ImportSettings(chart.ExportSettings());
        Assert.IsTrue(copy.EnableZoom);
        copy.ImportSettings("{}");
        Assert.IsFalse(copy.EnableZoom);
    }

    [TestMethod]
    public async Task HorizontalBarHoverMatchesTheWholePaintedBarAndClearsInGaps() {
        var chart = new Chart { GraphKind = GraphKind.HorizontalBars, Minimum = 0, ShowLegend = true };
        chart.Series.Add(new("Long", [100]));
        chart.Series.Add(new("Short", [10]));
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 100, 24);
        var bounds = chart.PlotBounds;
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        var geometry = new ChartGeometry();
        var data = chart.GetRenderData(100);
        geometry.Build(data, chart.RenderRange(data), chart.GraphKind, bounds.Width, bounds.Height);
        foreach (var shape in geometry.Primitives) {
            int x = bounds.X + shape.X0 + 1, y = bounds.Y + (shape.Y0 + shape.Y1) / 2;
            ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, x, y,
                () => host.TooltipContent is Paragraph { Text: { } text } && text.Contains(data[shape.Series].Property, StringComparison.Ordinal));
        }
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, bounds.Right - 1, bounds.Bottom - 1, () => host.TooltipContent is null);
    }

    [TestMethod]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.HorizontalBars)]
    public async Task CategoryBarLegendSelectsOriginalRowsRatherThanHidingTheSeries(GraphKind kind) {
        object source = new();
        var chart = new Chart { GraphKind = kind, ShowLegend = true };
        chart.Series.Add(new("Load", [20, 40]));
        chart.Series[0].Labels.AddRange(["CPU", "Memory"]);
        chart.Series[0].Sources.AddRange([source, new object()]);
        ChartSelection? selection = null;
        chart.SelectionChanged += value => selection = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 80, 24);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 2, 0, () => selection is not null);
        Assert.AreSame(source, selection!.Source);
        Assert.AreEqual(0, selection.PointIndex);
        Assert.IsTrue(chart.Series[0].Visible);
    }

    [TestMethod]
    public void LegendsMeasureWrapAndTruncateUnicodeLabelsInCells() {
        const string wide = "\u4e2d\u6587";
        var chart = new Chart { ShowLegend = true };
        chart.Series.Add(new(wide, [1, 2]));
        chart.Series.Add(new("Next", [2, 1]));
        var lines = VisualSnapshotRenderer.Render(chart, 13, 12).ToMarkupLines();
        Assert.Contains(wide, lines[0]);
        Assert.Contains("Next", lines[1], "Six cells plus spacing cannot share a thirteen-cell row with the second six-cell label.");
        Assert.AreEqual(2, chart.PlotBounds.Y);
        lines = VisualSnapshotRenderer.Render(chart, 5, 12).ToMarkupLines();
        Assert.Contains("\u4e2d\u2026", lines[0]);
        Assert.DoesNotContain(wide, lines[0]);
        Assert.AreEqual(4, ChartText.Width(wide));
        Assert.AreEqual(2, ChartText.Width("\U0001f600"));
        Assert.AreEqual(1, ChartText.Width("e\u0301"));
        Assert.AreEqual("e\u0301", ChartText.Fit("e\u0301x", 1));
        Assert.AreEqual("", ChartText.Fit("\U0001f600", 1));
        Assert.AreEqual("\U0001f600", ChartText.Fit("\U0001f600x", 2));
        const string family = "\U0001f469\u200d\U0001f469\u200d\U0001f467\u200d\U0001f466";
        Assert.AreEqual(2, ChartText.Width(family));
        Assert.AreEqual(family, ChartText.Fit(family + "x", 2));
        Assert.AreEqual("", ChartText.Fit(family, 1));
        Assert.AreEqual(2, ChartText.Width("\u2764\ufe0f"));
        Assert.AreEqual("\u2764\ufe0f", ChartText.Fit("\u2764\ufe0fx", 2));
    }

    [TestMethod]
    [DataRow((ChartRenderer)2)]
    [DataRow((ChartRenderer)3)]
    public void DisabledDrcsRendererValuesAreRejected(ChartRenderer renderer) {
        using var backend = new InMemoryTerminalBackend(new TerminalSize(40, 12), new TerminalCapabilities {
            AnsiEnabled = true,
            ColorLevel = TerminalColorLevel.TrueColor,
            IsInputRedirected = false,
            IsOutputRedirected = false,
            Graphics = new TerminalGraphicsCapabilities {
                SupportState = TerminalGraphicsSupportState.Confirmed,
                SupportsStaticImages = true,
                SupportedProtocols = [TerminalGraphicsProtocol.Sixel],
                PreferredProtocol = TerminalGraphicsProtocol.Sixel
            }
        });
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ChartPresenters.Create(session.Instance, renderer, cancellationToken: TestContext.CancellationToken));
        Assert.AreEqual("renderer", error.ParamName);
    }
    private sealed class UnrelatedPresenter : ITerminalGraphicsPresenter {
        public TerminalGraphicsCapabilities Capabilities => TerminalGraphicsCapabilities.None;
        public bool CanPresent(TerminalGraphicsCapabilities capabilities) =>
            true;
        public ValueTask PresentAsync(GraphicsCommandBuffer current, TerminalGraphicsPresentContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public void Reset() {
        }
        public void Dispose() {
        }
    }
    [TestMethod]
    public async Task UnrelatedGraphicsPresenterKeepsTheChartInCellFallback() {
        using var backend = new InMemoryTerminalBackend(new TerminalSize(40, 12));
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.DisableGraphics = true;
        using var session = Terminal.Open(backend, options, force: true);
        Assert.IsNull(ChartPresenters.Create(session.Instance, ChartRenderer.Native, cancellationToken: TestContext.CancellationToken));
        Assert.IsNull(ChartPresenters.Create(session.Instance, ChartRenderer.Auto, cancellationToken: TestContext.CancellationToken));
        using var presenter = new UnrelatedPresenter();
        var chart = new Chart([1, 4, 2]);
        await using var app = new TerminalApp(chart, session.Instance, new TerminalAppOptions { GraphicsPresenter = presenter });
        Assert.IsTrue(app.IsGraphicsPresentationEnabled);
        Assert.IsFalse(chart.UseSixel, "A foreign presenter cannot render Fullscreen's chart payloads.");
        Assert.IsFalse(ChartPresenters.Supports(app, GraphKind.Line));
    }
    [TestMethod]
    public async Task NativeChartAdaptersLinkValuesBoundsAndItemChangesWithoutReparentingSources() {
        var native = new LineChart([1, 2]) { Minimum = -5, Maximum = 10 };
        var owner = new VStack(native);
        var chart = ChartAdapters.Enhance(native);
        Assert.AreSame(native.Values, chart.Values);
        Assert.AreSame(owner, native.Parent);
        Assert.AreEqual(-5d, chart.Minimum);
        await using var ui = new DesignerUxTests.UiSession(chart, () => chart.Synchronize());
        native.Maximum = 30;
        native.Values.Add(25);
        ui.Tick();
        Assert.AreEqual(30d, chart.Maximum);
        Assert.AreEqual(25d, chart.GetData(100)[0].Points[2].Y);
        chart.Minimum = -12;
        ChartAdapters.Synchronize(chart);
        Assert.AreEqual(-12d, native.Minimum);
        var spark = new Sparkline([3, 4]);
        Assert.AreSame(spark.Values, ChartAdapters.Enhance(spark).Values);
        var bars = new BarChart([new(new TextBlock("A"), 5), new(new TextBlock("B"), -2)]);
        var enhancedBars = ChartAdapters.Enhance(bars);
        Assert.AreEqual(GraphKind.HorizontalBars, enhancedBars.GraphKind);
        Assert.AreEqual("A", enhancedBars.GetData(100)[0].Points[0].Label);
        bars.Items[0].Value = 9;
        Assert.AreEqual(9d, enhancedBars.GetData(100)[0].Points[0].Y);
        var breakdown = new BreakdownChart([new(3, new TextBlock("One")), new(7, new TextBlock("Two"))]);
        var enhancedBreakdown = ChartAdapters.Enhance(breakdown);
        Assert.AreEqual(10d, enhancedBreakdown.Range(enhancedBreakdown.GetData(100)).MaxY);
        breakdown.Segments[0].Value = 5;
        Assert.AreEqual(12d, enhancedBreakdown.Range(enhancedBreakdown.GetData(100)).MaxY);
        Assert.IsTrue(enhancedBars.ShowAxes);
    }
    [TestMethod]
    public async Task RichSelectionIdentifiesTheSeriesValuesAndSourceAtTheClickedPoint() {
        object low = new(), high = new();
        var chart = new Chart([-10]) { GraphKind = GraphKind.Scatter };
        chart.Sources.Add(low);
        var second = new ChartSeries("High", [10]);
        second.Sources.Add(high);
        chart.Series.Add(second);
        ChartSelection? selected = null, activated = null;
        chart.SelectionChanged += value => selected = value;
        chart.PointInvoked += value => activated = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 80, 20);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 40, 0, () => selected is not null);
        Assert.AreEqual(1, selected!.SeriesIndex);
        Assert.AreEqual("High", selected.SeriesName);
        Assert.AreEqual(0, selected.PointIndex);
        Assert.AreEqual(0d, selected.X);
        Assert.AreEqual(10d, selected.Y);
        Assert.AreSame(high, selected.Source);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 40, 0, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 40, 0, () => activated is not null);
        Assert.AreSame(high, activated!.Source);
    }
    [TestMethod]
    public async Task StackedSelectionUsesTheClickedSegmentInsteadOfItsUnstackedValue() {
        var chart = new Chart([10]) { GraphKind = GraphKind.StackedBars };
        chart.Series.Add(new("Upper", [10]));
        ChartSelection? selected = null;
        chart.SelectionChanged += value => selected = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 40, 20);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 20, 4, () => selected?.SeriesIndex == 1);
        Assert.AreEqual("Upper", selected!.SeriesName);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 20, 4, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 20, 15, () => selected?.SeriesIndex == 0);
        Assert.AreEqual(10d, selected!.Y);
    }

    [TestMethod]
    [DataRow(GraphKind.HorizontalBars)]
    [DataRow(GraphKind.Heatmap)]
    [DataRow(GraphKind.ErrorBars)]
    public async Task SharedGeometryStylesKeepClickedSourceInformation(GraphKind kind) {
        object first = new(), last = new();
        var chart = new Chart { GraphKind = kind };
        var series = new ChartSeries("Values", [2, 8]);
        series.Sources.AddRange([first, last]);
        series.LowerValues.AddRange([1, 7]);
        series.UpperValues.AddRange([3, 9]);
        chart.Series.Add(series);
        ChartSelection? selected = null, invoked = null;
        chart.SelectionChanged += value => selected = value;
        chart.PointInvoked += value => invoked = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 40, 20);
        int x = kind == GraphKind.ErrorBars ? 39 : 35;
        int y = kind == GraphKind.HorizontalBars ? 15 : kind == GraphKind.Heatmap ? 10 : 2;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => selected is not null);
        Assert.AreEqual(1, selected!.PointIndex);
        Assert.AreEqual(8d, selected.Y);
        Assert.AreSame(last, selected.Source);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => invoked is not null);
        Assert.AreSame(last, invoked!.Source);
    }
    [TestMethod]
    public void CategoryRowsUseEveryAvailableLabelAndHeatmapLabelsDoNotRepeat() {
        var bars = new Chart([1, 2, 3]) { ShowAxes = true, GraphKind = GraphKind.HorizontalBars, YAxis = new() { Scale = ChartScale.Category } };
        foreach (string name in new[] { "Alpha", "Beta", "Gamma" }) {
            bars.Labels.Add(name);
        }
        string text = string.Join('\n', VisualSnapshotRenderer.Render(bars, 100, 30).ToMarkupLines());
        Assert.Contains("Alpha", text);
        Assert.Contains("Beta", text);
        Assert.Contains("Gamma", text);
        var heat = new Chart { ShowAxes = true, GraphKind = GraphKind.Heatmap };
        heat.Series.Add(new("North", [1, 2]));
        heat.Series.Add(new("South", [2, 3]));
        var lines = VisualSnapshotRenderer.Render(heat, 100, 30).ToMarkupLines();
        Assert.AreEqual(1, lines.Count(line => line.Contains("North", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.Contains("South", StringComparison.Ordinal)));
    }
    [TestMethod]
    public void AxesFormatUnitsCategoriesDatesAndLogarithmicGaps() {
        var chart = new Chart([0, 1, 10, 100, -5]) { ShowAxes = true, YAxis = new() { Scale = ChartScale.Logarithmic, Unit = "ms" } };
        var rendered = chart.GetRenderData(100);
        Assert.IsNull(rendered[0].Points[0].Y);
        Assert.IsNull(rendered[0].Points[4].Y);
        Assert.AreEqual(0d, rendered[0].Points[1].Y);
        Assert.AreEqual(2d, rendered[0].Points[3].Y);
        var range = chart.RenderRange(rendered);
        Assert.AreEqual(0d, range.MinY);
        Assert.AreEqual(2d, range.MaxY);
        Assert.Contains("ms", string.Join('\n', VisualSnapshotRenderer.Render(chart, 100, 20).ToMarkupLines()));
        chart.YAxis = new();
        chart.Values.Clear();
        chart.Values.Add(3);
        chart.Values.Add(8);
        chart.Labels.Add("alpha");
        chart.Labels.Add("omega");
        chart.XAxis = new() {
            Scale = ChartScale.Category
        };
        string labels = string.Join('\n', VisualSnapshotRenderer.Render(chart, 100, 20).ToMarkupLines());
        Assert.Contains("alpha", labels);
        Assert.Contains("omega", labels);
        chart.XAxis = new() {
            Scale = ChartScale.Time,
            Format = "yyyy-MM-dd"
        };
        chart.XValues.Add(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks / (double)TimeSpan.TicksPerSecond);
        chart.XValues.Add(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero).UtcTicks / (double)TimeSpan.TicksPerSecond);
        string dates = string.Join('\n', VisualSnapshotRenderer.Render(chart, 120, 20).ToMarkupLines());
        Assert.Contains("2026-01-01", dates);
        Assert.Contains("2026-01-03", dates);
    }
    [TestMethod]
    public async Task ZoomPanResetAndShiftDragUseDataCoordinates() {
        var chart = new Chart([1, 2, 3]) { ShowAxes = true };
        chart.Zoom(2);
        Assert.AreEqual(.5, chart.MinimumX);
        Assert.AreEqual(1.5, chart.MaximumX);
        chart.Pan(.25);
        Assert.AreEqual(.75, chart.MinimumX);
        Assert.AreEqual(1.75, chart.MaximumX);
        chart.ResetView();
        Assert.IsNull(chart.MinimumX);
        Assert.IsNull(chart.MaximumX);
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 100, 30);
        var bounds = chart.PlotBounds;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, bounds.X, bounds.Y + 3, () => chart.SelectedRange is not null, TerminalModifiers.Shift);
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.Left, bounds.Right - 1, bounds.Y + 3, () => chart.SelectedRange!.MaximumX > 1.9, TerminalModifiers.Shift);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, bounds.Right - 1, bounds.Y + 3, () => true);
        Assert.AreEqual(new ChartSelectedRange(0, 2), chart.SelectedRange);
        Assert.ThrowsExactly<ArgumentException>(() => chart.SelectRange(new(double.NaN, 2)));
        Assert.AreEqual(new ChartSelectedRange(0, 2), chart.SelectedRange);
        ui.Text("0", () => chart.SelectedRange is null);
    }
    [TestMethod]
    public async Task LegendVisibilityAndExplicitStylesSurviveValueChanges() {
        var chart = new Chart([-5, -2]) { ShowLegend = true };
        chart.Series.Add(new("Other", [30, 40]) {
            Color = Color.Rgb(255, 0, 0),
            LineStyle = ChartLineStyle.Dashed,
            Marker = ChartMarker.Diamond
        });
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 100, 20);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 2, 0, () => !chart.GetData(100)[0].Visible);
        chart.Values.Add(-10);
        Assert.IsFalse(chart.GetData(100)[0].Visible, "A changed Values collection must not re-enable a hidden series.");
        Assert.AreEqual(30d, chart.RenderRange(chart.GetRenderData(100)).MinY);
        chart.SetSeriesStyle(1, new("Other", Color.Rgb(0, 255, 0), LineStyle: ChartLineStyle.Dotted));
        chart.Series[0].Values[0] = 35;
        var raster = new SixelPlot();
        chart.DrawPixels(raster, 100, 30);
        Assert.AreEqual(Color.Rgb(0, 255, 0), raster.Palette[6]);
        Assert.AreEqual(ChartLineStyle.Dotted, chart.GetData(100)[1].LineStyle);
    }
    [TestMethod]
    public void AdditionalChartsShareGeometryAndPreserveSignedStacksHeatAndErrorBounds() {
        var stacked = new Chart([10, -2]) { GraphKind = GraphKind.StackedBars };
        stacked.Series.Add(new("Other", [5, -3]));
        var range = stacked.Range(stacked.GetData(100));
        Assert.AreEqual(-5d, range.MinY);
        Assert.AreEqual(15d, range.MaxY);
        var raster = new SixelPlot();
        stacked.DrawPixels(raster, 80, 40);
        Assert.IsTrue(raster.Pixels.ToArray().Contains((byte)2));
        Assert.IsTrue(raster.Pixels.ToArray().Contains((byte)3));
        var horizontal = new Chart([-4, 8]) { GraphKind = GraphKind.HorizontalBars };
        horizontal.DrawPixels(raster, 80, 40);
        Assert.IsTrue(raster.Pixels.ToArray().Any(pixel => pixel > 1));
        var heat = new Chart([0, 5]) { GraphKind = GraphKind.Heatmap };
        heat.Series.Add(new("Second row", [10, double.NaN]));
        heat.DrawPixels(raster, 20, 20);
        Assert.AreEqual((byte)2, raster.Pixels[5 * 20 + 5]);
        Assert.AreEqual((byte)6, raster.Pixels[15 * 20 + 5]);
        Assert.IsLessThanOrEqualTo((byte)1, raster.Pixels[15 * 20 + 15]);
        var errors = new Chart { GraphKind = GraphKind.ErrorBars };
        var series = new ChartSeries("Bounds", [10]);
        series.LowerValues.Add(5);
        series.UpperValues.Add(15);
        errors.Series.Add(series);
        Assert.AreEqual(new ChartRange(0, 0, 5, 15), errors.Range(errors.GetData(100)));
        errors.DrawPixels(raster, 21, 21);
        Assert.IsGreaterThan((byte)1, raster.Pixels[10]);
        Assert.IsGreaterThan((byte)1, raster.Pixels[20 * 21 + 10]);
        series.UpperValues[0] = 4;
        Assert.ThrowsExactly<ArgumentException>(() => errors.GetData(100));
    }
    [TestMethod]
    [DataRow(GraphKind.HorizontalBars)]
    [DataRow(GraphKind.StackedBars)]
    [DataRow(GraphKind.Heatmap)]
    [DataRow(GraphKind.ErrorBars)]
    public void AdditionalChartsDrawTheirDataInCellFallback(GraphKind kind) {
        var chart = new Chart { GraphKind = kind };
        var series = new ChartSeries("Data", [2, 5]);
        series.LowerValues.Add(1);
        series.LowerValues.Add(4);
        series.UpperValues.Add(3);
        series.UpperValues.Add(6);
        chart.Series.Add(series);
        string text = string.Join('\n', VisualSnapshotRenderer.Render(chart, 40, 12).ToMarkupLines());
        if (kind == GraphKind.ErrorBars) { Assert.Contains("│", text); Assert.Contains("─", text); }
        else { Assert.Contains("█", text); }
        Assert.IsFalse(chart.UseSixel);
    }
    [TestMethod]
    public void LineStylesAndMarkersChangeBothRasterAndCellPresentation() {
        var chart = new Chart([0, 10]);
        var raster = new SixelPlot();
        chart.SetSeriesStyle(0, new(Marker: ChartMarker.None));
        chart.DrawPixels(raster, 60, 20);
        byte[] solid = raster.Pixels.ToArray();
        chart.SetSeriesStyle(0, new(LineStyle: ChartLineStyle.Dashed, Marker: ChartMarker.None));
        chart.DrawPixels(raster, 60, 20);
        Assert.IsFalse(solid.AsSpan().SequenceEqual(raster.Pixels), "Dashed strokes must change actual raster marks.");
        chart.GraphKind = GraphKind.Scatter;
        chart.SetSeriesStyle(0, new(Marker: ChartMarker.None));
        chart.DrawPixels(raster, 60, 20);
        Assert.IsFalse(raster.Pixels.ToArray().Any(value => value > 1));
        chart.SetSeriesStyle(0, new(Marker: ChartMarker.Diamond));
        chart.DrawPixels(raster, 60, 20);
        Assert.IsTrue(raster.Pixels.ToArray().Any(value => value > 1));
        Assert.Contains("◆", string.Join('\n', VisualSnapshotRenderer.Render(chart, 60, 20).ToMarkupLines()));
    }
    [TestMethod]
    public void SvgPngAndSettingsExportPreservePresentationWithoutSerializingSourceObjects() {
        var chart = new Chart([1, 2]) { GraphKind = GraphKind.Scatter, ShowAxes = true, Title = "Export <chart>", YAxis = new() { Unit = "MB" } };
        chart.Sources.Add(new {
            SecretSource = "not exported"
        });
        chart.Thresholds.Add(new(1.5, "limit", Color: Color.Rgb(255, 0, 0)));
        chart.Annotations.Add(new(.5, 1.5, "peak"));
        chart.SetSeriesStyle(0, new(Color: Color.Rgb(0, 255, 0), Marker: ChartMarker.Diamond));
        string settings = chart.ExportSettings();
        Assert.DoesNotContain("SecretSource", settings);
        var restored = new Chart([1, 2]);
        restored.ImportSettings(settings);
        Assert.AreEqual(chart.YAxis, restored.YAxis);
        Assert.AreEqual(chart.Title, restored.Title);
        Assert.AreEqual(Color.Rgb(255, 0, 0), restored.Thresholds[0].Color);
        Assert.AreEqual(Color.Rgb(0, 255, 0), restored.GetData(1)[0].Color);
        string svg = chart.ExportSvg(100, 30);
        Assert.AreEqual("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.Contains("peak", svg);
        Assert.Contains("limit", svg);
        Assert.Contains("MB", svg);
        byte[] png = chart.ExportPng(20, 10);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.AreEqual(20, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.AreEqual(10, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        using var idat = new MemoryStream();
        for (int offset = 8; offset < png.Length;) {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT") {
                idat.Write(png, offset + 8, length);
            }
            offset += length + 12;
        }
        idat.Position = 0;
        using var zip = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zip.CopyTo(raw);
        Assert.AreEqual(10 * (1 + 20 * 3), raw.Length);
        Assert.ThrowsExactly<ArgumentException>(() => restored.ApplySettings(restored.CaptureSettings() with { MinimumX = 10, MaximumX = 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => restored.ApplySettings(restored.CaptureSettings() with { Title = "must not apply", SelectedRange = new(3, 1) }));
        Assert.AreEqual(chart.Title, restored.Title);
    }
    [TestMethod]
    public void LiveAdapterBoundsItsBufferPausesAndStopsFollowingAfterNavigation() {
        var chart = new Chart();
        using var live = new LiveChartAdapter(chart, 3, TimeSpan.FromSeconds(4));
        for (int index = 0; index <= 5; index++) {
            live.Append(index, index * 2, "source-" + index);
        }
        Assert.AreEqual(3, live.Count);
        Assert.IsTrue(live.Tick());
        CollectionAssert.AreEqual(new[] { 3d, 4d, 5d }, chart.GetData(100)[0].Span.ToArray().Select(point => point.X).ToArray());
        live.Paused = true;
        live.Append(6, 12);
        Assert.IsFalse(live.Tick());
        Assert.AreEqual(5d, chart.GetData(100)[0].Span[^1].X);
        live.Resume();
        Assert.IsTrue(live.Tick());
        Assert.AreEqual(6d, chart.GetData(100)[0].Span[^1].X);
        chart.Pan(-.25);
        Assert.IsFalse(live.FollowLatest);
        double? limit = chart.MaximumX;
        live.Append(7, 14);
        live.Tick();
        Assert.AreEqual(limit, chart.MaximumX);
        chart.SelectRange(new(5, 6));
        Assert.IsTrue(live.Paused);
        Assert.IsFalse(live.FollowLatest);
        Assert.ThrowsExactly<ArgumentException>(() => live.Append(1, 2));
        Assert.AreEqual(3, live.Count);
        live.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => live.Append(8, 16));
    }
    [TestMethod]
    public void HorizontalLogValuesAndHeatmapZoomUseTheirVisibleCoordinates() {
        var bars = new Chart([1, 10, 100]) { GraphKind = GraphKind.HorizontalBars, XAxis = new() { Scale = ChartScale.Logarithmic } };
        var data = bars.GetRenderData(100);
        Assert.AreEqual(0d, data[0].Points[0].Y);
        Assert.AreEqual(2d, data[0].Points[2].Y);
        var range = bars.RenderRange(data);
        Assert.AreEqual(0d, range.MinX);
        Assert.AreEqual(2d, range.MaxX);
        bars.SetView(10, 100);
        Assert.IsNull(bars.GetRenderData(100)[0].Points[0].Y);
        var raster = new SixelPlot();
        bars.DrawPixels(raster, 80, 40);
        Assert.IsTrue(raster.Pixels.ToArray().Any(value => value > 1));
        var heat = new Chart([1, 2, 3, 4]) { GraphKind = GraphKind.Heatmap };
        heat.SetView(1, 2);
        heat.DrawPixels(raster, 20, 10);
        Assert.IsGreaterThan((byte)1, raster.Pixels[5 * 20]);
        Assert.IsGreaterThan((byte)1, raster.Pixels[5 * 20 + 19]);
        heat.YAxis = new() {
            Scale = ChartScale.Logarithmic
        };
        Assert.ThrowsExactly<ArgumentException>(() => heat.GetRenderData(100));
    }

    public TestContext TestContext { get; set; }
}
