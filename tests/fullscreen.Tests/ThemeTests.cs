using System.Collections;
using fullscreen.Themes;
using fullscreen.Tables;
using fullscreen.Files;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ThemeTests {
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
