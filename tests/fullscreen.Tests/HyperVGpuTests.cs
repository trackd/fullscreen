using System.Runtime.InteropServices;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class HyperVGpuTests {
    [TestMethod]
    public void WindowsSyntheticDisplayReportsUnknownCapacityAndActivity() {
        using var sampler = new WindowsGpuSampler(native: false);
        var adapters = (Dictionary<string, WindowsGpuAdapters.Adapter>)typeof(WindowsGpuSampler)
            .GetField("_adapters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sampler)!;
        adapters.Add("display:fixture", new("display:fixture", "Microsoft Hyper-V Video", 0, 0, Vendor: 0x1414, DisplayOnly: true));
        var frame = new CollectorFrame();
        sampler.AppendDevices(frame);
        Assert.AreEqual(1, frame.GpuCount);
        Assert.AreEqual("Microsoft Hyper-V Video", frame.Gpus[0].Name);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.IsNull(frame.Gpus[0].TotalMemory);
        Assert.IsNull(frame.Gpus[0].UsedMemory);
        Assert.Contains("Hyper-V synthetic display", frame.Gpus[0].Status!);
        Assert.IsFalse(sampler.Capabilities.HasFlag(CollectorCapabilities.GpuTotalMemory));
    }

    [TestMethod]
    public void HyperVNamesDoNotMisclassifyOtherMicrosoftOrAssignedGpus() {
        var names = new LinuxGpuNames();
        Assert.AreEqual("Microsoft Hyper-V Video", names.Resolve(null, null, "hyperv_drm"));
        Assert.AreEqual("Microsoft Hyper-V Video", names.Resolve("0x1414", "0x5353", "unknown"));
        Assert.AreEqual("Microsoft graphics", LinuxGpuNames.Friendly("1414", null, "unknown"));
        Assert.IsTrue(WindowsGpuAdapters.IsHyperVDisplay("Microsoft Hyper-V Video"));
        Assert.IsTrue(WindowsGpuAdapters.IsHyperVDisplay("Basic display", "PCI\\VEN_1414&DEV_5353"));
        Assert.IsFalse(WindowsGpuAdapters.IsHyperVDisplay("Microsoft Basic Render Driver", "PCI\\VEN_1414&DEV_008C"));
        Assert.IsFalse(WindowsGpuAdapters.IsHyperVDisplay("NVIDIA A10", "PCI\\VEN_10DE&DEV_2236"));
        Assert.AreEqual(840, Marshal.SizeOf<WindowsGpuAdapters.DisplayDevice>());
    }

    [TestMethod]
    public void VmbusDrmDisplayRetainsIdentityWithoutInventingGpuCounters() {
        using var fixture = new Fixture();
        fixture.Write("drm/card0/device/uevent", "DRIVER=hyperv_drm\n");
        fixture.Write("drm/card0/device/device_id", "{11111111-2222-3333-4444-555555555555}");
        var frame = new CollectorFrame();
        using var sampler = new LinuxGpuSampler(fixture.PathFor("proc"));
        sampler.Sample(frame, fixture.PathFor("drm"), 1, includeProcesses: false);
        Assert.AreEqual(1, frame.GpuCount);
        Assert.AreEqual("vmbus:11111111-2222-3333-4444-555555555555", frame.Gpus[0].Id);
        Assert.Contains("Hyper-V synthetic display", frame.Gpus[0].Status!);
        Assert.Contains("guest acceleration counters are not exposed", frame.GpuStatus);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.IsNull(frame.Gpus[0].TotalMemory);
    }

    [TestMethod]
    public void FramebufferDiscoveryPreservesAssignedGpuAndDoesNotDuplicateDrm() {
        using var fixture = new Fixture();
        fixture.Write("graphics/fb0/name", "hyperv_fb");
        fixture.Write("graphics/fb0/device/device_id", "{synthetic-instance}");
        var frame = new CollectorFrame { GpuCount = 1, GpuStatus = "GPU telemetry available" };
        frame.Gpus[0] = new("NVIDIA A10", "NVML", 42, 123, 456, 30) { Id = "assigned-gpu" };
        var assigned = frame.Gpus[0];
        LinuxHyperVDisplay.Append(frame, fixture.PathFor("graphics"));
        Assert.AreEqual(2, frame.GpuCount);
        Assert.AreEqual(assigned, frame.Gpus[0]);
        Assert.AreEqual("Microsoft Hyper-V Video", frame.Gpus[1].Name);
        Assert.AreEqual("vmbus:synthetic-instance", frame.Gpus[1].Id);
        Assert.IsNull(frame.Gpus[1].Utilization);
        Assert.IsNull(frame.Gpus[1].TotalMemory);
        LinuxHyperVDisplay.Append(frame, fixture.PathFor("graphics"));
        Assert.AreEqual(2, frame.GpuCount);
        frame.GpuCount = 1;
        frame.Gpus[0] = new("card0 hyperv_drm GPU", "DRM/hyperv_drm", null, null, null, null);
        LinuxHyperVDisplay.Append(frame, fixture.PathFor("graphics"));
        Assert.AreEqual(1, frame.GpuCount);
    }

    private sealed class Fixture : IDisposable {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fullscreen-hyperv-" + Guid.NewGuid().ToString("N"));
        internal string PathFor(string relative) => System.IO.Path.Combine(_root, relative);
        internal void Write(string relative, string content) {
            string path = PathFor(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
