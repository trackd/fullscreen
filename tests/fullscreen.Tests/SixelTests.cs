using fullscreen.Themes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Monitoring;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Ansi;

namespace fullscreen.Tests;

[TestClass]
public sealed partial class SixelTests {
    [TestMethod]
    [DoNotParallelize]
    public async Task PresenterDiagnosticsTrackEncodingCacheRepaintAndReset() {
        var metrics = new TerminalPixelMetrics(80, 120, 4, 12, 20, 10);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(20, 10));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(new XenoAtom.Terminal.UI.Controls.TextBlock(""), session.Instance);
        var history = new TimeSeries();
        history.Add(50, DateTimeOffset.UnixEpoch);
        var chart = new BuiltInTrend();
        chart.Update(history, history.Latest, 100);
        var bounds = new Rectangle(0, 0, 6, 2);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, bounds, bounds, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, 20, 10), 0, bounds, TerminalGraphicsTextFrameKind.Full], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        Assert.IsInstanceOfType<ITerminalGraphicsPresenterDiagnostics>(presenter);
        Assert.AreEqual(0L, presenter.GetDiagnosticsSnapshot().PresentationCount);
        using var output = new StringWriter();
        var writer = new AnsiWriter(output);
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        var first = presenter.GetDiagnosticsSnapshot();
        string text = output.ToString();
        int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", StringComparison.Ordinal) + 2;
        Assert.AreEqual("Sixel charts · Line", first.Name);
        Assert.AreEqual(TerminalGraphicsProtocol.Sixel, first.Protocol);
        Assert.AreEqual(1L, first.PresentationCount);
        Assert.AreEqual(1, first.LastCommandCount);
        Assert.AreEqual(1L, first.EncodedFrameCount);
        Assert.AreEqual(1, first.LastEncodedFrameCount);
        Assert.AreEqual((long)(end - start), first.PayloadByteCount);
        Assert.AreEqual(first.PayloadByteCount, first.LastPayloadByteCount);
        Assert.AreEqual(1L, first.CacheMissCount);
        Assert.AreEqual(1L, first.CacheStoreCount);
        Assert.AreEqual(first.TotalEncodeDuration, first.AverageEncodeDuration);
        Assert.AreEqual(first.TotalEncodeDuration, first.LastEncodeDuration);
        Assert.IsGreaterThan(TimeSpan.Zero, first.LastPresentationDuration);
        Assert.IsGreaterThan(0d, first.EffectiveFramesPerSecond);
        output.GetStringBuilder().Clear();
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        var cached = presenter.GetDiagnosticsSnapshot();
        Assert.AreEqual(2L, cached.PresentationCount);
        Assert.AreEqual(1L, cached.CacheHitCount);
        Assert.AreEqual(1L, cached.LastCacheHitCount);
        Assert.AreEqual(0L, cached.LastCacheMissCount);
        Assert.AreEqual(0L, cached.LastCacheStoreCount);
        Assert.AreEqual(1L, cached.EncodedFrameCount);
        Assert.AreEqual(0, cached.LastEncodedFrameCount);
        Assert.AreEqual(first.PayloadByteCount, cached.PayloadByteCount);
        Assert.AreEqual(0L, cached.LastPayloadByteCount);
        Assert.AreEqual(TimeSpan.Zero, cached.LastEncodeDuration);
        Assert.AreEqual(text, output.ToString(), "A text repaint must re-emit the cached payload.");
        presenter.Reset();
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        var reset = presenter.GetDiagnosticsSnapshot();
        Assert.AreEqual(3L, reset.PresentationCount);
        Assert.AreEqual(2L, reset.EncodedFrameCount);
        Assert.AreEqual(1, reset.LastEncodedFrameCount);
        Assert.AreEqual(2L, reset.CacheMissCount);
        Assert.AreEqual(2L, reset.CacheStoreCount);
        Assert.AreEqual(first.PayloadByteCount * 2, reset.PayloadByteCount);
        Assert.AreEqual(0L, reset.DroppedFrameCount);
    }
    [TestMethod]
    public void NetworkLanesHaveIndependentScalesAndTransmitRetainsItsVariation() {
        var surface = new MonitorSurface { View = MonitorView.Dashboard, MinHeight = 40, MaxHeight = 40 };
        for (int i = 0; i <= 120; i++) {
            surface.SetFrame(new CollectorFrame {
                CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i),
                ReceiveBytesPerSecond = 1000 + 200 * Math.Sin(i / 4d),
                SendBytesPerSecond = 10 + 8 * Math.Cos(i / 3d)
            });
        }

        XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(surface, 140, 40);
        var lanes = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.Bounds.Width > 0).ToArray();
        Assert.IsNotNull(lanes[2].PixelKey.Maximum);
        Assert.IsNotNull(lanes[3].PixelKey.Maximum);
        Assert.IsGreaterThan(1000d, lanes[2].PixelKey.Maximum!.Value);
        Assert.IsLessThanOrEqualTo(20d, lanes[3].PixelKey.Maximum!.Value);
        Assert.AreNotSame(lanes[2].PixelKey.Source, lanes[3].PixelKey.Source);
        var plot = new SixelPlot();
        lanes[3].DrawPixels(plot, 240, 61);
        var rows = new HashSet<int>();
        for (int x = 0; x < 240; x++) {
            for (int y = 0; y < 61; y++) {
                if (plot.Pixels[y * 240 + x] >= 3) {
                    rows.Add(y);
                    break;
                }
            }
        }

        Assert.IsGreaterThan(30, rows.Count, "A varying low-volume TX signal must occupy its own vertical range.");
    }
    [TestMethod]
    public void NewColorsAreDefinedOnEncounterAndOnlyActiveBandPlanesAreEmitted() {
        byte[] pixels = new byte[5 * 19];
        pixels[0] = 1;
        pixels[6 * 5 + 2] = 2;
        pixels[18 * 5 + 4] = 1;
        Color[] palette = [Color.Default, Color.Rgb(255, 0, 0), Color.Rgb(0, 255, 0), Color.Rgb(0, 0, 255)];
        string payload = SixelEncoder.Encode(pixels, 5, 19, palette);
        Assert.HasCount(1, PaletteEntryOneDefinitionPattern().Matches(payload));
        Assert.HasCount(1, PaletteEntryTwoDefinitionPattern().Matches(payload));
        Assert.DoesNotContain("#3", payload);
        CollectionAssert.AreEqual(pixels, Decode(payload).Pixels);
    }
    [TestMethod]
    public void RuntimeChartsUseRequestedGraphStyle() {
        foreach (var kind in Enum.GetValues<GraphKind>()) {
            var document = new TuiDocument { Root = new() { Id = "plot", Type = "Chart", GraphKind = kind.ToString() } };
            using var app = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()));
            Assert.AreEqual(kind, ((HistoryChart)app.Controls["plot"]).GraphKind);
        }
    }
    [TestMethod]
    public void EncoderRoundTripsEveryMaskMultipleColorsAndPartialBands() {
        Color[] palette = [Color.Rgb(13, 20, 32), Color.Rgb(88, 213, 230), Color.Rgb(112, 225, 176)];
        foreach (int height in new[] { 1, 5, 6, 7, 13 }) {
            const int width = 64;
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    pixels[y * width + x] = (byte)(((x >> (y % 6)) & 1) == 0 ? 0 : y < 6 ? 1 : 2);
                }
            }

            string sixel = SixelEncoder.Encode(pixels, width, height, palette);
            var decoded = Decode(sixel);
            Assert.AreEqual(width, decoded.Width);
            Assert.AreEqual(height, decoded.Height);
            CollectionAssert.AreEqual(pixels, decoded.Pixels);
            Assert.AreEqual((35, 84, 90), decoded.Colors[1]);
            Assert.IsFalse(sixel.EndsWith("-\x1b\\", StringComparison.Ordinal), "A final line feed can move the cursor beyond the image.");
        }
    }
    [TestMethod]
    public void EncoderCompressesInkAndOmitsTransparentPixelsAndUnusedColors() {
        Color[] palette = [Color.Rgb(0, 0, 0), Color.Rgb(255, 0, 0)];
        byte[] pixels = [.. Enumerable.Repeat((byte)1, 120 * 7)];
        string filled = SixelEncoder.Encode(pixels, 120, 7, palette);
        Assert.Contains("!120~", filled);
        Assert.IsFalse(filled.Contains("#0", StringComparison.Ordinal), "Transparent index zero must never be defined or selected.");
        Array.Clear(pixels);
        string cleared = SixelEncoder.Encode(pixels, 120, 7, palette);
        Assert.AreEqual("\x1bP0;1q\"1;1;120;7?\x1b\\", cleared, "An empty plot must still declare the full replacement canvas.");
        Assert.IsTrue(Decode(cleared).Pixels.All(pixel => pixel == 0));
        Assert.Throws<ArgumentException>(() => SixelEncoder.Encode([2], 1, 1, palette));
    }
    [TestMethod]
    public void SparseFramesKeepCanvasSizeAndPositionInkAcrossEmptyBands() {
        Color[] palette = [Color.Rgb(13, 20, 32), Color.Rgb(88, 213, 230), Color.Rgb(255, 0, 0)];
        byte[] pixels = new byte[40 * 60];
        pixels[13 * 40 + 3] = 1;
        pixels[37 * 40 + 20] = 1;
        var encoder = new SixelEncoder();
        string frame = encoder.EncodeFrame(pixels, 40, 60, palette);
        var decoded = Decode(frame);
        CollectionAssert.AreEqual(pixels, decoded.Pixels);
        Assert.AreEqual(60, decoded.Height, "Omitting trailing bands must not shrink the declared image.");
        Assert.IsFalse(frame.Contains("#0", StringComparison.Ordinal));
        Assert.IsFalse(frame.Contains("#2", StringComparison.Ordinal), "Unused palette entries should not be transmitted.");
        Assert.IsFalse(frame.EndsWith("-\x1b\\", StringComparison.Ordinal));
        Assert.IsLessThan(100, frame.Length, "Sparse canvas should encode as a short header and two marks.");
        Array.Clear(pixels);
        var empty = Decode(encoder.EncodeFrame(pixels, 40, 60, palette));
        Assert.IsTrue(empty.Pixels.All(pixel => pixel == 0), "A reused encoder must not retain old mask bits.");
    }
    [TestMethod]
    public void PixelGraphsPreservePeaksGapsAndProvideDistinctShapes() {
        var start = DateTimeOffset.UnixEpoch;
        var history = new TimeSeries();
        history.Add(10, start);
        history.Add(100, start.AddMilliseconds(100));
        history.Add(10, start.AddMilliseconds(200));
        history.Add(null, start.AddSeconds(30));
        history.Add(40, start.AddSeconds(60));
        history.Add(60, start.AddSeconds(61));
        var plot = new SixelPlot();
        var ink = Color.Rgb(88, 213, 230);
        var background = Color.Rgb(13, 20, 32);
        var shapes = new List<byte[]>();
        foreach (var kind in new[] { GraphKind.Line, GraphKind.Area, GraphKind.Bars }) {
            plot.Draw(history, start.AddSeconds(120), 100, 240, 61, ink, background, kind);
            Assert.IsGreaterThan((byte)1, plot.Pixels[0], "Compressed peak must reach the top edge.");
            for (int y = 1; y < 30; y++) {
                Assert.AreEqual((byte)0, plot.Pixels[y * 240 + 60], "Missing data cannot be bridged or filled.");
            }

            shapes.Add(plot.Pixels.ToArray());
        }
        Assert.IsFalse(shapes[0].SequenceEqual(shapes[1]));
        Assert.IsFalse(shapes[1].SequenceEqual(shapes[2]));
        plot.Draw(history, start.AddSeconds(120), 100, 240, 61, ink, background, GraphKind.Line, 60);
        for (int y = 0; y < 61; y++) {
            Assert.AreEqual((byte)6, plot.Pixels[y * 240 + 60]);
        }
    }
    [TestMethod]
    public void DiscretePlotsUseOriginalObservationsAndStepsHoldUntilNextSample() {
        var start = DateTimeOffset.UnixEpoch;
        var history = new TimeSeries();
        history.Add(20, start);
        history.Add(80, start.AddSeconds(20));
        history.Add(null, start.AddSeconds(30));
        history.Add(60, start.AddSeconds(40));
        var plot = new SixelPlot();
        var ink = Color.Rgb(88, 213, 230);
        var background = Color.Rgb(13, 20, 32);
        plot.Draw(history, start.AddSeconds(120), 100, 241, 101, ink, background, GraphKind.Step);
        Assert.AreEqual((byte)6, plot.Pixels[80 * 241 + 20], "Step holds the earlier value rather than interpolating.");
        Assert.AreEqual((byte)0, plot.Pixels[20 * 241 + 60], "An explicit null breaks a step.");
        Assert.AreEqual((byte)0, plot.Pixels[40 * 241 + 180], "A step must not extend across the unavailable tail.");
        plot.Draw(history, start.AddSeconds(120), 100, 241, 101, ink, background, GraphKind.Scatter);
        Assert.AreEqual((byte)6, plot.Pixels[20 * 241 + 40]);
        Assert.AreEqual((byte)0, plot.Pixels[80 * 241 + 20], "Scatter must not invent interpolated observations.");
        plot.Draw(history, start.AddSeconds(120), 100, 241, 101, ink, background, GraphKind.Stem);
        Assert.AreEqual((byte)6, plot.Pixels[75 * 241 + 40], "Stem connects an observation to its zero baseline.");
    }

    private static readonly int[] expected = [1, 2, 1, 2];

    [TestMethod]
    public void DistributionPlotsCountRawSamplesAndUseTukeyWhiskers() {
        var start = DateTimeOffset.UnixEpoch;
        var history = new TimeSeries();
        double?[] observations = [0, 1, 1, 2, 3, 4, null];
        for (int i = 0; i < observations.Length; i++) {
            history.Add(observations[i], start.AddSeconds(i));
        }

        var plot = new SixelPlot();
        plot.Draw(history, history.Latest, 4, 64, 61, Color.Rgb(88, 213, 230), Color.Rgb(13, 20, 32), GraphKind.Histogram);
        CollectionAssert.AreEqual(expected, plot.HistogramCounts.ToArray(), "The histogram must count observations, not interpolated pixels.");
        Assert.AreEqual(new BoxSummary(3, 5, 7, 1, 8), SixelPlot.Summarize([1, 2, 3, 4, 5, 6, 7, 8, 100]));
        Assert.AreEqual(new BoxSummary(5, 5, 5, 5, 5), SixelPlot.Summarize([5]));
    }
    [TestMethod]
    public void EveryGraphRoundTripsForEmptyConstantAndTinyRasters() {
        var history = new TimeSeries();
        var plot = new SixelPlot();
        var encoder = new SixelEncoder();
        foreach (var kind in Enum.GetValues<GraphKind>()) {
            foreach (var size in new[] { (1, 1), (2, 7), (63, 31) }) {
                foreach (bool populated in new[] { false, true }) {
                    history.Clear();
                    if (populated) {
                        history.Add(50, DateTimeOffset.UnixEpoch);
                        history.Add(50, DateTimeOffset.UnixEpoch.AddSeconds(1));
                    }
                    plot.Draw(history, history.Latest, 100, size.Item1, size.Item2, Color.Rgb(88, 213, 230), Color.Rgb(13, 20, 32), kind);
                    var decoded = Decode(encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette));
                    CollectionAssert.AreEqual(plot.Pixels.ToArray(), decoded.Pixels, $"{kind}: {size}, populated={populated}");
                    var direct = Decode(encoder.EncodePlot(plot));
                    CollectionAssert.AreEqual(plot.Pixels.ToArray(), direct.Pixels, $"Direct planes: {kind}: {size}, populated={populated}");
                }
            }
        }
    }
    [TestMethod]
    public void DirectPlanesPreserveOverpaintedShadesInspectionAndThemeChanges() {
        var history = new TimeSeries();
        var plot = new SixelPlot();
        var encoder = new SixelEncoder();
        for (int i = 0; i <= 120; i++) {
            history.Add(i % 17 == 0 ? null : 50 + 45 * Math.Sin(i / 3d), DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        foreach (var kind in Enum.GetValues<GraphKind>()) {
            foreach (var size in new[] { (63, 31), (11, 13), (63, 31) }) {
                var ink = kind == GraphKind.Line ? Color.Rgb(255, 0, 0) : Color.Rgb(0, 128, 255);
                plot.Draw(history, history.Latest, 100, size.Item1, size.Item2, ink, Color.Rgb(13, 20, 32), kind, 5);
                var actual = Decode(encoder.EncodePlot(plot));
                CollectionAssert.AreEqual(plot.Pixels.ToArray(), actual.Pixels, $"{kind}: {size}");
                Assert.AreEqual(((ink.ToRgb().R * 100 + 127) / 255, (ink.ToRgb().G * 100 + 127) / 255, (ink.ToRgb().B * 100 + 127) / 255), actual.Colors[6]);
            }
        }
    }
    [TestMethod]
    public void WarmRastersReuseBuffersAcrossSizesAndPlotTypes() {
        var history = new TimeSeries();
        for (int i = 0; i < 120; i++) {
            history.Add(i % 100, DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var plot = new SixelPlot();
        var ink = Color.Rgb(88, 213, 230);
        var background = Color.Rgb(13, 20, 32);
        var kinds = Enum.GetValues<GraphKind>();
        foreach (var kind in kinds) {
            plot.Draw(history, history.Latest, 100, 800, 180, ink, background, kind);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var kind in kinds) {
            plot.Draw(history, history.Latest, 100, 400, 90, ink, background, kind);
            plot.Draw(history, history.Latest, 100, 800, 180, ink, background, kind);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "Warm rasterization must not reallocate buffers as panel sizes alternate.");
    }
    [TestMethod]
    public void WarmEncoderOnlyAllocatesItsReturnedPayload() {
        var history = new TimeSeries();
        for (int i = 0; i < 120; i++) {
            history.Add(50 + 30 * Math.Sin(i / 5d), DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var plot = new SixelPlot();
        var encoder = new SixelEncoder();
        plot.Draw(history, history.Latest, 100, 800, 180, Color.Rgb(88, 213, 230), Color.Rgb(13, 20, 32), GraphKind.Area);
        string payload = "";
        for (int i = 0; i < 5; i++) {
            payload = encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        string reference = new('x', payload.Length);
        long stringBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(reference);
        before = GC.GetAllocatedBytesForCurrentThread();
        payload = encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette);
        long encodedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(payload);
        Assert.AreEqual(stringBytes, encodedBytes, "Encoding should reuse its planes and builder; only the output string is new.");
        for (int i = 0; i < 5; i++) {
            payload = encoder.EncodePlot(plot);
        }

        before = GC.GetAllocatedBytesForCurrentThread();
        reference = new('x', payload.Length);
        stringBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(reference);
        before = GC.GetAllocatedBytesForCurrentThread();
        payload = encoder.EncodePlot(plot);
        encodedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(payload);
        Assert.AreEqual(stringBytes, encodedBytes, "Direct plane encoding should allocate only its output string.");
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task ClippedPlotCropsPixelsWithoutRescalingTime() {
        var metrics = new TerminalPixelMetrics(80, 120, 4, 12, 20, 10);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(20, 10));
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.ForcedPixelMetrics = metrics;
        options.Graphics.DisableProbing = true;
        using var session = Terminal.Open(backend, options, force: true);
        await using var app = new TerminalApp(new XenoAtom.Terminal.UI.Controls.TextBlock(""), session.Instance);
        var history = new TimeSeries();
        for (int i = 0; i <= 120; i++) {
            history.Add(i / 1.2, DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var chart = new BuiltInTrend();
        chart.Update(history, history.Latest, 100);
        var bounds = new Rectangle(-2, 0, 6, 2);
        var clip = new Rectangle(0, 0, 4, 2);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, bounds, clip, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, 20, 10), 0, default(Rectangle), TerminalGraphicsTextFrameKind.None], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        using var output = new StringWriter();
        var writer = new AnsiWriter(output);
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        string text = output.ToString();
        int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", StringComparison.Ordinal) + 2;
        var decoded = Decode(text[start..end]);
        Assert.AreEqual(16, decoded.Width);
        Assert.AreEqual(24, decoded.Height);
        var full = new SixelPlot();
        chart.DrawPixels(full, 24, 24);
        for (int y = 0; y < 24; y++) {
            for (int x = 0; x < 16; x++) {
                Assert.AreEqual(full.Pixels[y * 24 + x + 8], decoded.Pixels[y * 16 + x]);
            }
        }

        history.Add(0, DateTimeOffset.UnixEpoch.AddSeconds(121));
        chart.Update(history, history.Latest, 100);
        output.GetStringBuilder().Clear();
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        string replacement = output.ToString();
        Assert.Contains("\x1b[4X", replacement, "Changed transparent plots must erase their old cell rectangle.");
        Assert.IsLessThan(replacement.IndexOf("\x1bP", StringComparison.Ordinal), replacement.IndexOf("\x1b[4X", StringComparison.Ordinal), "Erasure must precede the new image.");
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task DashboardUsesBufferedSixelAndCleansUpWhenPlotsDisappear() {
        var metrics = new TerminalPixelMetrics(400, 288, 4, 12, 100, 24);
        var graphics = new TerminalGraphicsCapabilities {
            PreferredProtocol = TerminalGraphicsProtocol.Sixel,
            SupportedProtocols = [TerminalGraphicsProtocol.Sixel],
            SupportState = TerminalGraphicsSupportState.Confirmed,
            SupportsStaticImages = true,
            PixelMetrics = metrics
        };
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 24), new TerminalCapabilities { AnsiEnabled = true, Graphics = graphics, ColorLevel = TerminalColorLevel.TrueColor, IsOutputRedirected = false, IsInputRedirected = false });
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Sixel;
        options.Graphics.ForcedPixelMetrics = metrics;
        options.Graphics.DisableProbing = true;
        using var session = Terminal.Open(backend, options, force: true);
        var surface = new MonitorSurface { View = MonitorView.Dashboard };
        surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch, CpuPercent = 50 });
        using var presenter = new SixelChartPresenter(graphics, metrics);
        ChartPresenters.Register(session.Instance, presenter);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, GraphicsPresenter = presenter });
        Assert.IsTrue(terminal.IsGraphicsPresentationEnabled, $"{session.Instance.Graphics.Capabilities.SupportState} / {string.Join(',', session.Instance.Graphics.Capabilities.SupportedProtocols)} / mux={session.Instance.Graphics.Capabilities.IsMultiplexer}");
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 10;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Tick();
            string first = backend.GetOutText();
            Assert.Contains("\x1bP0;1q", first);
            Assert.IsTrue(surface.EnumerateVisualsDepthFirst().OfType<XenoAtom.Terminal.UI.Controls.LineChart>().All(chart => chart.Bounds.Width == 0));
            int length = first.Length;
            terminal.RequestGraphicsRender();
            Tick();
            Assert.IsFalse(backend.GetOutText()[length..].Contains("\x1bP0;1q", StringComparison.Ordinal), "Unchanged plots should reuse their display.");
            var dialog = new XenoAtom.Terminal.UI.Controls.Dialog(new XenoAtom.Terminal.UI.Controls.TextBlock("A dialog above the chart")) { MinWidth = 100, MaxWidth = 100, MinHeight = 24, MaxHeight = 24 };
            dialog.Show();
            Tick();
            Tick();
            length = backend.GetOutText().Length;
            terminal.RequestGraphicsRender();
            Tick();
            Assert.IsFalse(backend.GetOutText()[length..].Contains("\x1bP0;1q", StringComparison.Ordinal), "Sixel must not cover a dialog.");
            dialog.Close();
            Tick();
            Tick();
            Assert.Contains("\x1bP0;1q", backend.GetOutText()[length..], "Closing a dialog must restore the plot.");
            length = backend.GetOutText().Length;
            surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(1), CpuPercent = 80 });
            surface.Refresh();
            Tick();
            Assert.Contains("\x1bP0;1q", backend.GetOutText()[length..], "New samples must replace the image.");
            length = backend.GetOutText().Length;
            backend.SetSize(new TerminalSize(80, 24));
            Tick();
            Tick();
            Assert.Contains("\x1bP0;1q", backend.GetOutText()[length..], "Resizing must redraw at the new pixel dimensions.");
            for (int i = 1; i <= 5; i++) {
                surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i * 20), CpuPercent = i % 2 == 0 ? 75 : 25 });
            }

            var renderedKinds = new HashSet<string>();
            foreach (var kind in MonitorSurface.TimeGraphTypes) {
                length = backend.GetOutText().Length;
                surface.GraphType = kind;
                surface.Refresh();
                Tick();
                string changed = backend.GetOutText()[length..];
                int begin = changed.IndexOf("\x1bP0;1q", StringComparison.Ordinal);
                Assert.IsGreaterThanOrEqualTo(0, begin, $"Changing GraphType to {kind} must send a new Sixel frame.");
                int end = changed.IndexOf("\x1b\\", begin, StringComparison.Ordinal) + 2;
                renderedKinds.Add(Convert.ToHexString(Decode(changed[begin..end]).Pixels));
            }
            Assert.HasCount(MonitorSurface.TimeGraphTypes.Length, renderedKinds, "Every monitor GraphType must produce a distinct CPU plot.");
            length = backend.GetOutText().Length;
            surface.View = MonitorView.Processes;
            surface.OpenColumns();
            surface.Refresh();
            Tick();
            Tick();
            string cleanup = backend.GetOutText()[length..];
            Assert.Contains("X", cleanup, "Removing plots must erase their streamed image regions.");
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    // Independent protocol interpreter: no encoder helpers or chart rasterization.
    internal static (int Width, int Height, byte[] Pixels, Dictionary<int, (int, int, int)> Colors) Decode(string text) {
        Assert.StartsWith("\x1bP0;1q\"1;1;", text);
        Assert.EndsWith("\x1b\\", text);
        int position = "\x1bP0;1q\"1;1;".Length;
        int Number() {
            int value = 0;
            while (position < text.Length && char.IsAsciiDigit(text[position])) {
                value = checked(value * 10 + text[position++] - '0');
            }

            return value;
        }
        int width = Number();
        Assert.AreEqual(';', text[position++]);
        int height = Number();
        // A fresh transparent canvas: unpainted pixels are index zero. Replacement
        // of the previous image is terminal behavior, verified separately in Contour.
        byte[] pixels = new byte[width * height];
        var colors = new Dictionary<int, (int, int, int)>();
        int x = 0, y = 0, selected = 0;
        while (position < text.Length - 2) {
            char token = text[position++];
            if (token == '#') {
                selected = Number();
                if (text[position] == ';') {
                    position++;
                    Assert.AreEqual(2, Number());
                    Assert.AreEqual(';', text[position++]);
                    int r = Number();
                    Assert.AreEqual(';', text[position++]);
                    int g = Number();
                    Assert.AreEqual(';', text[position++]);
                    int b = Number();
                    colors[selected] = (r, g, b);
                }
            }
            else if (token == '$') {
                x = 0;
            }
            else if (token == '-') {
                x = 0;
                y += 6;
            }
            else {
                int count = 1;
                if (token == '!') {
                    count = Number();
                    token = text[position++];
                }
                Assert.IsTrue(token is >= '?' and <= '~', $"Unexpected sixel token: {token}");
                for (int i = 0; i < count; i++, x++) {
                    Assert.IsLessThan(width, x);
                    for (int bit = 0; bit < 6; bit++) {
                        if (((token - '?') & (1 << bit)) != 0) {
                            Assert.IsLessThan(height, y + bit, "Partial band must not draw outside the declared height.");
                            pixels[(y + bit) * width + x] = (byte)selected;
                        }
                    }
                }
            }
        }
        return (width, height, pixels, colors);
    }

    [GeneratedRegex("#1;2;")]
    private static partial Regex PaletteEntryOneDefinitionPattern();
    [GeneratedRegex("#2;2;")]
    private static partial Regex PaletteEntryTwoDefinitionPattern();
}
