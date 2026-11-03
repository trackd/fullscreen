using System.Collections;
using fullscreen.Themes;
using fullscreen.Tables;
using fullscreen.Files;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ThemeTests {
    [TestMethod]
    public async Task NativeInputsInheritResolvedPaletteWithoutLiteralColorOverridesAcrossStates() {
        var input = new TextBox { Placeholder = "Loaded event metadata", MinWidth = 30 };
        var other = new Button("Next");
        var root = new VStack(input, other);
        await using var ui = new DesignerUxTests.UiSession(root, () => { });
        var palettes = ThemeCatalog.Names.Select(ThemeCatalog.Load).Append(
            ThemeCatalog.FromDictionary(new Hashtable { ["Base"] = "Light Modern", ["Panel"] = "#abc", ["Foreground"] = "#123" }));
        foreach (var palette in palettes) {
            palette.Apply(root);
            Assert.AreSame(palette.Controls, input.GetTheme(), palette.Name);
            var style = input.GetStyle<TextBoxStyle>();
            Assert.IsNull(style.Background, "Native state fills must not be pinned by literal style overrides.");
            ui.App.Focus(other);
            ui.Tick();
            Assert.IsFalse(input.HasFocus);
            Assert.IsTrue(style.BackgroundStyle(input.GetTheme(), input.HasFocus).TryGetBackground(out var normal));
            Assert.AreEqual(palette.Controls.InputFill, normal);
            Assert.AreNotEqual(palette.Background, normal, palette.Name);
            ui.App.Focus(input);
            ui.Tick();
            Assert.IsTrue(input.HasFocus);
            Assert.IsTrue(style.BackgroundStyle(input.GetTheme(), input.HasFocus).TryGetBackground(out var focused));
            Assert.AreEqual(palette.Controls.InputFillFocused, focused);
            Assert.AreNotEqual(normal, focused, palette.Name);
            input.IsEnabled = false;
            ui.Tick();
            Assert.IsFalse(input.IsEnabled);
            Assert.AreEqual(palette.Controls.Disabled, input.GetTheme().Disabled);
            input.IsEnabled = true;
        }
    }

    [TestMethod]
    public void InlineThemesRenderAllSurfaceFamiliesWithoutGlobalState() {
        Assert.HasCount(20, ThemeCatalog.Names);
        var original = new TableSurface(new TableModel(["Name"]), "Default");
        foreach (string name in ThemeCatalog.Names) {
            var theme = ThemeCatalog.Load(name);
            foreach (ThemedSurface surface in new ThemedSurface[] { new TableSurface(new TableModel(["Name"]), name), new FileManagerSurface(), new MonitorSurface() }) {
                theme.Apply(surface);
                Assert.AreSame(theme, surface.Palette);
                Assert.AreEqual(80, VisualSnapshotRenderer.Render(surface, 80, 24).Width);
                Assert.AreEqual(theme.IsDefault ? XenoAtom.Terminal.UI.Styling.Theme.Default.Background : theme.Background, surface.GetTheme().Background);
            }
        }
        Assert.AreSame(ThemePalette.Default, original.Palette);
        Assert.AreEqual(Color.Rgb(13, 20, 32), original.Palette.Background);
        Assert.AreEqual(Color.Rgb(255, 255, 255), ThemeCatalog.Load("Light (Visual Studio)").Background);
        Assert.AreEqual(Color.Rgb(30, 30, 30), ThemeCatalog.Load("Visual Studio Dark").Background);
    }


    [TestMethod]
    public void CustomPaletteInheritsNamedBaseAndDoesNotMutateIt() {
        var theme = ThemeCatalog.FromDictionary(new Hashtable { ["Base"] = "Light Modern", ["Name"] = "My light", ["background"] = "#123", ["Accent"] = "#ABCDEF", ["SelectedText"] = "#fff" });
        Assert.AreEqual("My light", theme.Name);
        Assert.AreEqual(Color.Rgb(17, 34, 51), theme.Background);
        Assert.AreEqual(Color.Rgb(171, 205, 239), theme.Accent);
        theme.Selected.TryGetForeground(out var text);
        Assert.AreEqual(Color.Rgb(255, 255, 255), text);
        Assert.AreEqual(ThemeCatalog.Load("Light Modern").Success, theme.Success);
        Assert.AreEqual(Color.Rgb(255, 255, 255), ThemeCatalog.Load("Light Modern").Background);
        Assert.AreSame(ThemePalette.Default, ThemeCatalog.FromDictionary(new Hashtable()));
        var surface = new TableSurface(new TableModel(["Name"]), "Fixture");
        theme.Apply(surface);
        ThemePalette.Default.Apply(surface);
        Assert.AreSame(ThemePalette.Default, surface.Palette);
        Assert.AreSame(XenoAtom.Terminal.UI.Styling.Theme.Default, surface.GetTheme());
    }

}
