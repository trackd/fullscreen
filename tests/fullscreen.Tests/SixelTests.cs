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
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize]
    public async Task ChartsInsideDialogsRemainVisibleWhileLaterDialogsMaskTheirPixels(bool earlierDialog) {
        var metrics = new TerminalPixelMetrics(400, 288, 4, 12, 100, 24);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 24));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(new XenoAtom.Terminal.UI.Controls.TextBlock("Background"), session.Instance,
            new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            time += Stopwatch.Frequency / 10;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [time]);
        }
        try {
            Tick();
            var behind = new XenoAtom.Terminal.UI.Controls.Dialog(new XenoAtom.Terminal.UI.Controls.TextBlock("Behind")) { Width = 100, Height = 24 };
            if (earlierDialog) {
                behind.Show();
                Tick();
            }
            var chart = new Chart([10, 30, 20]) { GraphKind = GraphKind.Bars, Maximum = 100 };
            var dialog = new XenoAtom.Terminal.UI.Controls.Dialog(chart) { Width = 90, Height = 20 };
            dialog.Show();
            Tick();
            Tick();
            var bounds = chart.PlotBounds;
            var commands = new GraphicsCommandBuffer {
                new GraphicsCommand(1, bounds, bounds, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
            };
            var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, 100, 24), 0, default(Rectangle), TerminalGraphicsTextFrameKind.None], null)!;
            using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
            using var output = new StringWriter();
            var writer = new AnsiWriter(output);
            await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
            byte[] Pixels() {
                string text = output.ToString();
                int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", start, StringComparison.Ordinal) + 2;
                return Decode(text[start..end]).Pixels;
            }
            Assert.IsTrue(Pixels().Any(pixel => pixel > 0), "A chart's containing dialog must not mask its own raster.");
            chart.Values[1] = 5;
            output.GetStringBuilder().Clear();
            await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
            Assert.MatchesRegex(@"\x1b\[\d+X", output.ToString(), "Changed transparent charts must erase old ink inside their containing dialog.");
            var cover = new XenoAtom.Terminal.UI.Controls.Dialog(new XenoAtom.Terminal.UI.Controls.TextBlock("Cover")) { Width = 100, Height = 24 };
            cover.Show();
            Tick();
            Tick();
            Assert.IsTrue(presenter.HasPendingOutput(commands, context));
            output.GetStringBuilder().Clear();
            await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
            Assert.IsTrue(Pixels().All(pixel => pixel == 0), "A later dialog must still mask the underlying chart.");
            Assert.DoesNotMatchRegex(@"\x1b\[\d+X", output.ToString(), "The overlying dialog must be protected from erasure.");
            cover.Close();
            Tick();
            output.GetStringBuilder().Clear();
            await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
            Assert.IsTrue(Pixels().Any(pixel => pixel > 0));
            output.GetStringBuilder().Clear();
            await presenter.PresentAsync(new GraphicsCommandBuffer(), context, writer, CancellationToken.None);
            Assert.MatchesRegex(@"\x1b\[\d+X", output.ToString(), "Removed charts must erase their old ink without masking their own dialog.");
            dialog.Close();
            if (earlierDialog) {
                behind.Close();
            }
        }
        finally {
            typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        }
    }

    [TestMethod]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.Area)]
    [DataRow(GraphKind.Heatmap)]
    [DataRow(GraphKind.StackedArea)]
    [DoNotParallelize]
    public async Task PresenterKeepsIndependentOwnedPayloadsWhenOneOfTwoChartsChanges(GraphKind kind) {
        var metrics = new TerminalPixelMetrics(80, 120, 4, 12, 20, 10);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(20, 10));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(new XenoAtom.Terminal.UI.Controls.TextBlock(""), session.Instance);
        var firstChart = new Chart([10, 50, 25]) { GraphKind = kind };
        var secondChart = new Chart([70, 20, 30]) { GraphKind = GraphKind.Pie };
        var firstBounds = new Rectangle(0, 0, 6, 2);
        var secondBounds = new Rectangle(0, 3, 6, 2);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, firstBounds, firstBounds, TerminalGraphicContent.FromObject(firstChart), ImageScaleMode.Stretch, false, 0, true, null),
            new GraphicsCommand(2, secondBounds, secondBounds, TerminalGraphicContent.FromObject(secondChart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, 20, 10), 0, new Rectangle(0, 0, 20, 10), TerminalGraphicsTextFrameKind.Full], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        using var output = new StringWriter();
        var writer = new AnsiWriter(output);
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        string[] original = [.. SixelPayloadPattern().Matches(output.ToString()).Select(match => match.Value)];
        Assert.HasCount(2, original);
        Assert.AreNotEqual(original[0], original[1]);
        output.GetStringBuilder().Clear();
        firstChart.Values[1] = 20;
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        string[] changed = [.. SixelPayloadPattern().Matches(output.ToString()).Select(match => match.Value)];
        Assert.HasCount(2, changed);
        Assert.AreNotEqual(original[0], changed[0]);
        Assert.AreEqual(original[1], changed[1], "Encoding the changed first chart must not corrupt the cached second chart.");
        Assert.AreEqual(1, presenter.GetDiagnosticsSnapshot().LastEncodedFrameCount);
        output.GetStringBuilder().Clear();
        await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
        string[] replay = [.. SixelPayloadPattern().Matches(output.ToString()).Select(match => match.Value)];
        CollectionAssert.AreEqual(changed, replay);
        Assert.AreEqual(0, presenter.GetDiagnosticsSnapshot().LastEncodedFrameCount);
    }

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
    [DoNotParallelize]
    [DataRow(GraphKind.Flamegraph, 80, 40, 8, 16, 1)]
    [DataRow(GraphKind.Flamegraph, 80, 40, 8, 16, 2)]
    [DataRow(GraphKind.Flamegraph, 80, 40, 8, 16, 4)]
    [DataRow(GraphKind.Flamegraph, 144, 40, 10, 23, 1)]
    [DataRow(GraphKind.Flamegraph, 144, 40, 10, 23, 2)]
    [DataRow(GraphKind.Flamegraph, 144, 40, 10, 23, 4)]
    [DataRow(GraphKind.Flamegraph, 73, 28, 9, 17, 1)]
    [DataRow(GraphKind.Flamegraph, 73, 28, 9, 17, 2)]
    [DataRow(GraphKind.Flamegraph, 73, 28, 9, 17, 4)]
    [DataRow(GraphKind.Gantt, 80, 40, 8, 16, 2)]
    [DataRow(GraphKind.Gantt, 144, 40, 10, 23, 2)]
    [DataRow(GraphKind.Gantt, 73, 28, 9, 17, 2)]
    public async Task FixedRowPresenterUsesCellHeightAndLeavesUnusedSpace(GraphKind kind, int columns, int rows, int cellWidth, int cellHeight, int frameRows) {
        System.Management.Automation.PSObject[] objects = kind == GraphKind.Flamegraph ? [
            System.Management.Automation.PSObject.AsPSObject(new { FrameId = "root", ParentId = "", Name = "main", Weight = 2 }),
            System.Management.Automation.PSObject.AsPSObject(new { FrameId = "a", ParentId = "root", Name = "alpha", Weight = 3 }),
            System.Management.Automation.PSObject.AsPSObject(new { FrameId = "b", ParentId = "a", Name = "beta", Weight = 5 })
        ] : [
            System.Management.Automation.PSObject.AsPSObject(new { Task = "Build", Start = DateTimeOffset.UnixEpoch, End = DateTimeOffset.UnixEpoch.AddHours(4) }),
            System.Management.Automation.PSObject.AsPSObject(new { Task = "Test", Start = DateTimeOffset.UnixEpoch.AddHours(2), End = DateTimeOffset.UnixEpoch.AddHours(6) }),
            System.Management.Automation.PSObject.AsPSObject(new { Task = "Ship", Start = DateTimeOffset.UnixEpoch.AddHours(6), End = DateTimeOffset.UnixEpoch.AddHours(6) })
        ];
        using var viewer = new ObjectChartApp(objects, null, kind == GraphKind.Flamegraph ? ["Weight"] : null, kind, "Fixed rows",
            new("FrameId", "ParentId", "Name"), CancellationToken.None);
        viewer.Surface.Plot.FlameFrameRows = frameRows;
        _ = XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(viewer.Root, columns, rows);
        var chart = viewer.Surface.Plot;
        var bounds = chart.PlotBounds;
        var metrics = new TerminalPixelMetrics(columns * cellWidth, rows * cellHeight, cellWidth, cellHeight, columns, rows);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(columns, rows));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(viewer.Root, session.Instance);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, bounds, chart.Bounds, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, columns, rows), 0, bounds, TerminalGraphicsTextFrameKind.Full], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        using var output = new StringWriter();
        await presenter.PresentAsync(commands, context, new AnsiWriter(output), CancellationToken.None);
        string text = output.ToString();
        int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", StringComparison.Ordinal) + 2;
        var decoded = Decode(text[start..end]);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, bounds.Width * cellWidth, bounds.Height * cellHeight);
        var expected = plot.Pixels.ToArray();
        int paintedHeight = (kind == GraphKind.Flamegraph ? 3 * frameRows : 6) * cellHeight;
        int emptyRows = bounds.Height * cellHeight - paintedHeight;
        int emptyStart = kind == GraphKind.Flamegraph ? 0 : paintedHeight;
        Assert.IsLessThan(0, expected.AsSpan(emptyStart * plot.Width, emptyRows * plot.Width).IndexOfAnyExcept((byte)0),
            "Fixed frame/task rows must leave unused viewport height in actual terminal-pixel metrics.");
        foreach (var label in chart.AnnotationBounds) {
            for (int row = (label.Y - bounds.Y) * cellHeight; row < (label.Bottom - bounds.Y) * cellHeight; row++) {
                expected.AsSpan(row * plot.Width + (label.X - bounds.X) * cellWidth, label.Width * cellWidth).Clear();
            }
        }
        CollectionAssert.AreEqual(expected, decoded.Pixels, "The real presenter keeps fixed frame rows and excludes only clipped label cells.");
        int barRow = kind == GraphKind.Flamegraph ? emptyRows : cellHeight;
        Assert.IsGreaterThanOrEqualTo(0, plot.Pixels.Slice(barRow * plot.Width, plot.Width * cellHeight).IndexOfAnyExcept((byte)0));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(GraphKind.FlowChart, 8, 16)]
    [DataRow(GraphKind.FlowChart, 10, 23)]
    [DataRow(GraphKind.FlowChart, 9, 17)]
    [DataRow(GraphKind.NetworkDiagram, 8, 16)]
    [DataRow(GraphKind.NetworkDiagram, 10, 23)]
    [DataRow(GraphKind.NetworkDiagram, 9, 17)]
    public async Task DiagramPresenterUsesRetainedTopologyAndBoundedLabels(GraphKind kind, int cellWidth, int cellHeight) {
        const int columns = 100, rows = 32;
        var chart = ChartDiagramTests.Example(kind);
        _ = XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(chart, columns, rows);
        var bounds = chart.PlotBounds;
        var metrics = new TerminalPixelMetrics(columns * cellWidth, rows * cellHeight, cellWidth, cellHeight, columns, rows);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(columns, rows));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(chart, session.Instance);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, bounds, chart.Bounds, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, columns, rows), 0, bounds, TerminalGraphicsTextFrameKind.Full], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        using var output = new StringWriter();
        await presenter.PresentAsync(commands, context, new AnsiWriter(output), CancellationToken.None);
        string text = output.ToString();
        int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", StringComparison.Ordinal) + 2;
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.StartsWith("\x1bP0;1q", text[start..end], "Diagram graphics preserve reserved text pixels using P2=1.");
        var decoded = Decode(text[start..end]);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, bounds.Width * cellWidth, bounds.Height * cellHeight);
        var expected = plot.Pixels.ToArray();
        foreach (var label in chart.AnnotationBounds) {
            Assert.IsTrue(bounds.Contains(label.X, label.Y));
            Assert.IsLessThanOrEqualTo(bounds.Right, label.Right);
            for (int y = (label.Y - bounds.Y) * cellHeight; y < (label.Bottom - bounds.Y) * cellHeight; y++) {
                expected.AsSpan(y * plot.Width + (label.X - bounds.X) * cellWidth, label.Width * cellWidth).Clear();
            }
        }
        CollectionAssert.AreEqual(expected, decoded.Pixels, "Actual Sixel output keeps topology geometry and bounded text exclusions.");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(80, 40, 8, 16)]
    [DataRow(144, 40, 10, 23)]
    [DataRow(73, 28, 9, 17)]
    public async Task ObjectPiePresenterPreservesFittedBoundaryBetweenLegendAndFooter(int columns, int rows, int cellWidth, int cellHeight) {
        System.Management.Automation.PSObject[] objects = [
            System.Management.Automation.PSObject.AsPSObject(new { Category = "CPU", Load = 25 }),
            System.Management.Automation.PSObject.AsPSObject(new { Category = "Memory", Load = 50 }),
            System.Management.Automation.PSObject.AsPSObject(new { Category = "Disk", Load = 75 }),
            System.Management.Automation.PSObject.AsPSObject(new { Category = "Network", Load = 100 })
        ];
        using var viewer = new ObjectChartApp(objects, "Category", ["Load"], GraphKind.Pie, "Resource load", CancellationToken.None);
        _ = XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(viewer.Root, columns, rows);
        var chart = viewer.Surface.Plot;
        var bounds = chart.PlotBounds;
        Assert.IsGreaterThan(chart.Bounds.Y, bounds.Y, "The row legend must be outside the graphics viewport.");
        Assert.IsLessThan(chart.Bounds.Bottom, bounds.Bottom, "The radial scale footer must be outside the graphics viewport.");
        var metrics = new TerminalPixelMetrics(columns * cellWidth, rows * cellHeight, cellWidth, cellHeight, columns, rows);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(columns, rows));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(new XenoAtom.Terminal.UI.Controls.TextBlock(""), session.Instance);
        var commands = new GraphicsCommandBuffer {
            new GraphicsCommand(1, bounds, chart.Bounds, TerminalGraphicContent.FromObject(chart), ImageScaleMode.Stretch, false, 0, true, null)
        };
        var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, columns, rows), 0, bounds, TerminalGraphicsTextFrameKind.Full], null)!;
        using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, metrics);
        using var output = new StringWriter();
        await presenter.PresentAsync(commands, context, new AnsiWriter(output), CancellationToken.None);
        string text = output.ToString();
        int start = text.IndexOf("\x1bP", StringComparison.Ordinal), end = text.IndexOf("\x1b\\", StringComparison.Ordinal) + 2;
        var decoded = Decode(text[start..end]);
        var expected = new SixelPlot();
        chart.DrawPixels(expected, bounds.Width * cellWidth, bounds.Height * cellHeight);
        CollectionAssert.AreEqual(expected.Pixels.ToArray(), decoded.Pixels, "Legend/footer placement must not crop either radial edge.");
        Assert.IsLessThanOrEqualTo(2, decoded.Pixels.Take(expected.Width).Count(pixel => pixel != 0));
        Assert.IsLessThanOrEqualTo(2, decoded.Pixels.Skip((expected.Height - 1) * expected.Width).Count(pixel => pixel != 0));
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
            if (ChartTopology.Supports(kind)) {
                document.Root.Bind = "nodes";
                document.Root.EdgeBind = "edges";
                using var diagramApp = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), new System.Collections.Hashtable {
                    ["nodes"] = new[] { new { NodeId = "a", Name = "A" } }, ["edges"] = Array.Empty<object>()
                });
                Assert.AreEqual(kind, ((Chart)diagramApp.Controls["plot"]).GraphKind);
                continue;
            }
            if (kind == GraphKind.Gantt) {
                document.Root.Bind = "tasks";
                using var taskApp = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), new System.Collections.Hashtable {
                    ["tasks"] = new[] { new { Task = "Build", Start = DateTimeOffset.UnixEpoch, End = DateTimeOffset.UnixEpoch.AddHours(1) } }
                });
                Assert.AreEqual(kind, ((Chart)taskApp.Controls["plot"]).GraphKind);
                continue;
            }
            if (kind == GraphKind.Flamegraph) {
                document.Root.Bind = "frames";
                using var frameApp = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), new System.Collections.Hashtable {
                    ["frames"] = new[] { new { FrameId = "root", ParentId = "", Weight = 1d } }
                });
                Assert.AreEqual(kind, ((Chart)frameApp.Controls["plot"]).GraphKind);
                continue;
            }
            if (ChartMarket.Supports(kind) || ChartStackedArea.Supports(kind)) {
                document.Root.Bind = "prices";
                using var numericApp = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), new System.Collections.Hashtable { ["prices"] = new[] { 100d, 110d, 105d } });
                Assert.AreEqual(kind, ((Chart)numericApp.Controls["plot"]).GraphKind);
                continue;
            }
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
        Assert.AreEqual(new BoxSummary(3, 5, 7, 1, 8), ChartStatistics.Summarize([1, 2, 3, 4, 5, 6, 7, 8, 100]));
        Assert.AreEqual(new BoxSummary(5, 5, 5, 5, 5), ChartStatistics.Summarize([5]));
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
                        if (kind == GraphKind.Radar) {
                            history.Add(50, DateTimeOffset.UnixEpoch.AddSeconds(2));
                        }
                    }
                    if (ChartTopology.Supports(kind) || kind is GraphKind.Flamegraph or GraphKind.Gantt) {
                        var chart = new Chart { GraphKind = kind };
                        if (populated) {
                            if (ChartTopology.Supports(kind)) {
                                chart.Nodes.AddRange([new("a", "A"), new("b", "B")]);
                                chart.Edges.Add(new("a", "b"));
                            }
                            else if (kind == GraphKind.Gantt) {
                                chart.Tasks.Add(new("Build", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), .5));
                                chart.Tasks.Add(new("Ship", DateTimeOffset.UnixEpoch.AddHours(1), DateTimeOffset.UnixEpoch.AddHours(1)));
                            }
                            else {
                                chart.Frames.Add(new("root", null, "root", 50));
                                chart.Frames.Add(new("child", "root", "child", 50));
                            }
                        }
                        chart.DrawPixels(plot, size.Item1, size.Item2);
                    }
                    else {
                        plot.Draw(history, history.Latest, 100, size.Item1, size.Item2, Color.Rgb(88, 213, 230), Color.Rgb(13, 20, 32), kind);
                    }
                    var decoded = Decode(encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette));
                    CollectionAssert.AreEqual(plot.Pixels.ToArray(), decoded.Pixels, $"{kind}: {size}, populated={populated}");
                    var direct = Decode(encoder.EncodePlot(plot));
                    CollectionAssert.AreEqual(plot.Pixels.ToArray(), direct.Pixels, $"Direct planes: {kind}: {size}, populated={populated}");
                }
            }
        }
    }
    [TestMethod]
    public void BulkRectangleFillsMatchPerPixelOverpaintingAcrossBandsAndResizes() {
        var actual = new SixelPlot();
        var reference = new SixelPlot();
        var chart = new Chart { GraphKind = GraphKind.Heatmap };
        var encoder = new SixelEncoder();
        var fill = typeof(SixelPlot).GetMethod("FillRectangle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<int, int, int, int, byte>>(actual);
        var pixel = typeof(SixelPlot).GetMethod("SetPixel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<int, int, byte>>(reference);
        var random = new Random(7284);
        foreach (var (width, height) in new[] { (63, 31), (1, 1), (11, 13), (2, 7), (63, 31) }) {
            chart.DrawPixels(actual, width, height);
            chart.DrawPixels(reference, width, height);
            for (int index = 0; index < 120; index++) {
                int left = random.Next(width), right = random.Next(left, width);
                int top = random.Next(height), bottom = random.Next(top, height);
                byte color = (byte)random.Next(1, SixelPlot.InkColors + 1);
                fill(left, top, right, bottom, color);
                for (int y = top; y <= bottom; y++) {
                    for (int x = left; x <= right; x++) {
                        pixel(x, y, color);
                    }
                }

                CollectionAssert.AreEqual(reference.Pixels.ToArray(), actual.Pixels.ToArray());
                Assert.AreEqual(encoder.EncodePlot(reference), encoder.EncodePlot(actual),
                    "Boundary metadata and retained planes must encode exactly like per-pixel replacement.");
            }

            CollectionAssert.AreEqual(actual.Pixels.ToArray(), Decode(encoder.EncodePlot(actual)).Pixels);
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

        foreach (var kind in Enum.GetValues<GraphKind>().Where(kind => !ChartTopology.Supports(kind) && kind is not (GraphKind.Flamegraph or GraphKind.Gantt))) {
            foreach (var size in new[] { (63, 31), (11, 13), (63, 31) }) {
                var ink = kind == GraphKind.Line ? Color.Rgb(255, 0, 0) : Color.Rgb(0, 128, 255);
                plot.Draw(history, history.Latest, 100, size.Item1, size.Item2, ink, Color.Rgb(13, 20, 32), kind, 5);
                var actual = Decode(encoder.EncodePlot(plot));
                CollectionAssert.AreEqual(plot.Pixels.ToArray(), actual.Pixels, $"{kind}: {size}");
                var expectedColor = (ChartRadial.IsRadial(kind) || kind is GraphKind.Waterfall or GraphKind.Bars or GraphKind.HorizontalBars ? plot.Palette[6] : ink).ToRgb();
                Assert.AreEqual(((expectedColor.R * 100 + 127) / 255, (expectedColor.G * 100 + 127) / 255, (expectedColor.B * 100 + 127) / 255), actual.Colors[6]);
            }
        }
    }
    [TestMethod]
    public void WarmRastersReuseBuffersAcrossSizesAndPlotTypes() {
        var history = new TimeSeries();
        for (int i = 0; i < 120; i++) {
            history.Add(1 + i % 100, DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        var plot = new SixelPlot();
        var ink = Color.Rgb(88, 213, 230);
        var background = Color.Rgb(13, 20, 32);
        var kinds = Enum.GetValues<GraphKind>().Where(kind => !ChartTopology.Supports(kind) && kind is not (GraphKind.Flamegraph or GraphKind.Gantt)).ToArray();
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
    public void BulkColumnsMatchStrongerShadePaintingWithClippingAndExistingStrokes() {
        var actual = new SixelPlot();
        var reference = new SixelPlot();
        var chart = new Chart([10, 80, 30, 90, 0]) { GraphKind = GraphKind.Line };
        var encoder = new SixelEncoder();
        var fill = typeof(SixelPlot).GetMethod("FillColumn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<int, int, int, byte>>(actual);
        var paint = typeof(SixelPlot).GetMethod("Paint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<int, int, byte>>(reference);
        var random = new Random(312);
        foreach (var (width, height) in new[] { (63, 31), (1, 1), (11, 13), (2, 7), (63, 31) }) {
            chart.DrawPixels(actual, width, height);
            chart.DrawPixels(reference, width, height);
            for (int index = 0; index < 160; index++) {
                int x = random.Next(-2, width + 2);
                int top = random.Next(-6, height + 6), bottom = random.Next(-6, height + 6);
                byte color = (byte)random.Next(1, SixelPlot.InkColors + 1);
                fill(x, top, bottom, color);
                for (int y = top; y <= bottom; y++) {
                    paint(x, y, color);
                }

                CollectionAssert.AreEqual(reference.Pixels.ToArray(), actual.Pixels.ToArray());
                Assert.AreEqual(encoder.EncodePlot(reference), encoder.EncodePlot(actual));
            }

            CollectionAssert.AreEqual(actual.Pixels.ToArray(), Decode(encoder.EncodePlot(actual)).Pixels);
        }
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
    public void MaskedFrameErasingDoesNotBoxWindowEnumeratorsPerCell() {
        using var output = new StringWriter(new System.Text.StringBuilder(65536));
        var writer = new AnsiWriter(output);
        var bounds = new Rectangle(0, 0, 160, 40);
        var windows = new List<Rectangle> { new(60, 10, 40, 15), new(155, 0, 5, 40) };
        for (int index = 0; index < 5; index++) {
            output.GetStringBuilder().Clear();
            ChartClipping.Erase(writer, bounds, windows, bounds);
        }

        output.GetStringBuilder().Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        ChartClipping.Erase(writer, bounds, windows, bounds);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0L, allocated, "Window checks and erase commands must reuse the span writer without allocations.");
        var erased = Enumerable.Range(0, 40).SelectMany(y => Enumerable.Range(0, y is >= 10 and < 25 ? 2 : 1)
            .Select(run => y is >= 10 and < 25
                ? $"\x1b[{y + 1};{(run == 0 ? 1 : 101)}H\x1b[{(run == 0 ? 60 : 55)}X"
                : $"\x1b[{y + 1};1H\x1b[155X"));
        Assert.AreEqual(string.Concat(erased), output.ToString(), "Popup and label cells must never be erased.");
    }

    [TestMethod]
    public void OwnedSpanPayloadsPreserveProtocolAndDoNotBorrowEncoderScratch() {
        var chart = new Chart([10, 50, 25, 80]) { GraphKind = GraphKind.Area };
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 240, 120);
        var encoder = new SixelEncoder();
        var first = new SixelPayload();
        var second = new SixelPayload();
        string direct = encoder.EncodePlot(plot);
        encoder.EncodePlot(plot, first);
        Assert.IsTrue(first.Span.SequenceEqual(direct));
        string frame = encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette);
        encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette, second);
        Assert.IsTrue(second.Span.SequenceEqual(frame));
        Assert.IsTrue(first.Span.SequenceEqual(direct), "Encoding another placement must not overwrite the first payload.");
        using var output = new StringWriter();
        var writer = new AnsiWriter(output);
        for (int index = 0; index < 10; index++) {
            encoder.EncodePlot(plot, first);
            encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette, second);
            output.GetStringBuilder().Clear();
            writer.Write(first.Span);
            writer.Write(second.Span);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            encoder.EncodePlot(plot, first);
            encoder.EncodeFrame(plot.Pixels, plot.Width, plot.Height, plot.Palette, second);
            output.GetStringBuilder().Clear();
            writer.Write(first.Span);
            writer.Write(second.Span);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "Owned span encoding should reuse planes, headers, builders and payload buffers.");
        CollectionAssert.AreEqual(plot.Pixels.ToArray(), Decode(new string(first.Span)).Pixels);
        chart.Values.Clear();
        chart.DrawPixels(plot, 20, 10);
        encoder.EncodePlot(plot, first);
        Assert.IsTrue(second.Span.SequenceEqual(frame), "A shorter replacement must not expose stale characters or another placement's data.");
        CollectionAssert.AreEqual(plot.Pixels.ToArray(), Decode(new string(first.Span)).Pixels);
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
    [GeneratedRegex("\x1bP.*?\x1b\\\\", RegexOptions.Singleline)]
    private static partial Regex SixelPayloadPattern();
}
