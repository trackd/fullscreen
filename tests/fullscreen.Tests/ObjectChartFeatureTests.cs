using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using fullscreen.Charts;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ObjectChartFeatureTests {
    public sealed class MutableRow(string name, double value) {
        public string Name { get; set; } = name;
        public string Group { get; set; } = "A";
        public double Y { get; set; } = value;
    }
    [TestMethod]
    [DataRow("PowerShell")]
    [DataRow("Dictionary")]
    [DataRow("DotNet")]
    public async Task CalculationsGroupingAndFilteringReturnOriginalInstancesWithoutChangingInput(string shape) {
        PSObject Create(string name, double value) =>
            shape switch {
                "Dictionary" => PSObject.AsPSObject(new Hashtable { ["Name"] = name, ["Group"] = "A", ["Y"] = value }),
                "DotNet" => PSObject.AsPSObject(new MutableRow(name, value)),
                _ => Item(name, "A", value)
            };
        PSObject[] originals = [Create("low", 1), Create("high", 3)];
        object[] bases = [.. originals.Select(item => item.BaseObject)];
        string[][] members = [.. originals.Select(item => item.Properties.Select(property => property.Name).ToArray())];
        using var app = new ObjectChartApp(originals, "Name", [
            new Hashtable { ["Name"] = "Y", ["Expression"] = "Y" },
            new Hashtable { ["Name"] = "Count", ["Expression"] = "Y" }
        ]);
        app.Surface.PassThru = true;
        app.SetFilter(ObjectChartFilter.Top, 1, "Y");
        app.SetAggregation("Group", ChartAggregate.Count);
        app.Surface.Plot.SelectRange(new(0, 0));
        var table = app.CreateDataTable();
        Assert.AreSame(originals[1], table.Rows.Single().Original);
        app.SetRowSubset([1]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("q", () => !app.Tick());
        Assert.AreSame(originals[1], app.Chosen.Single());
        for (int index = 0; index < originals.Length; index++) {
            Assert.AreSame(bases[index], originals[index].BaseObject);
            CollectionAssert.AreEqual(members[index], originals[index].Properties.Select(property => property.Name).ToArray());
            Assert.AreEqual(index == 0 ? "low" : "high", ObjectChartData.Read(originals[index], "Name"));
            Assert.AreEqual(index == 0 ? 1d : 3d, ObjectChartData.Read(originals[index], "Y"));
            Assert.AreEqual("A", ObjectChartData.Read(originals[index], "Group"));
            Assert.IsNull(ObjectChartData.Read(originals[index], "Count"));
            if (originals[index].BaseObject is IDictionary dictionary) {
                Assert.HasCount(3, dictionary);
            }
        }
    }
    private static PSObject Item(string name, string? group, double? value) {
        var item = new PSObject();
        item.Properties.Add(new PSNoteProperty("Name", name));
        item.Properties.Add(new PSNoteProperty("Group", group));
        item.Properties.Add(new PSNoteProperty("Y", value));
        return item;
    }
    private sealed class Clock : TimeProvider {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() =>
            _ticks;
        public void Advance(int milliseconds) =>
            _ticks += milliseconds * TimeSpan.TicksPerMillisecond;
    }
    [TestMethod]
    public void GroupedSeriesShareMembershipAndKeepTheirOwnAggregatesAndOriginalObjects() {
        var first = Item("first", "A", 1);
        first.Properties.Add(new PSNoteProperty("Z", 4d));
        var second = Item("second", "A", 3);
        second.Properties.Add(new PSNoteProperty("Z", 6d));
        using var app = new ObjectChartApp([first, second], "Name", ["Y", "Z"]);
        app.SetAggregation("Group", ChartAggregate.Sum);
        var y = app.Surface.Series[0].Points[0];
        var z = app.Surface.Series[1].Points[0];
        Assert.AreEqual(4d, y.Y);
        Assert.AreEqual(10d, z.Y);
        Assert.AreSame(y.Source, z.Source);
        Assert.AreSame(y.SourceIndices, z.SourceIndices);
        var group = Assert.IsInstanceOfType<ChartGroup>(y.Source);
        Assert.AreSame(first, group.Items[0]);
        Assert.AreSame(second, group.Items[1]);
        CollectionAssert.AreEqual(new[] { 0, 1 }, y.SourceIndices!);
        app.SetFilter(ObjectChartFilter.Top, 1, "Y");
        var filtered = app.Surface.Series[0].Points[0];
        Assert.AreEqual(3d, filtered.Y);
        CollectionAssert.AreEqual(new[] { 1 }, filtered.SourceIndices!);
        Assert.AreSame(second, Assert.IsInstanceOfType<ChartGroup>(filtered.Source).Items.Single());
        CollectionAssert.AreEqual(new object[] { first, second }, group.Items.ToArray(), "Filtering must not rewrite an earlier group snapshot.");
        Assert.AreEqual(1d, first.Properties["Y"].Value);
        Assert.AreEqual(3d, second.Properties["Y"].Value);
        Assert.HasCount(4, first.Properties);
        Assert.HasCount(4, second.Properties);
    }
    [TestMethod]
    [DataRow(ChartAggregate.Count, 3d)]
    [DataRow(ChartAggregate.Sum, 3d)]
    [DataRow(ChartAggregate.Average, 1.5d)]
    [DataRow(ChartAggregate.Minimum, 1d)]
    [DataRow(ChartAggregate.Maximum, 2d)]
    public void AggregationKeepsOriginalMembersAndHandlesMissingValues(ChartAggregate aggregate, double expected) {
        var first = Item("first", "A", 1);
        var second = Item("second", "A", 2);
        var missing = Item("missing", "A", null);
        var other = Item("other", "B", 5);
        using var app = new ObjectChartApp([first, second, missing, other], "Name", ["Y"]);
        app.SetAggregation("Group", aggregate);
        Assert.AreEqual(2, app.Surface.Series[0].Count);
        Assert.AreEqual(expected, app.Surface.Series[0].Points[0].Y);
        Assert.AreEqual("A", app.Surface.Series[0].Points[0].Label);
        var group = Assert.IsInstanceOfType<ChartGroup>(app.Surface.Series[0].Points[0].Source);
        CollectionAssert.AreEqual(new object[] { first, second, missing }, group.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, app.Surface.Series[0].Points[0].SourceIndices!);
        app.SetAggregation(null, ChartAggregate.None);
        Assert.AreEqual(4, app.Surface.Series[0].Count);
        Assert.AreSame(first, app.Surface.Series[0].Points[0].Source);
    }
    [TestMethod]
    public async Task GroupRangePassThruReturnsAllUnderlyingOriginalRowsIncludingGaps() {
        var first = Item("first", "A", 1);
        var second = Item("second", "A", null);
        var other = Item("other", "B", 5);
        using var app = new ObjectChartApp([first, second, other], "Name", ["Y"]);
        app.Surface.PassThru = true;
        app.SetAggregation("Group", ChartAggregate.Count);
        app.Surface.Plot.SelectRange(new(0, 0));
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("q", () => !app.Tick());
        var chosen = app.Chosen.ToArray();
        Assert.HasCount(2, chosen);
        Assert.AreSame(first, chosen[0]);
        Assert.AreSame(second, chosen[1]);
        Assert.IsNull(first.Properties["Count"], "Aggregation must not mutate the input objects.");
    }
    [TestMethod]
    public async Task FilteredDataOverlayShowsCalculatedValuesAndCanApplyATableSubset() {
        var low = Item("low", "A", 1);
        var high = Item("high", "A", 3);
        var mid = Item("mid", "B", 2);
        using var app = new ObjectChartApp([low, high, mid], "Name", ["Y", new Hashtable { ["Name"] = "Alias", ["Expression"] = "Y" }]);
        app.SetFilter(ObjectChartFilter.Top, 2);
        app.Surface.PassThru = true;
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("t", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Any());
        var table = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Single();
        Assert.HasCount(2, table.Model.Rows);
        Assert.AreSame(high, table.Model.Rows[0].Original);
        Assert.AreSame(mid, table.Model.Rows[1].Original);
        int calculated = Array.IndexOf(table.Model.Columns, "Alias (custom)");
        Assert.IsGreaterThanOrEqualTo(0, calculated);
        Assert.AreEqual(3d, table.Model.Rows[0].Values[calculated]);
        table.BeginFilter();
        ui.Text("mid", () => table.Model.Visible.Count == 1);
        var use = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock { Text: "Use table rows" });
        ui.Activate(use, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        CollectionAssert.AreEqual(new[] { 2 }, app.Surface.VisibleIndices);
        ui.Text("q", () => !app.Tick());
        Assert.AreSame(mid, app.Chosen.Single());
        Assert.IsNull(mid.Properties["Alias"]);
    }
    [TestMethod]
    public async Task EmptyDataOverlayAndEscapeLeaveTheViewerUsable() {
        using var app = new ObjectChartApp([]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("t", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Any());
        var table = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Single();
        Assert.IsEmpty(table.Model.Rows);
        Assert.Contains("Value", table.Model.Columns);
        ui.Key(TerminalKey.Escape, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsTrue(app.Tick());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => app.SetRowSubset([10]));
        Assert.IsNull(app.RowSubset);
    }
    [TestMethod]
    public void RefreshSchedulingPauseRecoveryAndFailureAreDeterministic() {
        var first = Item("first", "A", 1);
        var next = Item("next", "B", 5);
        using var app = new ObjectChartApp([first], "Name", ["Y"]);
        var clock = new Clock();
        app.Clock = clock;
        app.RefreshInterval = 1000;
        int calls = 0;
        app.RefreshSource = () => { calls++; return [next]; };
        clock.Advance(999);
        Assert.IsTrue(app.Tick());
        Assert.AreEqual(0, calls);
        Assert.AreSame(first, app.Data.Objects[0]);
        clock.Advance(1);
        Assert.IsTrue(app.Tick());
        Assert.AreEqual(1, calls);
        Assert.AreSame(next, app.Data.Objects[0]);
        app.Paused = true;
        clock.Advance(2000);
        app.Tick();
        Assert.AreEqual(1, calls);
        app.Paused = false;
        app.Tick();
        Assert.AreEqual(2, calls);
        app.RefreshSource = () => throw new IOException("fixture offline");
        clock.Advance(1000);
        Assert.IsTrue(app.Tick());
        Assert.IsTrue(app.Paused);
        Assert.AreSame(next, app.Data.Objects[0]);
        app.RefreshSource = () => [first];
        app.Paused = false;
        clock.Advance(1000);
        app.Tick();
        Assert.AreSame(first, app.Data.Objects[0]);
    }
    [TestMethod]
    public void InitiallyEmptyRefreshChoosesANumericDefaultWhenDataArrives() {
        using var app = new ObjectChartApp([]);
        app.ReplaceObjects([Item("new", "A", 7)]);
        CollectionAssert.AreEqual(new[] { "Y" }, app.YProperties);
        Assert.AreEqual(7d, app.Surface.Series[0].Points[0].Y);
    }
    [TestMethod]
    public void HorizontalRangeUsesBarValuesAndKeepsLabelsOnTheCategoryAxis() {
        using var app = new ObjectChartApp([Item("low", "A", 1), Item("high", "A", 50)], "Name", ["Y"], GraphKind.HorizontalBars);
        Assert.AreEqual(ChartScale.Linear, app.Surface.Plot.XAxis.Scale);
        Assert.AreEqual(ChartScale.Category, app.Surface.Plot.YAxis.Scale);
        app.Surface.Plot.SelectRange(new(10, 60));
        CollectionAssert.AreEqual(new[] { 1 }, app.ChosenIndices());
        app.SetAggregation("Group", ChartAggregate.Sum);
        Assert.AreEqual(ChartScale.Linear, app.Surface.Plot.XAxis.Scale);
        app.Surface.Plot.XAxis = new() {
            Scale = ChartScale.Logarithmic
        };
        app.Configure("Name", ["Y"], GraphKind.HorizontalBars);
        Assert.AreEqual(ChartScale.Linear, app.Surface.Plot.XAxis.Scale);
    }
    [TestMethod]
    public async Task DataOverlayFreezesRefreshUntilClosedWithoutLosingTheCurrentSnapshot() {
        var first = Item("first", "A", 1);
        var next = Item("next", "A", 2);
        using var app = new ObjectChartApp([first], "Name", ["Y"]);
        var clock = new Clock();
        app.Clock = clock;
        int calls = 0;
        app.RefreshSource = () => { calls++; return [next]; };
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("t", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Any());
        clock.Advance(2000);
        app.Tick();
        Assert.AreEqual(0, calls);
        var table = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Single();
        Assert.AreSame(first, table.Model.Rows[0].Original);
        ui.Key(TerminalKey.Escape, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        app.Tick();
        Assert.AreEqual(1, calls);
        Assert.AreSame(next, app.Data.Objects[0]);
    }
    [TestMethod]
    public void ConfigurationRoundTripPreservesCalculatedAxesFiltersAndOriginalObjects() {
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        var previous = Runspace.DefaultRunspace;
        Runspace.DefaultRunspace = runspace;
        try {
            var first = Item("first", "A", 1);
            var second = Item("second", "B", 5);
            using var app = new ObjectChartApp([first, second], "Name", [new Hashtable { ["Name"] = "Twice", ["Expression"] = ScriptBlock.Create("$_.Y * 2") }]);
            app.SetFilter(ObjectChartFilter.Top, 1);
            app.Surface.Plot.ShowLegend = true;
            app.Surface.Plot.Thresholds.Add(new(8, "limit"));
            string saved = app.ExportConfiguration();
            Assert.DoesNotContain("\u0000", saved);
            using var restored = new ObjectChartApp([first, second]);
            restored.ImportConfiguration(saved);
            Assert.AreEqual("Twice", restored.Data.Label(restored.YProperties[0]));
            Assert.AreEqual(10d, restored.Surface.Series[0].Points[0].Y);
            CollectionAssert.AreEqual(new[] { 1 }, restored.Surface.VisibleIndices);
            Assert.AreSame(second, restored.Data.Objects[1]);
            Assert.IsNull(second.Properties["Twice"]);
            Assert.IsTrue(restored.Surface.Plot.ShowLegend);
            Assert.AreEqual("limit", restored.Surface.Plot.Thresholds[0].Label);
            string csv = restored.ExportCsv();
            Assert.Contains("Twice (custom)", csv);
            Assert.Contains("second", csv);
            Assert.DoesNotContain("first", csv);
            Assert.ThrowsExactly<ArgumentException>(() => restored.ImportConfiguration(saved.Replace("\"Version\": 1", "\"Version\": 99", StringComparison.Ordinal)));
            Assert.AreEqual(10d, restored.Surface.Series[0].Points[0].Y, "An invalid configuration must leave the current chart intact.");
        }
        finally { Runspace.DefaultRunspace = previous; }
    }
}
