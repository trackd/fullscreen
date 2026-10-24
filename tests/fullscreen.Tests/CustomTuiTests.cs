using System.Collections;
using fullscreen.Custom;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;
using System.Diagnostics;
using System.Reflection;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CustomTuiTests {
    [TestMethod]
    public async Task RealFrameworkInputSubmitsBoundStateAndReturnsTheHandlerResult() {
        using var app = new CustomTuiApp(TuiDocument.Starter(), handler: (ev, state) => ev.Id == "submit" ? new(Close: true, Value: state["name"]) : null);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(110, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            terminal.Focus(app.Controls["name"]);
            ((TextBox)app.Controls["name"]).Text = "Sam";
            Tick();
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return app.Controls["submit"].HasFocus; }, 5000));
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return !app.Cancelled; }, 5000));
            Assert.AreEqual("Sam", app.Value);
            Assert.AreEqual("Sam", app.State["name"]);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public async Task DesignerPropertyTypingUpdatesLivePreviewAndUndoRestoresTheDocument() {
        using var app = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        using var backend = new InMemoryTerminalBackend(new TerminalSize(160, 42));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            app.Tree.SelectedIndex = 1;
            Tick();
            var editor = (TextArea)typeof(TuiDesignerApp).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
            editor.Text = "";
            terminal.Focus(editor);
            backend.PushEvent(new TerminalPasteEvent { Text = "Live preview changed" });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return app.Document.Root.Children[0].Text == "Live preview changed"; }, 5000));
            Assert.Contains("Live preview changed", backend.GetOutText());
            app.Undo();
            Assert.AreEqual("Welcome to your TUI", app.Document.Root.Children[0].Text);
            app.Redo();
            Assert.AreEqual("Live preview changed", app.Document.Root.Children[0].Text);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public void RuntimeDocumentRejectsCyclicControlTrees() {
        var starter = TuiDocument.Starter();
        starter.Root.Children.Add(starter.Root);
        Assert.Throws<ArgumentException>(() => starter.Validate());
    }
    [TestMethod]
    public void RuntimeCapturesInputPropagatesStateAndRunsHandlersOnlyOnTickThread() {
        var doc = TuiDocument.Starter();
        doc.Root.Children.Add(new() {
            Id = "echo",
            Type = "Text",
            Bind = "greeting"
        });
        int thread = Environment.CurrentManagedThreadId;
        var events = new List<TuiEvent>();
        using var app = new CustomTuiApp(doc, handler: (ev, state) => { Assert.AreEqual(thread, Environment.CurrentManagedThreadId); events.Add(ev); return new(new Hashtable { ["greeting"] = "Hello " + state["name"] }); });
        ((TextBox)app.Controls["name"]).Text = "Sam";
        Assert.IsEmpty(events);
        Assert.IsTrue(app.Tick());
        Assert.AreEqual("Sam", app.State["name"]);
        Assert.AreEqual("Hello Sam", ((TextBlock)app.Controls["echo"]).Text);
        Assert.IsTrue(events.Any(x => x.Id == "name" && x.Kind == "Change"));
        Assert.AreEqual(100, VisualSnapshotRenderer.Render(app.Root, 100, 25).Width);
    }
    [TestMethod]
    public void AllSupportedControlsBuildAndRenderWithoutExecutingHandlersInPreview() {
        var doc = TuiDocument.Starter();
        doc.Root.Children = [.. TuiDocument.ControlTypes.Select((type, i) => new TuiNode { Id = "node" + i, Type = type, Text = "Sample", Items = type == "Select" ? ["A", "B"] : [], Height = type == "TextArea" ? 3 : null })];
        doc.Root.Children.Single(node => node.Type == "SearchReplacePopup").Target = doc.Root.Children.Single(node => node.Type == "LogControl").Id;
        using var app = new CustomTuiApp(doc, handler: (_, _) => throw new InvalidOperationException("Preview executed a handler"), preview: true);
        Assert.IsTrue(app.Tick());
        foreach (var size in new[] { (140, 40), (80, 24), (35, 12) }) {
            Assert.AreEqual(size.Item1, VisualSnapshotRenderer.Render(app.Root, size.Item1, size.Item2).Width);
        }
    }
    [TestMethod]
    public async Task MinimalCustomAndDesignerLayoutsKeepAuthoredContentAndRestoreChrome() {
        using var app = new CustomTuiApp(TuiDocument.Starter());
        var root = (VStack)app.Root;
        app.SetMinimal(true);
        Assert.IsFalse(root.Children[0].IsVisible);
        Assert.IsFalse(root.Children[3].IsVisible);
        Assert.IsTrue(root.Children[2].IsVisible);
        Assert.AreEqual(100, VisualSnapshotRenderer.Render(root, 100, 25).Width);
        app.SetMinimal(false);
        Assert.IsTrue(root.Children[0].IsVisible);
        Assert.IsTrue(root.Children[3].IsVisible);
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        designer.SetMinimal(true);
        designer.Add("Text");
        Assert.HasCount(4, designer.Document.Root.Children);
        var stack = (Grid)designer.Root;
        Assert.IsFalse(stack.Cells[0].Content!.IsVisible);
        Assert.IsTrue(stack.Cells[1].Content!.IsVisible);
        ui.Tick();
        Assert.AreEqual(120, designer.Root.Bounds.Width);
        designer.SetMinimal(false);
        ui.Tick();
        Assert.IsTrue(stack.Cells[0].Content!.IsVisible, $"Designer bounds: {designer.Root.Bounds}");
    }
    [TestMethod]
    public void DesignerEditsUndoRedoAndSaveRoundTripKeepAValidPreview() {
        TuiDocument? saved = null;
        using var app = new TuiDesignerApp(TuiDocument.Starter(), (document, _) => saved = TuiDocument.FromJson(document.ToJson()));
        app.Add("CheckBox");
        Assert.IsTrue(app.Dirty);
        Assert.HasCount(4, app.Document.Root.Children);
        app.Undo();
        Assert.HasCount(3, app.Document.Root.Children);
        app.Redo();
        Assert.HasCount(4, app.Document.Root.Children);
        app.Move(-1);
        app.Document.Validate();
        app.SaveTo("fixture.json");
        Assert.IsFalse(app.Dirty);
        Assert.IsNotNull(saved);
        Assert.HasCount(4, saved.Root.Children);
        Assert.AreEqual(150, VisualSnapshotRenderer.Render(app.Root, 150, 40).Width);
    }
}
