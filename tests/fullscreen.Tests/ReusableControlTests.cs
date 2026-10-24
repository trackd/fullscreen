using fullscreen.Themes;
using System.Diagnostics;
using System.Reflection;
using fullscreen.Custom;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ReusableControlTests {
    [TestMethod]
    public void UserListAndCustomSurfaceDefinitionsRoundTripWithoutStartingWorkersInPreview() {
        var definition = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"Tabs","Children":[{"Id":"users","Type":"UserList","IncludeProcesses":true,"SelectionBind":"selectedUser"},{"Id":"commander","Type":"FileCommander","Path":"missing-left","SecondaryPath":"missing-right","SelectionBind":"file"},{"Id":"objects","Type":"ObjectExplorer","Bind":"rows","SelectionBind":"object"}]}}""");
        var clone = TuiDocument.FromJson(definition.ToJson());
        Assert.IsTrue(clone.Root.Children[0].IncludeProcesses);
        Assert.AreEqual("missing-right", clone.Root.Children[1].SecondaryPath);
        Assert.AreEqual("ObjectExplorer", clone.Root.Children[2].Type);
        Assert.IsFalse(new TuiNode().IncludeProcesses);
        Assert.Contains("IncludeProcesses", TuiControlCatalog.Describe("UserList").Properties);
        Assert.IsFalse(TuiControlCatalog.Describe("UserList").Properties.Contains("View"));
        using var preview = new CustomTuiApp(clone, preview: true);
        VisualSnapshotRenderer.Render(preview.Root, 130, 40);
        preview.Tick();
        Assert.IsTrue(preview.Controls.Values.OfType<TuiComponentHost>().All(host => !host.Running));
        clone.Root.Children[0].View = "Network";
        Assert.ThrowsExactly<ArgumentException>(clone.Validate);
        clone.Root.Children[0].View = "Dashboard";
        clone.Root.Children[1].SecondaryPath = new string('x', 4097);
        Assert.ThrowsExactly<ArgumentException>(clone.Validate);
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task ComposedObjectExplorerNavigatesBackAndRefreshesBoundSnapshots() {
        var original = new System.Management.Automation.PSObject();
        original.Properties.Add(new System.Management.Automation.PSNoteProperty("Name", "deployment"));
        original.Properties.Add(new System.Management.Automation.PSNoteProperty("Replicas", new[] { "first", "second" }));
        var definition = TuiDocument.FromJson("""{"Root":{"Id":"objects","Type":"ObjectExplorer","Bind":"rows","SelectionBind":"selected","Height":30}}""");
        using var app = new CustomTuiApp(definition, new System.Collections.Hashtable { ["rows"] = new[] { original } });
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 130, 40);
        var widget = (ReusableObjectExplorer)app.Controls["objects"];
        ui.App.Focus(widget.Application.Root);
        Assert.AreSame(original, app.State["selected"]);
        ui.Key(TerminalKey.Enter, () => widget.Application.NavigationDepth > 0);
        Assert.AreEqual(1, widget.Application.NavigationDepth);
        ui.Key(TerminalKey.Backspace, () => widget.Application.NavigationDepth == 0);
        app.State["rows"] = new[] { System.Management.Automation.PSObject.AsPSObject("replacement") };
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return widget.Application.Surface.Model.Rows.Count == 1 && widget.Application.Surface.Model.Rows[0].Cells.Contains("replacement"); }, 3000));
        Assert.AreEqual(0, widget.Application.NavigationDepth);
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task UserComponentReturnsTypedSelectionWithoutOwningAnotherTerminal() {
        var surface = new Monitoring.MonitorSurface { UsersOnly = true, IncludeUserProcesses = false };
        surface.Frame.Users[0] = new("ada", "console", "Active", "host", "", 1, IdleTime: TimeSpan.FromMinutes(5));
        surface.Frame.UserCount = 1;
        using var host = new TuiComponentHost("Users", () => new SurfaceApp(surface));
        await using var ui = new DesignerUxTests.UiSession(host, () => host.Tick(host), 130, 40);
        Assert.IsTrue(host.Running);
        var selected = Assert.IsInstanceOfType<Monitoring.UserViewRow>(host.Selection);
        Assert.AreEqual("ada", selected.UserName);
        Assert.AreEqual(TimeSpan.FromMinutes(5), selected.Logins.Single().IdleTime);
        Assert.IsNull(selected.Process);
    }
    private sealed class SurfaceApp(Visual root) : IFullscreenApp {
        public Visual Root => root;
        public bool Tick() =>
            true;
        public void Dispose() {
        }
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task ComposedTableRoutesSelectionAndSubmitAlongsideMultilineInput() {
        var definition = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"HStack","Children":[{"Id":"table","Type":"FTable","SelectionBind":"selected","Width":45,"Height":12},{"Id":"editor","Type":"TextArea","Bind":"source","Width":50,"Height":12}]}}""");
        using var custom = new CustomTuiApp(definition, handler: (ev, state) => ev.Kind == "Submit" ? new(Close: true, Value: ev.Value) : null);
        var widget = (ReusableTable)custom.Controls["table"];
        object first = new(), second = new();
        widget.Surface.Model.Rows.Add(new(0, first, ["First"], ["First"]));
        widget.Surface.Model.Rows.Add(new(1, second, ["Second"], ["Second"]));
        widget.Surface.Model.Rebuild();
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(custom.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            custom.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(widget.Surface);
        try {
            Tick();
            Assert.AreSame(first, custom.State["selected"]);
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Down });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return ReferenceEquals(second, custom.State["selected"]); }, 3000));
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return !custom.Cancelled; }, 3000));
            Assert.AreSame(second, custom.Value);
            Assert.AreEqual("table", custom.LastEvent);
            Assert.IsInstanceOfType<TextArea>(custom.Controls["editor"]);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public void CatalogMatchesSchemaAndMixedDefinitionsRoundTrip() {
        CollectionAssert.AreEquivalent(TuiDocument.ControlTypes, TuiControlCatalog.All.Select(info => info.Name).ToArray());
        Assert.IsTrue(TuiControlCatalog.Describe("FileBrowser").Live);
        Assert.Contains("Selection", TuiControlCatalog.Describe("FTable").Events);
        var document = TuiDocument.FromJson("""{"Root":{"Id":"tabs","Type":"Tabs","Children":[{"Id":"files","Type":"FileBrowser","Path":"/does-not-exist","SelectionBind":"file"},{"Id":"monitor","Type":"PerformanceMonitor","View":"Network","RefreshInterval":500},{"Id":"table","Type":"FTable","Bind":"rows","Columns":["Name","Status"],"SelectionBind":"selected"}]}}""");
        var clone = TuiDocument.FromJson(document.ToJson());
        Assert.AreEqual("Network", clone.Root.Children[1].View);
        Assert.AreEqual("selected", clone.Root.Children[2].SelectionBind);
        using var preview = new CustomTuiApp(document, preview: true);
        preview.Tick();
        VisualSnapshotRenderer.Render(preview.Root, 120, 32);
        Assert.IsTrue(preview.Controls.Values.OfType<TuiComponentHost>().All(host => !host.Running), "Designer preview must not access paths or collectors.");
        preview.ApplyTheme(ThemeCatalog.Load("Monokai"));
        Assert.AreEqual("Monokai", ((ReusableTable)preview.Controls["table"]).Surface.Palette.Name, "Inactive tab controls must receive the palette too.");
        document.Root.Children[2].SelectionBind = "rows";
        Assert.Throws<ArgumentException>(document.Validate);
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task LiveComponentsStartOnlyWhenVisibleAndDisposeOnHideAndClose() {
        int firstStarts = 0, secondStarts = 0, disposals = 0;
        using var first = new TuiComponentHost("First", () => { firstStarts++; return new FakeApp(() => disposals++); });
        using var second = new TuiComponentHost("Second", () => { secondStarts++; return new FakeApp(() => disposals++); });
        var tabs = new TabControl(new TabPage("First", first), new TabPage("Second", second)) { HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch };
        first.Tick(tabs);
        second.Tick(tabs);
        Assert.AreEqual(0, firstStarts + secondStarts);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 28));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(tabs, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Tick();
            first.Tick(tabs);
            second.Tick(tabs);
            Tick();
            Assert.AreEqual(1, firstStarts);
            Assert.AreEqual(0, secondStarts);
            tabs.SelectedIndex = 1;
            Tick();
            first.Tick(tabs);
            second.Tick(tabs);
            Tick();
            Assert.AreEqual(1, secondStarts);
            Assert.IsFalse(first.Running);
            Assert.AreEqual(1, disposals);
            second.Dispose();
            second.Dispose();
            Assert.AreEqual(2, disposals);
            using var preview = new TuiComponentHost("Preview", () => throw new InvalidOperationException("Preview started a worker."), preview: true);
            preview.Tick(tabs);
            Assert.IsFalse(preview.Running);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    private sealed class FakeApp(Action disposed) : IFullscreenApp {
        public Visual Root { get; } = new TextBlock("Embedded worker fixture");
        public bool Tick() =>
            true;
        public void Dispose() =>
            disposed();
    }
}
