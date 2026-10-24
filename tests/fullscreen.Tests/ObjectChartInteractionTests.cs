using System.Diagnostics;
using System.Management.Automation;
using System.Reflection;
using fullscreen.Charts;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ObjectChartInteractionTests {
    private static PSObject Item(double x, double y, string description) {
        var item = new PSObject();
        item.Properties.Add(new PSNoteProperty("X", x));
        item.Properties.Add(new PSNoteProperty("Y", y));
        item.Properties.Add(new PSNoteProperty("Description", description));
        return item;
    }
    [TestMethod]
    [DataRow(0, "Input order")]
    [DataRow(1, "Description")]
    [DataRow(2, "BoxPlot")]
    [DataRow(3, "All objects")]
    public async Task EachHeaderMenuUsesItsOwnColumn(int menu, string entry) {
        using var chart = new ObjectChartApp([Item(1, 2, "original")], "X", ["Y"]);
        await using var ui = new DesignerUxTests.UiSession(chart.Root, () => chart.Tick());
        int column = ObjectChartSurface.MenuColumn(menu);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, column + 2, 0, () => true);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, column + 2, 0,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == entry || text.Text == "  " + entry));
        var popup = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Popup>().Single(popup => popup.IsVisible);
        Assert.AreEqual(column, popup.Content!.Bounds.X);
        Assert.AreEqual(1, popup.Content.Bounds.Y);
    }
    [TestMethod]
    [DataRow(false, 120, 40)]
    [DataRow(true, 120, 40)]
    [DataRow(false, 60, 20)]
    public void CanvasUsesOneHeaderAndAtMostTwoFooterRows(bool minimal, int width, int height) {
        using var chart = new ObjectChartApp([Item(1, -2, "original"), Item(2, 8, "next")], "X", ["Y"]);
        chart.Surface.Minimal = minimal;
        chart.Surface.MinHeight = chart.Surface.MaxHeight = height;
        var lines = VisualSnapshotRenderer.Render(chart.Root, width, height).ToMarkupLines();
        Assert.AreEqual(1, chart.Surface.Plot.PlotBounds.Y);
        Assert.AreEqual(height - (minimal ? 3 : 4), chart.Surface.Plot.PlotBounds.Height);
        Assert.AreEqual(3, chart.Surface.Plot.PlotBounds.X, "A short numeric axis needs only three columns.");
        Assert.AreEqual(width, chart.Surface.Plot.PlotBounds.Right);
        Assert.Contains("Object #1", lines[height - (minimal ? 1 : 2)]);
        if (!minimal) { Assert.Contains("Double-click/D", lines[^1]); }
    }
    [TestMethod]
    public async Task MouseChartMenuAnchorsBelowChartAndChangesTheGraph() {
        using var chart = new ObjectChartApp([Item(1, 2, "original")], "X", ["Y"]);
        await using var ui = new DesignerUxTests.UiSession(chart.Root, () => chart.Tick());
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 25, 0, () => true);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 25, 0,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "Bars"));
        var bars = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(text => text.Text == "Bars");
        Assert.IsGreaterThanOrEqualTo(18, bars.Bounds.X, "The chart menu should open below Chart, rather than column one.");
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, bars.Bounds.X, bars.Bounds.Y,
            () => chart.Surface.GraphType == GraphKind.Bars);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, bars.Bounds.X, bars.Bounds.Y, () => true);
        Assert.IsFalse(ui.App.Root.EnumerateVisualsDepthFirst().OfType<Popup>().Any(popup => popup.IsVisible));
        ui.Text("g", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Any(text => text.Text == "Bars"));
    }
    [TestMethod]
    public async Task ChartClickSelectsNearestPointInBothDimensionsAndDoubleClickInspectsIt() {
        using var chart = new ObjectChartApp([Item(1, -10, "low original"), Item(1, 10, "high original")], "X", ["Y"], GraphKind.Scatter);
        await using var ui = new DesignerUxTests.UiSession(chart.Root, () => chart.Tick(), 100, 40);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 50, 3, () => chart.Surface.SelectedIndex == 1);
        Assert.Contains("high original", chart.Data.Objects[chart.Surface.SelectedIndex].Properties["Description"].Value!.ToString()!);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 50, 3, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 50, 3,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("high original", ui.Output);
    }
    [TestMethod]
    public async Task SixelRemainsVisibleOutsidePopupAndReturnsAfterClosingIt() {
        var metrics = new TerminalPixelMetrics(480, 480, 4, 12, 120, 40);
        var graphics = new TerminalGraphicsCapabilities {
            PreferredProtocol = TerminalGraphicsProtocol.Sixel,
            SupportedProtocols = [TerminalGraphicsProtocol.Sixel],
            SupportState = TerminalGraphicsSupportState.Confirmed,
            SupportsStaticImages = true,
            PixelMetrics = metrics
        };
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 40), new TerminalCapabilities {
            AnsiEnabled = true,
            Graphics = graphics,
            ColorLevel = TerminalColorLevel.TrueColor,
            IsOutputRedirected = false,
            IsInputRedirected = false
        });
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.DisableProbing = true;
        options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Sixel;
        options.Graphics.ForcedPixelMetrics = metrics;
        using var session = Terminal.Open(backend, options, force: true);
        using var chart = new ObjectChartApp([Item(0, -10, "low"), Item(10, 10, "high")], "X", ["Y"]);
        TerminalProbeTests.Observe(session.Instance, "\x1b[6;12;4t\x1b[?65;4c");
        using var presenter = Assert.IsInstanceOfType<SixelChartPresenter>(ChartPresenters.Create(session.Instance, ChartRenderer.Sixel, metrics));
        var root = new fullscreen.Hosting.FullscreenChrome(chart, fullscreen.Themes.ThemePalette.Default);
        await using var terminal = new TerminalApp(root, session.Instance, new TerminalAppOptions {
            HostKind = TerminalHostKind.Fullscreen,
            GraphicsPresenter = presenter,
            EnableMouse = true
        });
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            time += Stopwatch.Frequency / 10;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [time]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Tick();
            long encoded = presenter.GetDiagnosticsSnapshot().EncodedFrameCount;
            Assert.AreEqual(1L, encoded);
            var startupRefresh = new fullscreen.Hosting.StartupGraphicsRefresh();
            startupRefresh.Update(terminal, time);
            int beforeRefresh = backend.GetOutText().Length;
            startupRefresh.Update(terminal, time + Stopwatch.Frequency / 10);
            Tick();
            Assert.DoesNotContain("\x1bP0;1q", backend.GetOutText()[beforeRefresh..],
                "The startup refresh must wait for the initial console repaint to settle.");
            startupRefresh.Update(terminal, time + Stopwatch.Frequency / 4);
            Tick();
            Assert.Contains("\x1bP0;1q", backend.GetOutText()[beforeRefresh..],
                "Restore the chart after the startup cell repaint without any user input.");
            Assert.AreEqual(encoded, presenter.GetDiagnosticsSnapshot().EncodedFrameCount,
                "The startup refresh should reuse the encoded image.");
            int afterRefresh = backend.GetOutText().Length;
            startupRefresh.Update(terminal, time + Stopwatch.Frequency);
            Tick();
            Assert.DoesNotContain("\x1bP0;1q", backend.GetOutText()[afterRefresh..],
                "A static chart needs only one startup recovery, not continuous repainting.");
            int beforeClick = backend.GetOutText().Length;
            backend.PushEvent(new TerminalMouseEvent {
                Kind = TerminalMouseKind.Down,
                Button = TerminalMouseButton.Left,
                X = chart.Surface.Plot.PlotBounds.Right - 1,
                Y = chart.Surface.Plot.PlotBounds.Y
            });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return chart.Surface.SelectedIndex == 1; }, 2000));
            var background = chart.Surface.Plot.PixelBackground.ToRgb();
            Assert.Contains($"\x1b[48;2;{background.R};{background.G};{background.B}m", backend.GetOutText()[beforeClick..], "Selecting a point must preserve the themed Sixel background.");
            var popup = ContextMenuService.Show(chart.Surface, [new MenuItem("Overlay")], 25, 2);
            int before = backend.GetOutText().Length;
            Tick();
            string overlayFrame = backend.GetOutText()[before..];
            Assert.Contains("\x1bP0;1q", overlayFrame, "Opening a menu must keep the uncovered plot visible.");
            Assert.Contains("Overlay", overlayFrame);
            int start = overlayFrame.IndexOf("\x1bP", StringComparison.Ordinal), end = overlayFrame.IndexOf("\x1b\\", start, StringComparison.Ordinal) + 2;
            var raster = SixelTests.Decode(overlayFrame[start..end]);
            var covered = SixelChartPresenter.Intersect(chart.Surface.Plot.PlotBounds, popup.Content!.Bounds);
            Assert.IsGreaterThan(0, covered.Width);
            for (int y = (covered.Y - chart.Surface.Plot.PlotBounds.Y) * metrics.CellPixelHeight; y < (covered.Bottom - chart.Surface.Plot.PlotBounds.Y) * metrics.CellPixelHeight; y++) {
                for (int x = (covered.X - chart.Surface.Plot.PlotBounds.X) * metrics.CellPixelWidth; x < (covered.Right - chart.Surface.Plot.PlotBounds.X) * metrics.CellPixelWidth; x++) {
                    Assert.AreEqual((byte)0, raster.Pixels[y * raster.Width + x], "Sixel must not overpaint the menu's cells.");
                }
            }
            Assert.IsTrue(raster.Pixels.Any(pixel => pixel > 1), "The uncovered chart retains its data marks.");
            Assert.IsGreaterThan(encoded, presenter.GetDiagnosticsSnapshot().EncodedFrameCount);
            popup.Close();
            before = backend.GetOutText().Length;
            Tick();
            Assert.Contains("\x1bP0;1q", backend.GetOutText()[before..], "Closing the popup restores the complete plot.");
            var color = chart.Surface.Plot.PixelBackground.ToRgb();
            Assert.Contains($"\x1b[48;2;{color.R};{color.G};{color.B}m", backend.GetOutText()[before..], "Erasure restores the chart background, not the menu's last text background.");
            before = backend.GetOutText().Length;
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F12 });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return TerminalOverlayBounds.Debug(terminal).Width > 0; }, 2000));
            Tick();
            string debugFrame = backend.GetOutText()[before..];
            int debugStart = debugFrame.LastIndexOf("\x1bP0;1q", StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, debugStart);
            int debugEnd = debugFrame.IndexOf("\x1b\\", debugStart, StringComparison.Ordinal) + 2;
            var debugRaster = SixelTests.Decode(debugFrame[debugStart..debugEnd]);
            var debugCovered = SixelChartPresenter.Intersect(chart.Surface.Plot.PlotBounds, TerminalOverlayBounds.Debug(terminal));
            Assert.IsGreaterThan(0, debugCovered.Height);
            for (int y = (debugCovered.Y - chart.Surface.Plot.PlotBounds.Y) * metrics.CellPixelHeight; y < (debugCovered.Bottom - chart.Surface.Plot.PlotBounds.Y) * metrics.CellPixelHeight; y++) {
                int x = (debugCovered.X - chart.Surface.Plot.PlotBounds.X) * metrics.CellPixelWidth;
                Assert.AreEqual(-1, debugRaster.Pixels.AsSpan(y * debugRaster.Width + x, debugCovered.Width * metrics.CellPixelWidth).IndexOfAnyExcept((byte)0), "F12 diagnostic cells must contain no chart pixels.");
            }
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F12 });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return TerminalOverlayBounds.Debug(terminal).Width == 0; }, 2000));
            Assert.AreEqual(1, chart.Surface.SelectedIndex);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
}
