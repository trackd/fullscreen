using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class LinuxIntelPmuSamplerTests {
    [TestMethod]
    public void XePmuUsesQueriedEngineTopologyAndPairedTickCounters() {
        using var fixture = new Fixture();
        fixture.AddXe();
        var active = new FakeCounter(new(100, 1000, 1000), new(150, 2000, 1500), new(160, 3000, 2000));
        var total = new FakeCounter(new(1000, 1000, 1000), new(1200, 2000, 2000), new(10, 3000, 3000));
        var configurations = new List<ulong>();
        using (var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, config, cpu) => {
            configurations.Add(config);
            Assert.AreEqual(0, cpu);
            return (config & 0xFFF) == 2 ? active : total;
        }, pci => {
            Assert.AreEqual("0000:13:00.0", pci);
            return [new(4, 2, 1)];
        })) {
            Assert.IsNull(sampler.Sample("0000:13:00.0", "xe").Utilization);
            var row = sampler.Sample("0000:13:00.0", "xe");
            Assert.AreEqual(50d, row.Utilization, "Active and total ticks need independent perf multiplex scaling.");
            Assert.AreEqual(50d, row.Activity.Compute);
            Assert.IsNull(row.Activity.Render);
            Assert.IsNull(sampler.Sample("0000:13:00.0", "xe").Utilization, "Reset total ticks must rebaseline.");
            CollectionAssert.AreEqual(new[] { (1UL << 60) | (4UL << 20) | (2UL << 12) | 2, (1UL << 60) | (4UL << 20) | (2UL << 12) | 3 }, configurations);
        }
        Assert.IsTrue(active.Disposed);
        Assert.IsTrue(total.Disposed);
    }

    [TestMethod]
    public void XePartialCounterOpenFailureClosesDescriptorsAndKeepsClientFallbackAvailable() {
        using var fixture = new Fixture();
        fixture.AddXe();
        var active = new FakeCounter(new LinuxIntelPmuSampler.Reading(0, 1, 1));
        using var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, config, _) =>
            (config & 0xFFF) == 2 ? active : throw new UnauthorizedAccessException(), _ => [new(0, 0, 0)]);
        var result = sampler.Sample("0000:13:00.0", "xe");
        Assert.IsFalse(result.Available);
        Assert.IsNull(result.Utilization);
        Assert.Contains("access denied", result.Status);
        Assert.IsTrue(active.Disposed);
    }

    [TestMethod]
    public void XeTopologyParserRejectsTruncationAndMatchesTheFixedKernelAbi() {
        Assert.AreEqual(40, System.Runtime.InteropServices.Marshal.SizeOf<LinuxIntelPmuSampler.XeQuery>());
        byte[] buffer = new byte[40];
        BitConverter.GetBytes(1u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(buffer, 8);
        BitConverter.GetBytes((ushort)2).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)1).CopyTo(buffer, 12);
        CollectionAssert.AreEqual(new[] { new LinuxIntelPmuSampler.XeEngine(4, 2, 1) }, LinuxIntelPmuSampler.ParseXeEngines(buffer));
        Assert.Throws<IOException>(() => LinuxIntelPmuSampler.ParseXeEngines(buffer[..^1]));
        Assert.Throws<IOException>(() => LinuxIntelPmuSampler.ParseXeEngines([]));
    }
    [TestMethod]
    public void RelocatedIntegratedGpuUsesExactPciPmuAndBusiestEngineWithoutClients() {
        using var fixture = new Fixture();
        fixture.Add("i915_0000_13_00.0");
        fixture.Add("i915");
        var render = new FakeCounter(new(100, 1000, 1000), new(600, 2000, 2000));
        var video = new FakeCounter(new(0, 1000, 1000), new(250, 2000, 2000));
        var opened = new List<(uint, ulong, int)>();
        using (var sampler = new LinuxIntelPmuSampler(fixture.Root, (type, config, cpu) => {
            opened.Add((type, config, cpu));
            return config == 0 ? render : video;
        })) {
            var baseline = sampler.Sample("0000:13:00.0", "i915");
            Assert.IsTrue(baseline.Available);
            Assert.IsNull(baseline.Utilization);
            Assert.Contains("warming up", baseline.Status);
            var result = sampler.Sample("0000:13:00.0", "i915");
            Assert.AreEqual(50d, result.Utilization);
            Assert.AreEqual(50d, result.Activity.Render);
            Assert.AreEqual(25d, result.Activity.Video);
            Assert.IsNull(result.Activity.Encode, "i915 PMU video is not a separate encoder counter.");
            Assert.IsNull(result.Activity.Decode);
            Assert.IsTrue(baseline.EngineCapabilities.HasFlag(CollectorCapabilities.GpuVideo));
            Assert.HasCount(2, opened);
            CollectionAssert.AreEquivalent(new[] { (42u, 0UL, 2), (42u, 16UL, 2) }, opened);
        }
        Assert.IsTrue(render.Disposed);
        Assert.IsTrue(video.Disposed);
    }

    [TestMethod]
    public void CounterResetAndUnscheduledIntervalsRebaselineRatherThanInventActivity() {
        using var fixture = new Fixture();
        fixture.Add("i915", single: true);
        var counter = new FakeCounter(new(900, 1000, 1000), new(10, 2000, 2000),
            new(10, 3000, 2000), new(110, 4000, 2500), new(110, 5000, 3500));
        using var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, _, _) => counter);
        Assert.IsNull(sampler.Sample("0000:00:02.0", "i915").Utilization);
        Assert.IsNull(sampler.Sample("0000:00:02.0", "i915").Utilization);
        Assert.IsNull(sampler.Sample("0000:00:02.0", "i915").Utilization);
        Assert.AreEqual(20d, sampler.Sample("0000:00:02.0", "i915").Utilization, "Multiplexing uses running time, not enabled time.");
        Assert.AreEqual(0d, sampler.Sample("0000:00:02.0", "i915").Utilization, "Idle is a measured zero.");
    }

    [TestMethod]
    public void PartialOpenFailureClosesCountersAndReportsDeniedWithoutRetryingEveryRefresh() {
        using var fixture = new Fixture();
        fixture.Add("i915");
        var counter = new FakeCounter(new LinuxIntelPmuSampler.Reading(0, 1, 1));
        int attempts = 0;
        using var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, _, _) => ++attempts == 1 ? counter : throw new UnauthorizedAccessException());
        var result = sampler.Sample("0000:00:02.0", "i915");
        Assert.IsFalse(result.Available);
        Assert.IsNull(result.Utilization);
        Assert.Contains("access denied", result.Status);
        Assert.IsTrue(counter.Disposed);
        sampler.Sample("0000:00:02.0", "i915");
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void MissingPmuAndXeNeverBorrowAnotherAdaptersLegacyI915Counters() {
        using var fixture = new Fixture();
        fixture.Add("i915");
        int attempts = 0;
        using var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, _, _) => { attempts++; throw new IOException(); });
        var missing = sampler.Sample("0000:13:00.0", "i915");
        Assert.IsFalse(missing.Available);
        Assert.Contains("unavailable", missing.Status);
        Assert.IsFalse(sampler.Sample("0000:00:02.0", "xe").Available);
        Assert.AreEqual(0, attempts);
    }

    [TestMethod]
    public void FailedReadDoesNotPublishPartialEngineActivityAndNextSampleWarmsUp() {
        using var fixture = new Fixture();
        fixture.Add("i915", single: true);
        int reads = 0;
        using var sampler = new LinuxIntelPmuSampler(fixture.Root, (_, _, _) => new CallbackCounter(() => {
            if (++reads == 2) { throw new IOException(); }
            return new((ulong)reads * 10, (ulong)reads * 100, (ulong)reads * 100);
        }));
        sampler.Sample("0000:00:02.0", "i915");
        var failed = sampler.Sample("0000:00:02.0", "i915");
        Assert.IsNull(failed.Utilization);
        Assert.Contains("read failed", failed.Status);
        Assert.IsNull(sampler.Sample("0000:00:02.0", "i915").Utilization);
        Assert.AreEqual(10d, sampler.Sample("0000:00:02.0", "i915").Utilization);
    }

    internal sealed class FakeCounter(params LinuxIntelPmuSampler.Reading[] readings) : LinuxIntelPmuSampler.Counter {
        private int _index;
        internal bool Disposed;
        internal override LinuxIntelPmuSampler.Reading Read() => readings[Math.Min(_index++, readings.Length - 1)];
        public override void Dispose() => Disposed = true;
    }
    private sealed class CallbackCounter(Func<LinuxIntelPmuSampler.Reading> read) : LinuxIntelPmuSampler.Counter {
        internal override LinuxIntelPmuSampler.Reading Read() => read();
        public override void Dispose() { }
    }
    private sealed class Fixture : IDisposable {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-PMU-" + Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(Root);
        internal string AddXe() {
            string path = Path.Combine(Root, "xe_0000_13_00.0");
            Directory.CreateDirectory(Path.Combine(path, "events"));
            Directory.CreateDirectory(Path.Combine(path, "format"));
            File.WriteAllText(Path.Combine(path, "type"), "43");
            File.WriteAllText(Path.Combine(path, "events", "engine-active-ticks"), "event=0x02");
            File.WriteAllText(Path.Combine(path, "events", "engine-total-ticks"), "event=0x03");
            foreach (var item in new[] { ("event", "0-11"), ("gt", "60-63"), ("engine_class", "20-27"), ("engine_instance", "12-19") }) {
                File.WriteAllText(Path.Combine(path, "format", item.Item1), "config:" + item.Item2);
            }
            return path;
        }
        internal void Add(string name, bool single = false) {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(path, "events"));
            File.WriteAllText(Path.Combine(path, "type"), "42\n");
            File.WriteAllText(Path.Combine(path, "cpumask"), "2-3,6\n");
            File.WriteAllText(Path.Combine(path, "events", "rcs0-busy"), "config=0x0\n");
            if (!single) { File.WriteAllText(Path.Combine(path, "events", "vcs0-busy"), "config=0x10\n"); }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
