using System.Collections;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CommandTreeTests {
    [TestMethod]
    public async Task SharedRootCommandWorksWithMenuOnlyFocus() {
        var document = TuiDocument.FromJson("""{"Commands":[{"Id":"help","Label":"Help","Gesture":"Ctrl+H"}],"Root":{"Id":"menu","Type":"MenuBar","CommandIds":["help"]}}""");
        int calls = 0;
        using var application = new CustomTuiApp(document, handler: (evt, _) => {
            if (evt.Id == "help" && evt.Kind == "Command") {
                calls++;
            }
            return null;
        });
        await using var ui = new DesignerUxTests.UiSession(application.Root, () => application.Tick());
        ui.App.Focus(application.Controls["menu"].EnumerateVisualsDepthFirst().First(visual => visual.Focusable));
        ui.Tick();
        Assert.IsTrue(application.Controls["menu"].Commands.Single(command => command.Id == "help").CanExecute!(application.Root));
        ui.Key(TerminalKey.Unknown, () => calls == 1, TerminalModifiers.Ctrl, TerminalChar.CtrlH);
    }

    [TestMethod]
    public async Task SharedCommandUsesStateScopeMenuAndShortcutWithoutLeakingIntoDialogs() {
        var document = TuiDocument.FromJson("""
            {"Commands":[{"Id":"save","Label":"Save draft","Gesture":"Ctrl+S","Scope":"editor","EnabledBind":"dirty"}],
             "Root":{"Id":"body","Type":"VStack","Children":[
                {"Id":"menu","Type":"MenuBar","CommandIds":["save"]},
                {"Id":"editor","Type":"TextBox","Text":"Draft"},
                {"Id":"other","Type":"TextBox","Text":"Other"},
                {"Id":"palette","Type":"CommandPalette","Bind":"paletteOpen"},
                {"Id":"bar","Type":"CommandBar"}]}}
            """);
        var events = new List<TuiEvent>();
        using var application = new CustomTuiApp(document, new Hashtable { ["dirty"] = true }, handler: (evt, _) => {
            events.Add(evt);
            return null;
        });
        await using var ui = new DesignerUxTests.UiSession(application.Root, () => application.Tick());
        var editor = application.Controls["editor"];
        var command = editor.Commands.Single(item => item.Id == "save");
        var menu = (MenuBar)application.Controls["menu"];
        ui.App.Focus(editor);
        ui.Tick();
        Assert.IsTrue(command.CanExecute!(editor));
        Assert.Contains("Ctrl+S Save draft", ui.Output);
        ui.Key(TerminalKey.Unknown, () => events.Any(evt => evt.Id == "save" && evt.Kind == "Command"), TerminalModifiers.Ctrl, TerminalChar.CtrlS);
        int count = events.Count(evt => evt.Kind == "Command");
        Assert.AreSame(command, menu.Items[0].Command);
        menu.Items[0].Command!.Execute!(editor);
        ui.Tick();
        Assert.AreEqual(count + 1, events.Count(evt => evt.Kind == "Command"));
        application.State["paletteOpen"] = true;
        ui.Tick();
        ui.Tick();
        ui.Text("Save draft", () => ((CommandPalette)application.Controls["palette"]).QueryText == "Save draft");
        var paletteList = application.Controls["palette"].EnumerateVisualsDepthFirst().Single(visual => visual.GetType().IsGenericType && visual.GetType().GetGenericTypeDefinition() == typeof(OptionList<>));
        int ResultCount() {
            object items = paletteList.GetType().GetProperty("Items")!.GetValue(paletteList)!;
            return (int)items.GetType().GetProperty("Count")!.GetValue(items)!;
        }
        Assert.AreEqual(1, ResultCount());
        application.State["dirty"] = false;
        ui.Tick();
        ui.Tick();
        Assert.AreEqual(0, ResultCount(), "An open palette must refresh when state gates change without changing the query.");
        application.State["dirty"] = true;
        ui.Tick();
        ui.Tick();
        Assert.AreEqual(1, ResultCount());
        ui.Key(TerminalKey.Enter, () => events.Count(evt => evt.Kind == "Command") == count + 2);
        ui.Tick();
        application.State["dirty"] = false;
        ui.Tick();
        Assert.IsFalse(command.CanExecute!(editor));
        command.Execute!(editor);
        ui.Tick();
        Assert.AreEqual(count + 2, events.Count(evt => evt.Kind == "Command"));
        var footer = application.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(text => text.Text?.EndsWith("F12 diagnostics", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("Save draft", footer.Text!);
        application.State["dirty"] = true;
        ui.App.Focus(application.Controls["other"]);
        ui.Tick();
        Assert.IsFalse(command.IsVisible!(editor));
        ui.App.Focus(editor);
        ui.Tick();
        var modalEditor = new TextBox("Modal draft");
        var dialog = new Dialog { Content = modalEditor, IsModal = true, Width = 40, Height = 6 };
        dialog.Show();
        ui.Tick();
        ui.App.Focus(modalEditor);
        ui.Tick();
        Assert.IsFalse(command.CanExecute!(editor));
        Assert.DoesNotContain("Save draft", footer.Text!);
        dialog.Close();
        Assert.AreEqual("Draft", ((TextBox)editor).Text);
    }

    private sealed class LazyRow(string name, bool hasChildren) {
        public string Name { get; } = name;
        public bool HasChildren { get; } = hasChildren;
        public object Children => throw new InvalidOperationException($"Lazy trees must never inspect {Name}'s children.");
    }

    [TestMethod]
    public async Task LazyTreeRequestsOnlyExpandedBranchesCachesChildrenAndAcceptsEmptyLeaves() {
        var root = new LazyRow("Root", true);
        var branch = new LazyRow("Branch", true);
        var empty = new LazyRow("Empty", true);
        var requests = new List<object?>();
        var document = TuiDocument.FromJson("""{"Root":{"Id":"tree","Type":"TreeView","Bind":"rows","Lazy":true,"SelectionBind":"selected"}}""");
        using var application = new CustomTuiApp(document, new Hashtable { ["rows"] = new[] { root, empty } }, handler: (evt, _) => {
            if (evt.Kind != "Expand") {
                return null;
            }
            requests.Add(evt.Value);
            return new TuiReply(Value: ReferenceEquals(evt.Value, root) ? new[] { branch } : ReferenceEquals(evt.Value, branch) ? new[] { new LazyRow("Leaf", false) } : Array.Empty<object>());
        });
        await using var ui = new DesignerUxTests.UiSession(application.Root, () => application.Tick());
        var tree = (TreeView)application.Controls["tree"];
        Assert.IsEmpty(requests);
        var first = tree.Roots[0];
        ui.App.Focus(tree);
        ui.Key(TerminalKey.Right, () => first.IsExpanded && first.Children[0].Data == branch);
        Assert.HasCount(1, requests);
        Assert.AreSame(root, requests[0]);
        ui.Key(TerminalKey.Left, () => !first.IsExpanded);
        ui.Key(TerminalKey.Right, () => first.IsExpanded);
        Assert.HasCount(1, requests, "Reopening a loaded branch must use its cached children.");
        ui.Key(TerminalKey.Down, () => ReferenceEquals(tree.SelectedNode?.Data, branch));
        ui.Key(TerminalKey.Right, () => first.Children[0].Children[0].Data is LazyRow row && row.Name == "Leaf");
        Assert.HasCount(2, requests);
        Assert.AreSame(branch, requests[1]);
        tree.TrySelectNode(tree.Roots[1]);
        ui.Tick();
        ui.Key(TerminalKey.Right, () => tree.Roots[1].Children.Count == 0);
        Assert.HasCount(3, requests);
        Assert.AreSame(empty, application.State["selected"]);
        tree.Roots[1].IsExpanded = false;
        tree.Roots[1].IsExpanded = true;
        ui.Tick();
        Assert.HasCount(3, requests);
        application.State["rows"] = new[] { new LazyRow("Replacement", false) };
        ui.Tick();
        Assert.HasCount(1, tree.Roots);
        Assert.AreEqual("Replacement", ((LazyRow)tree.Roots[0].Data!).Name);
        Assert.HasCount(3, requests);
    }

    [TestMethod]
    public void SharedCommandsAndLazyTreeRoundTripAndRejectInvalidDefinitions() {
        var document = TuiDocument.FromJson("""{"Commands":[{"Id":"save","Label":"Save","Gesture":"Ctrl+S"}],"Root":{"Id":"menu","Type":"MenuBar","CommandIds":["save"]}}""");
        var copy = TuiDataFile.Parse(TuiDataFile.Generate(document, compact: true));
        Assert.AreEqual("Ctrl+S", copy.Commands.Single().Gesture);
        CollectionAssert.AreEqual(new[] { "save" }, copy.Root.CommandIds);
        Assert.Contains("'save:Command'", TuiScriptExporter.Generate(copy));
        foreach (string json in new[] {
            """{"Commands":[{"Id":"a","Label":"A","Gesture":"F12"}],"Root":{"Id":"root","Type":"Text"}}""",
            """{"Commands":[{"Id":"a","Label":"A","Gesture":"nonsense"}],"Root":{"Id":"root","Type":"Text"}}""",
            """{"Commands":[{"Id":"a","Label":"A","Scope":"missing"}],"Root":{"Id":"root","Type":"Text"}}""",
            """{"Commands":[{"Id":"a","Label":"A","Gesture":"Ctrl+S"},{"Id":"b","Label":"B","Gesture":"Ctrl+S"}],"Root":{"Id":"root","Type":"Text"}}""",
            """{"Root":{"Id":"root","Type":"MenuBar","CommandIds":["missing"]}}"""
        }) {
            Assert.ThrowsExactly<ArgumentException>(() => TuiDocument.FromJson(json), json);
        }
    }

    [TestMethod]
    public void SharedChartRangeSynchronizesStateAndOtherChartsWithoutCallbackEcho() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"body","Type":"VStack","Children":[{"Id":"first","Type":"Chart","RangeBind":"window"},{"Id":"second","Type":"Chart","RangeBind":"window"}]}}""");
        var events = new List<TuiEvent>();
        using var application = new CustomTuiApp(document, new Hashtable { ["window"] = new Hashtable { ["MinimumX"] = 2, ["MaximumX"] = 7 } },
            handler: (evt, _) => {
                if (evt.Kind == "Range") {
                    events.Add(evt);
                }
                return null;
            });
        application.Tick();
        var first = ((fullscreen.Charts.HistoryChart)application.Controls["first"]).Plot;
        var second = ((fullscreen.Charts.HistoryChart)application.Controls["second"]).Plot;
        Assert.AreEqual(new fullscreen.Charts.ChartSelectedRange(2, 7), first.SelectedRange);
        Assert.AreEqual(first.SelectedRange, second.SelectedRange);
        Assert.IsEmpty(events);
        application.Tick();
        ((Hashtable)application.State["window"]!)["MaximumX"] = 8;
        application.Tick();
        Assert.AreEqual(new fullscreen.Charts.ChartSelectedRange(2, 8), first.SelectedRange);
        Assert.AreEqual(first.SelectedRange, second.SelectedRange);
        Assert.IsEmpty(events);
        first.SelectRange(new(3, 5));
        application.Tick();
        Assert.HasCount(1, events);
        Assert.AreEqual(first.SelectedRange, events[0].Value);
        Assert.AreEqual(first.SelectedRange, application.State["window"]);
        Assert.AreEqual(first.SelectedRange, second.SelectedRange);
        application.State["window"] = null;
        application.Tick();
        Assert.IsNull(first.SelectedRange);
        Assert.IsNull(second.SelectedRange);
        Assert.HasCount(1, events);
        application.State["window"] = new Hashtable { ["MinimumX"] = 9, ["MaximumX"] = 2 };
        Assert.ThrowsExactly<ArgumentException>(() => application.Tick());
        Assert.IsNull(first.SelectedRange, "Invalid input must not replace the previous range.");
    }

    [TestMethod]
    public void LazyExpansionRejectsCyclesAndOverCapacityAtomicallyAndAllowsRetry() {
        var root = new LazyRow("Root", true);
        var leaf = new LazyRow("Leaf", false);
        var tree = new TreeView();
        var node = new TuiNode { Id = "tree", Type = "TreeView", Lazy = true };
        NativeTuiControls.Update(tree, node, new[] { root });
        tree.Roots[0].IsExpanded = true;
        TuiEvent? pending = null;
        void Request() => NativeTuiControls.ExpandTree(tree, "tree", value => {
            pending = value;
            return true;
        });
        Request();
        Assert.ThrowsExactly<ArgumentException>(() => pending!.Complete!(new[] { root }));
        Assert.IsNull(tree.Roots[0].Children[0].Data, "Rejected children must leave the original placeholder intact.");
        Request();
        Assert.ThrowsExactly<ArgumentException>(() => pending!.Complete!(Enumerable.Repeat(leaf, 10000)));
        Assert.HasCount(1, tree.Roots[0].Children);
        Request();
        pending!.Complete!(Enumerable.Repeat(leaf, 9999));
        Assert.HasCount(9999, tree.Roots[0].Children, "The root plus 9999 children is exactly the retained-node limit.");
        Assert.AreSame(leaf, tree.Roots[0].Children[^1].Data);
        pending = null;
        Request();
        Assert.IsNull(pending, "Successful results must stay cached.");
    }

    [TestMethod]
    public void LazyExpansionDefersDepthChecksUntilTheRequestedBranchAndPreservesItOnFailure() {
        var tree = new TreeView();
        NativeTuiControls.Update(tree, new TuiNode { Id = "tree", Type = "TreeView", Lazy = true }, new[] { new LazyRow("0", true) });
        var branch = tree.Roots[0];
        for (int depth = 1; depth <= 24; depth++) {
            branch.IsExpanded = true;
            TuiEvent? pending = null;
            NativeTuiControls.ExpandTree(tree, "tree", value => {
                pending = value;
                return true;
            });
            Assert.IsNotNull(pending);
            pending.Complete!(new[] { new LazyRow(depth.ToString(System.Globalization.CultureInfo.InvariantCulture), true) });
            branch = branch.Children[0];
        }
        branch.IsExpanded = true;
        TuiEvent? tooDeep = null;
        NativeTuiControls.ExpandTree(tree, "tree", value => {
            tooDeep = value;
            return true;
        });
        Assert.IsNotNull(tooDeep);
        Assert.ThrowsExactly<ArgumentException>(() => tooDeep.Complete!(new[] { new LazyRow("25", false) }));
        Assert.AreEqual("24", ((LazyRow)branch.Data!).Name);
        Assert.IsNull(branch.Children[0].Data);
    }
}
