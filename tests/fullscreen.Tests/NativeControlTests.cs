using fullscreen.Custom;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal;
using System.Collections;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeControlTests {
    [TestMethod]
    public void DesignerSearchDefaultsToAnExistingLogAndRejectsMissingTargetsWithoutMutation() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        string before = designer.Document.ToJson();
        designer.Add("SearchReplacePopup");
        Assert.AreEqual(before, designer.Document.ToJson());
        designer.Add("LogControl");
        var log = designer.Document.Root.Children.Single(node => node.Type == "LogControl");
        designer.Add("SearchReplacePopup");
        var search = designer.Document.Root.Children.Single(node => node.Type == "SearchReplacePopup");
        Assert.AreEqual(log.Id, search.Target);
        designer.Document.Validate();
    }
    [TestMethod]
    [DataRow("Popup")]
    [DataRow("CommandPalette")]
    [DataRow("SearchReplacePopup")]
    [DataRow("Toast")]
    public async Task NativeOverlaysKeepBooleanStateInSyncAndCloseWithoutClosingTheApplication(string type) {
        var overlay = new TuiNode { Id = "overlay", Type = type, Text = "Overlay content", Bind = "open", Items = type == "CommandPalette" ? ["Build project"] : [] };
        if (type == "SearchReplacePopup") {
            overlay.Target = "logs";
            overlay.Options["SearchText"] = System.Text.Json.JsonSerializer.SerializeToElement("Ready");
        }
        var definition = new TuiDocument { Root = new() { Id = "root", Type = "VStack", Children = [new() { Id = "anchor", Type = "TextBox", Text = "Keep focus" }, overlay, new() { Id = "logs", Type = "LogControl", Items = ["Ready", "Running"] }] } };
        using var app = new CustomTuiApp(definition, new Hashtable { ["open"] = false });
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 130, 40);
        ui.App.Focus(app.Controls["anchor"]);
        app.State["open"] = true;
        ui.Tick();
        ui.Tick();
        ui.Tick();
        Assert.IsTrue((bool)app.State["open"]!);
        if (type == "CommandPalette") {
            Assert.Contains("Build project", ui.Output);
        }
        if (type == "SearchReplacePopup") {
            Assert.AreEqual(1, ((LogControl)app.Controls["logs"]).MatchCount);
        }
        if (type == "Popup") {
            ui.Key(TerminalKey.Escape, () => app.Controls["overlay"].App is null);
            ui.Tick();
        }
        else {
            app.State["open"] = false;
            ui.Tick();
        }
        Assert.IsFalse((bool)app.State["open"]!);
        Assert.IsTrue(app.Tick());
        app.State["open"] = true;
        ui.Tick();
        ui.Tick();
        ui.Key(TerminalKey.F10, () => !app.Tick());
    }
    [TestMethod]
    public async Task NativeGridSortKeepsObjectIdentityAndPublishesEditsWithoutMutatingCallerRecords() {
        var first = new {
            Label = "two",
            Value = 2
        };
        var second = new {
            Label = "ten",
            Value = 10
        };
        var definition = TuiDocument.FromJson("""{"Root":{"Id":"grid","Type":"DataGridControl","Bind":"rows","Columns":["Label","Value"],"SelectionBind":"selected","Options":{"ReadOnly":false}}}""");
        TuiGridEdit? edit = null;
        using var app = new CustomTuiApp(definition, new Hashtable { ["rows"] = new[] { first, second } }, handler: (ev, _) => { if (ev.Kind == "Edit") { edit = (TuiGridEdit)ev.Value!; } return null; });
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 130, 40);
        var grid = (DataGridControl)app.Controls["grid"];
        Assert.IsFalse(grid.ReadOnly);
        Assert.IsTrue(grid.TrySetColumnSortDirection("Value", XenoAtom.Terminal.UI.DataGrid.DataGridSortDirection.Descending, true));
        ui.Tick();
        grid.SelectedRow = 0;
        ui.Tick();
        Assert.AreSame(second, app.State["selected"]);
        var document = (XenoAtom.Terminal.UI.DataGrid.DataGridDataTableDocument)grid.Document!;
        document.Table.Rows[0]["Value"] = 7;
        ui.Tick();
        Assert.IsNotNull(edit);
        Assert.AreSame(first, edit.Original);
        Assert.AreEqual(7, edit.Values["Value"]);
        Assert.AreEqual(2, first.Value);
    }
    [TestMethod]
    public void TreeBindingPreservesNestedLabelsAndRejectsCyclesAndOversizedCollections() {
        var leaf = new Hashtable { ["Name"] = "replica-1" };
        var root = new Hashtable { ["Name"] = "API", ["Children"] = new[] { leaf } };
        var definition = TuiDocument.FromJson("""{"Root":{"Id":"tree","Type":"TreeView","Bind":"nodes","SelectionBind":"selected"}}""");
        using (var app = new CustomTuiApp(definition, new Hashtable { ["nodes"] = new[] { root } })) {
            var tree = (TreeView)app.Controls["tree"];
            Assert.AreEqual("API", ((TextBlock)tree.Roots[0].Header!).Text);
            Assert.AreEqual("replica-1", ((TextBlock)tree.Roots[0].Children[0].Header!).Text);
            tree.Roots[0].IsExpanded = true;
            VisualSnapshotRenderer.Render(app.Root, 130, 40);
            tree.SelectedIndex = 1;
            app.Tick();
            Assert.AreSame(leaf, app.State["selected"]);
        }
        root["Children"] = new[] { root };
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(definition, new Hashtable { ["nodes"] = new[] { root } }));
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(definition, new Hashtable { ["nodes"] = Enumerable.Range(0, 10001).ToArray() }));
    }
    [TestMethod]
    public void NativeGridPreservesCellPositionsSizingAndOptionsThroughSerialization() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"grid","Type":"Grid","Options":{"Rows":["Auto","*"],"Columns":["2*","*"],"RowGap":1,"Padding":[1,2]},"Children":[{"Id":"cell","Type":"GridCell","Options":{"Row":1,"Column":1},"Children":[{"Id":"text","Type":"Text","Text":"Grid content"}]}]}}""");
        using var app = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), preview: true);
        var grid = Assert.IsInstanceOfType<Grid>(app.Controls["grid"]);
        Assert.AreEqual(2, grid.RowDefinitions.Count);
        Assert.AreEqual(2, grid.ColumnDefinitions.Count);
        Assert.AreEqual(1, grid.Cells.Single().Row);
        Assert.AreEqual(1, grid.Cells.Single().Column);
        Assert.AreEqual(1, grid.RowGap);
        Assert.AreEqual(2, grid.Padding.Top);
        string snapshot = string.Join('\n', VisualSnapshotRenderer.Render(app.Root, 130, 40).ToMarkupLines());
        Assert.Contains("Grid content", snapshot);
    }
    [TestMethod]
    public void NativeDataAndChartsLoadMeaningfulValuesAndClearReplacedSnapshots() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"bars","Type":"BarChart","Bind":"metrics"},{"Id":"breakdown","Type":"BreakdownChart","Bind":"metrics"},{"Id":"tasks","Type":"ProgressTaskGroup","Bind":"metrics"},{"Id":"line","Type":"LineChart","Bind":"series"},{"Id":"logs","Type":"LogControl","Bind":"lines"},{"Id":"flow","Type":"DocumentFlow","Bind":"lines"},{"Id":"table","Type":"Table","Bind":"metrics","Columns":["Label","Value"]},{"Id":"data","Type":"DataGridControl","Bind":"metrics","Columns":["Label","Value"],"SelectionBind":"selected"}]}}""");
        object[] metrics = [new { Label = "api", Value = 65.0 }, new { Label = "worker", Value = 35.0 }];
        using var app = new CustomTuiApp(document, new Hashtable { ["metrics"] = metrics, ["series"] = new[] { 10.0, 25.0, 18.0 }, ["lines"] = new[] { "Ready", "Serving requests" } });
        Assert.AreEqual(65, ((BarChart)app.Controls["bars"]).Items[0].Value);
        Assert.AreEqual(2, ((BreakdownChart)app.Controls["breakdown"]).Segments.Count);
        Assert.AreEqual(35, ((ProgressTaskGroup)app.Controls["tasks"]).Tasks[1].Value);
        Assert.AreEqual(3, ((LineChart)app.Controls["line"]).Values.Count);
        Assert.AreEqual(2, ((LogControl)app.Controls["logs"]).Count);
        Assert.AreEqual(2, ((DocumentFlow)app.Controls["flow"]).Items.Count);
        Assert.AreEqual(2, ((Table)app.Controls["table"]).RowCells.Count);
        Assert.AreEqual(2, ((DataGridControl)app.Controls["data"]).Document!.CurrentSnapshot.RowCount);
        ((DataGridControl)app.Controls["data"]).SelectedRow = 1;
        app.Tick();
        Assert.AreSame(metrics[1], app.State["selected"]);
        app.State["metrics"] = Array.Empty<object>();
        app.State["series"] = Array.Empty<double>();
        app.State["lines"] = Array.Empty<string>();
        app.Tick();
        Assert.AreEqual(0, ((BarChart)app.Controls["bars"]).Items.Count);
        Assert.AreEqual(0, ((LogControl)app.Controls["logs"]).Count);
        Assert.AreEqual(0, ((DataGridControl)app.Controls["data"]).Document!.CurrentSnapshot.RowCount);
    }
    [TestMethod]
    public async Task NativeInputUpdatesStateAndUsesSelectedEnumTypeWithRealKeys() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"slider","Type":"Slider","Bind":"amount","Options":{"Minimum":0,"Maximum":100,"Step":5}},{"Id":"number","Type":"NumberBox","Bind":"amount"},{"Id":"switch","Type":"Switch","Text":"Enabled","Bind":"enabled"},{"Id":"enum","Type":"EnumSelect","EnumType":"Orientation","Bind":"orientation"}]}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["amount"] = 20.0, ["enabled"] = false, ["orientation"] = "Vertical" });
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 130, 40);
        ui.App.Focus(app.Controls["slider"]);
        ui.Key(TerminalKey.Right, () => Equals(app.State["amount"], 25.0));
        Assert.AreEqual(25, ((NumberBox<double>)app.Controls["number"]).Value);
        ui.App.Focus(app.Controls["switch"]);
        ui.Key(TerminalKey.Space, () => app.State["enabled"] is true);
        Assert.AreEqual(Orientation.Vertical, ((EnumSelect<Orientation>)app.Controls["enum"]).Value);
    }
    [TestMethod]
    public async Task NativeDialogOpensClosesAndReopensFromBoundStateWithoutLeavingTheApp() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"name","Type":"TextBox","Text":"Keep me"},{"Id":"dialog","Type":"Dialog","Text":"Native dialog","Bind":"open","Children":[{"Id":"body","Type":"Text","Text":"Dialog content"}]}]}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["open"] = false });
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 130, 40);
        app.State["open"] = true;
        ui.Tick();
        ui.Tick();
        Assert.IsNotNull(app.Controls["dialog"].App);
        Assert.Contains("Dialog content", ui.Output);
        ui.Key(TerminalKey.Escape, () => app.Controls["dialog"].App is null);
        ui.Tick();
        Assert.IsFalse((bool)app.State["open"]!);
        Assert.IsTrue(app.Tick());
        app.State["open"] = true;
        ui.Tick();
        ui.Tick();
        Assert.IsNotNull(app.Controls["dialog"].App);
        app.State["open"] = false;
        ui.Tick();
        Assert.IsNull(app.Controls["dialog"].App);
    }
    public static IEnumerable<object[]> NativeWidgets() =>
        NativeTuiControls.Names.Select(name => new object[] { name });
    [TestMethod]
    public void NativeColorsRoundTripAsHexValuesAndPreserveAlphaAcrossTicks() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"picker","Type":"ColorPicker","Bind":"color","Options":{"AllowAlpha":true}}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["color"] = "#4DA6FF80" });
        app.Tick();
        app.Tick();
        var picker = (ColorPicker)app.Controls["picker"];
        Assert.AreEqual(128, picker.Value.A);
        Assert.AreEqual("#4da6ff80", ((string)app.State["color"]!).ToLowerInvariant());
        app.State["color"] = "#123456";
        app.Tick();
        app.Tick();
        Assert.AreEqual(255, picker.Value.A);
        Assert.AreEqual("#123456", ((string)app.State["color"]!).ToLowerInvariant());
    }
    [TestMethod]
    [DynamicData(nameof(NativeWidgets))]
    public void NativeWidgetUsesItsActualFrameworkTypeAndCanRenderWithoutDependencies(string name) {
        var node = new TuiNode { Id = "widget", Type = name, Text = "Example" };
        var definition = new TuiDocument { Root = node };
        if (name == "SearchReplacePopup") {
            node.Target = "logs";
            definition.Root = new() {
                Id = "root",
                Type = "VStack",
                Children = [node, new() { Id = "logs", Type = "LogControl", Items = ["Example log"] }]
            };
        }
        definition.Validate();
        using var app = new CustomTuiApp(definition, preview: true);
        Assert.AreEqual(NativeTuiControls.TypeOf(name), app.Controls["widget"].GetType());
        VisualSnapshotRenderer.Render(app.Root, 130, 40);
        app.Tick();
    }
}
