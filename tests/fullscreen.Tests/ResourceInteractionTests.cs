using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ResourceInteractionTests {
    [TestMethod]
    public async Task ProcessHistoryUpdatesLiveAndStopsOnPidReuse() {
        var surface = new MonitorSurface();
        var frame = new CollectorFrame { ProcessCount = 1 };
        frame.Processes[0] = new(42, "fixture", 10, 1024, 1, StartTimeTicks: 100);
        surface.SetFrame(frame);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 120, 40);
        surface.ShowProcessDetails(frame.Processes[0]);
        ui.Tick();
        frame.Processes[0] = frame.Processes[0] with { CpuPercent = 20, WorkingSet = 2048 };
        surface.SetFrame(frame);
        ui.Tick();
        var live = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(text => text.Text?.Contains("Last 64 samples", StringComparison.Ordinal) == true);
        Assert.Contains("CPU 20.0%", live.Text!);
        Assert.Contains("RAM 2.0 KiB", live.Text!);
        frame.Processes[0] = frame.Processes[0] with { StartTimeTicks = 200, CpuPercent = 99 };
        surface.SetFrame(frame);
        ui.Tick();
        Assert.Contains("identity changed", live.Text!);
        Assert.DoesNotContain("99", live.Text!);
        ui.Text("q", () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsFalse(surface.ExitRequested);
    }

    [TestMethod]
    public async Task InterfaceKeysSelectIndependentGraphsAndRecoverAfterRemoval() {
        var surface = new MonitorSurface { View = MonitorView.Network };
        var frame = new CollectorFrame { NetworkCount = 2, ReceiveBytesPerSecond = 30, SendBytesPerSecond = 40 };
        frame.Networks[0] = new("one", "eth0", 10, 15, 100, 200, 0, 1000) { Addresses = "192.0.2.1" };
        frame.Networks[1] = new("two", "eth1", 20, 25, 100, 200, 0, 1000);
        surface.SetFrame(frame);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 120, 40);
        ui.Text("]", () => surface.GraphInterface?.Id == "one");
        ui.Text("d", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("192.0.2.1", ui.Output);
        ui.Text("q", () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        ui.Text("]", () => surface.GraphInterface?.Id == "two");
        frame.NetworkCount = 1;
        surface.SetFrame(frame);
        ui.Tick();
        Assert.IsNull(surface.GraphInterface, "A removed interface must fall back to aggregate, not reuse its history for a different device.");
        ui.Text("]", () => surface.GraphInterface?.Id == "one");
        ui.Text("[", () => surface.GraphInterface is null);
    }

    [TestMethod]
    public async Task DiskTabKeysShowCapacityLatencyAndSelectedDeviceDetails() {
        var surface = new MonitorSurface { View = MonitorView.Dashboard };
        surface.SetFrame(new CollectorFrame { Disks = [new("one", "first-device", "Device", ReadIops: 7), new("two", "second-volume", "Volume", "ext4", 4096, 1024)] });
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 120, 40);
        ui.Text("5", () => surface.View == MonitorView.Disks);
        ui.Key(TerminalKey.Right, () => ui.Output.Contains("Latency", StringComparison.Ordinal));
        ui.Key(TerminalKey.Down, () => surface.SelectedDisk?.Id == "two");
        ui.Text("d", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        Assert.Contains("second-volume", ((TextBlock)dialog.Title!).Text!);
        Assert.Contains("ext4", ui.Output);
        ui.Text("q", () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsFalse(surface.ExitRequested);
    }
}
