using fullscreen.Monitoring;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorDialogTests {
    [TestMethod]
    [DataRow("text")]
    [DataRow("key")]
    [DataRow("child")]
    [DataRow("ctrl")]
    public async Task ProcessDetailsQuitKeyOnlyDismissesTheDialog(string input) {
        var surface = new MonitorSurface();
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        surface.ShowProcessDetails(new(42, "fixture", 0, 1000, 1));
        ui.Tick();
        Assert.IsNotNull(Dialog(ui));
        if (input == "child") {
            var scroll = Dialog(ui)!.EnumerateVisualsDepthFirst().OfType<ScrollViewer>().Single();
            ui.App.Focus(scroll);
        }
        if (input is "text" or "child") {
            ui.Text("q", () => Dialog(ui) is null);
        }
        else {
            ui.Key(TerminalKey.Unknown, () => Dialog(ui) is null,
                input == "ctrl" ? TerminalModifiers.Ctrl : default,
                input == "ctrl" ? TerminalChar.CtrlQ : 'q');
        }
        ui.Tick();
        Assert.IsFalse(surface.ExitRequested);
        ui.Text("q", () => surface.ExitRequested);
    }

    [TestMethod]
    [DataRow(24, false)]
    [DataRow(35, false)]
    [DataRow(40, false)]
    [DataRow(80, false)]
    [DataRow(40, true)]
    public async Task UserActionsUseRightAlignedNativeButtonsAndKeepLogoffConfirmation(int width, bool minimal) {
        var surface = new MonitorSurface { View = MonitorView.Users, ActionsEnabled = true, Minimal = minimal };
        surface.Frame.Platform = "Windows";
        surface.Frame.UserCount = 1;
        surface.Frame.Users[0] = new("Ada", "Console", "Active", "", "", 7, LogonTime: DateTimeOffset.UnixEpoch);
        int requests = 0;
        surface.SessionLogoffRequested += _ => requests++;
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, width, 16);
        Assert.AreSame(surface, ui.App.FocusedElement);
        var buttons = surface.EnumerateVisualsDepthFirst().OfType<Button>().ToArray();
        var logoff = buttons.Single(button => button.Content is TextBlock text && text.Text == "Log off…");
        var mark = buttons.Single(button => button.Content is TextBlock text && text.Text == "Mark all");
        if (width < 35) {
            Assert.IsFalse(logoff.IsVisible);
            Assert.IsFalse(mark.IsVisible);
            ui.Text("x", () => Dialog(ui) is not null);
            ui.Text("q", () => Dialog(ui) is null);
            Assert.IsFalse(surface.ExitRequested);
            Assert.AreEqual(0, requests);
            return;
        }
        Assert.IsTrue(logoff.IsVisible);
        Assert.AreEqual(width - 1, logoff.Bounds.Right);
        Assert.AreEqual(minimal ? 0 : 2, logoff.Bounds.Y);
        Assert.IsTrue(mark.IsVisible);
        if (mark.IsVisible) {
            Assert.IsLessThan(logoff.Bounds.X, mark.Bounds.Right);
            ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, mark.Bounds.X + 1, mark.Bounds.Y, () => mark.IsHovered);
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, mark.Bounds.X + 1, mark.Bounds.Y, () => mark.IsPressed);
            Assert.AreEqual(0, surface.MarkedSessionCount);
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, mark.Bounds.X + 1, mark.Bounds.Y, () => surface.MarkedSessionCount == 1);
            Assert.IsFalse(mark.IsPressed);
            Assert.IsTrue(mark.HasFocus);
            ui.Activate(mark, () => surface.MarkedSessionCount == 0);
        }
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, logoff.Bounds.X + 1, logoff.Bounds.Y, () => logoff.IsPressed);
        Assert.IsNull(Dialog(ui));
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, logoff.Bounds.X + 1, logoff.Bounds.Y, () => Dialog(ui) is not null);
        Assert.AreEqual(0, requests);
        Assert.AreEqual("Select sessions to log off", ((TextBlock)Dialog(ui)!.Title!).Text);
        ui.Text("q", () => Dialog(ui) is null);
        Assert.AreEqual(0, requests);
        Assert.IsFalse(surface.ExitRequested);
        Assert.DoesNotContain("[Log off", ui.Output);
        Assert.DoesNotContain("[Mark all", ui.Output);
    }

    [TestMethod]
    [DataRow("Default")]
    [DataRow("Light (Visual Studio)")]
    public async Task HelpUsesActivePaletteAndQOrEscapeOnlyDismissesTheOverlay(string themeName) {
        var surface = new MonitorSurface();
        var palette = ThemeCatalog.Load(themeName);
        palette.Apply(surface);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Key(TerminalKey.F1, () => Dialog(ui) is not null);
        var dialog = Dialog(ui)!;
        Assert.AreEqual(palette.Background, dialog.GetTheme().Background);
        Assert.AreEqual(palette.Controls.PopupSurface, dialog.GetTheme().PopupSurface);
        ui.Text("q", () => Dialog(ui) is null);
        Assert.IsFalse(surface.ExitRequested);
        ui.Key(TerminalKey.F1, () => Dialog(ui) is not null);
        ui.Text("Q", () => Dialog(ui) is null);
        Assert.IsFalse(surface.ExitRequested);
        ui.Key(TerminalKey.F1, () => Dialog(ui) is not null);
        ui.Key(TerminalKey.Escape, () => Dialog(ui) is null);
        Assert.IsFalse(surface.ExitRequested);
        ui.Text("q", () => surface.ExitRequested);
    }

    [TestMethod]
    public void HelpUsesOneBindingPerRowWhenHeightAllowsAndCompactsOtherwise() {
        var surface = new MonitorSurface();
        string tall = surface.HelpText(96, 60), compact = surface.HelpText(96, 24);
        Assert.Contains("C: columns\nF6: sort\nR: reverse sort", tall);
        Assert.Contains("PgUp/PgDn: select or scroll a page", tall);
        Assert.Contains("←/→: process columns (Shift in tree) / user branches", tall);
        Assert.IsLessThan(tall.Split('\n').Length, compact.Split('\n').Length);
        Assert.Contains("C: columns · F6: sort", compact);
    }

    [TestMethod]
    [DataRow(40, 12)]
    [DataRow(96, 24)]
    [DataRow(120, 60)]
    public async Task HelpKeepsCloseButtonInsideTheTerminal(int width, int height) {
        var surface = new MonitorSurface();
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, width, height);
        ui.Key(TerminalKey.F1, () => Dialog(ui) is not null);
        var dialog = Dialog(ui)!;
        var close = dialog.EnumerateVisualsDepthFirst().OfType<Button>().Single();
        Assert.IsGreaterThan(0, close.Bounds.Height);
        Assert.IsLessThanOrEqualTo(height, close.Bounds.Bottom);
        Assert.IsLessThanOrEqualTo(height, dialog.Bounds.Bottom);
        if (height >= 60) {
            Assert.IsGreaterThan(30, dialog.EnumerateVisualsDepthFirst().OfType<Paragraph>().Single().Bounds.Height);
        }
        ui.Key(TerminalKey.Escape, () => Dialog(ui) is null);
    }

    [TestMethod]
    public async Task UserDetailsAndActionConfirmationCanBeDismissedWithoutExecutingActions() {
        var surface = new MonitorSurface { View = MonitorView.Users, ActionsEnabled = true };
        surface.Frame.Platform = "Windows";
        surface.Frame.UserCount = 1;
        surface.Frame.Users[0] = new("HOST\\Ada", "Console", "Active", "", "", 1, LogonTime: DateTimeOffset.UtcNow);
        int requested = 0;
        surface.ActionRequested += _ => requested++;
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Text("d", () => Dialog(ui) is not null);
        ui.Text("q", () => Dialog(ui) is null);
        ui.Text("d", () => Dialog(ui) is not null);
        ui.Key(TerminalKey.Escape, () => Dialog(ui) is null);
        surface.UserSessionActions(surface.Users.Selected!)[0].Action.Invoke!();
        ui.Tick();
        Assert.IsNotNull(Dialog(ui));
        ui.Text("q", () => Dialog(ui) is null);
        Assert.AreEqual(0, requested);
        Assert.IsFalse(surface.ExitRequested);
    }

    [TestMethod]
    public async Task ColumnChooserQClosesTheOverlayWithoutQuitting() {
        var surface = new MonitorSurface();
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Text("c", () => surface.ColumnsOpen);
        ui.Text("q", () => !surface.ColumnsOpen);
        Assert.IsFalse(surface.ExitRequested);
    }
    [TestMethod]
    public async Task EditableServiceFilterKeepsTypedQAndEscapeClosesIt() {
        var surface = new MonitorSurface { View = MonitorView.Services };
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        typeof(MonitorSurface).GetMethod("EditServiceFilter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(surface, [ServiceColumn.Name]);
        ui.Tick();
        var input = Dialog(ui)!.EnumerateVisualsDepthFirst().OfType<TextBox>().Single();
        ui.Text("q", () => input.Text == "q");
        Assert.IsNotNull(Dialog(ui));
        Assert.IsFalse(surface.ExitRequested);
        ui.Key(TerminalKey.Escape, () => Dialog(ui) is null);
        Assert.IsFalse(surface.ExitRequested);
        typeof(MonitorSurface).GetMethod("EditServiceFilter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(surface, [ServiceColumn.Name]);
        ui.Tick();
        ui.Key(TerminalKey.Unknown, () => Dialog(ui) is null, TerminalModifiers.Ctrl, TerminalChar.CtrlQ);
        Assert.IsFalse(surface.ExitRequested);
    }
    private static Dialog? Dialog(DesignerUxTests.UiSession ui) =>
        ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().SingleOrDefault();
}
