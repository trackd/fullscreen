using System.ComponentModel;
using System.Reflection;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class MonitorCapabilityTests {
    private static CollectorCapabilities Detect(string platform, bool is64Bit = true, string version = "10.0.19045") =>
        CollectorCapabilityDetection.Detect(platform == "Windows", platform == "Linux", platform == "macOS", is64Bit, Version.Parse(version));
    private static ProcessColumn[] Available(MonitorSurface surface) =>
        [.. ((IReadOnlyList<ProcessColumn>)typeof(MonitorSurface).GetProperty("AvailableProcessColumns", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(surface)!)];

    [TestMethod]
    [DataRow("Linux")]
    [DataRow("macOS")]
    public void UnixProcessColumnsExcludeUnimplementedGpuAndNetworkButKeepDiskAndIdentity(string platform) {
        var supported = Detect(platform);
        Assert.AreEqual(CollectorCapabilities.None, supported & (CollectorCapabilities.ProcessNetwork | CollectorCapabilities.ProcessGraphics));
        Assert.AreEqual(CollectorCapabilities.ProcessDisk, supported & CollectorCapabilities.ProcessDisk);
        var surface = new MonitorSurface { Minimal = true, Frame = new CollectorFrame { Platform = platform, Capabilities = supported } };
        surface.ColumnSettings.Configure(Enum.GetValues<ProcessColumn>(), null, false);
        var columns = Available(surface);
        foreach (var column in new[] { ProcessColumn.Network, ProcessColumn.Gpu, ProcessColumn.GpuMemory }) {
            Assert.DoesNotContain(column, columns);
        }
        foreach (var column in new[] { ProcessColumn.Disk, ProcessColumn.ParentId, ProcessColumn.UserName, ProcessColumn.Status }) {
            Assert.Contains(column, columns);
        }
        Assert.AreEqual(CollectorMetrics.None, surface.RequiredMetrics & (CollectorMetrics.ProcessNetwork | CollectorMetrics.ProcessGpu));
        Assert.IsTrue(surface.ColumnSettings.Contains(ProcessColumn.Network), "Unsupported selections must not be erased from configuration.");
        surface.OpenColumns();
        string picker = string.Join("\n", VisualSnapshotRenderer.Render(surface, 80, 40).ToMarkupLines());
        Assert.DoesNotContain("GpuMemory", picker);
        Assert.DoesNotContain("Network", picker);
        Assert.Contains("Disk", picker);
    }

    [TestMethod]
    [DataRow(true, "6.1.7601", false, false, true)]
    [DataRow(true, "6.2.9200", true, false, true)]
    [DataRow(true, "10.0.14393", true, false, true)]
    [DataRow(true, "10.0.16299", true, true, true)]
    [DataRow(false, "10.0.19045", false, true, false)]
    public void WindowsCapabilitiesFollowVersionArchitectureAndActualCollectorImplementations(
        bool is64Bit, string version, bool io, bool gpu, bool nativeIdentity) {
        var supported = Detect("Windows", is64Bit, version);
        Assert.AreEqual(io, (supported & CollectorCapabilities.ProcessDisk) != 0);
        Assert.AreEqual(io, (supported & CollectorCapabilities.ProcessNetwork) != 0);
        Assert.AreEqual(gpu, (supported & CollectorCapabilities.ProcessGpu) != 0);
        Assert.AreEqual(gpu, (supported & CollectorCapabilities.ProcessGpuMemory) != 0);
        Assert.AreEqual(nativeIdentity, (supported & CollectorCapabilities.ProcessParent) != 0);
        Assert.AreEqual(nativeIdentity, (supported & CollectorCapabilities.ProcessStatus) != 0);
        Assert.AreEqual(CollectorCapabilities.ProcessOwner, supported & CollectorCapabilities.ProcessOwner);
    }

    [TestMethod]
    public void SupportedColumnsSurviveEmptySamplesAndCapabilityChangesInvalidateLayout() {
        var frame = new CollectorFrame { Capabilities = Detect("Windows") };
        var surface = new MonitorSurface { Minimal = true, Frame = frame };
        surface.ColumnSettings.Configure([ProcessColumn.Name, ProcessColumn.Network, ProcessColumn.Gpu, ProcessColumn.GpuMemory], null, false);
        surface.SetFrame(frame);
        VisualSnapshotRenderer.Render(surface, 120, 30);
        CollectionAssert.AreEqual(surface.ColumnSettings.Selected.ToArray(), Available(surface));
        frame.ProcessCount = 1;
        frame.Processes[0] = new(7, "fixture", 0, 1024, 1, NetworkBytesPerSecond: 100, GpuPercent: 10, GpuMemory: 500);
        surface.SetFrame(frame);
        CollectionAssert.AreEqual(surface.ColumnSettings.Selected.ToArray(), Available(surface));
        frame.Processes[0] = frame.Processes[0] with { NetworkBytesPerSecond = null, GpuPercent = null, GpuMemory = null };
        surface.SetFrame(frame);
        CollectionAssert.AreEqual(surface.ColumnSettings.Selected.ToArray(), Available(surface));
        surface.OpenColumns();
        frame.Capabilities &= ~CollectorCapabilities.ProcessGpuMemory;
        surface.SetFrame(frame);
        Assert.DoesNotContain(ProcessColumn.GpuMemory, Available(surface));
        Assert.Contains(ProcessColumn.Gpu, Available(surface));
        string picker = string.Join("\n", VisualSnapshotRenderer.Render(surface, 120, 40).ToMarkupLines());
        Assert.DoesNotContain("GpuMemory", picker, "An open chooser must update when the target reports a missing counter provider.");
        frame.Capabilities = Detect("Linux");
        surface.SetFrame(frame);
        CollectionAssert.AreEqual(new[] { ProcessColumn.Name }, Available(surface));
        Assert.IsTrue(surface.ColumnSettings.Contains(ProcessColumn.Network));
    }

    [TestMethod]
    public void ProviderMissingIsDifferentFromWarmupAccessDeniedOrSamplingFailure() {
        Assert.IsTrue(WindowsGpuSampler.MissingCounter(unchecked((int)0xC0000BB8))); // no object
        Assert.IsTrue(WindowsGpuSampler.MissingCounter(unchecked((int)0xC0000BB9))); // no counter
        Assert.IsFalse(WindowsGpuSampler.MissingCounter(unchecked((int)0x800007D1))); // no instance
        Assert.IsFalse(WindowsGpuSampler.MissingCounter(5));
        Assert.IsTrue(WindowsIoSampler.UnsupportedProvider(new Win32Exception(50)));
        Assert.IsFalse(WindowsIoSampler.UnsupportedProvider(new Win32Exception(5)));
        Assert.IsFalse(WindowsIoSampler.UnsupportedProvider(new Win32Exception(1450)));
    }

    [TestMethod]
    public void DeviceGpuColumnsFollowProviderCapabilitiesIndependentlyOfNullValues() {
        var frame = new CollectorFrame { Capabilities = CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory };
        var surface = new MonitorSurface { View = MonitorView.Gpu, Frame = frame };
        Assert.IsTrue(surface.IsColumnVisible(GpuColumn.Utilization));
        Assert.IsTrue(surface.IsColumnVisible(GpuColumn.UsedMemory));
        Assert.IsFalse(surface.IsColumnVisible(GpuColumn.TotalMemory));
        Assert.IsFalse(surface.IsColumnVisible(GpuColumn.Temperature));
        surface.OpenColumns();
        string picker = string.Join("\n", VisualSnapshotRenderer.Render(surface, 120, 40).ToMarkupLines());
        Assert.Contains("Utilization", picker);
        Assert.DoesNotContain("Temperature", picker);
        Assert.DoesNotContain("TotalMemory", picker);
        frame.Capabilities = CollectorCapabilities.None;
        Assert.IsTrue(surface.IsColumnVisible(GpuColumn.Device));
        Assert.IsTrue(surface.IsColumnVisible(GpuColumn.Provider));
        Assert.IsFalse(surface.IsColumnVisible(GpuColumn.Utilization));
    }

    [TestMethod]
    public void MissingDrmProviderAddsNoCapabilitiesOrDevicesToExistingNvmlSample() {
        var frame = new CollectorFrame { GpuCount = 1 };
        frame.Gpus[0] = new("NVML fixture", "NVML", 50, 1024, 2048, 40);
        var original = frame.Gpus[0];
        string missing = Path.Combine(Path.GetTempPath(), "Fullscreen-DRM-" + Guid.NewGuid().ToString("N"));
        Assert.AreEqual(CollectorCapabilities.None, GpuSampler.SampleDrm(frame, missing));
        Assert.AreEqual(1, frame.GpuCount);
        Assert.AreEqual(original, frame.Gpus[0]);
    }

    [TestMethod]
    public void DrmCapabilitiesFollowOnlyExposedCounters() {
        string root = Path.Combine(Path.GetTempPath(), "Fullscreen-DRM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var frame = new CollectorFrame();
            Assert.AreEqual(CollectorCapabilities.None, GpuSampler.SampleDrm(frame, root));
            string device = Path.Combine(root, "card0", "device");
            Directory.CreateDirectory(device);
            File.WriteAllText(Path.Combine(device, "gpu_busy_percent"), "42");
            Assert.AreEqual(CollectorCapabilities.GpuUtilization, GpuSampler.SampleDrm(frame, root));
            Assert.AreEqual(42d, frame.Gpus[0].Utilization);
            File.WriteAllText(Path.Combine(device, "mem_info_vram_used"), "1024");
            File.WriteAllText(Path.Combine(device, "mem_info_vram_total"), "2048");
            frame.GpuCount = 0;
            Assert.AreEqual(CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuTotalMemory,
                GpuSampler.SampleDrm(frame, root));
            Assert.AreEqual(1, frame.GpuCount);
        }
        finally {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow(ProcessSort.Gpu, CollectorCapabilities.ProcessGpu, CollectorMetrics.ProcessGpu)]
    [DataRow(ProcessSort.GpuMemory, CollectorCapabilities.ProcessGpuMemory, CollectorMetrics.ProcessGpu)]
    [DataRow(ProcessSort.Disk, CollectorCapabilities.ProcessDisk, CollectorMetrics.ProcessDisk)]
    [DataRow(ProcessSort.Network, CollectorCapabilities.ProcessNetwork, CollectorMetrics.ProcessNetwork)]
    public void UserSortRequestsOnlySupportedCountersWhileProcessesAreEnabled(
        ProcessSort sort, CollectorCapabilities capability, CollectorMetrics metric) {
        var frame = new CollectorFrame { Capabilities = CollectorCapabilities.ProcessOwner | capability };
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame, IncludeUserProcesses = true };
        surface.Model.Sort = sort;
        const CollectorMetrics summaries = CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu;
        const CollectorMetrics baseline = summaries | CollectorMetrics.Users | CollectorMetrics.Processes | CollectorMetrics.Owners;
        Assert.AreEqual(baseline | metric, surface.RequiredMetrics);
        frame.Capabilities &= ~capability;
        Assert.AreEqual(baseline, surface.RequiredMetrics);
        frame.Capabilities = CollectorCapabilities.ProcessOwner | capability;
        surface.IncludeUserProcesses = false;
        Assert.AreEqual(summaries | CollectorMetrics.Users, surface.RequiredMetrics);
        surface.IncludeUserProcesses = true;
        Assert.AreEqual(baseline | metric | CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu,
            surface.RequiredMetrics);
    }

    [TestMethod]
    public void CapabilitiesCrossTheWireAndCommandLineReaderBelongsToTheStandaloneCollector() {
        Assert.AreSame(typeof(PerformanceCollector).Assembly, typeof(ProcessOwnerReader).Assembly);
        Assert.AreEqual("fullscreen.Collector", typeof(ProcessOwnerReader).Assembly.GetName().Name);
        Assert.IsFalse(typeof(ProcessOwnerReader).Assembly.GetReferencedAssemblies().Any(assembly =>
            assembly.Name is "fullscreen" or "System.Management.Automation" || assembly.Name!.StartsWith("XenoAtom", StringComparison.Ordinal)));
        var frame = new CollectorFrame { Capabilities = Detect("Linux"), Platform = "Linux" };
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        Assert.AreEqual(frame.Capabilities, decoded.Capabilities);
        var copy = new CollectorFrame();
        copy.CopyFrom(decoded);
        Assert.AreEqual(frame.Capabilities, copy.Capabilities);
        var surface = new MonitorSurface { Frame = copy };
        Assert.DoesNotContain(ProcessColumn.Network, Available(surface), "Target capabilities must override the Windows UI host.");
        frame.Capabilities = null;
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        Assert.IsNull(decoded.Capabilities, "Unknown capability state must not mean unsupported.");
    }
}
