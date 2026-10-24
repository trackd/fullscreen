using fullscreen.Charts;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI.Hosting;
using System.Diagnostics;
using System.Reflection;

namespace fullscreen.Tests;

[TestClass]
public sealed class DrcsTests {
    private const string FontHeader = "\x1bP0;0;0;10;0;2;16;1{ @";
    private sealed class Harness : IAsyncDisposable {
        private readonly InMemoryTerminalBackend _backend = new(new TerminalSize(60, 20));
        private readonly TerminalSession _session;
        private readonly TerminalApp _app;
        private long _time = Stopwatch.GetTimestamp();
        public Harness(Visual root, DrcsChartPresenter presenter) {
            _session = Terminal.Open(_backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
            ChartPresenters.Register(_session.Instance, presenter);
            _app = new TerminalApp(root, _session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, GraphicsPresenter = presenter });
            typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_app, null);
        }
        public bool Graphics => _app.IsGraphicsPresentationEnabled;
        public string Output => _backend.GetOutText();
        public string Tick() {
            int start = Output.Length;
            _time += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_app, [_time]);
            return Output[start..];
        }
        public async ValueTask DisposeAsync() {
            typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_app, null);
            await _app.DisposeAsync();
            _session.Dispose();
            _backend.Dispose();
        }
    }
    private static HistoryChart Chart(GraphKind kind, int count = 121) {
        var chart = new HistoryChart { MinHeight = 6, MaxHeight = 6, GraphKind = kind };
        for (int i = 0; i < count; i++) {
            chart.Add(50 + 40 * Math.Sin(i / 9d), DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        return chart;
    }
    private static int Downloads(string output) =>
        output.Split(FontHeader).Length - 1;

    [TestMethod]
    public void ExplicitDrcsSelectionHonorsDisabledGraphicsAndMultiplexerFallback() {
        using var presenter = new DrcsChartPresenter(TerminalGraphicsCapabilities.None);
        Assert.IsTrue(presenter.CanPresent(TerminalGraphicsCapabilities.None));
        Assert.IsFalse(presenter.CanPresent(new TerminalGraphicsCapabilities { SupportState = TerminalGraphicsSupportState.Disabled }));
        Assert.IsFalse(presenter.CanPresent(new TerminalGraphicsCapabilities { IsMultiplexer = true }));
    }
    [TestMethod]
    public async Task FontIsSentOnceWhileDataChangesAndBoxPlotUsesText() {
        var area = Chart(GraphKind.Area);
        var box = Chart(GraphKind.BoxPlot);
        using var presenter = new DrcsChartPresenter(TerminalGraphicsCapabilities.None);
        await using var harness = new Harness(new XenoAtom.Terminal.UI.Controls.VStack(area, box), presenter);
        string initial = harness.Tick();
        Assert.IsTrue(harness.Graphics);
        Assert.AreEqual(1, Downloads(initial));
        Assert.AreEqual(1, initial.Split("\x1b- @\x0e").Length - 1, "Only the glyph chart selects the soft font.");
        Assert.IsGreaterThanOrEqualTo(0, initial.IndexOfAny(['┌', '│', '─']), "BoxPlot is drawn with box-drawing text.");
        Assert.Contains("\x0f\x1b)B\x1b[0m", initial);
        Assert.AreEqual("", harness.Tick(), "An unchanged frame should produce no output.");
        for (int i = 121; i < 181; i++) {
            area.Add(50 + 40 * Math.Sin(i / 9d), DateTimeOffset.UnixEpoch.AddSeconds(i));
            string update = harness.Tick();
            Assert.AreEqual(0, Downloads(update), "Data changes must never resend the font.");
            Assert.DoesNotContain("\x1bP", update);
        }
    }
    [TestMethod]
    public async Task CoveredChartLeavesNoPendingOutput() {
        using var presenter = new DrcsChartPresenter(TerminalGraphicsCapabilities.None);
        var chart = Chart(GraphKind.Area);
        chart.MinHeight = chart.MaxHeight = 20;
        await using var harness = new Harness(chart, presenter);
        harness.Tick();
        var dialog = new XenoAtom.Terminal.UI.Controls.Dialog(new XenoAtom.Terminal.UI.Controls.TextBlock("Above the chart")) { MinWidth = 20, MaxWidth = 20, MinHeight = 4, MaxHeight = 4 };
        dialog.Show();
        harness.Tick();
        harness.Tick();
        Assert.AreEqual("", harness.Tick(), "A chart hidden by a popup must not present every frame.");
        dialog.Close();
        Assert.Contains("\x1b- @\x0e", harness.Tick() + harness.Tick(), "Closing the popup redraws the chart.");
    }
    [TestMethod]
    public async Task VisibleScopeSendsAFontOnlyWhenANewKindIsNotCovered() {
        var chart = Chart(GraphKind.Area);
        using var presenter = new DrcsChartPresenter(TerminalGraphicsCapabilities.None, DrcsFontScope.Visible);
        await using var harness = new Harness(chart, presenter);
        Assert.AreEqual(1, Downloads(harness.Tick()));
        chart.GraphKind = GraphKind.Histogram;
        chart.Add(10, DateTimeOffset.UnixEpoch.AddSeconds(121));
        Assert.AreEqual(0, Downloads(harness.Tick()), "The Area font already covers Histogram.");
        chart.GraphKind = GraphKind.Line;
        chart.Add(20, DateTimeOffset.UnixEpoch.AddSeconds(122));
        Assert.AreEqual(1, Downloads(harness.Tick()));
    }
    [TestMethod]
    public async Task UniversalScopeSendsOneFontForEveryKind() {
        var chart = Chart(GraphKind.Area);
        using var presenter = new DrcsChartPresenter(TerminalGraphicsCapabilities.None, DrcsFontScope.Universal);
        await using var harness = new Harness(chart, presenter);
        Assert.AreEqual(1, Downloads(harness.Tick()));
        int second = 121;
        foreach (var kind in Enum.GetValues<GraphKind>().Where(kind => kind <= GraphKind.BoxPlot)) {
            chart.GraphKind = kind;
            chart.Add(30, DateTimeOffset.UnixEpoch.AddSeconds(second++));
            Assert.AreEqual(0, Downloads(harness.Tick()), kind.ToString());
        }
    }
    [TestMethod]
    public void FontsFitOneSetAndSingleKindsGetFinerSteps() {
        foreach (var kind in Enum.GetValues<GraphKind>().Where(kind => kind != GraphKind.BoxPlot)) {
            var font = DrcsFont.For(DrcsFont.Mask(kind));
            Assert.IsLessThanOrEqualTo(DrcsEncoder.Capacity, font.Count, kind.ToString());
            Assert.IsGreaterThanOrEqualTo(8, font.Levels, kind.ToString());
        }
        var universal = DrcsFont.For(DrcsFont.AllKinds);
        Assert.IsLessThanOrEqualTo(DrcsEncoder.Capacity, universal.Count);
        Assert.IsLessThan(DrcsFont.For(DrcsFont.Mask(GraphKind.Area)).Levels, universal.Levels);
        Assert.AreEqual(default, universal.Glyphs[0], "Slot 0 (space) stays blank.");
    }
    [TestMethod]
    public void EveryKindLaysOutVisibleCellsFromItsFont() {
        var history = new TimeSeries();
        for (int i = 0; i <= 120; i++) {
            history.Add(50 + 30 * Math.Sin(i / 9d), DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        double[] scratch = [];
        var font = DrcsFont.For(DrcsFont.AllKinds);
        foreach (var kind in Enum.GetValues<GraphKind>().Where(kind => kind <= GraphKind.BoxPlot)) {
            var cells = new char[40 * 8];
            var chart = new BuiltInTrend { GraphKind = kind };
            chart.Update(history, history.Latest, 100);
            var key = chart.PixelKey;
            bool text = DrcsFont.Layout(font, key, 40, 8, cells, ref scratch);
            Assert.AreEqual(kind == GraphKind.BoxPlot, text, kind.ToString());
            Assert.IsTrue(cells.Any(cell => cell != ' '), kind.ToString());
            if (!text) {
                Assert.IsTrue(cells.All(cell => cell >= ' ' && cell < (char)(0x20 + font.Count)), kind.ToString());
            }
        }
    }
    [TestMethod]
    public void AreaFillsBelowTheValueAndLeavesAboveBlank() {
        var history = new TimeSeries();
        for (int i = 0; i <= 120; i++) {
            history.Add(50, DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var font = DrcsFont.For(DrcsFont.Mask(GraphKind.Area));
        double[] scratch = [];
        var cells = new char[10 * 4];
        var chart = new BuiltInTrend { GraphKind = GraphKind.Area };
        chart.Update(history, history.Latest, 100);
        DrcsFont.Layout(font, chart.PixelKey, 10, 4, cells, ref scratch);
        char solid = (char)(0x20 + font.Glyphs.IndexOf(new DrcsEncoder.Tile(ulong.MaxValue, ulong.MaxValue, uint.MaxValue)));
        Assert.AreEqual(new string(' ', 20) + new string(solid, 20), new string(cells));
    }
    [TestMethod]
    public void FontDownloadRoundTripsEveryGlyphPixel() {
        var bank = Enumerable.Range(0, 96).Select(i => new DrcsEncoder.Tile((ulong)i, (ulong)(i * 17), (uint)(i * 31))).ToArray();
        string font = DrcsEncoder.Download(bank);
        Assert.StartsWith(FontHeader, font);
        Assert.EndsWith("\x1b\\", font);
        string[] glyphs = font[FontHeader.Length..^2].Split(';');
        Assert.HasCount(96, glyphs);
        for (int i = 0; i < bank.Length; i++) {
            string[] bands = glyphs[i].Split('/');
            for (int y = 0; y < 16; y++) {
                for (int x = 0; x < 10; x++) {
                    Assert.AreEqual(bank[i].Pixel(y * 10 + x), ((bands[y / 6][x] - 63) & (1 << (y % 6))) != 0);
                }
            }
        }
    }
}
