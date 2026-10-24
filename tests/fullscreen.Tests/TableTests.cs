using fullscreen.Tables;
using System.Diagnostics;
using System.Reflection;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
public sealed class TableTests {
    [TestMethod]
    public void RegexAndWildcardFiltersAreCaseInsensitiveAndInvalidPatternsAreRecoverable() {
        var model = new TableModel(["Message"]);
        model.Rows.Add(new(1, "error", ["ERROR 42"], ["ERROR 42"]));
        model.Rows.Add(new(2, "info", ["Information"], ["Information"]));
        model.Filter = "regex:^error [0-9]+$";
        model.Rebuild();
        Assert.HasCount(1, model.Visible);
        Assert.AreEqual(1L, model.Current!.Id);
        model.Filter = "wildcard:*FORMAT?ON";
        model.Rebuild();
        Assert.HasCount(1, model.Visible);
        Assert.AreEqual(2L, model.Current!.Id);
        model.Filter = "regex:[";
        model.Rebuild();
        Assert.IsNotNull(model.FilterError);
        Assert.IsEmpty(model.Visible);
        model.Filter = "";
        model.Rebuild();
        Assert.IsNull(model.FilterError);
        Assert.HasCount(2, model.Visible);
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task MinimalTableUsesTopRowsForHeadersAndMouseSelectionAndKeepsTabShortcuts() {
        var model = new TableModel(["Name"]);
        for (int i = 0; i < 40; i++) {
            model.Rows.Add(new(i, i, [$"row {i}"], [i]));
        }

        model.Rebuild();
        var surface = new TableSurface(model, "Hidden title") { Minimal = true, Tabs = ["One", "Two"] };
        surface.TabRequested += index => surface.ActiveTab = index;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(80, 24));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(surface);
        try {
            Tick();
            Assert.IsFalse(backend.GetOutText().Contains("Hidden title", StringComparison.Ordinal));
            backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 5, Y = 2 });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return model.Selected == 1; }, 5000));
            backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 5, Y = 2 });
            Tick();
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.ActiveTab == 1; }, 5000));
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.End });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return model.Selected == 39; }, 5000));
            Assert.AreEqual(17, model.Offset);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task NarrowTabStripKeepsWorktreeAndLaterTabsVisible() {
        var surface = new TableSurface(new TableModel(["Path"]), "Git fixture") { Tabs = ["Changes", "Staged", "History", "Branches", "Remotes", "Worktrees", "Stashes", "Tags"], ActiveTab = 5 };
        surface.TabRequested += index => { surface.ActiveTab = index; surface.Refresh(); };
        using var backend = new InMemoryTerminalBackend(new TerminalSize(80, 24));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(surface);
        try {
            Tick();
            Assert.Contains("6 Worktrees", backend.GetOutText());
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.ActiveTab == 6 && backend.GetOutText().Contains("7 Stashes", StringComparison.Ordinal); }, 5000));
            backend.PushEvent(new TerminalTextEvent { Text = "8" });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.ActiveTab == 7 && backend.GetOutText().Contains("8 Tags", StringComparison.Ordinal); }, 5000));
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    private static readonly string[] expected = ["Ab", "xab", "abd", "abc"];
    private static readonly string[] expectedArray = ["Ab", "abd", "abc"];

    [TestMethod]
    public void NarrowingFilterKeepsSortWithoutRebuilding() {
        var model = new TableModel(["Name", "Count"]) { SortColumn = 1, Descending = true };
        string[] names = ["abc", "abd", "xab", "zzz", "Ab"];
        for (int i = 0; i < names.Length; i++) {
            model.Rows.Add(new(i, names[i], [names[i], $"{i}"], [names[i], i]));
        }
        model.Filter = "a";
        model.Rebuild();
        model.Filter = "ab";
        model.Narrow();
        string[] narrowed = [.. model.Visible.Select(row => row.Cells[0])];
        model.Rebuild();
        CollectionAssert.AreEqual(model.Visible.Select(row => row.Cells[0]).ToArray(), narrowed);
        CollectionAssert.AreEqual(expected, narrowed);
        model.Filter = "regex:^ab";
        model.Narrow();
        CollectionAssert.AreEqual(expectedArray, model.Visible.Select(row => row.Cells[0]).ToArray());
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task FrameworkGridFiltersSortsChoosesColumnsAndAcceptsMarks() {
        var model = new TableModel(["Name", "Count"]);
        model.Rows.Add(new(1, new object(), ["alpha", "10"], ["alpha", 10]));
        model.Rows.Add(new(2, new object(), ["beta", "2"], ["beta", 2]));
        model.Rebuild();
        var surface = new TableSurface(model, "Objects fixture") { EnterAccepts = true };
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 32));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
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
        terminal.Focus(surface);
        try {
            Tick();
            Assert.Contains("alpha", backend.GetOutText());
            Send(new TerminalKeyEvent { Key = TerminalKey.Down }, () => model.Current?.Id == 2);
            Send(new TerminalTextEvent { Text = " " }, () => model.Marked.Contains(2));
            Send(new TerminalTextEvent { Text = "/" }, () => backend.GetOutText().Contains("FILTER", StringComparison.Ordinal));
            Send(new TerminalPasteEvent { Text = "alpha" }, () => model.Filter == "alpha");
            Assert.HasCount(1, model.Visible);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => model.Filter.Length == 0);
            Send(new TerminalTextEvent { Text = "c" }, () => backend.GetOutText().Contains("● Name", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.HasFocus);
            surface.Tabs = ["Formatted", "Properties", "JSON"];
            surface.TabRequested += index => { surface.ActiveTab = index; surface.SetDocument(index == 2 ? "{\n  \"name\": \"alpha\"\n}" : null); };
            surface.Refresh();
            Send(new TerminalTextEvent { Text = "3" }, () => surface.ActiveTab == 2 && backend.GetOutText().Contains("selected original object", StringComparison.Ordinal));
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 18, Y = 0 }, () => surface.ActiveTab == 1);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 18, Y = 0 }, () => true);
            Send(new TerminalTextEvent { Text = "3" }, () => surface.ActiveTab == 2);
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => surface.ActiveTab == 0);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => surface.Accepted);
            Assert.IsTrue(surface.ExitRequested);
            Assert.AreEqual(2L, model.Chosen.Single().Id);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public void SortsNumbersFiltersAndPreservesMarkedOriginals() {
        var model = new TableModel(["Name", "Count"]);
        object original = new();
        model.Rows.Add(new(1, original, ["ten", "10"], ["ten", 10]));
        model.Rows.Add(new(2, new object(), ["two", "2"], ["two", 2L]));
        model.SortColumn = 1;
        model.Rebuild();
        Assert.AreEqual(2L, model.Current!.Id);
        model.ToggleMark();
        model.Filter = "ten";
        model.Rebuild();
        Assert.HasCount(1, model.Visible);
        Assert.AreSame(original, model.Current!.Original);
        Assert.AreEqual(2L, model.Chosen.Single().Id);
        model.Select(int.MaxValue, 0);
        Assert.AreEqual(0, model.Selected);
        model.Filter = "missing";
        model.Rebuild();
        Assert.IsNull(model.Current);
    }
    [TestMethod]
    public void SanitizesTerminalControlsAndBoundsDisplay() {
        Assert.AreEqual("a b c", TableModel.Text("a\u001bb\nc"));
        Assert.HasCount(4096, TableModel.Text(new string('x', 5000)));
        Assert.IsLessThan(0, TableModel.Compare(null, 2));
    }
    [TestMethod]
    public void GroupsBeforeSortingWithinGroups() {
        var model = new TableModel(["Group", "Count"]) { GroupColumn = 0, SortColumn = 1, Descending = true };
        model.Rows.Add(new(1, "a", ["b", "2"], ["b", 2]));
        model.Rows.Add(new(2, "b", ["a", "1"], ["a", 1]));
        model.Rows.Add(new(3, "c", ["a", "3"], ["a", 3]));
        model.Rebuild();
        CollectionAssert.AreEqual(new long[] { 3, 2, 1 }, model.Visible.Select(row => row.Id).ToArray());
    }
}
