using fullscreen.Themes;
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
public sealed class DesignerInteractionTests {
    [TestMethod]
    public void DropsMoveControlsAndRejectCyclesWithoutChangingTheDocument() {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        Assert.IsTrue(designer.Place("HStack", null, "root"));
        string container = designer.Document.Root.Children[^1].Id;
        Assert.IsTrue(designer.Place(null, "name", container));
        Assert.AreEqual("name", designer.Document.Root.Children[^1].Children[0].Id);
        string before = designer.Document.ToJson();
        Assert.IsFalse(designer.Place(null, "root", container));
        Assert.AreEqual(before, designer.Document.ToJson());
        designer.Undo();
        Assert.IsTrue(designer.Document.Root.Children.Any(node => node.Id == "name"));
        designer.Redo();
        Assert.AreEqual("name", designer.Document.Root.Children[^1].Children[0].Id);
    }
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PaletteClickAndDragPlaceAControlInThePreview(bool drag) {
        using var designer = new TuiDesignerApp(TuiDocument.Starter(), (_, _) => { });
        using var backend = new InMemoryTerminalBackend(new TerminalSize(180, 48));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(designer.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            designer.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Tick();
            designer.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Single(input => input.Placeholder == "Search controls").Text = "Button";
            Tick();
            var palette = designer.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().First(item => item.Text == "Button" && item.Parent is Button);
            var target = designer.Preview.EnumerateVisualsDepthFirst().OfType<TextBlock>().First(item => item.Text == "Welcome to your TUI");
            void Send(TerminalMouseKind kind, int x, int y) {
                backend.PushEvent(new TerminalMouseEvent { Kind = kind, Button = TerminalMouseButton.Left, X = x, Y = y });
                for (int i = 0; i < 15; i++) {
                    Tick();
                    Thread.Sleep(2);
                }
            }
            Send(TerminalMouseKind.Down, palette.Bounds.X + 1, palette.Bounds.Y);
            if (drag) {
                Send(TerminalMouseKind.Move, target.Bounds.X + 1, target.Bounds.Y);
            }

            Send(TerminalMouseKind.Up, drag ? target.Bounds.X + 1 : palette.Bounds.X + 1, drag ? target.Bounds.Y : palette.Bounds.Y);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return designer.Document.Root.Children.Count == 4; }, 3000));
            Assert.IsTrue(designer.Document.Root.Children.Any(node => node.Type == "Button" && node.Id != "submit"));
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public void StylingSurvivesRoundTrip() {
        var document = TuiDocument.Starter();
        var heading = document.Root.Children[0];
        heading.Foreground = "#12abef";
        heading.Background = "#112233";
        heading.Bold = true;
        heading.HorizontalAlignment = "Center";
        var clone = TuiDocument.FromJson(document.ToJson());
        Assert.AreEqual("#12abef", clone.Root.Children[0].Foreground);
        using var app = new CustomTuiApp(clone);
        app.ApplyTheme(ThemePalette.Default);
        VisualSnapshotRenderer.Render(app.Root, 100, 30);
        Assert.AreEqual(Align.Center, app.Controls[heading.Id].HorizontalAlignment);
        // Script generation uses PowerShell's own quoting APIs and is exercised
        // inside a real PowerShell host by DesignerScaffold.Tests.ps1.
        heading.Foreground = "invalid";
        Assert.Throws<ArgumentException>(() => document.Validate());
    }
}
