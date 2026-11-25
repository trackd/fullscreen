using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class GpuNameTests {
    [TestMethod]
    public void DashboardNamesTheDeviceWhoseUtilizationIsDisplayed() {
        var frame = new CollectorFrame { GpuCount = 2 };
        frame.Gpus[0] = new("Intel graphics", "fixture", 10, null, null, null);
        frame.Gpus[1] = new("NVIDIA RTX", "fixture", 80, null, null, null);
        var surface = new MonitorSurface { View = MonitorView.Dashboard, Frame = frame };
        Assert.AreEqual("NVIDIA RTX", surface.GpuDeviceName);
        string rendered = string.Join('\n', VisualSnapshotRenderer.Render(surface, 120, 40).ToMarkupLines());
        Assert.Contains("GPU · NVIDIA RTX %", rendered);
        Assert.DoesNotContain("busiest device", rendered);
        frame.Gpus[0] = frame.Gpus[0] with { Utilization = 90 };
        Assert.AreEqual("Intel graphics", surface.GpuDeviceName);
        frame.Gpus[0] = frame.Gpus[0] with { Utilization = null };
        Assert.AreEqual("NVIDIA RTX", surface.GpuDeviceName);
    }

    [TestMethod]
    public void MissingUsageAndLegacyNamesHaveReadableNonMisleadingLabels() {
        var frame = new CollectorFrame { GpuCount = 1 };
        frame.Gpus[0] = new("card0 vmwgfx GPU 0x0405", "DRM/vmwgfx", null, null, null, null);
        var surface = new MonitorSurface { Frame = frame };
        Assert.AreEqual("VMware graphics", surface.GpuDeviceName);
        frame.GpuCount = 2;
        frame.Gpus[1] = new("card1 Intel GPU 0x5912", "DRM/i915", null, null, null, null);
        Assert.AreEqual("activity unavailable", surface.GpuDeviceName, "No busiest device can be chosen without measurements.");
        frame.Gpus[0] = frame.Gpus[0] with { Utilization = 0 };
        frame.Gpus[1] = frame.Gpus[1] with { Utilization = 0 };
        Assert.AreEqual("VMware graphics", surface.GpuDeviceName, "Measured idle ties retain the first adapter.");
        frame.GpuCount = 0;
        Assert.AreEqual("no provider", surface.GpuDeviceName);
    }

    [TestMethod]
    public void InstalledPciNamesAreScopedToTheCorrectVendorAndIgnoreSubsystemEntries() {
        string[] lines = ["15ad  VMware", "\t0405  SVGA II Adapter", "\t\t8086 5912  unrelated subsystem", "8086  Intel Corporation", "\t5912  HD Graphics 630", "\t1234  Another device"];
        Assert.AreEqual("HD Graphics 630", LinuxGpuNames.FindModel(lines, "8086", "5912"));
        Assert.AreEqual("SVGA II Adapter", LinuxGpuNames.FindModel(lines, "15ad", "0405"));
        Assert.IsNull(LinuxGpuNames.FindModel(lines, "15ad", "5912"));
        Assert.IsNull(LinuxGpuNames.FindModel(lines, "8086", "ffff"));
        Assert.AreEqual("Intel HD Graphics 630", LinuxGpuNames.Friendly("8086", "HD Graphics 630", "i915"));
        Assert.AreEqual("Intel Arc A770", LinuxGpuNames.Friendly("8086", "DG2 [Arc A770]", "xe"));
        Assert.AreEqual("VMware graphics", LinuxGpuNames.Friendly("15ad", null, "vmwgfx"));
        Assert.AreEqual("Intel graphics", LinuxGpuNames.Friendly("8086", null, "i915"));
        Assert.AreEqual("AMD Radeon RX 7900", LinuxGpuNames.Friendly("1002", "Navi 31 [Radeon RX 7900]", "amdgpu"));
    }
}
