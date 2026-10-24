using System.Diagnostics;
using System.Management.Automation;
using System.Reflection;
using fullscreen.Objects;
using fullscreen.Tables;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ObjectNavigationTests {
    private sealed record Item(string Name, int Count);

    [TestMethod]
    public void PropertyRowsUseThemeColorsForNamesStringsNumbersBooleansNullsAndObjects() {
        var model = ObjectSnapshot.Inspect(new {
            Text = "hello",
            Number = 42,
            Flag = true,
            Nothing = (object?)null,
            Items = new[] { 1, 2 },
            Explicit = "\u001b[38;2;255;0;0mred\u001b[0m"
        });
        var surface = new TableSurface(model, "Property colors");
        ThemeCatalog.FromDictionary(new System.Collections.Hashtable {
            ["Accent"] = "#112233",
            ["Success"] = "#445566",
            ["Purple"] = "#778899",
            ["Warning"] = "#AABBCC",
            ["Selection"] = "#0D1420"
        }).Apply(surface);
        var lines = VisualSnapshotRenderer.Render(surface, 120, 35).ToMarkupLines();
        Assert.Contains("[#112233 on #0d1420]Text", lines[4]);
        Assert.Contains("[#445566 on #0d1420]hello", lines[4]);
        Assert.Contains("[#778899 on #0d1420]42", lines[5]);
        Assert.Contains("[#aabbcc on #0d1420]True", lines[6]);
        Assert.Contains("[#aabbcc on #0d1420]null", lines[7]);
        Assert.Contains("[#112233 on #0d1420]System.Int32[[]]", lines[8]);
        Assert.Contains("[#ff0000 on #0d1420]red", lines[9]);
        Assert.IsNull(model.Rows[3].Values[1]);
        model.Filter = "null";
        model.Rebuild();
        Assert.HasCount(1, model.Visible);
        Assert.AreEqual("Nothing", model.Current!.Cells[0]);
    }

    [TestMethod]
    public void ExpandedArrayColumnsColorValuesByTheirActualType() {
        var model = ObjectSnapshot.Create([PSObject.AsPSObject(new { Name = "item", Count = 42, Ready = false })]);
        var surface = new TableSurface(model, "Typed columns");
        ThemeCatalog.FromDictionary(new System.Collections.Hashtable {
            ["Success"] = "#445566",
            ["Purple"] = "#778899",
            ["Warning"] = "#AABBCC",
            ["Selection"] = "#0D1420"
        }).Apply(surface);
        string line = VisualSnapshotRenderer.Render(surface, 120, 35).ToMarkupLines()[4];
        Assert.Contains("[#445566 on #0d1420]item", line);
        Assert.Contains("[#778899 on #0d1420]42", line);
        Assert.Contains("[#aabbcc on #0d1420]False", line);
        Assert.AreEqual(42, model.Rows[0].Values[1]);
    }

    [TestMethod]
    public void JsonBracesAndBracketsUseAccentWithoutRecoloringEscapedStrings() {
        var surface = new TableSurface(new TableModel(["Value"]), "JSON colors");
        ThemeCatalog.FromDictionary(new System.Collections.Hashtable {
            ["Accent"] = "#112233",
            ["Success"] = "#445566",
            ["Purple"] = "#778899"
        }).Apply(surface);
        surface.SetDocument("""{"items":[{"text":"escaped \" [ ] { }","n":-1.25e-3}]}""");
        string line = VisualSnapshotRenderer.Render(surface, 120, 35).ToMarkupLines()[3];
        Assert.Contains("[#112233 on #0d1420]{", line);
        Assert.Contains("[#112233 on #0d1420][[{", line);
        Assert.Contains("[#445566 on #0d1420]\"escaped \\\" [[ ]] { }\"[/]", line);
        Assert.Contains("[#778899 on #0d1420]-1.25e-3[/]", line);
        Assert.Contains("[#112233 on #0d1420]}]]}[/]", line);
    }

    [TestMethod]
    public void ArrayNodesSortFilterAndRestoreParentStateAcrossViews() {
        var items = new[] { new Item("ten", 10), new Item("two", 2) };
        using var app = new ObjectTableApp([PSObject.AsPSObject(new { Items = items, Other = "root only" })], new() {
            View = ObjectView.Expanded
        });
        var root = app.Surface.Model;
        root.Filter = "root only";
        root.Rebuild();
        app.InspectSelected(root.Current!);
        var parent = app.Surface.Model;
        parent.Select(parent.Visible.FindIndex(row => row.Cells[0] == "Items"), 20);
        app.InspectSelected(parent.Current!);
        Assert.AreEqual("$[0].Items", app.NodePath);
        var array = app.Surface.Model;
        int count = Array.IndexOf(array.Columns, "Count");
        array.SortColumn = count;
        array.Rebuild();
        Assert.AreEqual(2, array.Visible[0].Values[count]);
        array.Filter = "two";
        array.Rebuild();
        Assert.HasCount(1, array.Visible);
        app.SwitchView(ObjectView.Json);
        Assert.AreEqual(2, app.NavigationDepth);
        Assert.AreEqual("$[0].Items", app.Surface.DocumentHint);
        app.SwitchView(ObjectView.Expanded);
        Assert.AreSame(array, app.Surface.Model);
        Assert.AreEqual("two", array.Filter);
        app.InspectSelected(array.Current!);
        Assert.AreEqual("$[0].Items[1]", app.NodePath);
        Assert.AreEqual("two", app.Surface.Model.Rows.Single(row => row.Cells[0] == "Name").Values[1]);
        Assert.IsTrue(app.GoBack());
        Assert.AreSame(array, app.Surface.Model);
        Assert.IsTrue(app.GoBack());
        Assert.AreSame(parent, app.Surface.Model);
        Assert.AreEqual("Items", parent.Current!.Cells[0]);
        Assert.IsTrue(app.GoBack());
        Assert.AreSame(root, app.Surface.Model);
        Assert.AreEqual("root only", root.Filter);
        Assert.IsFalse(app.GoBack());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task KeyboardMenuJsonSearchAndBackKeepTheSelectedNode() {
        using var app = new ObjectTableApp([PSObject.AsPSObject(new Item("needle", 42))], new() {
            View = ObjectView.Expanded
        });
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 35));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        void Send(TerminalEvent ev, Func<bool> condition) {
            backend.PushEvent(ev);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return condition(); }, 5000));
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(app.Surface);
        try {
            Tick();
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => app.NavigationDepth == 1);
            Send(new TerminalKeyEvent { Key = TerminalKey.F10 }, () => backend.GetOutText().Contains("Back to parent", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => app.Surface.HasFocus);
            Send(new TerminalTextEvent { Text = "3" }, () => app.View == ObjectView.Json);
            Assert.AreEqual("$[0]", app.NodePath);
            Send(new TerminalTextEvent { Text = "/" }, () => backend.GetOutText().Contains("FIND", StringComparison.Ordinal));
            Send(new TerminalTextEvent { Text = "needle" }, () => app.Surface.Status.Contains("Found 'needle'", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => app.NavigationDepth == 1);
            Send(new TerminalKeyEvent { Key = TerminalKey.Backspace }, () => app.NavigationDepth == 0);
            Assert.IsFalse(app.Surface.ExitRequested);
            Assert.AreEqual(ObjectView.Expanded, app.View);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => app.NavigationDepth == 1);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 3, Y = 2 }, () => app.NavigationDepth == 0);
            Assert.IsFalse(app.Surface.ExitRequested);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    [TestMethod]
    public void ExpandedCellsRetainAnsiStylesWithoutTerminalCommandsOrSearchNoise() {
        string value = "\u001b[38;2;255;0;0mred\u001b[0m\u001b[2J\u001b]0;title\a plain";
        var model = ObjectSnapshot.Create([PSObject.AsPSObject(new { Value = value })]);
        Assert.AreEqual("red plain", model.Rows[0].Cells[0]);
        Assert.AreEqual(value, model.Rows[0].Values[0]);
        Assert.IsTrue(model.CellStyles[(0, 0)][0].Style.TryGetForeground(out var color));
        Assert.AreEqual(Color.Rgb(255, 0, 0), color);
        Assert.IsFalse(model.CellStyles[(0, 0)][^1].Style.TryGetForeground(out _), "An ANSI reset must inherit the table theme.");
        string rendered = string.Join('\n', VisualSnapshotRenderer.Render(new TableSurface(model, "Colors"), 120, 35).ToMarkupLines());
        Assert.Contains("[#ff0000 on", rendered);
        Assert.Contains("red", rendered);
        model.Filter = "red plain";
        model.Rebuild();
        Assert.HasCount(1, model.Visible);
        model.Filter = "38;2";
        model.Rebuild();
        Assert.IsEmpty(model.Visible);
    }

    [TestMethod]
    public void ScalarValueRowsCannotBeOpenedRecursively() {
        var timestamp = new DateTime(2026, 11, 2, 19, 16, 50);
        using var app = new ObjectTableApp([PSObject.AsPSObject(new { LastAccessTime = timestamp })], new() {
            View = ObjectView.Expanded
        });

        app.InspectSelected(app.Surface.Model.Current!);
        var property = app.Surface.Model.Rows.Single(row => row.Cells[0] == "LastAccessTime");
        app.InspectSelected(property);
        Assert.AreEqual("$[0].LastAccessTime", app.NodePath);
        Assert.AreEqual("Value", app.Surface.Model.Rows.Single().Cells[0]);
        Assert.AreEqual(timestamp, app.Surface.Model.Rows.Single().Values[1]);

        app.InspectSelected(app.Surface.Model.Current!);

        Assert.AreEqual(2, app.NavigationDepth);
        Assert.AreEqual("$[0].LastAccessTime", app.NodePath);
        Assert.Contains("Scalar values have no nested members", app.Surface.Status);
    }

    [TestMethod]
    public void CompoundNodesRespectDepthLimitAndAllowBack() {
        using var app = new ObjectTableApp([PSObject.AsPSObject("hello")], new() {
            View = ObjectView.Expanded
        });
        for (int i = 0; i < 17; i++) {
            app.OpenNode(new Item("nested", 42), "Child");
        }
        Assert.AreEqual(16, app.NavigationDepth);
        Assert.AreEqual(42, app.Surface.Model.Rows.Single(row => row.Cells[0] == "Count").Values[1]);
        Assert.Contains("Maximum inspection depth", app.Surface.Status);
        Assert.IsTrue(app.GoBack());
        Assert.AreEqual(15, app.NavigationDepth);
    }
}
