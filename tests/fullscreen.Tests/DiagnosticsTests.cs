using System.Reflection;
using System.Text.Json;
using fullscreen.Custom;
using fullscreen.Files;
using fullscreen.Monitoring;
using fullscreen.Tables;
using fullscreen.Hosting;
using fullscreen.Charts;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticsTests {
    [TestMethod]
    [DataRow(120, 40)]
    [DataRow(80, 32)]
    public async Task F12TerminalDetailsTrackRendererGeometryAndKeepInputFocus(int width, int height) {
        using var application = new CustomTuiApp(new TuiDocument { Root = new TuiNode { Id = "draft", Type = "TextBox", Text = "Keep draft" } });
        var chrome = new FullscreenChrome(application, ThemePalette.Default);
        await using var ui = new DesignerUxTests.UiSession(chrome, () => {
            application.Tick();
            chrome.UpdateDiagnostics(ChartRenderer.Native, null);
        }, width: width, height: height);
        ui.App.Focus(application.Controls["draft"]);
        var focus = ui.App.FocusedElement;
        Assert.AreEqual(default, chrome.DiagnosticsBounds);
        ui.Key(TerminalKey.F12, () => TerminalOverlayBounds.DebugVisible(ui.App));
        ui.Tick();
        ui.Tick();
        Assert.AreEqual(5, chrome.DiagnosticsBounds.Height);
        var native = TerminalOverlayBounds.Debug(ui.App);
        Assert.AreEqual(native.Bottom - 1, chrome.DiagnosticsBounds.Y, "Details must share the existing overlay's bottom border.");
        Assert.AreEqual(native.Width, chrome.DiagnosticsBounds.Width);
        Assert.IsLessThan(height, chrome.DiagnosticsBounds.Bottom, "Details must not be pinned to the viewport footer.");
        Assert.AreSame(focus, ui.App.FocusedElement);
        Assert.Contains($"{width}x{height} cells", ui.Output);
        Assert.Contains("Native -> Native", ui.Output);
        using var graphics = new SixelChartPresenter(new TerminalGraphicsCapabilities {
            SupportState = TerminalGraphicsSupportState.Confirmed,
            PreferredProtocol = TerminalGraphicsProtocol.None,
            DetectionSource = "VT DA1"
        }, new TerminalPixelMetrics(960, 640, 8, 16, 120, 40)) {
            UsesFallbackMetrics = true
        };
        chrome.UpdateDiagnostics(ChartRenderer.Auto, graphics);
        string text = string.Join("\n", chrome.DiagnosticsLines);
        Assert.Contains("Auto -> Sixel", text);
        Assert.Contains("source=VT DA1", text);
        Assert.Contains("cell 8x16px; surface 960x640px (8x16 fallback)", text);
        Assert.AreEqual(application.Root.Bounds, chrome.Bounds, "Diagnostics must not reduce the application canvas.");
        var details = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TerminalDiagnosticsOverlay>().Single();
        Assert.IsFalse(details.IsHitTestVisible);
        Assert.AreEqual(chrome.DiagnosticsBounds, TerminalOverlayBounds.Window(details), "Graphics must mask only the painted details, not the viewport-sized host.");
        ui.Key(TerminalKey.F12, () => chrome.DiagnosticsBounds.Height == 0);
        Assert.AreSame(focus, ui.App.FocusedElement);
        var modalEditor = new TextBox("Modal draft");
        var dialog = new Dialog { Content = modalEditor, IsModal = true, Width = 100, Height = 30 };
        dialog.Show();
        ui.Tick();
        ui.App.Focus(modalEditor);
        ui.Key(TerminalKey.F12, () => chrome.DiagnosticsBounds.Height > 0);
        ui.Tick();
        Assert.AreSame(modalEditor, ui.App.FocusedElement);
        Assert.AreSame(details, ui.App.Root.EnumerateVisualsDepthFirst().Last(visual => ReferenceEquals(visual.Parent, ui.App.Root)), "The complete F12 overlay must stay above modal windows.");
        ui.Key(TerminalKey.F12, () => chrome.DiagnosticsBounds.Height == 0);
        Assert.AreEqual("Modal draft", modalEditor.Text);
        dialog.Close();
        Assert.AreEqual("Keep draft", ((TextBox)application.Controls["draft"]).Text);
    }

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
        await using var ui = new DesignerUxTests.UiSession(root, () => {
            custom?.Tick();
            designer?.Tick();
        });
        var focus = kind == "Custom" ? custom!.Controls["notes"] : ui.App.FocusedElement;
        if (focus is not null) {
            ui.App.Focus(focus);
        }
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
        if (designer is not null) {
            Assert.IsFalse(designer.Dirty);
        }
        if (root is MonitorSurface monitor) {
            Assert.IsFalse(monitor.ExitRequested);
            Assert.AreEqual(MonitorView.Processes, monitor.View);
        }
        if (root is FileManagerSurface files) {
            Assert.IsFalse(files.ExitRequested);
        }

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
