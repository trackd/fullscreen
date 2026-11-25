using System.Diagnostics;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ResourceGraphicsTests {
    [TestMethod]
    [DataRow(24)]
    [DataRow(30)]
    [DataRow(44)]
    public void DashboardKeepsCpuAndMemoryPlotsWhenResourceDetailsArePresent(int height) {
        var surface = new MonitorSurface { View = MonitorView.Dashboard, MinHeight = height, MaxHeight = height };
        surface.SetFrame(Fixture());
        _ = VisualSnapshotRenderer.Render(surface, 100, height);
        var plots = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.IsVisible).ToArray();
        Assert.HasCount(5, plots);
        Assert.IsGreaterThan(0, plots[0].Bounds.Height, "Resource details must leave room for the CPU plot.");
        Assert.IsGreaterThan(0, plots[1].Bounds.Height, "Resource details must leave room for the memory plot.");
    }
    [TestMethod]
    [DataRow(MonitorView.Disks, true, 2)]
    [DataRow(MonitorView.Disks, true, 12)]
    [DataRow(MonitorView.Disks, false, 2)]
    [DataRow(MonitorView.Processes, true, 2)]
    [DataRow(MonitorView.Processes, false, 2)]
    public async Task StorageOverviewAndMiniGraphsUseSixelWithSupportedTerminals(MonitorView view, bool supported, int drives) {
        var metrics = new TerminalPixelMetrics(1040, 800, 8, 16, 130, 50);
        TerminalGraphicsCapabilities graphics = supported ? new TerminalGraphicsCapabilities {
            PreferredProtocol = TerminalGraphicsProtocol.Sixel,
            SupportedProtocols = [TerminalGraphicsProtocol.Sixel],
            SupportState = TerminalGraphicsSupportState.Confirmed,
            SupportsStaticImages = true,
            PixelMetrics = metrics
        } : TerminalGraphicsCapabilities.None;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(130, 50), new TerminalCapabilities {
            AnsiEnabled = true,
            Graphics = graphics,
            ColorLevel = TerminalColorLevel.TrueColor,
            IsOutputRedirected = false,
            IsInputRedirected = false
        });
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.DisableProbing = true;
        options.Graphics.DisableGraphics = !supported;
        if (supported) { options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Sixel; options.Graphics.ForcedPixelMetrics = metrics; }
        using TerminalSession session = Terminal.Open(backend, options, force: true);
        var surface = new MonitorSurface { View = view };
        CollectorFrame frame = Fixture();
        if (drives != 2) { frame.Disks = [.. Enumerable.Range(0, drives).Select(index => new DiskRow("drive" + index, "Drive " + index, "Device", ReadBytes: 10, WriteBytes: 20, ReadIops: 1, WriteIops: 2))]; }
        surface.SetFrame(frame);
        frame.CapturedAt = frame.CapturedAt.AddSeconds(1);
        surface.SetFrame(frame);
        using var presenter = new SixelChartPresenter(graphics, metrics);
        ChartPresenters.Register(session.Instance, presenter);
        await using var app = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, GraphicsPresenter = presenter });
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            time += Stopwatch.Frequency / 10;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [time]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        try {
            Tick();
            Assert.AreEqual(supported, backend.GetOutText().Contains("\x1bP0;1q", StringComparison.Ordinal));
            long expectedCharts = view == MonitorView.Disks ? 4 + Math.Min(drives, 6) : 6;
            Assert.AreEqual(supported ? expectedCharts : 0L, presenter.GetDiagnosticsSnapshot().EncodedFrameCount, "Each overview and visible drive mini graph must use the graphics presenter.");
            Assert.DoesNotContain("not exposed", backend.GetOutText());
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null); }
    }

    [TestMethod]
    public async Task TallDiskViewsCreateChartsForEveryVisibleDriveAndHideThemOnNavigation() {
        var surface = new MonitorSurface { View = MonitorView.Disks, MinHeight = 100, MaxHeight = 100 };
        CollectorFrame frame = Fixture();
        frame.Disks = [.. Enumerable.Range(0, 12).Select(index => new DiskRow("drive" + index, "Drive " + index, "Device", ReadBytes: 10, WriteBytes: 20, ReadIops: 1, WriteIops: 2))];
        surface.SetFrame(frame);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 130, 100);
        Assert.HasCount(16, surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.IsVisible).ToArray());
        ui.Text("7", () => surface.View == MonitorView.Dashboard);
        ui.Tick();
        Assert.HasCount(5, surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.IsVisible).ToArray());
    }

    [TestMethod]
    public void MissingTemperatureDoesNotProduceAnAvailabilityMessageInTheDashboard() {
        var surface = new MonitorSurface { View = MonitorView.Dashboard, MinHeight = 44, MaxHeight = 44 };
        surface.SetFrame(Fixture());
        string text = string.Join('\n', VisualSnapshotRenderer.Render(surface, 130, 44).ToMarkupLines());
        Assert.Contains("CPU 0", text);
        Assert.Contains("LOGICAL PROCESSORS", text);
        Assert.Contains("MEMORY BREAKDOWN", text);
        Assert.Contains("[#58d5e6 on #141e2e]CPU 0", text, "CPU labels must use the panel background rather than painting page-colored rectangles.");
        Assert.DoesNotContain("Temperature", text);
        Assert.DoesNotContain("not exposed", text);
        Assert.DoesNotContain("sensors unavailable", text);
    }

    private static CollectorFrame Fixture() {
        var frame = new CollectorFrame {
            CapturedAt = DateTimeOffset.UnixEpoch,
            CpuPercent = 40,
            TotalMemory = 4096,
            AvailableMemory = 1024,
            CpuDetails = new("CPU fixture", [new(0, 40)], []),
            MemoryDetails = new(Free: 512, Cached: 1024),
            GpuCount = 1,
            ReceiveBytesPerSecond = 100,
            SendBytesPerSecond = 50,
            Disks = [new("a", "Drive A", "Device", ReadBytes: 10, WriteBytes: 20, ReadIops: 2, WriteIops: 3), new("b", "Drive B", "Device", ReadBytes: 20, WriteBytes: 30, ReadIops: 4, WriteIops: 5)]
        };
        frame.Gpus[0] = new("GPU", "fixture", 30, null, null, null);
        return frame;
    }
}
