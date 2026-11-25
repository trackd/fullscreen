using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ResourceDetailsTests {
    [TestMethod]
    public void StoragePrecedesDashboardAndProcessesRequestIopsOverview() {
        CollectionAssert.AreEqual(new[] { MonitorView.Processes, MonitorView.Services, MonitorView.Network, MonitorView.Gpu, MonitorView.Disks, MonitorView.Users, MonitorView.Dashboard }, MonitorSurface.NavigationViews);
        var surface = new MonitorSurface { View = MonitorView.Processes };
        Assert.AreEqual(CollectorMetrics.Disks, surface.RequiredMetrics & CollectorMetrics.Disks);
        surface.Minimal = true;
        Assert.AreEqual(CollectorMetrics.None, surface.RequiredMetrics & CollectorMetrics.Disks, "Hidden IOPS graphs must not keep storage providers active.");
    }
    [TestMethod]
    public void CpuDeltasKeepWaitAndStealDistinctAndResetOnRegression() {
        using var sampler = new ResourceSampler();
        Assert.IsNull(sampler.CpuDelta(7, [100, 0, 100, 100, 0, 0, 0, 0]).Usage);
        var core = sampler.CpuDelta(7, [120, 0, 120, 140, 10, 0, 0, 10], 2400);
        Assert.AreEqual(50d, core.Usage);
        Assert.AreEqual(20d, core.User);
        Assert.AreEqual(20d, core.System);
        Assert.AreEqual(10d, core.Wait);
        Assert.AreEqual(10d, core.Steal);
        Assert.AreEqual(2400d, core.Megahertz);
        Assert.IsNull(sampler.CpuDelta(7, [120, 0, 120, 140, 10, 0, 0, 10]).Usage, "No elapsed ticks is not measured idle.");
        Assert.IsNull(sampler.CpuDelta(7, [1, 0, 1, 1, 0, 0, 0, 0]).Usage);
        Assert.IsNull(sampler.CpuDelta(8, [200, 0, 200, 200]).Usage, "A new processor cannot inherit another processor's baseline.");
        core = sampler.CpuDelta(8, [210, 0, 200, 210]);
        Assert.AreEqual(50d, core.Usage);
        Assert.IsNull(core.Wait);
        Assert.IsNull(core.Steal);
    }

    [TestMethod]
    public void MemoryCategoriesPreserveFreeCacheSwapAndCommitSemantics() {
        var memory = ResourceSampler.LinuxMemory("MemFree: 10 kB\nCached: 100 kB\nSReclaimable: 20 kB\nShmem: 5 kB\nSwapTotal: 50 kB\nSwapFree: 30 kB\nCommitted_AS: 200 kB\nCommitLimit: 300 kB\n");
        Assert.AreEqual(10UL * 1024, memory.Free);
        Assert.AreEqual(115UL * 1024, memory.Cached);
        Assert.AreEqual(20UL * 1024, memory.SwapUsed);
        Assert.AreEqual(200UL * 1024, memory.Committed);
        Assert.IsNull(ResourceSampler.LinuxMemory("").Free);
        Assert.IsNull(ResourceSampler.LinuxMemory("SwapTotal: 1 kB\nSwapFree: 2 kB").SwapUsed);
        Assert.AreEqual(0UL, ResourceSampler.LinuxMemory("Cached: 1 kB\nShmem: 99 kB").Cached, "Shared-memory subtraction cannot underflow.");
        Assert.AreEqual(12.5d, ResourceSampler.Pressure("some avg10=12.50 avg60=1.00 total=100\nfull avg10=2.00 avg60=0.00", "some"));
        Assert.AreEqual(2d, ResourceSampler.Pressure("full avg10=2.00 avg60=0.00", "full"));
        Assert.IsNull(ResourceSampler.Pressure("some avg10=200", "some"));
        Assert.IsNull(ResourceSampler.Pressure("", "full"));
    }

    [TestMethod]
    public void MacStorageUsesBytesAndNanosecondsAndLeavesMissingCountersUnknown() {
        using var sampler = new DiskSampler();
        string Xml(int operations, int bytes, int time) => $"<plist><array><dict><key>IORegistryEntryID</key><integer>1</integer><key>Statistics</key><dict><key>Operations (Read)</key><integer>{operations}</integer><key>Bytes (Read)</key><integer>{bytes}</integer><key>Total Time (Read)</key><integer>{time}</integer><key>Operations (Write)</key><integer>0</integer><key>Bytes (Write)</key><integer>0</integer></dict></dict></array></plist>";
        Assert.IsNull(sampler.ParseMac(Xml(1, 1024, 1_000_000), 1)[0].ReadBytes);
        var row = sampler.ParseMac(Xml(3, 3072, 5_000_000), 2)[0];
        Assert.AreEqual(1024d, row.ReadBytes);
        Assert.AreEqual(1d, row.ReadIops);
        Assert.AreEqual(2d, row.ReadLatency);
        Assert.IsNull(row.WriteLatency);
        Assert.IsNull(row.Busy);
        Assert.IsNull(row.Queue);
        Assert.HasCount(0, sampler.ParseMac("<plist><dict><key>Statistics</key><dict/></dict></plist>", 1));
        Assert.HasCount(0, sampler.ParseMac("invalid xml", 1));
    }

    [TestMethod]
    public void CpuTemperatureDiscoveryExcludesGpuSensorsAndImplausibleValues() {
        using var fixture = new Fixture();
        fixture.Write("hwmon0/name", "coretemp");
        fixture.Write("hwmon0/temp1_label", "Package id 0");
        fixture.Write("hwmon0/temp1_input", "63000");
        fixture.Write("hwmon0/temp2_input", "999000");
        fixture.Write("hwmon1/name", "amdgpu");
        fixture.Write("hwmon1/temp1_input", "90000");
        var sensors = ResourceSampler.Temperatures(fixture.Root);
        Assert.HasCount(1, sensors);
        Assert.AreEqual("coretemp · Package id 0", sensors[0].Name);
        Assert.AreEqual(63d, sensors[0].Celsius);
    }

    [TestMethod]
    public void DiskDeltasCalculateOperationsLatencyAndQueueWithoutCountingPartitions() {
        using var sampler = new DiskSampler();
        Assert.IsNull(sampler.Delta("sda", "sda", [100, 0, 100, 100, 100, 0, 100, 100, 4, 100, 100], 2).ReadIops);
        var row = sampler.Delta("sda", "sda", [104, 0, 108, 120, 102, 0, 116, 106, 0, 600, 1100], 2);
        Assert.AreEqual(2d, row.ReadIops);
        Assert.AreEqual(1d, row.WriteIops);
        Assert.AreEqual(2048d, row.ReadBytes);
        Assert.AreEqual(4096d, row.WriteBytes);
        Assert.AreEqual(5d, row.ReadLatency);
        Assert.AreEqual(3d, row.WriteLatency);
        Assert.AreEqual(25d, row.Busy);
        Assert.AreEqual(0.5d, row.Queue);
        row = sampler.Delta("sda", "sda", [104, 0, 108, 120, 102, 0, 116, 106, 0, 600, 1100], 2);
        Assert.AreEqual(0d, row.ReadIops);
        Assert.IsNull(row.ReadLatency, "No completed operations means undefined latency, not zero latency.");
        Assert.IsNull(sampler.Delta("sda", "sda", new ulong[11], 2).ReadIops);
        Assert.IsNull(DiskSampler.Parse("1 2 3"));
        Assert.IsNull(DiskSampler.Parse("1 2 3 4 5 bad 7 8 9 10 11"));
        using var fixture = new Fixture();
        fixture.Write("block/sda/stat", "1 0 2 3 4 0 5 6 0 7 8");
        fixture.Write("block/sda/sda1/stat", "1 0 2 3 4 0 5 6 0 7 8");
        using var discovery = new DiskSampler(fixture.Root);
        Assert.HasCount(1, discovery.Linux(1), "Partition directories must not be enumerated as another whole device.");
    }

    [TestMethod]
    public void ResourceProtocolAndCopiesPreserveRemoteIdentityAndUnknowns() {
        var frame = new CollectorFrame {
            CpuDetails = new("REMOTE CPU", [new(7, 42, Wait: 3, Megahertz: 2400)], [new("Package", 65)], 123, 1, 2, 3, 4),
            MemoryDetails = new(Free: 1, Cached: 2, SwapTotal: 3, SwapUsed: 1, Committed: 8, CommitLimit: 10, Compressed: 4, Wired: 5, Scope: "fixture"),
            Disks = [new("disk-id", "Remote disk", "Device", ReadBytes: 1024, ReadIops: 7, ReadLatency: 2, Queue: 1, Temperature: 40)],
            GpuCount = 1,
            NetworkCount = 1,
            ProcessCount = 1
        };
        frame.Gpus[0] = new("GPU", "fixture", null, null, null, null) { MemoryClockMHz = 123, PcieReceiveBytes = 456, PcieSendBytes = 789, PerformanceState = 2 };
        frame.Networks[0] = new("net", "eth0", 1, 2, 3, 4, 0, 1000) { Addresses = "192.0.2.1" };
        frame.Processes[0] = new(123, "process", 1, 2, 3) { DiskReadBytesPerSecond = 100, DiskWriteBytesPerSecond = 200 };
        var copy = new CollectorFrame();
        copy.CopyFrom(frame);
        frame.Disks = [];
        Assert.HasCount(1, copy.Disks);
        var remote = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(copy), remote);
        Assert.AreEqual("REMOTE CPU", remote.CpuDetails!.Name);
        CollectionAssert.AreEqual(copy.CpuDetails!.Cores, remote.CpuDetails.Cores);
        CollectionAssert.AreEqual(copy.CpuDetails.Temperatures, remote.CpuDetails.Temperatures);
        Assert.AreEqual(copy.MemoryDetails, remote.MemoryDetails);
        CollectionAssert.AreEqual(copy.Disks, remote.Disks);
        Assert.AreEqual(copy.Gpus[0], remote.Gpus[0]);
        Assert.AreEqual(copy.Networks[0], remote.Networks[0]);
        Assert.AreEqual(copy.Processes[0], remote.Processes[0]);
        Assert.IsNull(remote.Disks[0].WriteIops);
    }

    [TestMethod]
    public void DisksAreDemandDrivenAndDashboardShowsRemoteCpuAndMemory() {
        var surface = new MonitorSurface { View = MonitorView.Dashboard };
        Assert.AreEqual(CollectorMetrics.None, surface.RequiredMetrics & CollectorMetrics.Disks);
        var frame = new CollectorFrame { CpuDetails = new("REMOTE CPU", [new(0, 42)], [new("Package", 65)]), MemoryDetails = new(Free: 1024, Cached: 2048), Disks = [new("disk-id", "Remote disk", "Device", ReadIops: 7)] };
        surface.SetFrame(frame);
        string dashboard = string.Join('\n', VisualSnapshotRenderer.Render(surface, 130, 50).ToMarkupLines());
        Assert.Contains("REMOTE CPU", dashboard);
        Assert.Contains("65.0°C", dashboard);
        Assert.Contains("Cache", dashboard);
        Assert.DoesNotContain("Remote disk", dashboard);
        surface = new MonitorSurface { View = MonitorView.Disks, Frame = frame };
        Assert.AreEqual(CollectorMetrics.Disks, surface.RequiredMetrics & CollectorMetrics.Disks);
        string disks = string.Join('\n', VisualSnapshotRenderer.Render(surface, 130, 40).ToMarkupLines());
        Assert.Contains("IOPS · read", disks);
        Assert.Contains("Remote disk", disks);
        Assert.Contains("7.0", disks);
        Assert.AreEqual("·▁█", MonitorSurface.ResourceSparkline([null, 0, 100]));
    }

    private sealed class Fixture : IDisposable {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "fullscreen-resource-" + Guid.NewGuid().ToString("N"));
        internal void Write(string relative, string value) {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
