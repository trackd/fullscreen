using System.Collections;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartInspectionTests {
    public static IEnumerable<TestDataRow<GraphKind>> GraphKinds =>
        Enum.GetValues<GraphKind>().Select(kind => new TestDataRow<GraphKind>(kind) { DisplayName = kind.ToString() });

    [TestMethod]
    [DynamicData(nameof(GraphKinds))]
    public async Task EveryGraphKind_HoverClickAndEnter_ShowDetailsAndInvokeTheOriginalPoint(GraphKind kind) {
        var source = new object();
        var chart = new Chart { GraphKind = kind };
        if (ChartTopology.Supports(kind)) {
            chart.Nodes.AddRange([new("a", "Alpha", Source: source), new("b", "Beta", Source: source)]);
            chart.Edges.Add(new("a", "b", Source: source));
        }
        else if (kind == GraphKind.Gantt) {
            chart.Tasks.Add(new("Alpha", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), .5, source));
        }
        else if (kind == GraphKind.Flamegraph) {
            chart.Frames.Add(new("root", null, "Alpha", 50, source));
        }
        else {
            chart.Values.AddRange([20, 50, 30]);
            chart.Labels.AddRange(["Alpha", "Beta", "Gamma"]);
            chart.Sources.AddRange([source, source, source]);
        }
        ChartSelection? clicked = null, invoked = null;
        chart.SelectionChanged += selection => clicked = selection;
        chart.PointInvoked += selection => invoked = selection;
        var root = new VStack(new TextBlock("Outside"), chart) { VerticalAlignment = Align.Stretch };
        await using var ui = new DesignerUxTests.UiSession(root, () => { }, 90, 28);
        var bounds = chart.PlotBounds;
        (int X, int Y)? hit = null;
        for (int y = bounds.Y; y < bounds.Bottom && hit is null; y++) {
            for (int x = bounds.X; x < bounds.Right && hit is null; x++) {
                if (chart.Pick(x, y) >= 0) {
                    hit = (x, y);
                }
            }
        }
        Assert.IsNotNull(hit, $"No selectable point for {kind}.");
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, hit.Value.X, hit.Value.Y,
            () => chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single().TooltipContent is not null);
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        Assert.AreEqual(200, host.ShowDelayMilliseconds);
        for (int tick = 0; tick < 18; tick++) {
            ui.Tick();
        }
        Assert.IsNull(host.TooltipContent!.App, "The tooltip must not open before the hover delay.");
        for (int tick = 0; tick < 4; tick++) {
            ui.Tick();
        }
        Assert.AreSame(ui.App, host.TooltipContent.App, $"The native tooltip did not open for {kind}.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(((Paragraph)host.TooltipContent).Text));
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, hit.Value.X, hit.Value.Y, () => clicked is not null);
        Assert.AreSame(source, clicked!.Source);
        Assert.IsNull(host.TooltipContent, "A click must dismiss the hover tooltip.");
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, hit.Value.X, hit.Value.Y, () => true);
        ui.Key(TerminalKey.Enter, () => invoked is not null);
        Assert.AreSame(source, invoked!.Source);
    }

    [TestMethod]
    public async Task Tooltip_LeavingRefreshingDisablingAndHiding_DismissesWithoutChangingSelection() {
        var chart = new Chart();
        chart.Values.AddRange([10, 20, 30]);
        var tabs = new TabControl(new TabPage("Chart", chart), new TabPage("Other", new TextBlock("Other")));
        var root = new VStack(new TextBlock("Outside"), tabs) { VerticalAlignment = Align.Stretch };
        tabs.VerticalAlignment = Align.Stretch;
        await using var ui = new DesignerUxTests.UiSession(root, () => { }, 90, 28);
        TooltipHost Host() => chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();

        void Hover() {
            ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2,
                () => Host().TooltipContent is not null);
            for (int tick = 0; tick < 23; tick++) {
                ui.Tick();
            }
            Assert.AreSame(ui.App, Host().TooltipContent!.App);
            Assert.IsNull(chart.Selection, "Hover must not select or invoke a point.");
        }

        Hover();
        var content = Host().TooltipContent!;
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.Right - 2, chart.PlotBounds.Y + 2,
            () => Host().TooltipContent != content);
        Assert.IsNull(content.App);
        var movedContent = Host().TooltipContent!;
        for (int tick = 0; tick < 18; tick++) {
            ui.Tick();
        }
        Assert.IsNull(movedContent.App, "Moving to another point must restart the hover delay.");
        for (int tick = 0; tick < 4; tick++) {
            ui.Tick();
        }
        Assert.AreSame(ui.App, movedContent.App);
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.Right - 2, chart.PlotBounds.Y + 2,
            () => ReferenceEquals(movedContent, Host().TooltipContent));
        ui.Tick();
        Assert.AreSame(ui.App, movedContent.App, "Moving within the same point must not recreate its tooltip.");
        content = movedContent;
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, 0, 0, () => content.App is null);
        Hover();
        content = Host().TooltipContent!;
        chart.Values[0] = 100;
        ui.Tick();
        Assert.IsNull(content.App);
        Assert.IsNull(Host().TooltipContent);
        Hover();
        content = Host().TooltipContent!;
        chart.Interactive = false;
        ui.Tick();
        Assert.IsNull(content.App);
        Assert.IsNull(Host().TooltipContent);
        chart.Interactive = true;
        ui.Tick();
        Hover();
        content = Host().TooltipContent!;
        chart.ShowTooltips = false;
        Assert.IsNull(content.App);
        Assert.IsNull(Host().TooltipContent);
        chart.ShowTooltips = true;
        ui.Tick();
        Hover();
        content = Host().TooltipContent!;
        tabs.SelectedIndex = 1;
        ui.Tick();
        Assert.IsNull(content.App, "A tooltip must not remain over a hidden tab.");
    }

    [TestMethod]
    [DataRow(GraphKind.Histogram)]
    [DataRow(GraphKind.BoxPlot)]
    public async Task DistributionHover_WithHiddenSeries_ShowsTheVisibleSeriesStatistics(GraphKind kind) {
        var chart = new Chart { GraphKind = kind };
        chart.Series.Add(new("Samples", [99]) { Visible = false });
        chart.Series.Add(new("Samples", [1, 2, 2, 4, 8]));
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 90, 28);
        var bounds = chart.PlotBounds;
        int x = bounds.X + (bounds.Width - 1) / 7, y = bounds.Y + bounds.Height / 2;
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, x, y,
            () => chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single().TooltipContent is not null);
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        var text = ((Paragraph)host.TooltipContent!).Text;
        Assert.IsNotNull(text);
        if (kind == GraphKind.Histogram) {
            Assert.Contains("Observation: 2", text);
            Assert.Contains("Count: 2", text);
        }
        else {
            Assert.Contains("Count: 5", text);
            Assert.Contains("Minimum: 1", text);
            Assert.Contains("Maximum: 8", text);
            Assert.Contains("Median: 2", text);
            Assert.Contains("Q1: 2", text);
            Assert.Contains("Q3: 4", text);
        }
    }

    [TestMethod]
    public async Task BoxPlot_ClickingAnotherLane_SelectsThatSeries() {
        var chart = new Chart { GraphKind = GraphKind.BoxPlot };
        chart.Series.Add(new("First", [10, 20, 30]));
        chart.Series.Add(new("Second", [10, 20, 30]));
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 90, 28);
        var bounds = chart.PlotBounds;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2 + bounds.Height / 4, () => chart.Selection is not null);
        Assert.AreEqual("Second", chart.Selection!.SeriesName);
    }

    [TestMethod]
    [DataRow(GraphKind.Flamegraph)]
    [DataRow(GraphKind.Gantt)]
    public async Task StructuredCharts_SampledUnchangedData_RetainsHoverAndSourceInteractions(GraphKind kind) {
        var source = new System.Management.Automation.PSObject();
        void Set(string name, object? value) => source.Properties.Add(new System.Management.Automation.PSNoteProperty(name, value));
        Set("FrameId", "root");
        Set("ParentId", null);
        Set("Name", "Work");
        Set("Weight", 50d);
        Set("Start", DateTimeOffset.UnixEpoch);
        Set("End", DateTimeOffset.UnixEpoch.AddHours(1));
        Set("Progress", .5);
        var document = new TuiDocument {
            Root = new() {
                Id = "chart", Type = "Chart", Bind = "rows", GraphKind = kind.ToString(),
                FrameNameProperty = "Name", TaskNameProperty = "Name", ProgressProperty = "Progress"
            }
        };
        var events = new List<TuiEvent>();
        using var app = new CustomTuiApp(document, new Hashtable { ["rows"] = new[] { source } },
            handler: (evt, _) => { events.Add(evt); return null; }, tickInterval: 1);
        var chart = (Chart)app.Controls["chart"];
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => {
            // Advance the real sampling clock on every UI frame, including the hover delay.
            Thread.Sleep(2);
            _ = app.Tick();
        }, 90, 28);
        var bounds = chart.PlotBounds;
        var hit = (from y in Enumerable.Range(bounds.Y, bounds.Height)
                   from x in Enumerable.Range(bounds.X, bounds.Width)
                   where chart.Pick(x, y) >= 0
                   select (X: x, Y: y)).First();
        long version = chart.DataVersion;
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, hit.X, hit.Y,
            () => chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single().TooltipContent is not null);
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        for (int tick = 0; tick < 25; tick++) {
            ui.Tick();
        }
        Assert.AreEqual(version, chart.DataVersion, "Unchanged sampled data must not invalidate chart interactions.");
        Assert.AreSame(ui.App, host.TooltipContent?.App, "Sampling must not cancel the 200 ms hover delay.");
        var content = host.TooltipContent!;
        source.Properties[kind == GraphKind.Flamegraph ? "Weight" : "Progress"].Value = kind == GraphKind.Flamegraph ? 75d : .75;
        ui.Tick();
        Assert.IsGreaterThan(version, chart.DataVersion, "In-place source changes must still refresh the chart.");
        Assert.IsNull(content.App);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, hit.X, hit.Y,
            () => events.Any(evt => evt.Kind == "Selection"));
        Assert.AreSame(source, events.Last(evt => evt.Kind == "Selection").Value);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, hit.X, hit.Y, () => true);
        ui.Key(TerminalKey.Enter, () => events.Any(evt => evt.Kind == "Invoke"));
        Assert.AreSame(source, events.Last(evt => evt.Kind == "Invoke").Value);
    }

    [TestMethod]
    public async Task NumericCollection_Sampling_RetainsHoverUntilValuesChange() {
        var values = new[] { 10d, double.NaN, 30d };
        var document = new TuiDocument {
            Root = new() { Id = "chart", Type = "Chart", Bind = "values" }
        };
        using var app = new CustomTuiApp(document, new Hashtable { ["values"] = values }, tickInterval: 1);
        var chart = (Chart)app.Controls["chart"];
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => {
            Thread.Sleep(2);
            _ = app.Tick();
        }, 90, 28);
        long version = chart.DataVersion;
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2,
            () => chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single().TooltipContent is not null);
        var host = chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single();
        for (int tick = 0; tick < 25; tick++) {
            ui.Tick();
        }
        Assert.AreEqual(version, chart.DataVersion);
        Assert.AreSame(ui.App, host.TooltipContent?.App);
        var content = host.TooltipContent!;
        values[0] = 100;
        ui.Tick();
        Assert.IsGreaterThan(version, chart.DataVersion);
        Assert.IsNull(content.App);
        Assert.AreEqual(100d, chart.Values[0]);
    }

    [TestMethod]
    public async Task NumericCollection_SelectionAndInvocation_PublishTheValue() {
        var events = new List<TuiEvent>();
        var document = new TuiDocument {
            Root = new() { Id = "chart", Type = "Chart", Bind = "values", SelectionBind = "selected", ShowTooltips = false }
        };
        var copy = TuiDataFile.Parse(TuiDataFile.Generate(TuiDocument.FromJson(document.ToJson())));
        using var app = new CustomTuiApp(copy, new Hashtable { ["values"] = new[] { 10d, 20d, 30d } },
            handler: (evt, _) => { events.Add(evt); return null; });
        var chart = (Chart)app.Controls["chart"];
        Assert.IsFalse(chart.ShowTooltips);
        var restored = new Chart();
        restored.ImportSettings(chart.ExportSettings());
        Assert.IsFalse(restored.ShowTooltips);
        restored.ImportSettings("{}");
        Assert.IsTrue(restored.ShowTooltips, "Old settings without this option should retain the default.");
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => _ = app.Tick(), 90, 28);
        ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2, () => true);
        for (int tick = 0; tick < 25; tick++) {
            ui.Tick();
        }
        Assert.IsNull(chart.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Single().TooltipContent);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2,
            () => app.State.ContainsKey("selected"));
        Assert.AreEqual(chart.Selection!.Y, app.State["selected"]);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, chart.PlotBounds.X + 2, chart.PlotBounds.Y + 2, () => true);
        ui.Key(TerminalKey.Enter, () => events.Any(evt => evt.Kind == "Invoke"));
        Assert.AreEqual(chart.Selection.Y, events.Single(evt => evt.Kind == "Selection").Value);
        Assert.AreEqual(chart.Selection.Y, events.Single(evt => evt.Kind == "Invoke").Value);
    }
}
