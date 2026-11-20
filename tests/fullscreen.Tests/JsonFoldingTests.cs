using System.Reflection;
using fullscreen.Tables;
using XenoAtom.Terminal;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class JsonFoldingTests {
    [TestMethod]
    public async Task JsonFoldsNavigateWithKeysAndMouseRetainStateSearchHiddenValuesAndCopyOriginal() {
        const string json = """
            {
              "items": [
                {
                  "text": "needle with literal [braces] and escaped \"quote\""
                }
              ],
              "empty": {}
            }
            """;
        var surface = new TableSurface(new TableModel(["Value"]), "JSON") { EnableJsonFolding = true, Minimal = true };
        surface.SetDocument(json);
        string[] Lines() => (string[])typeof(TableSurface).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
        int expanded = Lines().Length;
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 90, 24);
        ui.App.Focus(surface);
        ui.Key(TerminalKey.Enter, () => Lines().Length == 1);
        ui.Key(TerminalKey.Unknown, () => surface.SelectedDocumentText == json, TerminalModifiers.Ctrl, 'a');
        ui.Key(TerminalKey.Escape, () => surface.SelectedDocumentText.Length == 0);
        surface.SetDocument(null);
        surface.SetDocument(json);
        Assert.HasCount(1, Lines(), "Returning to the same snapshot retains collapsed branches.");
        ui.Key(TerminalKey.Right, () => Lines().Length == expanded);
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Left, () => Lines().Length < expanded);
        Assert.Contains("…", Lines()[1]);
        ui.Text("/", () => true);
        ui.Text("needle", () => Lines().Length == expanded);
        ui.Key(TerminalKey.Escape, () => true);
        ui.Key(TerminalKey.Home, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 0, 0, () => Lines().Length == 1);
        surface.SetDocument("{ truncated");
        Assert.Contains("truncated", Lines()[0]);
        ui.Key(TerminalKey.Enter, () => Lines().Length == 1);
    }
}
