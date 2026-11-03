using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class EtwCollectorTests {
    private const long Birth = 1000000;
    private static ProcessRow[] Rows() =>
        [new(42, "fixture", 0, 0, 1, StartTimeTicks: Birth)];
    private static byte[] Network(uint pid, uint size) {
        var payload = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, pid);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), size);
        return payload;
    }
    private static byte[] Disk(uint size, uint tid, bool x64 = true) {
        var payload = new byte[x64 ? 52 : 44];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), size);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(x64 ? 48 : 40), tid);
        return payload;
    }
    private static EtwIoCounters Seed(ProcessRow[] rows, bool disk = true, bool network = true) {
        var counters = new EtwIoCounters(disk, network);
        counters.Sample(rows, [new(100, 42, Birth + 1)], 0, 0, Birth + 2);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        Assert.IsNull(rows[0].NetworkBytesPerSecond);
        return counters;
    }
    [TestMethod]
    public void TcpAndUdpV4V6CountPayloadPidAndBytesButNotRetransmits() {
        var rows = Rows();
        var counters = Seed(rows);
        foreach (Guid provider in new[] { EtwIoCounters.TcpProvider, EtwIoCounters.UdpProvider }) {
            foreach (byte opcode in new byte[] { 10, 11, 26, 27 }) {
                counters.Accept(provider, opcode, 2, 0x40, 999, 0, Birth + 10, Network(42, 1024));
            }
        }

        counters.Accept(EtwIoCounters.TcpProvider, 14, 2, 0x40, 42, 0, Birth + 10, Network(42, 99999));
        counters.Sample(rows, [], 2, 0, Birth + 20);
        Assert.AreEqual(4096d, rows[0].NetworkBytesPerSecond);
        counters.Sample(rows, [], 1, 0, Birth + 30);
        Assert.AreEqual(0d, rows[0].NetworkBytesPerSecond, "Counters reset after every sample.");
    }
    [TestMethod]
    public void DiskCompletionsUseIssuingThreadAcrossBothPointerSizesAndHeaderPidWhenPresent() {
        var rows = Rows();
        var counters = Seed(rows);
        counters.Accept(EtwIoCounters.DiskProvider, 10, 3, 0x40, uint.MaxValue, uint.MaxValue, Birth + 10, Disk(4096, 100));
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x20, uint.MaxValue, uint.MaxValue, Birth + 11, Disk(2048, 100, false));
        counters.Accept(EtwIoCounters.DiskProvider, 10, 2, 0x40, 42, 999, Birth + 12, Disk(1024, 999));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.AreEqual(7168d, rows[0].DiskBytesPerSecond);
    }
    [TestMethod]
    public void ThreadEventsTrackShortLivedThreadsBetweenProcessSnapshots() {
        var rows = Rows();
        var counters = Seed(rows);
        counters.Accept(EtwIoCounters.ThreadProvider, 1, 3, 0x40, 0, 0, Birth + 10, Network(42, 101));
        counters.Accept(EtwIoCounters.ThreadProvider, 2, 3, 0x40, 0, 0, Birth + 11, Network(42, 101));
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x40, uint.MaxValue, uint.MaxValue, Birth + 12, Disk(8192, 101));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.AreEqual(8192d, rows[0].DiskBytesPerSecond);
    }
    [TestMethod]
    public void PidAndTidReuseRejectDelayedEventsFromThePreviousProcess() {
        var rows = Rows();
        var counters = Seed(rows);
        rows[0] = rows[0] with {
            StartTimeTicks = Birth + 100
        };
        counters.Sample(rows, [new(100, 42, Birth + 101)], 1, 0, Birth + 102);
        Assert.IsNull(rows[0].NetworkBytesPerSecond);
        counters.Accept(EtwIoCounters.TcpProvider, 10, 2, 0x40, 0, 0, Birth + 50, Network(42, 9999));
        counters.Accept(EtwIoCounters.ThreadProvider, 1, 3, 0x40, 0, 0, Birth + 50, Network(42, 100));
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x40, uint.MaxValue, uint.MaxValue, Birth + 120, Disk(4096, 100));
        counters.Sample(rows, [], 1, 0, Birth + 130);
        Assert.AreEqual(0d, rows[0].NetworkBytesPerSecond);
        Assert.AreEqual(4096d, rows[0].DiskBytesPerSecond);
    }
    [TestMethod]
    public void LostEventsInvalidateRatesAndThreadMapRecoversFromTheOsSnapshot() {
        var rows = Rows();
        var counters = Seed(rows);
        counters.Accept(EtwIoCounters.TcpProvider, 10, 2, 0x40, 0, 0, Birth + 10, Network(42, 1024));
        counters.Sample(rows, [new(100, 42, Birth + 1)], 1, 1, Birth + 20);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        Assert.IsNull(rows[0].NetworkBytesPerSecond);
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x40, uint.MaxValue, uint.MaxValue, Birth + 30, Disk(2048, 100));
        counters.Sample(rows, [], 1, 1, Birth + 40);
        Assert.AreEqual(2048d, rows[0].DiskBytesPerSecond);
        Assert.AreEqual(0d, rows[0].NetworkBytesPerSecond);
    }
    [TestMethod]
    public void MalformedOrUnknownSchemasInvalidateOnlyTheAffectedMetric() {
        var rows = Rows();
        var counters = Seed(rows);
        counters.Accept(EtwIoCounters.TcpProvider, 10, 99, 0x40, 0, 0, Birth + 10, Network(42, 1024));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.IsNull(rows[0].NetworkBytesPerSecond);
        Assert.AreEqual(0d, rows[0].DiskBytesPerSecond);
        counters.Accept(EtwIoCounters.DiskProvider, 10, 3, 0x40, 42, 0, Birth + 30, new byte[8]);
        counters.Sample(rows, [], 1, 0, Birth + 40);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        Assert.AreEqual(0d, rows[0].NetworkBytesPerSecond);
        counters.Fault();
        counters.Sample(rows, [], 1, 0, Birth + 50);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        Assert.IsNull(rows[0].NetworkBytesPerSecond);
    }
    [TestMethod]
    public void DisabledMetricsAndUnmappedDiskEventsNeverBecomeInventedRates() {
        var rows = Rows();
        var counters = Seed(rows, disk: false);
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x40, 42, 0, Birth + 10, Disk(1024, 100));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        counters = Seed(rows);
        counters.Accept(EtwIoCounters.DiskProvider, 11, 3, 0x40, uint.MaxValue, uint.MaxValue, Birth + 10, Disk(1024, 999));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.IsNull(rows[0].DiskBytesPerSecond);
        counters.Sample([], [], 1, 0, Birth + 30);
        counters.Sample(rows, [], 1, 0, Birth + 40);
        Assert.IsNull(rows[0].NetworkBytesPerSecond, "Departed PIDs must lose their baselines.");
    }
    [TestMethod]
    public void SteadyStateEventDecodingDoesNotAllocateManagedObjects() {
        Assert.AreEqual(System.Runtime.GCLatencyMode.Batch, System.Runtime.GCSettings.LatencyMode,
            "Exact allocation measurements require the test host's background GC to be disabled.");
        var rows = Rows();
        var counters = Seed(rows);
        byte[] payload = Network(42, 1024);
        // Warm exactly the measured method, including its loop, before counting.
        // Separate warmup/measurement loops in the test can tier independently.
        _ = DecodeAllocationBatch(counters, payload);
        Assert.AreEqual(0L, DecodeAllocationBatch(counters, payload));
        counters.Sample(rows, [], 1, 0, Birth + 20);
        Assert.AreEqual(20000 * 1024d, rows[0].NetworkBytesPerSecond);
        Assert.IsGreaterThanOrEqualTo(1024L, MeasureAllocationControl(),
            "The allocation counter must still detect deliberately allocated objects.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureAllocationControl() {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var allocated = new byte[1024];
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(allocated);
        return bytes;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long DecodeAllocationBatch(EtwIoCounters counters, byte[] payload) {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) {
            counters.Accept(EtwIoCounters.TcpProvider, 10, 2, 0x40, 0, 0, Birth + 10, payload);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    [TestMethod]
    public void WindowsSdkStructuresMatchThe64BitAbi() {
        if (!Environment.Is64BitProcess) {
            Assert.Inconclusive("ETW collector supports x64/arm64 hosts.");
        }

        Assert.AreEqual(48, Marshal.SizeOf<EtwNative.WnodeHeader>());
        Assert.AreEqual(120, Marshal.SizeOf<EtwNative.TraceProperties>());
        Assert.AreEqual(80, Marshal.SizeOf<EtwNative.EventHeader>());
        Assert.AreEqual(112, Marshal.SizeOf<EtwNative.EventRecord>());
        Assert.AreEqual(88, Marshal.SizeOf<EtwNative.EventTrace>());
        Assert.AreEqual(172, Marshal.SizeOf<EtwNative.TimeZoneInformation>());
        Assert.AreEqual(280, Marshal.SizeOf<EtwNative.LogfileHeader>());
        Assert.AreEqual(448, Marshal.SizeOf<EtwNative.TraceLogfile>());
        Assert.AreEqual((nint)96, Marshal.OffsetOf<EtwNative.EventRecord>(nameof(EtwNative.EventRecord.UserData)));
        Assert.AreEqual((nint)424, Marshal.OffsetOf<EtwNative.TraceLogfile>(nameof(EtwNative.TraceLogfile.EventRecordCallback)));
    }
    [TestMethod]
    [TestCategory("WindowsETW")]
    public void NativeSessionCollectsTrafficAndDisposesWhenPermitted() {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) {
            Assert.Inconclusive("Windows x64/arm64 integration test.");
            return;
        }
        using var processSampler = new ProcessSampler();
        processSampler.Sample(false, true);
        ProcessRow[] rows = [.. processSampler.Rows];
        int processId = Environment.ProcessId;
        var counters = new EtwIoCounters(true, true);
        counters.Sample(rows, processSampler.ThreadIdentities, 0, 0, DateTime.UtcNow.ToFileTimeUtc());
        WindowsKernelTrace trace;
        try {
            trace = new(counters, true, true);
        }
        catch (Win32Exception error) when (error.NativeErrorCode is 5 or 1314) {
            using var sampler = new WindowsIoSampler();
            sampler.Enrich(rows, processSampler.ThreadIdentities, true, true);
            Assert.IsTrue(rows.All(row => row.NetworkBytesPerSecond is null && row.DiskBytesPerSecond is null), "Access denial must leave unavailable values.");
            Assert.Inconclusive("Live ETW requires an elevated host or explicitly granted tracing privileges.");
            return;
        }
        using (trace) {
            using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.Client.ReceiveTimeout = 3000;
            using var sender = new UdpClient();
            var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
            byte[] payload = new byte[1024];
            byte[] diskPayload = new byte[1024 * 1024];
            string file = Path.Combine(Path.GetTempPath(), "Fullscreen-ETW-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
                bool sawNetwork = false, sawDisk = false;
                Assert.IsTrue(SpinWait.SpinUntil(() => {
                    // Keep producing events after startup or an incomplete ETW sample.
                    sender.Send(payload, endpoint);
                    receiver.Client.Receive(payload);
                    output.Position = 0;
                    output.Write(diskPayload);
                    output.Flush(true);
                    if (!trace.TryGetLoss(out ulong lost)) {
                        Thread.Sleep(100);
                        return false;
                    }

                    processSampler.Sample(false, true);
                    rows = [.. processSampler.Rows];
                    counters.Sample(rows, processSampler.ThreadIdentities, 1, lost, DateTime.UtcNow.ToFileTimeUtc());
                    var row = rows.Single(row => row.Id == processId);
                    sawNetwork |= row.NetworkBytesPerSecond > 0;
                    sawDisk |= row.DiskBytesPerSecond > 0;
                    if (!sawNetwork || !sawDisk) {
                        Thread.Sleep(100);
                    }

                    return sawNetwork && sawDisk;
                }, 8000), $"Native per-process events: network={sawNetwork}, physical disk={sawDisk}.");
            }
            finally {
                if (File.Exists(file)) {
                    File.Delete(file);
                }
            }
        }
        Assert.IsFalse(trace.Healthy);
        Assert.IsFalse(trace.TryGetLoss(out _));
        trace.Dispose(); // Idempotent close must not touch a recycled trace handle.
    }
}
