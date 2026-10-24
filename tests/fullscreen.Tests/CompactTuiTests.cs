using System.Management.Automation;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Custom;
using fullscreen.Hosting;
using fullscreen.Monitoring;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CompactTuiTests {
    private sealed class TestApp : IFullscreenApp {
        public Visual Root { get; } = new Chart([1, 3, 2]);
        internal bool Closed { get; private set; }
        public void RequestClose() =>
            Closed = true;
        public bool Tick() =>
            !Closed;
        public void Dispose() { }
    }
    [TestMethod]
    public async Task SharedCloseUsesTopRightWithoutTakingCanvasRows() {
        using var app = new TestApp();
        var chrome = new FullscreenChrome(app, ThemePalette.Default);
        await using var ui = new DesignerUxTests.UiSession(chrome, () => app.Tick(), 100, 40);
        Assert.AreEqual(chrome.Bounds, app.Root.Bounds);
        Assert.AreEqual(97, chrome.CloseBounds.X);
        Assert.AreEqual(0, chrome.CloseBounds.Y);
        Assert.AreEqual(1, chrome.CloseBounds.Height);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 98, 0, () => true);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 98, 0, () => app.Closed);
        Assert.IsFalse(app.Tick());
    }
    [TestMethod]
    public void ClosingChartCancelsPassThru() {
        using var app = new ObjectChartApp([PSObject.AsPSObject(3)]);
        app.Surface.PassThru = true;
        app.RequestClose();
        Assert.IsFalse(app.Tick());
        Assert.IsEmpty(app.Chosen);
        Assert.IsFalse(app.Surface.Accepted);
    }
    [TestMethod]
    public async Task SharedDesignerCloseRetainsUnsavedChangesUntilConfirmed() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        var chrome = new FullscreenChrome(designer, ThemePalette.Default);
        await using var ui = new DesignerUxTests.UiSession(chrome, () => designer.Tick(), 100, 40);
        var editors = (Dictionary<string, Visual>)typeof(TuiDesignerApp).GetField("_editors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(designer)!;
        ((TextBox)editors["Id"]).Text = "unsavedRoot";
        Assert.IsTrue(designer.ApplyProperties());
        Assert.IsTrue(designer.Dirty);
        ((IFullscreenApp)designer).RequestClose();
        ui.Tick();
        Assert.IsTrue(designer.Tick());
        Assert.Contains("Discard unsaved changes", ui.Output);
    }
    [TestMethod]
    [DataRow(MonitorView.Processes)]
    [DataRow(MonitorView.Dashboard)]
    [DataRow(MonitorView.Services)]
    [DataRow(MonitorView.Users)]
    [DataRow(MonitorView.Network)]
    [DataRow(MonitorView.Gpu)]
    public async Task MonitorKeepsOneFooterRowAndKeysAreInHelp(MonitorView view) {
        var surface = new MonitorSurface { View = view, SampleMilliseconds = 1.25, MinHeight = 40, MaxHeight = 40 };
        string[] rows = [.. VisualSnapshotRenderer.Render(surface, 130, 40).ToMarkupLines()];
        Assert.Contains("ms collect", rows[39]);
        Assert.Contains("[? Help]", rows[39]);
        Assert.IsLessThan(rows[39].IndexOf("/ all", StringComparison.Ordinal),
            rows[39].IndexOf("[? Help]", StringComparison.Ordinal));
        Assert.DoesNotContain("F12 diagnostics", rows[38]);
        Assert.DoesNotContain("G Area", rows[39]);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 130, 40);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 3, 39, () => true);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 3, 39,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("Monitor keys and legend", ui.Output);
        Assert.Contains("2 services", ui.Output);
        Assert.Contains("6 dashboard", ui.Output);
        Assert.Contains("F12", ui.Output);
    }
    [TestMethod]
    public void UsersExcludeUnnamedSessionsAndProcesses() {
        var frame = new CollectorFrame { UserCount = 3, ProcessCount = 2 };
        frame.Users[0] = new("", "Services", "Active", "", "");
        frame.Users[1] = new("  ", "listener", "Listen", "", "");
        frame.Users[2] = new("Ada", "Console", "Active", "", "");
        frame.Processes[0] = new(1, "unknown", 1, 1, 1);
        frame.Processes[1] = new(2, "owned", 1, 1, 1, UserName: "Ada");
        var view = new UserView();
        view.Update(frame);
        Assert.HasCount(1, view.Rows);
        Assert.AreEqual("Ada", view.Rows[0].UserName);
        Assert.AreEqual(1, view.Rows[0].ProcessCount);
    }
    [TestMethod]
    public void ProcessTreeCanCollapseAndExpandEveryRoot() {
        ProcessRow[] rows = [new(1, "first", 1, 1, 1), new(2, "child", 1, 1, 1, ParentId: 1), new(3, "other", 1, 1, 1), new(4, "child2", 1, 1, 1, ParentId: 3)];
        var model = new ProcessView { Tree = true };
        model.Update(rows);
        Assert.AreEqual(4, model.Count);
        model.CollapseAll(true);
        model.Update(rows);
        Assert.AreEqual(2, model.Count);
        model.CollapseAll(false);
        model.Update(rows);
        Assert.AreEqual(4, model.Count);
    }
}
