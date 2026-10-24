using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class CollectorTests {
    [TestMethod]
    public void MissingGpuCountersRemainLastWhenSortIsReversed() {
        var view = new ProcessView { Sort = ProcessSort.Gpu, Ascending = true };
        view.Update([new(1, "missing", 0, 0, 1), new(2, "measured", 0, 0, 1, 10)]);
        Assert.AreEqual(2, view.Rows[0].Id);
    }
    [TestMethod]
    public void SourceFailurePreservesLastSuccessfulSnapshot() {
        var source = new FailingSource();
        using var app = new MonitorApp(60, 100, "", ProcessSort.Cpu, MonitorView.Processes, source);
        var surface = (MonitorSurface)app.Root;
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.SourceError is not null; }, 3000));
        Assert.AreEqual("fixture-host", surface.Frame.MachineName);
        Assert.AreEqual(1, surface.Model.Count);
        Assert.Contains("fixture disconnect", surface.SourceError!);
    }
    private sealed class FailingSource : IMonitorSource {
        private int _calls;
        public string Label => "fixture-host";
        public bool IsRemote => true;
        public void Sample(CollectorFrame destination, CancellationToken token) {
            if (++_calls > 1) {
                throw new IOException("fixture disconnect");
            }

            destination.MachineName = Label;
            destination.ProcessCount = 1;
            destination.Processes[0] = new(1, "process", 10, 1000, 1);
        }
        public void Dispose() {
        }
    }
    [TestMethod]
    public void WireFormatRoundTripsEveryCounterAndMissingValues() {
        var original = new CollectorFrame { MachineName = "remote-host", Platform = "test", CapturedAt = DateTimeOffset.UtcNow, CpuPercent = 23, TotalMemory = 16ul << 30, AvailableMemory = 8ul << 30, ReceiveBytesPerSecond = 1234, SendBytesPerSecond = 5678, ProcessCount = 2, NetworkCount = 1, GpuCount = 1 };
        original.Processes[0] = new(42, "process界", 70, 1000, 8, 25, 512000, ParentId: 7, UserName: "domain\\alice", StartTimeTicks: 12345, CpuTimeTicks: 9876543210, Status: "Suspended", DiskBytesPerSecond: 123456, NetworkBytesPerSecond: 7890);
        original.Processes[1] = new(43, "missing", 0, 500, 2);
        original.Networks[0] = new("id", "ethernet", 100, 200, 300, 400, 2, 1000000000);
        original.Gpus[0] = new("test GPU", "fixture", 25, 512000, 1024000, 45);
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(original), decoded);
        Assert.AreEqual(original.CapturedAt, decoded.CapturedAt);
        Assert.AreEqual(original.MachineName, decoded.MachineName);
        Assert.AreEqual(original.Processes[0], decoded.Processes[0]);
        Assert.AreEqual(original.Processes[1], decoded.Processes[1]);
        Assert.AreEqual(original.Networks[0], decoded.Networks[0]);
        Assert.AreEqual(original.Gpus[0], decoded.Gpus[0]);
        Assert.AreEqual(original.ReceiveBytesPerSecond, decoded.ReceiveBytesPerSecond);
        Assert.IsNull(decoded.Processes[1].GpuPercent);
    }
    [TestMethod]
    public void WireFormatRejectsWrongProtocolAndTruncatedPayloads() {
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode([0, 0, 0, 0], new()));
        byte[] payload = FrameCodec.Encode(new CollectorFrame());
        Assert.Throws<EndOfStreamException>(() => FrameCodec.Decode(payload[..^1], new()));
    }
    [TestMethod]
    public void NetworkRateHandlesCounterResetAndInitialSample() {
        Assert.AreEqual(50.0, NetworkSampler.Rate(200, 100, 2));
        Assert.IsNull(NetworkSampler.Rate(100, 200, 2));
        Assert.IsNull(NetworkSampler.Rate(200, 100, 0));
    }
    [TestMethod]
    public void CollectorReportsLiveNetworkAndOptionalGpu() {
        using var collector = new PerformanceCollector();
        collector.Capture();
        var frame = collector.Capture();
        Assert.IsGreaterThan(0, frame.ProcessCount);
        Assert.AreEqual(Environment.MachineName, frame.MachineName);
        foreach (ref readonly var row in frame.Networks.AsSpan(0, frame.NetworkCount)) {
            Assert.IsGreaterThanOrEqualTo(0L, row.ReceivedBytes);
            if (row.ReceiveBytesPerSecond.HasValue) {
                Assert.IsGreaterThanOrEqualTo(0.0, row.ReceiveBytesPerSecond.Value);
            }
        }
        foreach (ref readonly var row in frame.Gpus.AsSpan(0, frame.GpuCount)) {
            Console.WriteLine($"GPU: {row.Name}, {row.Provider}, {row.Utilization}%, {row.UsedMemory}/{row.TotalMemory} bytes, {row.Temperature} C");
        }

        Console.WriteLine($"{frame.NetworkCount} interfaces; {frame.GpuCount} GPUs; {frame.SampleMilliseconds:F1} ms; {frame.SampleAllocation} bytes");
    }
}
