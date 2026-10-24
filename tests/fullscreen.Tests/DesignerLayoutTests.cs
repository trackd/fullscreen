using System.Reflection;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;
using fullscreen.Themes;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DesignerLayoutTests {
    private static T Field<T>(TuiDesignerApp designer, string name) =>
        (T)typeof(TuiDesignerApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(designer)!;
    private static Visual Editor(TuiDesignerApp designer, string name) =>
        Field<Dictionary<string, Visual>>(designer, "_editors")[name];
    private static Button Button(TuiDesignerApp designer, string text) =>
        designer.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock label && label.Text == text);

    [TestMethod]
    public async Task PaletteControlNamesStartAtTheSameColumnAsCategoryHeadingsWithoutPrefixes() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        var sidebar = Field<Border>(designer, "_sidebar");
        var labels = sidebar.EnumerateVisualsDepthFirst().OfType<TextBlock>().ToArray();
        var heading = labels.Single(label => label.Text == "APPLICATION");
        var control = labels.Single(label => label.Text == "FileBrowser");
        Assert.AreEqual(heading.Bounds.X, control.Bounds.X);
        Assert.AreEqual(sidebar.Bounds.X + 1, control.Bounds.X);
        Assert.AreEqual(Field<ScrollViewer>(designer, "_paletteViewport").ViewportWidth, heading.Bounds.Width, "Category color bars must fill the list width.");
        Assert.IsFalse(labels.Any(label => (label.Text ?? "").StartsWith('＋')));
        Assert.IsGreaterThan(0, control.Bounds.Width);
    }

    [TestMethod]
    public void PaletteContainsTheEntireCatalogAndCategoryBarsFollowTheTheme() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        var palette = Field<List<(string Type, Visual Visual)>>(designer, "_palette");
        CollectionAssert.AreEquivalent(TuiDocument.ControlTypes, palette.Select(item => item.Type).ToArray());
        Assert.AreEqual("CONTROLS · " + TuiDocument.ControlTypes.Length, Field<TextBlock>(designer, "_paletteHeading").Text);
        foreach (string themeName in new[] { "Dark Modern", "Light Modern" }) {
            var theme = ThemeCatalog.Load(themeName);
            designer.ApplyTheme(theme);
            var categories = Field<List<(string Category, Visual Visual)>>(designer, "_paletteCategories");
            var data = categories.Single(item => item.Category == "Data").Visual.GetStyle<TextBlockStyle>();
            var input = categories.Single(item => item.Category == "Input").Visual.GetStyle<TextBlockStyle>();
            Assert.AreEqual(theme.Success, data.Background);
            Assert.AreEqual(theme.Warning, input.Background);
            Assert.AreNotEqual(data.Background, input.Background);
            Assert.AreEqual(theme.Background, data.Foreground);
            Assert.AreEqual(data.Foreground, input.Foreground);
            Assert.IsTrue(data.FillBackground);
            Assert.IsTrue(input.FillBackground);
            foreach (var item in palette) {
                var category = categories.Single(category => category.Category == TuiControlCatalog.Describe(item.Type).Category);
                var button = Assert.IsInstanceOfType<Button>(((TooltipHost)item.Visual).Content);
                Assert.AreEqual(category.Visual.GetStyle<TextBlockStyle>().Background, button.Content!.GetStyle<TextBlockStyle>().Foreground);
                Assert.AreNotEqual(category.Visual.GetStyle<TextBlockStyle>().TextStyle, button.Content.GetStyle<TextBlockStyle>().TextStyle);
            }
            Assert.AreEqual("DATA", ((TextBlock)categories.Single(item => item.Category == "Data").Visual).Text);
        }
    }

    [TestMethod]
    [DataRow("TextBox")]
    [DataRow("TextArea")]
    [DataRow("Grid")]
    public async Task SearchFindsControlsBeyondTheViewportAndEnterPrefersAnExactName(string name) {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        var search = Field<TextBox>(designer, "_paletteSearch");
        var scroll = Field<ScrollViewer>(designer, "_paletteViewport");
        scroll.VerticalOffset = 50;
        search.Text = name;
        ui.Tick();
        Assert.AreEqual(0, scroll.VerticalOffset);
        Assert.IsTrue(Field<TextBlock>(designer, "_paletteHeading").Text!.StartsWith("FOUND · ", StringComparison.Ordinal));
        ui.App.Focus(search);
        ui.Key(TerminalKey.Enter, () => designer.Document.Root.Children.Count == 4);
        Assert.AreEqual(name, designer.Document.Root.Children[^1].Type);
    }

    [TestMethod]
    [DataRow(80, 40)]
    [DataRow(100, 40)]
    [DataRow(120, 40)]
    public async Task CanvasUsesRemainingWidthWithControlsRightAndShallowPropertiesBelow(int width, int height) {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { }, path: new string('x', 160) + ".tui.psd1");
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick(), width, height);
        ui.Tick();
        var canvas = Field<Padder>(designer, "_previewHost");
        var sidebar = Field<Border>(designer, "_sidebar");
        var properties = Field<Padder>(designer, "_inspectorHost");
        Assert.AreEqual(0, canvas.Bounds.X);
        Assert.AreEqual(1, canvas.Bounds.Y, "The entire header must occupy exactly one row, even with a long filename.");
        var heading = Field<TextBlock>(designer, "_heading");
        Assert.AreEqual(1, heading.Bounds.Height);
        var headerButtons = designer.Root.EnumerateVisualsDepthFirst().OfType<TooltipHost>().Where(host => host.Bounds.Y == 0).ToArray();
        Assert.HasCount(8, headerButtons);
        foreach (var button in headerButtons) {
            Assert.AreEqual(0, button.Bounds.Y);
            Assert.AreEqual(1, button.Bounds.Height);
            Assert.IsLessThanOrEqualTo(width, button.Bounds.Right);
            var icon = Assert.IsInstanceOfType<TextBlock>(((Button)button.Content!).Content);
            Assert.Contains(icon.Text!, ui.Output, "Every toolbar icon must actually render, including symbols wider than one cell.");
        }
        Assert.AreEqual(20, sidebar.Bounds.Width);
        Assert.AreEqual(width, sidebar.Bounds.Right);
        Assert.IsLessThanOrEqualTo(sidebar.Bounds.X, canvas.Bounds.Right);
        Assert.AreEqual(width, properties.Bounds.Width);
        Assert.IsGreaterThanOrEqualTo(canvas.Bounds.Bottom, properties.Bounds.Y);
        Assert.AreEqual(4, properties.Bounds.Height);
        Assert.AreEqual(height, properties.Bounds.Bottom, "No footer may reserve space below the properties panel.");
        Assert.AreEqual(width - 21, canvas.Bounds.Width);
        Assert.IsFalse(designer.Root.EnumerateVisualsDepthFirst().Contains(designer.Tree), "The sidebar is a control palette, not a layout tree.");
        Assert.IsGreaterThanOrEqualTo(8, canvas.Bounds.Height);
        Assert.IsFalse(designer.Dirty, "View layout must not modify the document.");
    }

    [TestMethod]
    public async Task AlwaysVisiblePropertiesFollowSelectionAndEscapeDismissesUntilNextSelection() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        var panel = Field<Padder>(designer, "_inspectorHost");
        designer.Tree.SelectedIndex = 1;
        ui.Tick();
        Assert.AreEqual("Welcome to your TUI", ((TextArea)Editor(designer, "Text")).Text);
        Assert.AreEqual(4, panel.Bounds.Height);
        Assert.AreEqual(1, Editor(designer, "Text").Bounds.Height);
        Assert.AreEqual(panel.Bounds.Bottom, Editor(designer, "Text").Bounds.Bottom, "The panel must end at the value row, without blank rows below it.");
        ui.Key(TerminalKey.F3, () => ui.App.FocusedElement == Editor(designer, "Id"));
        ui.Key(TerminalKey.Escape, () => !panel.IsVisible);
        Assert.AreSame(Field<Padder>(designer, "_previewHost"), ui.App.FocusedElement);
        designer.Tree.SelectedIndex = 2;
        ui.Tick();
        Assert.IsTrue(panel.IsVisible);
        Assert.AreEqual("World", ((TextArea)Editor(designer, "Text")).Text);
        Assert.Contains("root / name", Field<TextBlock>(designer, "_selectionCaption").Text!);
        Assert.IsFalse(designer.Dirty);
    }

    [TestMethod]
    [DataRow("Always", true)]
    [DataRow("OnDemand", false)]
    [DataRow("Hidden", false)]
    public async Task PropertyVisibilityAndHeightAreRememberedOutsideTheDocument(string mode, bool visible) {
        string directory = Directory.CreateTempSubdirectory("fullscreen-designer-").FullName;
        try {
            string path = Path.Combine(directory, "designer.json");
            using (var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { }, preferencesPath: path)) {
                await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
                designer.SetPropertiesMode(Enum.Parse<DesignerPropertiesMode>(mode));
                typeof(TuiDesignerApp).GetMethod("ResizeInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(designer, [2]);
                Assert.IsFalse(designer.Dirty);
            }
            using var restored = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { }, preferencesPath: path);
            await using var next = new DesignerUxTests.UiSession(restored.Root, () => restored.Tick());
            restored.Tree.SelectedIndex = 1;
            next.Tick();
            var panel = Field<Padder>(restored, "_inspectorHost");
            Assert.AreEqual(visible, panel.IsVisible);
            Assert.AreEqual(7, TuiDesignerPreferences.Load(path).Height);
            if (!visible) {
                next.Key(TerminalKey.F3, () => panel.IsVisible && next.App.FocusedElement == Editor(restored, "Id"));
            }
            Assert.AreEqual(4, panel.Bounds.Height, "A remembered height is a maximum; simple fields must not leave empty rows.");
            Assert.IsFalse(restored.Document.ToJson().Contains("OnDemand", StringComparison.Ordinal));
            Assert.IsFalse(restored.Dirty);
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LayoutGroupEditsRemainUndoableAndValidationRevealsAHiddenPanel() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        var width = (TextBox)Editor(designer, "Width");
        ui.Activate(Button(designer, "Layout"), () => width.Parent!.IsVisible);
        ui.Key(TerminalKey.F3, () => ui.App.FocusedElement == width);
        width.Text = "24";
        Assert.IsTrue(designer.ApplyProperties());
        Assert.AreEqual(24, designer.Document.Root.Width);
        designer.Undo();
        Assert.IsNull(designer.Document.Root.Width);
        designer.SetPropertiesMode(DesignerPropertiesMode.Hidden);
        width.Text = "bad width";
        Assert.IsFalse(designer.ApplyProperties());
        ui.Tick();
        Assert.IsTrue(Field<Padder>(designer, "_inspectorHost").IsVisible);
        Assert.AreSame(width, ui.App.FocusedElement);
        Assert.AreEqual("bad width", width.Text);
        Assert.IsNull(designer.Document.Root.Width);
    }

    [TestMethod]
    public async Task CanvasRightClickSelectsTheClickedControlAndKeyboardOpensTheSameActions() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        await using var ui = new DesignerUxTests.UiSession(designer.Root, () => designer.Tick());
        var text = designer.Preview.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(label => label.Text == "Welcome to your TUI");
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Right, text.Bounds.X + 1, text.Bounds.Y,
            () => designer.Tree.SelectedIndex == 1 && ui.Output.Contains("Duplicate", StringComparison.Ordinal));
        ui.Key(TerminalKey.Escape, () => ui.App.FocusedElement?.GetType().Name != "ContextMenuList");
        ui.Key(TerminalKey.F7, () => ui.App.FocusedElement?.GetType().Name == "ContextMenuList");
        Assert.IsFalse(designer.Dirty);
    }

    [TestMethod]
    public void DuplicateAndWrapKeepUniqueIdsAndUndoRestoresTheOriginalStructure() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        designer.Tree.SelectedIndex = 2;
        designer.Tick();
        typeof(TuiDesignerApp).GetMethod("DuplicateSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(designer, null);
        CollectionAssert.AreEqual(new[] { "heading", "name", "name_copy", "submit" }, designer.Document.Root.Children.Select(node => node.Id).ToArray());
        Assert.AreEqual("World", designer.Document.Root.Children[2].Text);
        typeof(TuiDesignerApp).GetMethod("WrapSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(designer, ["HStack"]);
        Assert.AreEqual("HStack", designer.Document.Root.Children[2].Type);
        Assert.AreEqual("name_copy", designer.Document.Root.Children[2].Children.Single().Id);
        designer.Undo();
        Assert.AreEqual("name_copy", designer.Document.Root.Children[2].Id);
        designer.Undo();
        CollectionAssert.AreEqual(new[] { "heading", "name", "submit" }, designer.Document.Root.Children.Select(node => node.Id).ToArray());
    }
}
