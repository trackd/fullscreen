using System.Reflection;
using System.Runtime.InteropServices;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class GpuDepthTests {
    [TestMethod]
    public void WindowsProcessEnginesRemainSeparatedByAdapterAndProcess() {
        using var sampler = new WindowsGpuSampler(native: false);
        const string first = "luid_0x00000000_0x00000001_phys_0";
        const string second = "luid_0x00000000_0x00000002_phys_0";
        sampler.AddCounter("pid_10_" + first + "_eng_0_engtype_3D", 40, 0);
        sampler.AddCounter("pid_20_" + first + "_eng_0_engtype_3D", 50, 0);
        sampler.AddCounter("pid_10_" + second + "_eng_0_engtype_VideoEncode", 80, 0);
        sampler.AddCounter("pid_10_" + first, 100, 1);
        sampler.AddCounter("pid_10_" + first, 200, 4);
        sampler.AddCounter("pid_10_" + second, 300, 1);
        sampler.AddCounter("pid_10_" + second, 400, 4);
        var details = sampler.ProcessDetails(10);
        Assert.HasCount(2, details);
        Assert.AreEqual(40d, details.Single(detail => detail.AdapterId == first).Activity.Render);
        Assert.AreEqual(80d, details.Single(detail => detail.AdapterId == second).Activity.Encode);
        var row = GpuProcessAccounting.Apply(new(10, "process", 0, 0, 1), details);
        Assert.AreEqual(80d, row.GpuPercent);
        Assert.AreEqual(1000UL, row.GpuMemory);
        Assert.AreEqual(400UL, row.GpuDedicatedMemory);
        Assert.AreEqual(600UL, row.GpuSharedMemory);
        Assert.AreEqual("encode, render", row.GpuEngine);
        Assert.Contains(second, row.GpuAdapter!);
        Assert.AreEqual(50d, sampler.ProcessDetails(20).Single().Utilization);
    }

    [TestMethod]
    public void IncompleteOrDuplicateGpuAttributionNeverProducesACompleteTotal() {
        var first = new ProcessGpuUsage("one", "fixture", 20, 100, new(Render: 20), "measured");
        var second = new ProcessGpuUsage("two", "fixture", null, null, default, "denied");
        var partial = GpuProcessAccounting.Apply(new(), [first, second]);
        Assert.IsNull(partial.GpuPercent);
        Assert.IsNull(partial.GpuMemory);
        Assert.HasCount(2, partial.GpuDetails!);
        var duplicate = GpuProcessAccounting.Apply(new(), [first, first]);
        Assert.IsNull(duplicate.GpuMemory);
        Assert.IsNull(duplicate.GpuPercent);
        var missing = GpuProcessAccounting.Apply(new(1, "old", 0, 0, 1, GpuPercent: 50, GpuMemory: 100), []);
        Assert.IsNull(missing.GpuPercent);
        Assert.IsNull(missing.GpuMemory);
        var sm = GpuProcessAccounting.Apply(new(), [first with { Activity = default, SmActivity = 20 }]);
        Assert.AreEqual("SM", sm.GpuEngine, "SM activity must not be mislabeled as compute-only activity.");
    }

    [TestMethod]
    public void NvidiaPartitionAmbiguityAndPidNamespacesStayUnattributed() {
        var partitions = NvmlSampler.MergeProcesses([
            new() { Pid = 10, Memory = 100, GpuInstance = 1, ComputeInstance = 1 },
            new() { Pid = 10, Memory = 200, GpuInstance = 2, ComputeInstance = 1 }], []);
        Assert.IsNull(partitions[10]);
        Assert.IsTrue(NvmlSampler.CanAttributeProcesses("6.12", "NSpid:\t123\n", "0::/", false, null));
        Assert.IsFalse(NvmlSampler.CanAttributeProcesses("6.12-microsoft-standard-WSL2", "NSpid:\t123\n", "0::/", false, null));
        Assert.IsFalse(NvmlSampler.CanAttributeProcesses("6.12", "NSpid:\t123\t1\n", "0::/", false, null));
        Assert.IsFalse(NvmlSampler.CanAttributeProcesses("6.12", "NSpid:\t123\n", "0::/kubepods/workload", false, null));
        Assert.IsFalse(NvmlSampler.CanAttributeProcesses("6.12", "NSpid:\t123\n", "0::/", true, null));
        Assert.IsFalse(NvmlSampler.CanAttributeProcesses("6.12", "Pid:\t123\n", "0::/", false, null));
        Assert.AreEqual("unsupported", NvmlSampler.Availability(3));
        Assert.AreEqual("permission denied", NvmlSampler.Availability(4));
        const long epoch = 621355968000000000;
        Assert.IsTrue(NvmlSampler.SampleMatchesBirth(100, epoch + 1000));
        Assert.IsFalse(NvmlSampler.SampleMatchesBirth(99, epoch + 1000));
        Assert.IsFalse(NvmlSampler.SampleMatchesBirth(ulong.MaxValue, epoch));
    }

    [TestMethod]
    public void AppleGraphicsFootprintSeparatesGraphicsChargesFromOrdinaryProcessMemory() {
        const string json = """
            {"processes":[{"pid":42,"categories":{"Metal (graphics)":{"dirty":1024,"swapped":100},"IOSurface (graphics)":{"dirty":2048,"swapped":200},"heap":{"dirty":900000}}}]}
            """;
        Assert.AreEqual(new AppleGpuMemorySampler.Reading(3072, 300), AppleGpuMemorySampler.Parse(json, 42));
        Assert.IsNull(AppleGpuMemorySampler.Parse(json, 43));
        Assert.AreEqual(new AppleGpuMemorySampler.Reading(0, 0), AppleGpuMemorySampler.Parse("{\"processes\":[{\"pid\":42,\"categories\":{}}]}", 42));
        Assert.IsNull(AppleGpuMemorySampler.Parse(json.Replace("\"dirty\":1024", "\"dirty\":-1"), 42));
        Assert.IsNull(AppleGpuMemorySampler.Parse(json.Replace("\"dirty\":1024", "\"other\":1024"), 42));
        Assert.IsNull(AppleGpuMemorySampler.Parse("not json", 42));
        Assert.IsNull(AppleGpuMemorySampler.Parse(json.Replace("\"swapped\":100", "\"other\":100"), 42)!.Value.Swapped);
    }

    [TestMethod]
    public void AppleFootprintRejectsPidReuseBeforeAndDuringMeasurement() {
        int reads = 0;
        using var before = new AppleGpuMemorySampler(_ => { reads++; return "{}"; }, _ => 200);
        Assert.IsNull(before.Measure(42, 100));
        Assert.AreEqual(0, reads);
        long identity = 100;
        using var during = new AppleGpuMemorySampler(_ => {
            identity = 200;
            return "{\"processes\":[{\"pid\":42,\"categories\":{}}]}";
        }, _ => identity);
        Assert.IsNull(during.Measure(42, 100));
        Assert.IsTrue(AppleGpuMemorySampler.Fresh(1000, 0, 100));
        Assert.IsFalse(AppleGpuMemorySampler.Fresh(1001, 0, 100));
        Assert.IsFalse(AppleGpuMemorySampler.Fresh(0, 1, 100));
    }

    [TestMethod]
    public async Task AppleFootprintRunsOffTheSamplingThreadAndCannotPublishAfterDisposal() {
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int callingThread = Environment.CurrentManagedThreadId;
        using var sampler = new AppleGpuMemorySampler(_ => {
            started.TrySetResult(Environment.CurrentManagedThreadId);
            if (!release.Wait(TimeSpan.FromSeconds(5))) { throw new IOException("Fixture timed out."); }
            return "{\"processes\":[{\"pid\":42,\"categories\":{}}]}";
        }, _ => 100);
        try {
            sampler.Schedule([new(42, "fixture", 0, 0, 1, StartTimeTicks: 100)], 1, [42]);
            Assert.AreNotEqual(callingThread, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsNull(sampler.Get(42, 100).Value);
            sampler.Dispose();
            release.Set();
            var worker = (Task)typeof(AppleGpuMemorySampler).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sampler)!;
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNull(sampler.Get(42, 100).Value);
            Assert.IsNull(sampler.Get(42, 200).Value);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void AmdReadOnlySensorsRespectSupportFlagsUnitsAndPowerScope() {
        var sensors = new AmdWindowsGpuSampler.Sensor[256];
        sensors[1] = new(1, 900);
        sensors[8] = new(1, 45);
        sensors[19] = new(1, 70);
        sensors[20] = new(1, 25);
        sensors[23] = new(1, 80);
        var input = new GpuRow("AMD", "Windows PDH", 60, 100, 200, null);
        var row = AmdWindowsGpuSampler.Apply(input, sensors, true);
        Assert.AreEqual(900d, row.ClockMHz);
        Assert.AreEqual(45d, row.Temperature);
        Assert.AreEqual(70d, row.Utilization);
        Assert.AreEqual(25d, row.Activity.Memory);
        Assert.AreEqual(80d, row.PowerWatts);
        Assert.AreEqual("ASIC", row.PowerScope);
        sensors[73] = new(1, 100);
        sensors[20] = new(0, 99);
        row = AmdWindowsGpuSampler.Apply(input, sensors, true);
        Assert.AreEqual(100d, row.PowerWatts);
        Assert.AreEqual("board", row.PowerScope);
        Assert.IsNull(row.Activity.Memory);
        Assert.IsNull(AmdWindowsGpuSampler.Apply(input, sensors, false).PowerWatts);
        Assert.AreEqual(1572, Marshal.SizeOf<AmdWindowsGpuSampler.AdapterInfo>());
        Assert.AreEqual(2052, Marshal.SizeOf<AmdWindowsGpuSampler.Output>());
    }

    [TestMethod]
    public void ProcessGpuDetailsAndAvailabilitySurviveTransportAndExplainEachAdapter() {
        var frame = new CollectorFrame { ProcessCount = 1, GpuCount = 1 };
        frame.Processes[0] = GpuProcessAccounting.Apply(new(42, "fixture", 0, 0, 1), [
            new("adapter-A", "fixture", 40, 100, new(Render: 40), "measured") { MemoryKind = "private resident" },
            new("adapter-B", "fixture", null, null, default, "permission denied")
        ]);
        frame.Gpus[0] = new("GPU", "fixture", null, null, null, null) { Status = "PMU permission denied" };
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        CollectionAssert.AreEqual(frame.Processes[0].GpuDetails!, decoded.Processes[0].GpuDetails!);
        Assert.AreEqual(frame.Gpus[0].Status, decoded.Gpus[0].Status);
        string text = MonitorSurface.FormatGpuProcessDetails(decoded.Processes[0]);
        Assert.Contains("adapter-A", text);
        Assert.Contains("Render 40%", text);
        Assert.Contains("adapter-B", text);
        Assert.Contains("permission denied", text);
        Assert.Contains("Unavailable", text);
        var surface = new MonitorSurface { Minimal = true };
        surface.ColumnSettings.Configure([ProcessColumn.Name, ProcessColumn.GpuAdapter, ProcessColumn.GpuEngine], null, false);
        Assert.AreEqual(CollectorMetrics.ProcessGpu, surface.RequiredMetrics & CollectorMetrics.ProcessGpu);
    }

    [TestMethod]
    public void VersionTenSnapshotsDecodeWithoutInventingProcessDetailsOrStatus() {
        // Captured using the previous v10 collector, independent of the current encoder.
        const string legacy = "Ok1TRgAA/////wAAAAAAAAAAAAAAAAAA+P//////////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAPj/AAAAAAAA+P8YV2FpdGluZyBmb3IgR1BVIHByb3ZpZGVyFldhaXRpbmcgZm9yIGludGVyZmFjZXMBAAAAKgAAAA5sZWdhY3ktcHJvY2VzcwAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAA+P////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD4/wAAAAAAAPj//////////////////////wAAAAABAAAACmxlZ2FjeS12MTAHZml4dHVyZQAAAAAAAChAZAAAAAAAAADIAAAAAAAAAAAAAAAAAPj/AAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/AAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/AAAAAAAA+P8AAAAAAAD4/wAAAAAAAPj/AAAAAAAA+P8AAP////////////////////8AG1dhaXRpbmcgZm9yIHNlcnZpY2UgbWFuYWdlcgAAAAAhTm90IHJlcXVlc3RlZCBieSB0aGUgY3VycmVudCB2aWV3AAAAAA==";
        var frame = new CollectorFrame();
        FrameCodec.Decode(Convert.FromBase64String(legacy), frame);
        Assert.AreEqual(42, frame.Processes[0].Id);
        Assert.AreEqual("legacy-v10", frame.Gpus[0].Name);
        Assert.IsNull(frame.Processes[0].GpuDetails);
        Assert.IsNull(frame.Gpus[0].Status);
        Assert.AreEqual(12d, frame.Gpus[0].Utilization);
    }
}
