using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class CrossPlatformGpuTests {
    [TestMethod]
    public void WindowsAdaptersKeepSeparateEnginesAndDedicatedSharedMemory() {
        using var sampler = new WindowsGpuSampler(native: false);
        const string first = "luid_0x00000000_0x00000001_phys_0";
        const string second = "luid_0x00000000_0x00000002_phys_0";
        sampler.AddCounter("pid_10_" + first + "_eng_0_engtype_3D", 30, 0);
        sampler.AddCounter("pid_20_" + first + "_eng_0_engtype_3D", 40, 0);
        sampler.AddCounter("pid_20_" + first + "_eng_1_engtype_VideoDecode", 90, 0);
        sampler.AddCounter("pid_30_" + second + "_eng_0_engtype_Compute_0", 20, 0);
        sampler.AddCounter(first, 1000, 2);
        sampler.AddCounter(first, 2000, 3);
        sampler.AddCounter(second, 50, 3);
        sampler.AddCounter("invalid", 100, 0);
        sampler.AddCounter(first, double.NaN, 2);
        var frame = new CollectorFrame();
        sampler.AppendDevices(frame);
        Assert.AreEqual(2, frame.GpuCount);
        var one = frame.Gpus.Single(row => row.Id == first);
        var two = frame.Gpus.Single(row => row.Id == second);
        Assert.AreEqual(90d, one.Utilization);
        Assert.AreEqual(70d, one.Activity.Render);
        Assert.AreEqual(90d, one.Activity.Decode);
        Assert.IsNull(one.Activity.Video);
        Assert.AreEqual(1000UL, one.DedicatedMemory);
        Assert.AreEqual(2000UL, one.SharedMemory);
        Assert.AreEqual(3000UL, one.UsedMemory);
        Assert.AreEqual(20d, two.Activity.Compute);
        Assert.IsNull(two.DedicatedMemory);
        Assert.IsNull(two.UsedMemory, "A missing dedicated counter cannot become a complete total.");
    }

    [TestMethod]
    public void VendorAndWindowsRowsMergeOnlyOnExactAdapterIdentity() {
        var frame = new CollectorFrame { GpuCount = 1 };
        frame.Gpus[0] = new("same name", "NVIDIA NVML", 66, 100, 1000, 45) {
            Id = "adapter-one",
            Activity = new(Encode: 22, Memory: 33),
            PowerWatts = 40
        };
        var adapters = new CollectorFrame { GpuCount = 2 };
        adapters.Gpus[0] = new("same name", "Windows PDH", 55, 120, null, null) {
            Id = "adapter-one",
            DedicatedMemory = 100,
            SharedMemory = 20,
            Activity = new(Compute: 55, Decode: 12)
        };
        adapters.Gpus[1] = adapters.Gpus[0] with { Id = "adapter-two" };
        GpuSampler.MergeWindowsDevices(frame, adapters);
        Assert.AreEqual(2, frame.GpuCount);
        Assert.AreEqual(new GpuActivity(Compute: 55, Encode: 22, Decode: 12, Memory: 33), frame.Gpus[0].Activity);
        Assert.AreEqual(40d, frame.Gpus[0].PowerWatts);
        Assert.AreEqual(20UL, frame.Gpus[0].SharedMemory);
        Assert.AreEqual("adapter-two", frame.Gpus[1].Id);
    }

    [TestMethod]
    public void NvidiaProcessListsDeduplicateAndPreserveUnavailableMemory() {
        NvmlSampler.ProcessInfo Info(uint pid, ulong bytes) => new() { Pid = pid, Memory = bytes };
        var processes = NvmlSampler.MergeProcesses([Info(10, 1024), Info(11, ulong.MaxValue)],
            [Info(10, 1024), Info(11, 500), Info(12, 0), Info(uint.MaxValue, 1)]);
        Assert.HasCount(3, processes);
        Assert.AreEqual(1024UL, processes[10]);
        Assert.IsNull(processes[11]);
        Assert.AreEqual(0UL, processes[12]);
    }

    [TestMethod]
    public void NvidiaDeviceSnapshotsSumMemoryOncePerGpuAndUseBusiestReportedEngine() {
        NvmlSampler.ProcessInfo Info(ulong bytes) => new() { Pid = 10, Memory = bytes };
        var snapshots = new NvmlSampler.ProcessSnapshot[] {
            new([Info(1024)], [Info(1024)], new() { [10] = 20 }),
            new([Info(2048)], [], new() { [10] = 70 })
        };
        var result = NvmlSampler.CombineSnapshots(snapshots);
        Assert.AreEqual((70d, 3072UL), result[10]);
        var denied = NvmlSampler.CombineSnapshots([snapshots[0], new(null, [], null)]);
        Assert.AreEqual(20d, denied[10].Usage);
        Assert.IsNull(denied[10].Memory, "A failed list cannot produce a complete memory total.");
        Assert.IsNull(NvmlSampler.CombineSnapshots([new([Info(1024)], [], null)])[10].Usage);
        Assert.HasCount(0, NvmlSampler.CombineSnapshots([new([], [], new())]));
    }

    [TestMethod]
    public void NvidiaNativeProcessLayoutMatchesTheVersionedAbi() {
        Assert.AreEqual(24, System.Runtime.InteropServices.Marshal.SizeOf<NvmlSampler.ProcessInfo>());
        Assert.AreEqual(new IntPtr(8), System.Runtime.InteropServices.Marshal.OffsetOf<NvmlSampler.ProcessInfo>("Memory"));
    }

    [TestMethod]
    public void AppleDeviceReportsUnifiedAllocationsWithoutInventingVramCapacity() {
        var sampler = new AppleGpuSampler(_ => Accelerator);
        var frame = new CollectorFrame();
        sampler.Sample(frame, true, false);
        Assert.AreEqual(1, frame.GpuCount);
        var gpu = frame.Gpus[0];
        Assert.AreEqual("Apple M4 GPU", gpu.Name);
        Assert.AreEqual("ioreg:42", gpu.Id);
        Assert.AreEqual(75d, gpu.Utilization);
        Assert.AreEqual(65d, gpu.Activity.Render);
        Assert.AreEqual(4096UL, gpu.SharedMemory);
        Assert.AreEqual(4096UL, gpu.UsedMemory);
        Assert.IsNull(gpu.DedicatedMemory);
        Assert.IsNull(gpu.TotalMemory);
        Assert.IsNull(gpu.PowerWatts);
        Assert.Contains("unified", gpu.MemoryKind!);
        Assert.AreEqual(CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuRender | CollectorCapabilities.GpuUsedMemory, sampler.Capabilities);
    }

    [TestMethod]
    public void AppleClientRatesTrackEachClientAndRebaselineAfterResetOrDisappearance() {
        string clients = Clients((100, 1, 1_000_000_000), (100, 2, 2_000_000_000));
        var sampler = new AppleGpuSampler(_ => clients);
        sampler.Sample(new(), false, true, 1);
        Assert.IsNull(sampler.ProcessUtilization(100));
        clients = Clients((100, 1, 1_200_000_000), (100, 2, 2_100_000_000));
        sampler.Sample(new(), false, true, 1);
        Assert.AreEqual(30d, sampler.ProcessUtilization(100));
        clients = Clients((100, 1, 1_400_000_000));
        sampler.Sample(new(), false, true, 1);
        Assert.AreEqual(20d, sampler.ProcessUtilization(100), "Closing another client must not subtract its lifetime time.");
        clients = Clients((100, 1, 10));
        sampler.Sample(new(), false, true, 1);
        Assert.IsNull(sampler.ProcessUtilization(100));
        clients = Clients((100, 1, 10));
        sampler.Sample(new(), false, true, 1);
        Assert.AreEqual(0d, sampler.ProcessUtilization(100));
        clients = Clients();
        sampler.Sample(new(), false, true, 1);
        Assert.IsNull(sampler.ProcessUtilization(100));
        clients = Clients((101, 1, 900_000_000));
        sampler.Sample(new(), false, true, 1);
        Assert.IsNull(sampler.ProcessUtilization(101), "A reused client or PID must warm up.");
    }

    [TestMethod]
    public void AppleMissingOrMalformedStatisticsRemainUnknownAndRecover() {
        string? xml = null;
        var sampler = new AppleGpuSampler(_ => xml);
        var frame = new CollectorFrame();
        sampler.Sample(frame, true, true, 1);
        Assert.AreEqual(0, frame.GpuCount);
        Assert.IsFalse(sampler.HasProcessCounters);
        xml = "<plist><broken>";
        sampler.Sample(frame, true, true, 1);
        Assert.AreEqual(0, frame.GpuCount);
        xml = Accelerator.Replace("<integer>75</integer>", "<real>NaN</real>");
        sampler.Sample(frame, true, false, 1);
        Assert.AreEqual(1, frame.GpuCount);
        Assert.IsNull(frame.Gpus[0].Utilization);
        Assert.AreEqual(4096UL, frame.Gpus[0].UsedMemory);
    }

    [TestMethod]
    public void AdapterIdentityAndSplitProcessMemorySurviveTransportAndRender() {
        var frame = new CollectorFrame { GpuCount = 1, ProcessCount = 1 };
        frame.Gpus[0] = new("split GPU", "fixture", 25, 3072, 8192, null) {
            Id = "adapter-id",
            DedicatedMemory = 1024,
            SharedMemory = 2048,
            MemoryKind = "split fixture"
        };
        frame.Processes[0] = new(1, "split process", 0, 0, 1) { GpuDedicatedMemory = 1000, GpuSharedMemory = 2000 };
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        Assert.AreEqual(frame.Gpus[0], decoded.Gpus[0]);
        Assert.AreEqual(frame.Processes[0], decoded.Processes[0]);
        var surface = new MonitorSurface { View = MonitorView.Gpu, Frame = decoded };
        surface.ConfigureDetailColumns(null, null, [GpuColumn.Device, GpuColumn.DedicatedMemory, GpuColumn.SharedMemory, GpuColumn.Identity], null);
        string rendered = string.Join('\n', VisualSnapshotRenderer.Render(surface, 100, 24).ToMarkupLines());
        Assert.Contains("Dedicated", rendered);
        Assert.Contains("Shared", rendered);
        Assert.Contains("adapter-id", rendered);
    }

    [TestMethod]
    public void SplitProcessMemoryColumnsSortFilterAndRequestGpuSampling() {
        ProcessRow[] rows = [new(1, "one", 0, 0, 1) { GpuDedicatedMemory = 100, GpuSharedMemory = 500 },
            new(2, "two", 0, 0, 1) { GpuDedicatedMemory = 300, GpuSharedMemory = 200 }, new(3, "unknown", 0, 0, 1)];
        var model = new ProcessView { Sort = ProcessSort.GpuDedicatedMemory };
        model.Update(rows);
        Assert.AreEqual(2, model.Rows[0].Id);
        Assert.AreEqual(3, model.Rows[2].Id);
        model.ColumnFilters[ProcessColumn.GpuSharedMemory] = ">300";
        model.Update(rows);
        Assert.AreEqual(1, model.Count);
        Assert.AreEqual(1, model.Rows[0].Id);
        var surface = new MonitorSurface { Minimal = true };
        surface.ColumnSettings.Configure([ProcessColumn.Name, ProcessColumn.GpuDedicatedMemory, ProcessColumn.GpuSharedMemory], null, false);
        Assert.AreEqual(CollectorMetrics.ProcessGpu, surface.RequiredMetrics & CollectorMetrics.ProcessGpu);
    }

    [TestMethod]
    public void VersionNineFramesDecodeWithAdapterMetadataUnknown() {
        var frame = new CollectorFrame { GpuCount = 1 };
        frame.Gpus[0] = new("v9", "provider", 12, 100, 200, 123.456789) { Activity = new(Render: 12) };
        // Frozen v9 bytes, with render accounting but no adapter or resource extensions.
        byte[] legacy = Convert.FromBase64String("OU1TRgAA/////wAAAAAAAAAAAAAAAAAA+P//////////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAPj/AAAAAAAA+P8YV2FpdGluZyBmb3IgR1BVIHByb3ZpZGVyFldhaXRpbmcgZm9yIGludGVyZmFjZXMAAAAAAAAAAAEAAAACdjkIcHJvdmlkZXIAAAAAAAAoQGQAAAAAAAAAyAAAAAAAAAALC+4HPN1eQAAAAAAAAChAAAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/AAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/AAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/ABtXYWl0aW5nIGZvciBzZXJ2aWNlIG1hbmFnZXIAAAAAIU5vdCByZXF1ZXN0ZWQgYnkgdGhlIGN1cnJlbnQgdmlldwAAAAA=");
        var decoded = new CollectorFrame();
        FrameCodec.Decode(legacy, decoded);
        Assert.AreEqual(frame.Gpus[0], decoded.Gpus[0]);
        Assert.IsNull(decoded.Gpus[0].Id);
        Assert.IsNull(decoded.Gpus[0].DedicatedMemory);
    }

    private const string Accelerator = """
        <?xml version="1.0"?><!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist><array><dict><key>IORegistryEntryID</key><integer>42</integer><key>model</key><string>Apple M4 GPU</string>
        <key>PerformanceStatistics</key><dict><key>Device Utilization %</key><integer>75</integer>
        <key>Renderer Utilization %</key><integer>65</integer><key>Alloc system memory</key><integer>4096</integer></dict></dict></array></plist>
        """;
    private static string Clients(params (int Pid, ulong Client, ulong Time)[] clients) =>
        "<plist><array>" + string.Concat(clients.Select(client => $"<dict><key>IOUserClientCreator</key><string>pid {client.Pid}, test</string><key>IORegistryEntryID</key><integer>{client.Client}</integer><key>AppUsage</key><array><dict><key>accumulatedGPUTime</key><integer>{client.Time}</integer></dict></array></dict>")) + "</array></plist>";
}
