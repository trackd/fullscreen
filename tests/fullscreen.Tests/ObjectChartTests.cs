using System.Collections;
using System.Management.Automation;
using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI.Hosting;
using System.Reflection;
using System.Diagnostics;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ObjectChartTests {
    [TestMethod]
    public void ProcessesDefaultToWorkingSetAndStillAllowExplicitY() {
        using var process = Process.GetCurrentProcess();
        var item = PSObject.AsPSObject(process);
        item.Properties.Add(new PSNoteProperty("SI", 1));
        using var automatic = new ObjectChartApp([item]);
        CollectionAssert.AreEqual(new[] { nameof(Process.WorkingSet64) }, automatic.YProperties);
        Assert.AreSame(item, automatic.Data.Objects[0]);
        using var explicitY = new ObjectChartApp([item], yProperties: ["SI"]);
        CollectionAssert.AreEqual(new[] { "SI" }, explicitY.YProperties);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PassThruReturnsTheFilteredOriginalObjectsInViewOrder(bool passThru, bool useF10) {
        var first = Item(("Name", "first"), ("Score", 2), ("Payload", new[] { 1, 2 }));
        var second = Item(("Name", "second"), ("Score", 5));
        var third = Item(("Name", "third"), ("Score", 9));
        using var app = new ObjectChartApp([first, second, third], "Name",
            ["Score", new Hashtable { ["Name"] = "Displayed", ["Expression"] = "Score" }]);
        app.Surface.PassThru = passThru;
        app.SetFilter(ObjectChartFilter.Top, 2, "Score");
        Assert.IsEmpty(app.Chosen, "Opening the viewer must not emit input before confirmation.");
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        if (passThru) {
            Assert.Contains("return rows", ui.Output);
        }
        if (useF10) {
            ui.Key(TerminalKey.F10, () => !app.Tick());
        }
        else {
            ui.Text("q", () => !app.Tick());
        }
        var chosen = app.Chosen.ToArray();
        if (!passThru) {
            Assert.IsEmpty(chosen);
            return;
        }
        Assert.HasCount(2, chosen, "Multiple series must not duplicate returned objects.");
        Assert.AreSame(third, chosen[0]);
        Assert.AreSame(second, chosen[1]);
        Assert.IsNull(chosen[0].Properties["Displayed"], "Chart-only calculations must not alter original objects.");
        Assert.HasCount(3, app.Data.Objects, "Filtering output must not discard the retained full dataset.");
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PassThruEscapeAndPipelineCancellationDoNotReturnRows(bool pipelineCancellation) {
        using var stop = new CancellationTokenSource();
        using var app = new ObjectChartApp([PSObject.AsPSObject(3)], token: stop.Token);
        app.Surface.PassThru = true;
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        if (pipelineCancellation) {
            ui.Text("q", () => app.Surface.Accepted);
            Assert.HasCount(1, app.Chosen);
            stop.Cancel();
        }
        else {
            ui.Key(TerminalKey.Escape, () => !app.Tick());
            Assert.IsFalse(app.Surface.Accepted);
        }
        Assert.IsEmpty(app.Chosen);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PassThruIncludesMissingPlotValuesAndSupportsAnEmptyFilteredSubset(bool emptySubset) {
        var first = Item(("Name", "one"), ("Value", null));
        var second = Item(("Name", "two"), ("Value", 4));
        using var app = new ObjectChartApp([first, second], yProperties: ["Value"]);
        app.Surface.PassThru = true;
        if (emptySubset) {
            app.SetFilter(ObjectChartFilter.Top, 30, "Name");
        }
        else {
            app.SetFilter(ObjectChartFilter.First, 1);
        }
        Assert.IsEmpty(app.Surface.ValidPoints);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("q", () => !app.Tick());
        if (emptySubset) {
            Assert.IsEmpty(app.Chosen, "An accepted filter with no numeric ranking matches returns no objects.");
        }
        else {
            Assert.AreSame(first, app.Chosen.Single(), "The output subset follows the row filter, not numeric-plot eligibility.");
        }
    }
    [TestMethod]
    public void CountFiltersRankFiniteNumbersStablyAndKeepInputSlicesInOrder() {
        var data = new ObjectChartData([PSObject.AsPSObject(-2), PSObject.AsPSObject(9), PSObject.AsPSObject(9),
            Item(("Value", null)), PSObject.AsPSObject(double.NaN), PSObject.AsPSObject(4), PSObject.AsPSObject(double.NegativeInfinity), PSObject.AsPSObject("5")]);
        CollectionAssert.AreEqual(new[] { 1, 2 }, data.Select(ObjectChartFilter.Top, 2, "Value", TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { 0, 5 }, data.Select(ObjectChartFilter.Bottom, 2, "Value", TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { 0, 1 }, data.Select(ObjectChartFilter.First, 2, null, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { 6, 7 }, data.Select(ObjectChartFilter.Last, 2, null, TestContext.CancellationToken));
        Assert.HasCount(4, data.Select(ObjectChartFilter.Top, 100, "Value", TestContext.CancellationToken));
        Assert.HasCount(8, data.Select(ObjectChartFilter.All, 2, null, TestContext.CancellationToken));
        Assert.IsEmpty(data.Select(ObjectChartFilter.Top, 2, null, TestContext.CancellationToken));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => data.Select(ObjectChartFilter.Top, 2, "Value", stop.Token));
    }
    [TestMethod]
    public void RankingFiltersWholeObjectsAcrossSeriesAndRestoresAllRows() {
        var original = new[] { Item(("Name", "first"), ("A", 4), ("B", 50)), Item(("Name", "second"), ("A", 1), ("B", 100)),
            Item(("Name", "third"), ("A", 7), ("B", 20)), Item(("Name", "fourth"), ("A", -2), ("B", 200)) };
        using var app = new ObjectChartApp(original, "Name", ["A", "B"]);
        app.SetFilter(ObjectChartFilter.Top, 2);
        CollectionAssert.AreEqual(new[] { 2, 0 }, app.Surface.VisibleIndices);
        CollectionAssert.AreEqual(new[] { 2, 0 }, app.Surface.Series[1].Points.Select(point => point.Index).ToArray());
        Assert.AreEqual(20d, app.Surface.Series[1].Points[0].Y);
        Assert.AreEqual("third", app.Surface.Series[1].Points[0].Label);
        app.SetFilter(ObjectChartFilter.Top, 2, "B");
        CollectionAssert.AreEqual(new[] { 3, 1 }, app.Surface.VisibleIndices);
        app.Configure("Name", ["A"], GraphKind.Histogram);
        Assert.AreEqual(-2d, app.Surface.Series[0].Points[0].Y, "The ranking field stays independent of the plotted field.");
        Assert.AreEqual("B", app.RankProperty);
        app.SetFilter(ObjectChartFilter.All, 30);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, app.Surface.VisibleIndices);
        Assert.AreSame(original[3], app.Data.Objects[3]);
        Assert.ThrowsExactly<ArgumentException>(() => app.SetFilter(ObjectChartFilter.Top, 0));
        Assert.AreEqual(ObjectChartFilter.All, app.FilterMode);
        Assert.HasCount(4, app.Surface.VisibleIndices);
    }
    [TestMethod]
    public async Task FilterMenuOffersTopThirtyAndNavigationInspectsOriginalRows() {
        using var app = new ObjectChartApp([.. Enumerable.Range(0, 100).Select(value => PSObject.AsPSObject(value))]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("f", () => ui.Output.Contains("Top 30", StringComparison.Ordinal));
        for (int index = 0; index < 3; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Enter, () => app.FilterMode == ObjectChartFilter.Top);
        Assert.HasCount(30, app.Surface.VisibleIndices);
        Assert.AreEqual(99, app.Surface.VisibleIndices[0]);
        Assert.AreEqual(70, app.Surface.VisibleIndices[^1]);
        Assert.Contains("30/100 objects", ui.Output);
        ui.Key(TerminalKey.Home, () => app.Surface.SelectedIndex == 99);
        ui.Key(TerminalKey.Right, () => app.Surface.SelectedIndex == 98);
        ui.Text("d", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("Value: 98", ui.Output);
        Assert.Contains("Object 99", ui.Output);
        var close = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single().EnumerateVisualsDepthFirst().OfType<Button>()
            .Single(button => button.Content is TextBlock label && label.Text == "Close");
        ui.Activate(close, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        app.SetFilter(ObjectChartFilter.All, 30);
        ui.Tick();
        Assert.HasCount(100, app.Surface.VisibleIndices);
        Assert.AreEqual(98, app.Surface.SelectedIndex, "Keep the original selected object when it is still visible.");
    }
    [TestMethod]
    public async Task CustomCountValidatesBeforeApplyingAndEmptyRankingsCanRecover() {
        using var app = new ObjectChartApp([.. Enumerable.Range(0, 100).Select(value => PSObject.AsPSObject(value))]);
        app.SetFilter(ObjectChartFilter.Top, 30);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("f", () => ui.Output.Contains("Count: 30", StringComparison.Ordinal));
        for (int index = 0; index < 5; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Right, () => ui.Output.Contains("Custom…", StringComparison.Ordinal));
        for (int index = 0; index < 4; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        var input = dialog.EnumerateVisualsDepthFirst().OfType<TextBox>().Single();
        var apply = dialog.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock label && label.Text == "Apply");
        input.Text = "0";
        ui.Activate(apply, () => ui.Output.Contains("Enter a whole number", StringComparison.Ordinal));
        Assert.AreEqual(30, app.Count);
        input.Text = "7";
        ui.Activate(apply, () => app.Count == 7);
        Assert.HasCount(7, app.Surface.VisibleIndices);
        using var empty = new ObjectChartApp([Item(("Name", "text"), ("A", 4))]);
        empty.SetFilter(ObjectChartFilter.Top, 30, "Name");
        Assert.AreEqual(-1, empty.Surface.SelectedIndex);
        Assert.Contains("No rows match", string.Join('\n', VisualSnapshotRenderer.Render(empty.Root, 100, 25).ToMarkupLines()));
        empty.SetFilter(ObjectChartFilter.All, 30);
        Assert.AreEqual(0, empty.Surface.SelectedIndex);
        Assert.AreEqual(4d, empty.Surface.Series[0].Points[0].Y);
    }
    private static PSObject Item(params (string Name, object? Value)[] values) {
        var item = new PSObject();
        foreach (var (name, value) in values) {
            item.Properties.Add(new PSNoteProperty(name, value));
        }
        return item;
    }
    [TestMethod]
    public void CompleteObjectsSurviveChangingAxesSeriesAndGraphType() {
        var first = Item(("Name", "one"), ("X", 10), ("A", -3), ("B", 4), ("Hidden", "original detail"));
        var second = Item(("Name", "two"), ("X", 100), ("A", 8), ("B", 9));
        using var app = new ObjectChartApp([first, second], "X", ["A"]);
        Assert.AreSame(first, app.Data.Objects[0]);
        Assert.AreEqual(10, app.Surface.Series[0].Points[0].X);
        Assert.AreEqual(100, app.Surface.Series[0].Points[1].X);
        Assert.AreEqual(-3d, app.Surface.Series[0].Points[0].Y);
        app.Configure("Name", ["B", "A"], GraphKind.Bars);
        Assert.HasCount(2, app.Surface.Series);
        Assert.AreEqual("one", app.Surface.Series[0].Points[0].Label);
        Assert.AreEqual(0, app.Surface.Series[0].Points[0].X);
        Assert.AreEqual(9d, app.Surface.Series[0].Points[1].Y);
        Assert.AreEqual("original detail", first.Properties["Hidden"].Value);
        first.Properties["A"].Value = 21;
        app.Configure(null, ["A"], GraphKind.Scatter);
        Assert.AreEqual(21d, app.Surface.Series[0].Points[0].Y);
        Assert.AreSame(second, app.Data.Objects[1]);
        Assert.ThrowsExactly<ArgumentException>(() => app.Configure("Missing", ["A"], GraphKind.Line));
        Assert.AreEqual(GraphKind.Scatter, app.Surface.GraphType);
    }
    [TestMethod]
    public void HeterogeneousObjectsDictionariesScalarsAndBadGettersProduceGaps() {
        var bad = Item(("Value", null));
        bad.Properties.Add(new PSScriptProperty("Broken", ScriptBlock.Create("throw 'getter failed'")));
        var data = new ObjectChartData([PSObject.AsPSObject(3), PSObject.AsPSObject(new Hashtable { ["Value"] = -2, ["Other"] = 7 }), bad,
            Item(("Value", double.NaN)), Item(("Value", double.PositiveInfinity)), Item(("Value", "123"))]);
        Assert.Contains("Broken", data.Properties);
        Assert.Contains("Other", data.Properties);
        var points = data.Project(null, ["value"], TestContext.CancellationToken)[0].Points;
        Assert.AreEqual(3d, points[0].Y);
        Assert.AreEqual(-2d, points[1].Y);
        Assert.IsTrue(points.Skip(2).All(point => point.Y is null));
        Assert.IsTrue(data.Project(null, ["Broken"], TestContext.CancellationToken)[0].Points.All(point => point.Y is null));
        Assert.IsNull(ObjectChartData.Number(true));
    }
    [TestMethod]
    public void DateAxesPreserveElapsedSpacingAndDurationsUseSeconds() {
        var date = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
        var data = new ObjectChartData([Item(("When", date), ("Elapsed", TimeSpan.FromMinutes(2))), Item(("When", date.AddHours(3)), ("Elapsed", TimeSpan.FromMinutes(4)))]);
        var points = data.Project("When", ["Elapsed"], TestContext.CancellationToken)[0].Points;
        Assert.AreEqual(10800, points[1].X - points[0].X);
        Assert.AreEqual(120d, points[0].Y);
        Assert.AreEqual(240d, points[1].Y);
    }
    [TestMethod]
    public void InvalidXAndCancellationDoNotBecomeInventedZeroValues() {
        var data = new ObjectChartData([Item(("X", 2), ("Y", 3)), Item(("Y", 99)), Item(("X", double.NaN), ("Y", 42))]);
        Assert.IsNull(data.Project("X", ["Y"], TestContext.CancellationToken)[0].Points[1].Y);
        Assert.IsNull(data.Project("X", ["Y"], TestContext.CancellationToken)[0].Points[2].Y);
        Assert.ThrowsExactly<ArgumentException>(() => data.Project(null, ["missing"], TestContext.CancellationToken));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => data.Project(null, ["Y"], stop.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => new ObjectChartData([PSObject.AsPSObject(1)], stop.Token));
    }
    [TestMethod]
    [DataRow(GraphKind.Line, "•")]
    [DataRow(GraphKind.Area, "░")]
    [DataRow(GraphKind.Bars, "█")]
    [DataRow(GraphKind.Step, "•")]
    [DataRow(GraphKind.Scatter, "•")]
    [DataRow(GraphKind.Stem, "│")]
    [DataRow(GraphKind.Histogram, "█")]
    [DataRow(GraphKind.BoxPlot, "═")]
    public void EveryGraphTypeRendersSignedDataWithVisibleMarks(GraphKind kind, string glyph) {
        using var app = new ObjectChartApp([PSObject.AsPSObject(-3), PSObject.AsPSObject(0), PSObject.AsPSObject(7)], graphType: kind);
        string output = string.Join('\n', VisualSnapshotRenderer.Render(app.Root, 100, 25).ToMarkupLines());
        Assert.Contains(kind.ToString(), output);
        Assert.Contains(glyph, string.Join('\n', output.Split('\n').Skip(3).Take(17)));
        Assert.Contains("3 objects", output);
        Assert.Contains("-3", output);
    }
    [TestMethod]
    public void HistogramCountsIncludeBothExtremesAndScalingHandlesLargeSignedNumbers() {
        var data = new ObjectChartData([PSObject.AsPSObject(-4), PSObject.AsPSObject(-2), PSObject.AsPSObject(0), PSObject.AsPSObject(4)]);
        int[] bins = Chart.Histogram(data.Project(null, ["Value"], TestContext.CancellationToken)[0].Points, -4, 4, 4);
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, bins);
        Assert.AreEqual(.5, Chart.Fraction(0, -double.MaxValue, double.MaxValue));
        Assert.AreEqual(.5, Chart.Fraction(4, 4, 4));
        Assert.AreEqual(0, Chart.Fraction(-double.MaxValue, -double.MaxValue, double.MaxValue));
        Assert.AreEqual(1, Chart.Fraction(double.MaxValue, -double.MaxValue, double.MaxValue));
    }
    [TestMethod]
    public void DistributionGraphsIgnoreMissingXButKeepTheAxisForSwitchingBack() {
        using var app = new ObjectChartApp([Item(("X", null), ("Y", 7)), Item(("X", 2), ("Y", 9))], "X", ["Y"]);
        Assert.IsNull(app.Surface.Series[0].Points[0].Y);
        app.Configure("X", ["Y"], GraphKind.Histogram);
        Assert.AreEqual(7d, app.Surface.Series[0].Points[0].Y);
        Assert.AreEqual("X", app.XProperty);
        app.Configure(app.XProperty, app.YProperties, GraphKind.Line);
        Assert.IsNull(app.Surface.Series[0].Points[0].Y);
    }
    [TestMethod]
    public async Task MenusChangeValuesAxesAndTypeAndInspectTheCompleteObject() {
        var original = Item(("Name", "sample"), ("A", 12), ("B", 27), ("Hidden", "retained"));
        using var app = new ObjectChartApp([original], yProperties: ["A"]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("y", () => ui.Output.Contains("Hidden", StringComparison.Ordinal));
        ui.Key(TerminalKey.Home, () => true);
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Enter, () => app.YProperties.Contains("B"));
        ui.Text("x", () => ui.Output.Contains("Input order", StringComparison.Ordinal));
        ui.Key(TerminalKey.Home, () => true);
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Enter, () => app.XProperty == "Name");
        ui.Text("g", () => ui.Output.Contains("BoxPlot", StringComparison.Ordinal));
        foreach (string name in Enum.GetNames<GraphKind>()) {
            Assert.AreEqual(1, ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Count(text => text.Text == name), "Each graph appears once, regardless of its renderer.");
        }
        for (int index = 0; index < 7; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Enter, () => app.Surface.GraphType == GraphKind.BoxPlot);
        ui.Text("d", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("retained", ui.Output);
        Assert.AreSame(original, app.Data.Objects.Single());
    }
    [TestMethod]
    public void EmptyAndNonnumericInputRemainUsableAndSmallWindowsRender() {
        using var app = new ObjectChartApp([Item(("Name", "plain text"))]);
        Assert.IsEmpty(app.YProperties);
        Assert.Contains("Choose numeric Y", string.Join('\n', VisualSnapshotRenderer.Render(app.Root, 100, 20).ToMarkupLines()));
        VisualSnapshotRenderer.Render(app.Root, 20, 8);
        using var empty = new ObjectChartApp([]);
        Assert.IsEmpty(empty.Surface.Series);
        VisualSnapshotRenderer.Render(empty.Root, 80, 24);
    }
    [TestMethod]
    [DataRow(GraphKind.Line)]
    [DataRow(GraphKind.Area)]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.Step)]
    [DataRow(GraphKind.Scatter)]
    [DataRow(GraphKind.Stem)]
    [DataRow(GraphKind.Histogram)]
    [DataRow(GraphKind.BoxPlot)]
    public void SixelRasterSupportsEveryGraphAndRestoresHistoryChartColors(GraphKind kind) {
        using var app = new ObjectChartApp([PSObject.AsPSObject(-3), PSObject.AsPSObject(0), PSObject.AsPSObject(7)], graphType: kind);
        var raster = new SixelPlot();
        app.Surface.Plot.DrawPixels(raster, 65, 35);
        Assert.IsTrue(raster.Pixels.ToArray().Any(pixel => pixel >= 2), "The raster must contain data strokes in addition to the background grid.");
        Assert.Contains("\x1bP0;1q", new SixelEncoder().EncodePlot(raster));
        var history = new TimeSeries();
        history.Add(3, DateTimeOffset.UnixEpoch);
        raster.Draw(history, history.Latest, 10, 65, 35, app.Surface.Palette.Accent, app.Surface.Palette.Background, GraphKind.Line);
        Assert.AreEqual(app.Surface.Palette.Accent, raster.Palette[6], "Sharing the presenter raster must not leak object-series colors into ftop.");
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameGraphUsesSixelWhenSupportedAndCellFallbackOtherwise(bool supported) {
        var metrics = new TerminalPixelMetrics(400, 288, 4, 12, 100, 24);
        var graphics = supported ? new TerminalGraphicsCapabilities {
            PreferredProtocol = TerminalGraphicsProtocol.Sixel,
            SupportedProtocols = [TerminalGraphicsProtocol.Sixel],
            SupportState = TerminalGraphicsSupportState.Confirmed,
            SupportsStaticImages = true,
            PixelMetrics = metrics
        } : TerminalGraphicsCapabilities.None;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 24), new TerminalCapabilities {
            AnsiEnabled = true,
            Graphics = graphics,
            ColorLevel = TerminalColorLevel.TrueColor,
            IsOutputRedirected = false,
            IsInputRedirected = false
        });
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.DisableProbing = true;
        options.Graphics.DisableGraphics = !supported;
        if (supported) {
            options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Sixel;
            options.Graphics.ForcedPixelMetrics = metrics;
        }
        using var session = Terminal.Open(backend, options, force: true);
        using var chart = new ObjectChartApp([PSObject.AsPSObject(-3), PSObject.AsPSObject(7)]);
        using var presenter = new SixelChartPresenter(graphics, metrics);
        ChartPresenters.Register(session.Instance, presenter);
        await using var terminal = new TerminalApp(chart.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, GraphicsPresenter = presenter });
        Assert.AreEqual(supported, terminal.IsGraphicsPresentationEnabled);
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            time += Stopwatch.Frequency / 10;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [time]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Tick();
            Assert.AreEqual(supported, backend.GetOutText().Contains("\x1bP0;1q", StringComparison.Ordinal));
            long encoded = presenter.GetDiagnosticsSnapshot().EncodedFrameCount;
            Assert.AreEqual(supported ? 1L : 0L, encoded);
            Tick();
            Assert.AreEqual(encoded, presenter.GetDiagnosticsSnapshot().EncodedFrameCount, "An unchanged chart should reuse its raster.");
            chart.Configure(null, ["Value"], GraphKind.Bars);
            Tick();
            Assert.AreEqual(GraphKind.Bars, chart.Surface.GraphType);
            Assert.AreEqual(supported ? encoded + 1 : 0, presenter.GetDiagnosticsSnapshot().EncodedFrameCount);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    public TestContext TestContext { get; set; } = null!;
}
