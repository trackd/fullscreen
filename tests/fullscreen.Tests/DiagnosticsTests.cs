using System.Reflection;
using System.Text.Json;
using fullscreen.Custom;
using fullscreen.Files;
using fullscreen.Monitoring;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticsTests {
    [TestMethod]
    [DataRow("Monitor")]
    [DataRow("Files")]
    [DataRow("Table")]
    [DataRow("Custom")]
    [DataRow("Designer")]
    public async Task F12TogglesDiagnosticsWithoutChangingFocusOrApplicationState(string kind) {
        using var custom = kind == "Custom" ? new CustomTuiApp(new TuiDocument { Root = new TuiNode { Id = "notes", Type = "TextArea", Text = "Keep this draft", Height = 4 } }) : null;
        using var designer = kind == "Designer" ? new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { }) : null;
        Visual root = kind switch {
            "Monitor" => new MonitorSurface(),
            "Files" => new FileManagerSurface(),
            "Table" => new TableSurface(new TableModel(["Name"]), "Fixture"),
            "Custom" => custom!.Root,
            _ => designer!.Root
        };
        await using var ui = new DesignerUxTests.UiSession(root, () => { custom?.Tick(); designer?.Tick(); });
        var focus = kind == "Custom" ? custom!.Controls["notes"] : ui.App.FocusedElement;
        if (focus is not null) { ui.App.Focus(focus); }
        var visible = typeof(TerminalApp).GetField("_debugOverlayVisible", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool IsVisible() =>
            (bool)visible.GetValue(ui.App)!;
        Assert.IsFalse(IsVisible());
        ui.Key(TerminalKey.F12, IsVisible);
        Assert.AreSame(focus, ui.App.FocusedElement);
        Assert.Contains("FPS", ui.Output);
        ui.Key(TerminalKey.F12, () => !IsVisible());
        Assert.AreSame(focus, ui.App.FocusedElement);
        if (custom is not null) {
            Assert.AreEqual("Keep this draft", ((TextArea)custom.Controls["notes"]).Text);
            Assert.IsTrue(custom.Tick());
        }
        if (designer is not null) { Assert.IsFalse(designer.Dirty); }
        if (root is MonitorSurface monitor) { Assert.IsFalse(monitor.ExitRequested); Assert.AreEqual(MonitorView.Processes, monitor.View); }
        if (root is FileManagerSurface files) { Assert.IsFalse(files.ExitRequested); }

        var editor = new TextBox("Modal draft");
        var dialog = new Dialog { Content = editor, IsModal = true, Width = 45, Height = 6 };
        dialog.Show();
        ui.Tick();
        ui.App.Focus(editor);
        ui.Key(TerminalKey.F12, IsVisible);
        Assert.AreSame(editor, ui.App.FocusedElement);
        ui.Key(TerminalKey.F12, () => !IsVisible());
        Assert.AreSame(editor, ui.App.FocusedElement);
        Assert.AreEqual("Modal draft", editor.Text);
        dialog.Close();
    }

    [TestMethod]
    public void CompactScaffoldPreservesDefaultsAndNestedStateWhileRemovingLayoutNoise() {
        var document = TuiDocument.FromJson("""{"State":{"Root":{"Id":"data","Type":"Text","Enabled":true,"Text":""},"Children":[{"Value":0}]},"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"input","Type":"TextBox","Enabled":false,"Width":32,"Text":"Draft","TabTitle":"Input"},{"Id":"code","Type":"Code","Language":"powershell"}]}}""");
        string compact = TuiDataFile.Generate(document, compact: true);
        using var expected = JsonDocument.Parse(document.ToJson());
        using var actual = JsonDocument.Parse(TuiDataFile.Parse(compact).ToJson());
        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), "Compact output must preserve layout and arbitrary nested state values.");
        Assert.IsLessThan(TuiDataFile.Generate(document).Length / 2, compact.Length);
        Assert.DoesNotContain("'RefreshInterval'", compact);
        Assert.Contains("'Enabled' = $false", compact);
        Assert.Contains("'Language' = 'powershell'", compact);
        Assert.Contains("'Type' = 'VStack'", compact);
    }
}
