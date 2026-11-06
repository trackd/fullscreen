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
public sealed class DesignerUxTests {
    private static Visual Editor(TuiDesignerApp designer, string name) =>
        ((Dictionary<string, Visual>)typeof(TuiDesignerApp).GetField("_editors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(designer)!)[name];
    private static Button Button(Visual root, string label) =>
        root.EnumerateVisualsDepthFirst().OfType<Button>()
        .Single(button => button.Content is TextBlock text && text.Text == label && Visible(button));
    private static bool Visible(Visual visual) {
        for (Visual? current = visual; current is not null; current = current.Parent) {
            if (!current.IsVisible) {
                return false;
            }
        }
        return true;
    }
    private static Dialog? DialogFor(Visual? visual) {
        for (; visual is not null; visual = visual.Parent) {
            if (visual is Dialog dialog) {
                return dialog;
            }
        }
        return null;
    }
    internal sealed class UiSession : IAsyncDisposable {
        private readonly InMemoryTerminalBackend _backend;
        private readonly TerminalSession _session;
        private readonly Action _tick;
        private long _time = Stopwatch.GetTimestamp();
        public TerminalApp App {
            get;
        }
        public string Output => _backend.GetOutText();
        public UiSession(Visual root, Action tick, int width = 120, int height = 40) {
            _tick = tick;
            _backend = new(new TerminalSize(width, height));
            _session = Terminal.Open(_backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
            App = new TerminalApp(root, _session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
            typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(App, null);
            Tick();
            Tick();
        }
        public void Tick() {
            _tick();
            _time += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(App, [_time]);
        }
        public void Key(TerminalKey key, Func<bool> observed, TerminalModifiers modifiers = default, char? character = null) {
            _backend.PushEvent(new TerminalKeyEvent { Key = key, Modifiers = modifiers, Char = character });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000), $"Key {key} did not reach expected state. Focus={App.FocusedElement?.GetType().Name}, dialogs={App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Count()}. {_backend.GetOutText()[^Math.Min(500, _backend.GetOutText().Length)..]}");
        }
        public void Text(string text, Func<bool> observed) {
            _backend.PushEvent(new TerminalTextEvent { Text = text });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000), $"Text '{text}' did not reach expected state.");
        }
        public void Mouse(TerminalMouseKind kind, TerminalMouseButton button, int x, int y, Func<bool> observed, TerminalModifiers modifiers = default) {
            _backend.PushEvent(new TerminalMouseEvent { Kind = kind, Button = button, X = x, Y = y, Modifiers = modifiers });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000), $"Mouse input did not reach the expected state. Focus={App.FocusedElement?.GetType().Name}, output={Output[^Math.Min(Output.Length, 1600)..]}");
        }
        public void Wheel(int x, int y, int delta, Func<bool> observed, TerminalModifiers modifiers = default) {
            _backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Wheel, X = x, Y = y, WheelDelta = delta, Modifiers = modifiers });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000),
                "Wheel input did not reach the expected state. " + string.Join("; ", App.Root.EnumerateVisualsDepthFirst().OfType<ScrollViewer>()
                    .Select(view => $"bounds={view.Bounds}, offset={view.VerticalOffset}, viewport={view.ViewportHeight}, content={view.Content?.Bounds}")));
        }
        public void Activate(Visual target, Func<bool> observed) {
            Tick();
            Assert.IsGreaterThan(0, target.Bounds.Width);
            App.Focus(target);
            _backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed(); }, 3000), $"Click did not reach expected state. Focus={App.FocusedElement?.GetType().Name}, dialogs={App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Count()}. {_backend.GetOutText()[^Math.Min(500, _backend.GetOutText().Length)..]}");
        }
        public async ValueTask DisposeAsync() {
            typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(App, null);
            await App.DisposeAsync();
            _session.Dispose();
            _backend.Dispose();
        }
    }
    [TestMethod]
    [DataRow("")]
    [DataRow("Application title")]
    public void MinimalChromeTogglesWithoutHidingContentForTitledAndInlineHeadingLayouts(string title) {
        using var app = new CustomTuiApp(new TuiDocument {
            Title = title,
            Root = new TuiNode { Id = "content", Type = "Text", Text = "Application content" }
        });
        foreach (bool minimal in new[] { false, true, false }) {
            app.SetMinimal(minimal);
            Assert.IsTrue(app.Controls["content"].IsVisible);
            var children = ((VStack)app.Root).Children;
            Assert.IsTrue(children[title.Length == 0 ? 0 : 2].IsVisible);
            Assert.AreEqual(!minimal, children[^1].IsVisible);
            if (title.Length > 0) {
                Assert.AreEqual(!minimal, children[0].IsVisible);
                Assert.AreEqual(!minimal, children[1].IsVisible);
            }
        }
    }

    [TestMethod]
    [DataRow("Stretch", false)]
    [DataRow("Start", true)]
    public void FullHeightLayoutsConstrainPaneViewportsWhileNaturalLayoutsStillScroll(string alignment, bool wholeLayoutScrolls) {
        using var app = new CustomTuiApp(new TuiDocument {
            Root = new TuiNode {
                Id = "sources",
                Type = "OptionList",
                VerticalAlignment = alignment,
                Items = [.. Enumerable.Range(1, 100).Select(index => $"Channel {index}")]
            }
        });
        VisualSnapshotRenderer.Render(app.Root, 80, 24);
        var source = app.Controls["sources"];
        if (wholeLayoutScrolls) {
            Assert.IsGreaterThan(24, source.Bounds.Height);
        }
        else {
            Assert.IsLessThan(24, source.Bounds.Height);
            Assert.IsGreaterThan(0, source.Bounds.Height);
        }
    }

    [TestMethod]
    public async Task InvalidDraftCloseRequiresConfirmationAndCancelPreservesInput() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new UiSession(designer.Root, () => designer.Tick());
        var width = (TextBox)Editor(designer, "Width");
        width.Text = "bad width";
        ui.Key(TerminalKey.F10, () => DialogFor(ui.App.FocusedElement) is not null);
        Assert.IsTrue(designer.Tick(), "Opening the discard dialog must not close the designer.");
        ui.Key(TerminalKey.Escape, () => DialogFor(ui.App.FocusedElement) is null);
        Assert.AreEqual("bad width", width.Text);
        Assert.IsTrue(designer.Dirty);
        Assert.IsTrue(designer.Tick());
        ui.Key(TerminalKey.F10, () => DialogFor(ui.App.FocusedElement) is not null);
        var confirm = DialogFor(ui.App.FocusedElement)!;
        ui.Activate(Button(confirm, "Confirm"), () => !designer.Tick());
        Assert.IsFalse(designer.Tick());
    }
    [TestMethod]
    public async Task SaveFailureRetainsFilenameAndAllowsRetryInTheSameDialog() {
        string? saved = null;
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, path) => {
            if (path != "good.psd1") {
                throw new IOException("Choose an existing directory.");
            }
            saved = path;
        }, exists: _ => false);
        await using var ui = new UiSession(designer.Root, () => designer.Tick());
        ui.Activate(Button(designer.Root, "▣"), () => ui.App.FocusedElement is TextBox);
        var input = Assert.IsInstanceOfType<TextBox>(ui.App.FocusedElement);
        var dialog = DialogFor(input)!;
        input.Text = "missing/layout.psd1";
        ui.Activate(Button(dialog, "Save"), () => dialog.EnumerateVisualsDepthFirst().OfType<Paragraph>().Any(text => text.IsVisible && (text.Text ?? "").Contains("existing directory", StringComparison.Ordinal)));
        Assert.AreEqual("missing/layout.psd1", input.Text);
        Assert.AreSame(dialog, DialogFor(ui.App.FocusedElement));
        Assert.IsNull(saved);
        input.Text = "good.psd1";
        ui.Activate(Button(dialog, "Save"), () => saved is not null);
        Assert.AreEqual("good.psd1", saved);
        Assert.AreEqual("good.psd1", designer.Path);
        Assert.IsNull(DialogFor(ui.App.FocusedElement));
        Assert.IsFalse(designer.Dirty);
    }
    [TestMethod]
    public async Task LinkedExportConfirmsLayoutReplacementAndRetainsFailedExportChoices() {
        int writes = 0;
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { }, exists: path => path.EndsWith(".tui.psd1", StringComparison.Ordinal),
            exportScript: (_, path, linked) => {
                writes++;
                Assert.IsTrue(linked);
                if (path != "good.ps1") {
                    throw new IOException("Export directory is unavailable.");
                }
            });
        await using var ui = new UiSession(designer.Root, () => designer.Tick());
        ui.Activate(Button(designer.Root, "↗"), () => ui.App.FocusedElement is TextBox);
        var input = Assert.IsInstanceOfType<TextBox>(ui.App.FocusedElement);
        var dialog = DialogFor(input)!;
        input.Text = "missing.ps1";
        ui.Activate(Button(dialog, "Export"), () => DialogFor(ui.App.FocusedElement) != dialog);
        var confirm = DialogFor(ui.App.FocusedElement)!;
        Assert.IsTrue(confirm.EnumerateVisualsDepthFirst().OfType<Paragraph>().Any(text => (text.Text ?? "").Contains("missing.tui.psd1", StringComparison.Ordinal)));
        Assert.AreEqual(0, writes);
        ui.Key(TerminalKey.Escape, () => DialogFor(ui.App.FocusedElement) == dialog);
        Assert.AreEqual(0, writes);
        ui.Activate(Button(dialog, "Export"), () => DialogFor(ui.App.FocusedElement) != dialog);
        ui.Activate(Button(DialogFor(ui.App.FocusedElement)!, "Confirm"), () => writes == 1);
        Assert.AreEqual("missing.ps1", input.Text);
        Assert.AreSame(dialog, DialogFor(ui.App.FocusedElement));
        Assert.IsTrue(dialog.EnumerateVisualsDepthFirst().OfType<CheckBox>().Single().IsChecked);
        Assert.IsTrue(dialog.EnumerateVisualsDepthFirst().OfType<Paragraph>().Any(text => text.IsVisible && (text.Text ?? "").Contains("unavailable", StringComparison.Ordinal)));
        input.Text = "good.ps1";
        ui.Activate(Button(dialog, "Export"), () => DialogFor(ui.App.FocusedElement) != dialog);
        ui.Activate(Button(DialogFor(ui.App.FocusedElement)!, "Confirm"), () => writes == 2);
        Assert.IsNull(DialogFor(ui.App.FocusedElement));
    }

    [TestMethod]
    public async Task DesignerSaveCreatesACompanionAndConfirmsLayoutReplacement() {
        string directory = Directory.CreateTempSubdirectory("fullscreen-save-").FullName;
        try {
            string layout = Path.Combine(directory, "app.tui.json");
            string script = Path.ChangeExtension(layout, ".ps1");
            int writes = 0;
            using var designer = new TuiDesignerApp(TuiDocument.Starter(), (document, path) => {
                TuiScriptExporter.Export(document, path, overwriteLayout: true);
                writes++;
            }, path: layout);
            await using var ui = new UiSession(designer.Root, () => designer.Tick());
            ui.Activate(Button(designer.Root, "▣"), () => writes == 1);
            Assert.IsTrue(File.Exists(script));
            Assert.Contains("Import-TuiDefinition", File.ReadAllText(script));
            File.AppendAllText(script, "\n# Custom handlers");
            byte[] before = File.ReadAllBytes(script);
            ui.Activate(Button(designer.Root, "▣"), () => DialogFor(ui.App.FocusedElement) is not null);
            Assert.AreEqual(1, writes);
            ui.Key(TerminalKey.Escape, () => DialogFor(ui.App.FocusedElement) is null);
            Assert.AreEqual(1, writes);
            ui.Activate(Button(designer.Root, "▣"), () => DialogFor(ui.App.FocusedElement) is not null);
            ui.Activate(Button(DialogFor(ui.App.FocusedElement)!, "Confirm"), () => writes == 2);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(script));
            Assert.AreEqual(designer.Document.ToJson(), TuiDocument.Load(layout).ToJson());
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task DataExportConfirmsTheExactJsonAndCompanionPaths() {
        string? exported = null;
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { },
            exists: path => path.EndsWith(".json", StringComparison.Ordinal),
            exportScript: (_, path, _) => exported = path);
        await using var ui = new UiSession(designer.Root, () => designer.Tick());
        ui.Activate(Button(designer.Root, "↗"), () => ui.App.FocusedElement is TextBox);
        var input = Assert.IsInstanceOfType<TextBox>(ui.App.FocusedElement);
        var dialog = DialogFor(input)!;
        input.Text = "app.tui.json";
        ui.Activate(Button(dialog, "Export"), () => DialogFor(ui.App.FocusedElement) != dialog);
        var confirmation = DialogFor(ui.App.FocusedElement)!;
        string message = string.Join("", confirmation.EnumerateVisualsDepthFirst().OfType<Paragraph>().Select(text => text.Text));
        Assert.Contains("app.tui.json", message);
        Assert.Contains("app.tui.ps1", message);
        Assert.DoesNotContain(".psd1", message);
        Assert.IsNull(exported);
        ui.Activate(Button(confirmation, "Confirm"), () => exported is not null);
        Assert.AreEqual("app.tui.json", exported);
    }

    [TestMethod]
    public async Task MultilineTabMovesFocusAndF10ExitsBeforeAChildConsumesIt() {
        var document = new TuiDocument {
            Root = new() {
                Id = "root",
                Type = "VStack",
                Children = [
            new() { Id = "notes", Type = "TextArea", Text = "Keep this text" }, new() { Id = "next", Type = "TextBox" }
        ]
            }
        };
        using var app = new CustomTuiApp(document);
        await using var ui = new UiSession(app.Root, () => app.Tick());
        ui.App.Focus(app.Controls["notes"]);
        ui.Key(TerminalKey.Tab, () => ui.App.FocusedElement == app.Controls["next"]);
        Assert.AreEqual("Keep this text", ((TextArea)app.Controls["notes"]).Text);
        ui.Key(TerminalKey.Tab, () => ui.App.FocusedElement == app.Controls["notes"], TerminalModifiers.Shift);
        app.Controls["notes"].KeyDown((_, e) => e.Handled = true);
        ui.Key(TerminalKey.F10, () => !app.Tick());
        Assert.IsTrue(app.Cancelled);
    }
    [TestMethod]
    public void TabCaptionsRoundTripIndependentlyFromIdsAndContent() {
        var document = new TuiDocument {
            Root = new() {
                Id = "tabs",
                Type = "Tabs",
                Children = [
            new() { Id = "general_settings", Type = "Text", TabTitle = "General settings", Text = "Content remains independent" }
        ]
            }
        };
        var clone = TuiDocument.FromJson(document.ToJson());
        Assert.AreEqual("General settings", clone.Root.Children[0].TabTitle);
        Assert.AreEqual("general_settings", clone.Root.Children[0].Id);
        using var app = new CustomTuiApp(clone);
        VisualSnapshotRenderer.Render(app.Root, 120, 40);
        Assert.IsTrue(app.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "General settings"));
        Assert.AreEqual("Content remains independent", ((TextBlock)app.Controls["general_settings"]).Text);
    }
    [TestMethod]
    public void DefinitionErrorsNameTheControlAndExpectedValueWithoutClrTypes() {
        var wrongWidth = Assert.Throws<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"notes","Type":"TextArea","Width":"wide"}}"""));
        Assert.Contains("Control 'notes', property 'Width'", wrongWidth.Message);
        Assert.Contains("whole number", wrongWidth.Message);
        Assert.Contains("wide", wrongWidth.Message);
        Assert.DoesNotContain("System.Nullable", wrongWidth.Message);
        var duplicate = Assert.Throws<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"duplicate","Type":"Text"},{"Id":"duplicate","Type":"Text"}]}}"""));
        Assert.Contains("Duplicate control ID 'duplicate'", duplicate.Message);
        var nested = Assert.Throws<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"nested","Type":"Text","Height":"tall"}]}}"""));
        Assert.Contains("Control 'nested', property 'Height'", nested.Message);
        Assert.Contains("tall", nested.Message);
    }
    [TestMethod]
    public void LinkedExporterRefusesToOverwriteAnUnrelatedLayoutWithoutApproval() {
        string directory = Directory.CreateTempSubdirectory("fullscreen-export-").FullName;
        try {
            string script = Path.Combine(directory, "app.ps1"), layout = Path.ChangeExtension(script, ".tui.psd1");
            File.WriteAllText(layout, "Original layout");
            var error = Assert.Throws<IOException>(() => TuiScriptExporter.Export(TuiDocument.Starter(), script, linkedLayout: true));
            Assert.Contains(layout, error.Message);
            Assert.AreEqual("Original layout", File.ReadAllText(layout));
            Assert.IsFalse(File.Exists(script));
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public void BoundaryActionsExplainUnavailableMovesWithoutChangingTheDocument() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        string before = designer.Document.ToJson();
        designer.Move(1);
        Assert.AreEqual(before, designer.Document.ToJson());
        Assert.IsTrue(designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "The root cannot be moved."));
        designer.Tree.SelectedIndex = 1;
        designer.Tick();
        designer.Move(-1);
        Assert.AreEqual(before, designer.Document.ToJson());
        Assert.IsTrue(designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "Already the first control in this container."));
    }
    [TestMethod]
    [DataRow(100, 40)]
    [DataRow(120, 40)]
    [DataRow(80, 24)]
    [DataRow(60, 20)]
    [DataRow(120, 39)]
    public async Task DesignerRemainsEditableBelowTheRecommendedSize(int width, int height) {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new UiSession(designer.Root, () => designer.Tick(), width, height);
        ui.Tick();
        ui.Tick();
        Assert.IsFalse(designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "TUI DESIGNER · enlarge the terminal"));
        Assert.AreEqual(width, designer.Root.Bounds.Width);
        Assert.AreEqual(height, designer.Root.Bounds.Height);
        Assert.IsGreaterThan(0, designer.Preview.Bounds.Width);
        Assert.IsGreaterThan(0, designer.Preview.Bounds.Height);
        var search = designer.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Single(input => input.Placeholder == "Search controls");
        ui.Key(TerminalKey.F2, () => ui.App.FocusedElement == search);
        search.Text = "TextBox";
        ui.Tick();
        ui.Key(TerminalKey.Enter, () => designer.Document.Root.Children.Count == 4);
        Assert.AreEqual("TextBox", designer.Document.Root.Children[^1].Type);
        ui.Key(TerminalKey.F3, () => ui.App.FocusedElement == Editor(designer, "Id"));
    }
    [TestMethod]
    public async Task InvalidPropertyIsRevealedAndFocusedWithoutLosingTheDraft() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new UiSession(designer.Root, () => designer.Tick());
        var width = (TextBox)Editor(designer, "Width");
        width.Text = "bad width";
        Assert.IsFalse(designer.ApplyProperties());
        ui.Tick();
        Assert.AreSame(width, ui.App.FocusedElement);
        Assert.AreEqual("bad width", width.Text);
        Assert.IsTrue(width.Parent!.Parent!.EnumerateVisualsDepthFirst().OfType<Paragraph>().Any(text => text.IsVisible && (text.Text ?? "").Contains("bad width", StringComparison.Ordinal)));
        Assert.IsNull(designer.Document.Root.Width);
    }
    [TestMethod]
    public async Task PaneShortcutsTransferFocusToTheVisiblePane() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new UiSession(designer.Root, () => designer.Tick(), 100, 40);
        var search = designer.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Single(input => input.Placeholder == "Search controls");
        ui.App.Focus(search);
        ui.Key(TerminalKey.F3, () => ui.App.FocusedElement is TextBox input && input != search);
        Assert.IsTrue(search.Parent!.Parent!.IsVisible, "The control list stays available while properties are edited.");
        ui.Key(TerminalKey.F2, () => ui.App.FocusedElement == search);
        Assert.IsTrue(search.Parent!.Parent!.IsVisible);
    }
}
