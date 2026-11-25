using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class LinuxGpuSamplerTests {
    [TestMethod]
    public void PrivilegedTelemetryMatchesPciIdentityWhenFriendlyNamesDiffer() {
        using var fixture = new Fixture();
        fixture.PmuFiles();
        var privileged = new CollectorFrame { GpuCount = 1 };
        privileged.Gpus[0] = new("Intel HD Graphics 630", "DRM/i915 + PMU", 42, null, null, null) { Id = "0000:00:02.0" };
        using var sampler = new LinuxGpuSampler(fixture.Proc,
            new(fixture.Pmu, (_, _, _) => throw new UnauthorizedAccessException()), () => privileged, allowSudo: true);
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(42d, frame.Gpus[0].Utilization);
        Assert.AreEqual("Intel HD Graphics 630", frame.Gpus[0].Name);
        Assert.Contains("(sudo)", frame.Gpus[0].Provider);
        privileged.Gpus[0] = privileged.Gpus[0] with { Id = "0000:99:00.0", Name = "card0 Intel GPU 0x1234" };
        frame = fixture.Capture(sampler);
        Assert.IsNull(frame.Gpus[0].Utilization, "An equal display name cannot override a different PCI identity.");
    }
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
        fixture.AddDevice("card0", "0000:00:02.0", "xe");
        fixture.Write(10, 4, Client("xe", 1, "drm-cycles-vcs: 100\ndrm-total-cycles-vcs: 1000\ndrm-engine-capacity-vcs: 2\ndrm-resident-system: 12 KiB\ndrm-total-system: 99 MiB"));
        var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        fixture.Write(10, 4, Client("xe", 1, "drm-cycles-vcs: 600\ndrm-total-cycles-vcs: 2000\ndrm-engine-capacity-vcs: 2\ndrm-total-system: 99 MiB\ndrm-resident-system: 12 KiB"));
        var frame = fixture.Capture(sampler, 7);
        Assert.AreEqual(25d, frame.Gpus[0].Utilization, "GPU clock counters must not use the CPU sampling interval.");
        Assert.AreEqual(25d, frame.Gpus[0].Activity.Video);
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
        Assert.IsNotNull(LinuxGpuSampler.ParseClient("drm-driver: amdgpu\ndrm-pdev: 0000:00:02.0\ndrm-client-id: 1\n"));
        var malformed = LinuxGpuSampler.ParseClient(Client("i915", 1, "drm-engine-render: -1 ns\ndrm-memory-system: 8 bananas"));
        Assert.IsNotNull(malformed);
        Assert.IsEmpty(malformed.Busy);
        Assert.IsNull(malformed.UsedMemory);
    }

    [TestMethod]
    public void IntelPmuWorksWithoutClientsAlongsideVmwareAndDoesNotInventMemory() {
        using var fixture = new Fixture();
        fixture.AddDevice("card0", "0000:00:0f.0", "vmwgfx");
        fixture.AddDevice("card1", "0000:13:00.0");
        fixture.Write(777, 10, "pos: 0\nflags: 0100002\nmnt_id: 26\nino: 450\n");
        string pmuPath = Path.Combine(fixture.Pmu, "i915_0000_13_00.0");
        Directory.CreateDirectory(Path.Combine(pmuPath, "events"));
        File.WriteAllText(Path.Combine(pmuPath, "type"), "42");
        File.WriteAllText(Path.Combine(pmuPath, "cpumask"), "0");
        File.WriteAllText(Path.Combine(pmuPath, "events", "rcs0-busy"), "config=0x0");
        var counter = new LinuxIntelPmuSamplerTests.FakeCounter(new(0, 1000, 1000), new(750, 2000, 2000));
        using var sampler = new LinuxGpuSampler(fixture.Proc, new(fixture.Pmu, (_, _, _) => counter));
        var first = fixture.Capture(sampler);
        Assert.AreEqual(2, first.GpuCount);
        Assert.IsNull(first.Gpus.Take(first.GpuCount).Single(row => row.Name.StartsWith("card1 ", StringComparison.Ordinal)).Utilization);
        var frame = fixture.Capture(sampler);
        var intel = frame.Gpus.Take(frame.GpuCount).Single(row => row.Name.StartsWith("card1 ", StringComparison.Ordinal));
        var vmware = frame.Gpus.Take(frame.GpuCount).Single(row => row.Name.StartsWith("card0 ", StringComparison.Ordinal));
        Assert.AreEqual(75d, intel.Utilization);
        Assert.Contains("PMU", intel.Provider);
        Assert.IsNull(intel.UsedMemory);
        Assert.IsNull(intel.TotalMemory);
        Assert.IsNull(vmware.Utilization);
        Assert.Contains("no readable DRM accounting clients", frame.GpuStatus);
        Assert.IsTrue(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuUtilization));
        var remote = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), remote);
        Assert.AreEqual(intel, remote.Gpus.Take(remote.GpuCount).Single(row => row.Name == intel.Name));
    }

    [TestMethod]
    public void AmdDeviceSysfsWinsOverClientEstimateAndReadsOnlyItsOwnTemperature() {
        using var fixture = new Fixture();
        fixture.AddDevice("card0", "0000:00:02.0", "amdgpu");
        fixture.DeviceFile("gpu_busy_percent", "80");
        fixture.DeviceFile("mem_info_vram_used", "4096");
        fixture.DeviceFile("mem_info_vram_total", "8192");
        fixture.DeviceFile("hwmon/hwmon7/temp1_input", "42500");
        fixture.Write(10, 4, Client("amdgpu", 1, "drm-engine-gfx: 0 ns\ndrm-resident-vram: 99 MiB"));
        using var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        fixture.Write(10, 4, Client("amdgpu", 1, "drm-engine-gfx: 500000000 ns\ndrm-resident-vram: 99 MiB"));
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(80d, frame.Gpus[0].Utilization);
        Assert.AreEqual(50d, frame.Gpus[0].Activity.Render, "Client engine activity remains distinct from the sysfs device total.");
        Assert.Contains("clients", frame.Gpus[0].Provider);
        Assert.AreEqual(4096UL, frame.Gpus[0].UsedMemory);
        Assert.AreEqual(8192UL, frame.Gpus[0].TotalMemory);
        Assert.AreEqual(42.5d, frame.Gpus[0].Temperature);
        Assert.IsTrue(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuTemperature));
    }

    [TestMethod]
    public void AmdProcessCountersDeduplicateFdsAndSharedClientsStayUnattributed() {
        using var fixture = new Fixture();
        fixture.AddDevice("card0", "0000:00:02.0", "amdgpu");
        string first = Client("amdgpu", 1, "drm-engine-gfx: 0 ns\ndrm-resident-gtt: 64 KiB\ndrm-shared-gtt: 0");
        fixture.Write(10, 4, first);
        fixture.Write(10, 5, first);
        using var sampler = new LinuxGpuSampler(fixture.Proc);
        fixture.Capture(sampler);
        Assert.IsNull(sampler.ProcessUtilization(10));
        Assert.AreEqual(64UL * 1024, sampler.ProcessMemory(10));
        string second = first.Replace("gfx: 0 ns", "gfx: 250000000 ns", StringComparison.Ordinal);
        fixture.Write(10, 4, second);
        fixture.Write(10, 5, second);
        Assert.AreEqual(25d, fixture.Capture(sampler).Gpus[0].Utilization);
        Assert.AreEqual(25d, sampler.ProcessUtilization(10));
        var detail = sampler.ProcessDetails(10).Single();
        Assert.AreEqual("0000:00:02.0", detail.AdapterId);
        Assert.AreEqual(25d, detail.Activity.Render);
        Assert.AreEqual(64UL * 1024, detail.Memory);
        fixture.Write(11, 6, second);
        fixture.Capture(sampler);
        Assert.IsNull(sampler.ProcessUtilization(10));
        Assert.IsNull(sampler.ProcessUtilization(11));
        Assert.IsNull(sampler.ProcessMemory(10));
        Assert.IsNull(sampler.ProcessMemory(11));
        Assert.IsNull(sampler.ProcessDetails(10).Single().Utilization);
        Assert.Contains("Shared DRM client", sampler.ProcessDetails(10).Single().Status);
    }

    [TestMethod]
    public void AllocatedMemoryAndMalformedRegionsAreNotReportedAsResidentUsage() {
        var allocated = LinuxGpuSampler.ParseClient(Client("xe", 1, "drm-total-system: 99 MiB"));
        Assert.IsNotNull(allocated);
        Assert.IsNull(allocated.UsedMemory);
        var partial = LinuxGpuSampler.ParseClient(Client("xe", 1, "drm-resident-system: 12 KiB\ndrm-resident-vram0: broken"));
        Assert.IsNotNull(partial);
        Assert.IsNull(partial.UsedMemory);
        var shared = LinuxGpuSampler.ParseClient(Client("i915", 1, "drm-resident-system0: 12 KiB\ndrm-shared-system0: 4 KiB"));
        Assert.IsNotNull(shared);
        Assert.AreEqual(12UL * 1024, shared.UsedMemory);
        Assert.IsNull(shared.PrivateMemory);
        var malformedSharing = LinuxGpuSampler.ParseClient(Client("i915", 1, "drm-resident-system0: 12 KiB\ndrm-shared-system0: 0\ndrm-shared-system0: broken"));
        Assert.IsNotNull(malformedSharing);
        Assert.IsNull(malformedSharing.PrivateMemory);
    }

    [TestMethod]
    public void MissingResidentMemoryOnOneClientKeepsDeviceTotalUnknown() {
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("i915", 1, "drm-resident-system0: 12 KiB"));
        fixture.Write(11, 4, Client("i915", 2, "drm-engine-render: 0 ns"));
        using var sampler = new LinuxGpuSampler(fixture.Proc);
        Assert.IsNull(fixture.Capture(sampler).Gpus[0].UsedMemory);
    }

    [TestMethod]
    public void LinuxMissingPciFieldResolvesActualRenderNodeRatherThanAssumingCardNumber() {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("Requires Linux readlink."); }
        using var fixture = new Fixture();
        fixture.AddDevice("renderD129", "0000:00:02.0");
        fixture.AddDevice("renderD128", "0000:00:0f.0", "vmwgfx");
        fixture.Write(10, 4, Client("i915", 1, "drm-resident-system0: 12 KiB").Replace("drm-pdev: 0000:00:02.0\n", "", StringComparison.Ordinal));
        fixture.Link(10, 4, "/dev/dri/renderD129");
        fixture.Write(11, 4, "pos: 0\nflags: 0100002\n");
        fixture.Link(11, 4, "/dev/dri/renderD128");
        using var sampler = new LinuxGpuSampler(fixture.Proc);
        Assert.AreEqual(12UL * 1024, fixture.Capture(sampler).Gpus[0].UsedMemory);
    }

    [TestMethod]
    public void ProcessOnlySamplingDoesNotOpenPmuAndDisablesPreviouslyOpenedCounters() {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Pmu, "i915");
        Directory.CreateDirectory(Path.Combine(path, "events"));
        File.WriteAllText(Path.Combine(path, "type"), "42");
        File.WriteAllText(Path.Combine(path, "cpumask"), "0");
        File.WriteAllText(Path.Combine(path, "events", "rcs0-busy"), "config=0x0");
        int opens = 0;
        var counter = new LinuxIntelPmuSamplerTests.FakeCounter(new LinuxIntelPmuSampler.Reading(0, 1, 1));
        using var sampler = new LinuxGpuSampler(fixture.Proc, new(fixture.Pmu, (_, _, _) => { opens++; return counter; }));
        fixture.Capture(sampler, devices: false);
        Assert.AreEqual(0, opens);
        fixture.Capture(sampler);
        Assert.AreEqual(1, opens);
        Assert.IsFalse(counter.Disposed);
        fixture.Capture(sampler, devices: false);
        Assert.IsTrue(counter.Disposed);
    }

    [TestMethod]
    public void LinuxProcfsDenialRemainsVisibleBetweenDiscoveryScansAndClearsAfterRecovery() {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root") {
            Assert.Inconclusive("Requires a non-root Linux user to enforce directory permissions.");
            return;
        }
        using var fixture = new Fixture();
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 0 ns"));
        fixture.Write(11, 4, Client("i915", 2, "drm-engine-render: 0 ns"));
        string inaccessible = Path.GetDirectoryName(fixture.PathFor(11, 4))!;
        var permissions = File.GetUnixFileMode(inaccessible);
        using var sampler = new LinuxGpuSampler(fixture.Proc);
        try {
            File.SetUnixFileMode(inaccessible, UnixFileMode.None);
            Assert.Contains("procfs access denied", fixture.Capture(sampler, processes: false).GpuStatus);
            Assert.Contains("procfs access denied", fixture.Capture(sampler, processes: false).GpuStatus);
            File.SetUnixFileMode(inaccessible, permissions);
            Assert.Contains("procfs access denied", fixture.Capture(sampler, processes: false).GpuStatus);
            Assert.DoesNotContain("procfs access denied", fixture.Capture(sampler, seconds: 5, processes: false).GpuStatus);
        }
        finally { File.SetUnixFileMode(inaccessible, permissions); }
    }

    [TestMethod]
    public void SudoFallbackEnrichesDevicesAfterDenialWithoutElevatingProcessMetrics() {
        using var fixture = new Fixture();
        fixture.PmuFiles();
        fixture.Write(10, 4, Client("i915", 1, "drm-engine-render: 0 ns"));
        int requests = 0;
        using var sampler = new LinuxGpuSampler(fixture.Proc,
            new(fixture.Pmu, (_, _, _) => throw new UnauthorizedAccessException()), () => {
                requests++;
                var privileged = new CollectorFrame {
                    GpuCount = 1,
                    ProcessCount = 1,
                    Capabilities = CollectorCapabilities.GpuUtilization,
                    GpuStatus = "card0: PMU busiest engine"
                };
                privileged.Gpus[0] = new("card0 Intel GPU 0x1234", "DRM/i915 + PMU", 42, null, null, null);
                privileged.Processes[0] = new(10, "root-reader", 0, 0, 1, GpuPercent: 99, GpuMemory: 4096);
                return privileged;
            }, allowSudo: true);
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(42d, frame.Gpus[0].Utilization);
        Assert.Contains("(sudo)", frame.Gpus[0].Provider);
        Assert.Contains("GPU reader: sudo -n", frame.GpuStatus);
        Assert.IsTrue(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuUtilization));
        Assert.IsNull(sampler.ProcessUtilization(10));
        Assert.IsNull(sampler.ProcessMemory(10));
        fixture.Capture(sampler, devices: false);
        Assert.AreEqual(1, requests, "Process-only collection must not request elevation.");
    }

    [TestMethod]
    public void SudoIsDisabledByDefaultEvenWhenDeviceAccessIsDenied() {
        using var fixture = new Fixture();
        fixture.PmuFiles();
        int requests = 0;
        using var sampler = new LinuxGpuSampler(fixture.Proc,
            new(fixture.Pmu, (_, _, _) => throw new UnauthorizedAccessException()), () => { requests++; return new CollectorFrame(); });
        var frame = fixture.Capture(sampler);
        Assert.AreEqual(0, requests);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.Contains("PMU access denied", frame.GpuStatus);
        Assert.DoesNotContain("GPU reader: sudo -n", frame.GpuStatus);
    }

    [TestMethod]
    public void SudoFailureKeepsOriginalPermissionReasonAndMissingProvidersDoNotTriggerElevation() {
        using var fixture = new Fixture();
        int requests = 0;
        using (var missing = new LinuxGpuSampler(fixture.Proc, new(fixture.Pmu), () => { requests++; return null; }, allowSudo: true)) {
            fixture.Capture(missing);
            Assert.AreEqual(0, requests);
        }
        fixture.PmuFiles();
        using var denied = new LinuxGpuSampler(fixture.Proc,
            new(fixture.Pmu, (_, _, _) => throw new UnauthorizedAccessException()), () => { requests++; return null; }, allowSudo: true);
        var frame = fixture.Capture(denied);
        Assert.AreEqual(1, requests);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.Contains("PMU access denied", frame.GpuStatus);
        Assert.Contains("sudo collector unavailable", frame.GpuStatus);
    }

    [TestMethod]
    public void UnavailableSudoIsNonInteractiveAndIsNotRestartedOnEveryRefresh() {
        int attempts = 0;
        using var helper = new SudoCollector(info => {
            attempts++;
            Assert.StartsWith("-n -- ", info.Arguments);
            Assert.Contains("-NoProfile", info.Arguments);
            Assert.Contains("-NonInteractive", info.Arguments);
            Assert.IsFalse(info.UseShellExecute);
            Assert.IsTrue(info.RedirectStandardInput);
            return null;
        });
        Assert.IsNull(helper.Sample(CollectorMetrics.Gpu));
        Assert.IsNull(helper.Sample(CollectorMetrics.Gpu));
        Assert.AreEqual(1, attempts);
        Assert.Contains("unavailable", helper.Status);
    }

    private static string Client(string driver, int id, string counters) =>
        $"drm-driver: {driver}\ndrm-pdev: 0000:00:02.0\ndrm-client-id: {id}\n{counters}\n";

    private sealed class Fixture : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Fullscreen-Intel-" + Guid.NewGuid().ToString("N"));
        internal string Proc => Path.Combine(_root, "proc");
        internal string Pmu => Path.Combine(_root, "pmu");
        private string Drm => Path.Combine(_root, "drm");
        internal Fixture() {
            AddDevice("card0", "0000:00:02.0");
            Directory.CreateDirectory(Proc);
        }
        internal void AddDevice(string card, string slot, string driver = "i915") {
            string device = Path.Combine(Drm, card, "device");
            Directory.CreateDirectory(device);
            File.WriteAllText(Path.Combine(device, "vendor"), driver == "amdgpu" ? "0x1002\n" : "0x8086\n");
            File.WriteAllText(Path.Combine(device, "device"), "0x1234\n");
            File.WriteAllText(Path.Combine(device, "uevent"), $"DRIVER={driver}\nPCI_SLOT_NAME={slot}\n");
        }
        internal void DeviceFile(string name, string value) {
            string path = Path.Combine(Drm, "card0", "device", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        internal void PmuFiles() {
            string path = Path.Combine(Pmu, "i915");
            Directory.CreateDirectory(Path.Combine(path, "events"));
            File.WriteAllText(Path.Combine(path, "type"), "42");
            File.WriteAllText(Path.Combine(path, "cpumask"), "0");
            File.WriteAllText(Path.Combine(path, "events", "rcs0-busy"), "config=0x0");
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
        internal CollectorFrame Capture(LinuxGpuSampler sampler, double seconds = 1, bool devices = true, bool processes = true) {
            var frame = new CollectorFrame();
            sampler.Sample(frame, Drm, seconds, includeDevices: devices, includeProcesses: processes);
            return frame;
        }
        public void Dispose() {
            Directory.Delete(_root, true);
        }
    }
}
