using System.Diagnostics;
using System.Reflection;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DesignerWorkflowTests {
    private static Visual Editor(TuiDesignerApp designer, string name) {
        return ((Dictionary<string, Visual>)typeof(TuiDesignerApp).GetField("_editors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(designer)!)[name];
    }
    [TestMethod]
    public void PendingInspectorDraftIsDirtyAndUndoRedoRetainTheDraft() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        designer.Tree.SelectedIndex = 1;
        designer.Tick();
        ((TextArea)Editor(designer, "Text")).Text = "Draft heading";
        Assert.IsTrue(designer.Dirty, "Uncommitted fields must count as unsaved edits.");
        designer.Undo();
        Assert.AreEqual("Welcome to your TUI", designer.Document.Root.Children[0].Text);
        designer.Redo();
        Assert.AreEqual("Draft heading", designer.Document.Root.Children[0].Text);
        designer.SaveTo("fixture.psd1");
        Assert.IsFalse(designer.Dirty);
        designer.Undo();
        Assert.IsTrue(designer.Dirty);
        designer.Redo();
        Assert.IsFalse(designer.Dirty, "Restoring the saved snapshot must clear the modified indicator.");
    }
    [TestMethod]
    public void InvalidDraftBlocksHistoryWithoutLosingInputOrMutatingTheDocument() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        designer.Add("Button");
        var width = (TextBox)Editor(designer, "Width");
        width.Text = "bad width";
        string before = designer.Document.ToJson();
        designer.Undo();
        Assert.AreEqual(before, designer.Document.ToJson());
        Assert.AreEqual("bad width", width.Text);
        Assert.IsTrue(designer.Dirty);
    }
    [TestMethod]
    public void InspectorUsesCatalogFieldsAndTypeChangesResetUnsupportedValues() {
        var document = TuiDocument.Starter();
        document.Root.Children.Add(new() {
            Id = "choice",
            Type = "Select",
            Items = ["one", "", " two "]
        });
        using var designer = new TuiDesignerApp(document, (_, _) => { });
        designer.Tree.SelectedIndex = 4;
        designer.Tick();
        Assert.AreEqual("one\n\n two ", ((TextArea)Editor(designer, "Items")).Text);
        var type = (Select<string>)Editor(designer, "Type");
        type.SelectedIndex = type.Items.IndexOf("Text");
        designer.Tick();
        Assert.IsTrue(designer.ApplyProperties());
        var changed = designer.Document.Root.Children[^1];
        Assert.AreEqual("Text", changed.Type);
        Assert.IsEmpty(changed.Items);
        var columns = designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().First(text => text.Text == "Columns");
        Assert.IsFalse(columns.Parent!.IsVisible);
        ((TextBox)Editor(designer, "Width")).Text = "bad width";
        string before = designer.Document.ToJson();
        Assert.IsFalse(designer.ApplyProperties());
        Assert.AreEqual(before, designer.Document.ToJson());
        Assert.IsTrue(designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text?.Contains("choice") == true && text.Text.Contains("Width")));
    }
    [TestMethod]
    public void CustomTuiContentAndChartsUseViewportWidthInsteadOfAnInfiniteScrollWidth() {
        var document = new TuiDocument { Root = new() { Id = "root", Type = "VStack", Children = [new() { Id = "chart", Type = "Chart", Height = 8 }] } };
        using var app = new CustomTuiApp(document);
        foreach (int width in new[] { 80, 140 }) {
            VisualSnapshotRenderer.Render(app.Root, width, 30);
            Assert.AreEqual(width, app.Controls["chart"].Bounds.Width);
            Assert.IsLessThanOrEqualTo(width, app.Controls["chart"].Bounds.Right);
        }
    }
    [TestMethod]
    public void PaletteAndProgrammaticAddShareUsefulDefaultsAndRejectUnknownTypesWithoutMutation() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        designer.Add("Chart");
        var added = designer.Document.Root.Children[^1];
        Assert.AreEqual(added.Id, added.Bind);
        Assert.AreEqual(10, added.Height);
        designer.Tree.SelectedIndex = 0;
        designer.Tick();
        Assert.IsTrue(designer.Place("Chart", null, "root"));
        var dropped = designer.Document.Root.Children[^1];
        Assert.AreEqual(dropped.Id, dropped.Bind);
        Assert.AreEqual(added.Height, dropped.Height);
        string before = designer.Document.ToJson();
        designer.Add("UnknownWidget");
        Assert.AreEqual(before, designer.Document.ToJson());
    }
    [TestMethod]
    public async Task SearchKeyboardActivationAndResponsivePanesKeepThePreviewUsable() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 40));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(designer.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            designer.Tick();
            time += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [time]);
        }
        void Send(TerminalEvent input, Func<bool> observed) {
            backend.PushEvent(input);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000));
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        try {
            Tick();
            Tick();
            Assert.IsGreaterThan(50, designer.Preview.Bounds.Width);
            var search = designer.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Single(input => input.Placeholder == "Search controls");
            search.Text = "Button";
            Tick();
            app.Focus(search);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => designer.Document.Root.Children.Count == 4);
            Assert.AreEqual("Button", designer.Document.Root.Children[^1].Type);
            search.Text = "no-such-control";
            Tick();
            Assert.IsTrue(designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(text => text.Text == "No matching controls").IsVisible);
            Send(new TerminalKeyEvent { Key = TerminalKey.F3 }, () => app.FocusedElement == Editor(designer, "Id"));
            Assert.IsTrue(search.Parent!.Parent!.IsVisible, "Properties must not hide the right-hand control list.");
            Assert.IsGreaterThan(50, designer.Preview.Bounds.Width);
            backend.SetSize(new TerminalSize(180, 48));
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return designer.Root.Bounds.Width == 180; }, 3000));
            Tick();
            Tick();
            Assert.IsGreaterThan(100, designer.Preview.Bounds.Width);
            designer.SetMinimal(true);
            Tick();
            Assert.IsGreaterThan(150, designer.Preview.Bounds.Width);
            designer.SetMinimal(false);
            Tick();
            Assert.IsGreaterThan(100, designer.Preview.Bounds.Width);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null); }
    }
}
