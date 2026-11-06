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

    [TestMethod]
    public void SyntaxAndChartOverridesAreIndependentAndInheritEveryUnspecifiedRole() {
        foreach (string name in ThemeCatalog.Names) {
            var basis = ThemeCatalog.Load(name);
            for (int index = 0; index < ThemePalette.RoleNames.Length; index++) {
                string role = ThemePalette.RoleNames[index];
                var custom = ThemeCatalog.FromDictionary(new Hashtable { ["Base"] = name, [role.ToLowerInvariant()] = "#123" });
                for (int other = 0; other < ThemePalette.RoleNames.Length; other++) {
                    Assert.AreEqual(other == index ? Color.Rgb(17, 34, 51) : basis.RoleColor(other),
                        custom.RoleColor(other), $"{name}: {role} must not change {ThemePalette.RoleNames[other]}.");
                }
                Assert.AreEqual(basis.Success, custom.Success);
                Assert.AreEqual(basis.Error, custom.Error);
                Assert.AreNotEqual(Color.Rgb(17, 34, 51), ThemeCatalog.Load(name).RoleColor(index));
            }
        }
    }

    [TestMethod]
    public void OriginalThemeScopesInheritanceAndChartFallbacksHaveKnownColors() {
        var dark = ThemeCatalog.Load("Dark+");
        Assert.AreEqual(Color.Rgb(78, 201, 176), dark.Type);
        Assert.AreEqual(Color.Rgb(206, 145, 120), dark.StringLiteral);
        Assert.AreEqual(Color.Rgb(181, 206, 168), dark.Number);
        Assert.AreEqual(dark.Type, ThemeCatalog.Load("Dark Modern").Type);
        var monokai = ThemeCatalog.Load("Monokai");
        Assert.AreEqual(Color.Rgb(249, 38, 114), monokai.Keyword);
        Assert.AreEqual(Color.Rgb(253, 151, 31), monokai.Parameter);
        Assert.AreEqual(Color.Rgb(106, 126, 200), monokai.ChartBlue);
        Assert.AreEqual(Color.Rgb(89, 164, 249), dark.ChartBlue);
        Assert.AreEqual(dark.ChartSequentialLow, dark.SequentialColor(0));
        Assert.AreEqual(dark.ChartSequentialHigh, dark.SequentialColor(1));
        Assert.AreEqual(dark.ChartNegative, dark.DivergingColor(0));
        Assert.AreEqual(dark.ChartNeutral, dark.DivergingColor(.5f));
        Assert.AreEqual(dark.ChartPositive, dark.DivergingColor(1));
    }

    [TestMethod]
    public void JsonUsesIndependentPropertyStringNumberKeywordAndPunctuationColors() {
        var palette = ThemeCatalog.FromDictionary(new Hashtable {
            ["Property"] = "#123456",
            ["String"] = "#234567",
            ["Number"] = "#345678",
            ["Keyword"] = "#456789",
            ["Punctuation"] = "#56789A"
        });
        var surface = new TableSurface(new TableModel(["Value"]), "JSON");
        palette.Apply(surface);
        surface.SetDocument("""{"name":"hello","count":42,"ready":true}""");
        string svg = CellBufferSvgExporter.Export(VisualSnapshotRenderer.Render(surface, 100, 20));
        foreach (var color in new[] { palette.Property, palette.StringLiteral, palette.Number, palette.Keyword, palette.Punctuation }) {
            Assert.Contains($"rgb({color.R},{color.G},{color.B})", svg);
        }
    }

}
