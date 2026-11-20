using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class LinuxGpuSamplerTests {
    [TestMethod]
    public void I915ClientDeltasDeduplicateDescriptorsAndAccountForEngineCapacity() {
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 100000000 ns\ndrm-engine-video: 0 ns\ndrm-engine-capacity-video: 2\ndrm-memory-system: 128 KiB"));
        fixture.Write(11, 8, File.ReadAllText(fixture.PathFor(10, 4)));
        fixture.Write(12, 4, Client("i915", 2, "drm-engine-render: 0 ns\ndrm-memory-system: 64 KiB"));
        var sampler = new LinuxGpuSampler(fixture.Proc);
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(1, frame.GpuCount, "Intel must be discovered without gpu_busy_percent.");
        Assert.Contains("Intel GPU", frame.Gpus[0].Name);
        Assert.IsNull(frame.Gpus[0].Utilization, "First counters need a baseline.");
        Assert.AreEqual(192UL * 1024, frame.Gpus[0].UsedMemory);
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 600000000 ns\ndrm-engine-video: 1000000000 ns\ndrm-engine-capacity-video: 2\ndrm-memory-system: 128 KiB"));
        fixture.Write(11, 8, File.ReadAllText(fixture.PathFor(10, 4)));
        fixture.Write(12, 4, Client("i915", 2, "drm-engine-render: 250000000 ns\ndrm-memory-system: 64 KiB"));
        frame = fixture.Capture(sampler);
        Assert.AreEqual(75d, frame.Gpus[0].Utilization, "Render is 50% + 25%; the two-engine video class is 50%.");
        Assert.AreEqual(192UL * 1024, frame.Gpus[0].UsedMemory, "A shared client must not be counted per descriptor or PID.");
        Assert.IsTrue(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuUtilization));
        Assert.IsTrue(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuUsedMemory));
        var remote = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), remote);
        Assert.AreEqual(frame.Gpus[0], remote.Gpus[0]);
        Assert.AreEqual(frame.GpuStatus, remote.GpuStatus);
    }

    [TestMethod]
    public void XeUsesGpuCycleDeltasAndPrefersResidentMemoryRegardlessOfFieldOrder() {
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("xe", 1, "drm-cycles-vcs: 100\ndrm-total-cycles-vcs: 1000\ndrm-engine-capacity-vcs: 2\ndrm-resident-system: 12 KiB\ndrm-total-system: 99 MiB"));
        var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        fixture.Write(10, 4, Client("xe", 1, "drm-cycles-vcs: 600\ndrm-total-cycles-vcs: 2000\ndrm-engine-capacity-vcs: 2\ndrm-total-system: 99 MiB\ndrm-resident-system: 12 KiB"));
        var frame = fixture.Capture(sampler, 7);
        Assert.AreEqual(25d, frame.Gpus[0].Utilization, "GPU clock counters must not use the CPU sampling interval.");
        Assert.AreEqual(12UL * 1024, frame.Gpus[0].UsedMemory);
        Assert.IsNull(frame.Gpus[0].TotalMemory, "Shared system memory must not be invented as dedicated VRAM.");
    }

    [TestMethod]
    public void CounterRegressionKeepsHighWaterMarkAndClosedClientsDoNotSubtractUsage() {
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 100000000 ns"));
        var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 80000000 ns"));
        Assert.AreEqual(0d, fixture.Capture(sampler).Gpus[0].Utilization);
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 200000000 ns"));
        Assert.AreEqual(10d, fixture.Capture(sampler).Gpus[0].Utilization);
        File.Delete(fixture.PathFor(10, 4));
        Assert.IsNull(fixture.Capture(sampler).Gpus[0].Utilization);
    }

    [TestMethod]
    public void ClientIdsAreScopedToTheirDevice() {
        using var fixture = new Fixture();
        fixture.AddDevice("card1", "0000:03:00.0");
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 0 ns"));
        fixture.Write(11, 4, Client("i915", 1, "drm-engine-render: 0 ns").Replace("0000:00:02.0", "0000:03:00.0", StringComparison.Ordinal));
        var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 500000000 ns"));
        fixture.Write(11, 4, Client("i915", 1, "drm-engine-render: 250000000 ns").Replace("0000:00:02.0", "0000:03:00.0", StringComparison.Ordinal));
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(2, frame.GpuCount);
        Assert.AreEqual(50d, frame.Gpus.Take(frame.GpuCount).Single(row => row.Name.StartsWith("card0", StringComparison.Ordinal)).Utilization);
        Assert.AreEqual(25d, frame.Gpus.Take(frame.GpuCount).Single(row => row.Name.StartsWith("card1", StringComparison.Ordinal)).Utilization);
    }

    [TestMethod]
    public void LinuxDescriptorDiscoverySkipsRegularFilesAndKeepsGpuDeviceLinks() {
        if (!OperatingSystem.IsLinux()) {
            Assert.Inconclusive("Linux readlink discovery requires a Linux runtime.");
        }
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("i915", 1, "drm-memory-system: 99 MiB"));
        fixture.Write(10, 5, Client("i915", 2, "drm-memory-system: 32 KiB"));
        fixture.Link(10, 4, "/tmp/ordinary-file");
        fixture.Link(10, 5, "/dev/dri/renderD128");
        var frame = fixture.Capture(new LinuxGpuSampler(fixture.Proc));
        Assert.AreEqual(32UL * 1024, frame.Gpus[0].UsedMemory);
    }

    [TestMethod]
    public void DetectedIntelWithoutReadableCountersRemainsUnknownWithAUsefulStatus() {
        using var fixture = new Fixture();
        fixture.Write(10, 4, "pos: 0\nflags: 0100002\n");
        var sampler = new LinuxGpuSampler(fixture.Proc);
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(1, frame.GpuCount);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.IsNull(frame.Gpus[0].UsedMemory);
        Assert.AreEqual(CollectorCapabilities.None, sampler.Capabilities);
        Assert.Contains("no readable", frame.GpuStatus);
        Assert.IsNull(LinuxGpuSampler.ParseClient("drm-driver: amdgpu\ndrm-pdev: 0000:00:02.0\ndrm-client-id: 1\n"));
        var malformed = LinuxGpuSampler.ParseClient(Client("i915", 1, "drm-engine-render: -1 ns\ndrm-memory-system: 8 bananas"));
        Assert.IsNotNull(malformed);
        Assert.IsEmpty(malformed.Busy);
        Assert.IsNull(malformed.UsedMemory);
    }

    private static string Client(string driver, int id, string counters) =>
        $"drm-driver: {driver}\ndrm-pdev: 0000:00:02.0\ndrm-client-id: {id}\n{counters}\n";

    private sealed class Fixture : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Fullscreen-Intel-" + Guid.NewGuid().ToString("N"));
        internal string Proc => Path.Combine(_root, "proc");
        private string Drm => Path.Combine(_root, "drm");
        internal Fixture() {
            AddDevice("card0", "0000:00:02.0");
            Directory.CreateDirectory(Proc);
        }
        internal void AddDevice(string card, string slot) {
            string device = Path.Combine(Drm, card, "device");
            Directory.CreateDirectory(device);
            File.WriteAllText(Path.Combine(device, "vendor"), "0x8086\n");
            File.WriteAllText(Path.Combine(device, "device"), "0x1234\n");
            File.WriteAllText(Path.Combine(device, "uevent"), $"DRIVER=i915\nPCI_SLOT_NAME={slot}\n");
        }
        internal string PathFor(int pid, int fd) => Path.Combine(Proc, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "fdinfo", fd.ToString(System.Globalization.CultureInfo.InvariantCulture));
        internal void Write(int pid, int fd, string text) {
            string path = PathFor(pid, fd);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        internal void Link(int pid, int fd, string target) {
            string path = PathFor(pid, fd).Replace("/fdinfo/", "/fd/", StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, target);
        }
        internal CollectorFrame Capture(LinuxGpuSampler sampler, double seconds = 1) {
            var frame = new CollectorFrame();
            sampler.Sample(frame, Drm, seconds);
            return frame;
        }
        public void Dispose() {
            Directory.Delete(_root, true);
        }
    }
}
