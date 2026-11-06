using System.Collections.Concurrent;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;
using System.Diagnostics;
using System.Reflection;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
public sealed class MinimalTests {
    private const CollectorMetrics Summaries = CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu;
    [TestMethod]
    public void CollectorClearsUnrequestedDataAndSkipsProcessEnumeration() {
        using var collector = new PerformanceCollector();
        var frame = collector.Capture(CollectorMetrics.Processes);
        Assert.IsGreaterThan(0, frame.ProcessCount);
        Assert.IsNull(frame.TotalMemory);
        Assert.AreEqual(0, frame.GpuCount);
        foreach (ref readonly var row in frame.Processes.AsSpan(0, frame.ProcessCount)) {
            Assert.IsNull(row.UserName);
            Assert.IsNull(row.GpuPercent);
            Assert.IsNull(row.DiskBytesPerSecond);
            Assert.IsNull(row.NetworkBytesPerSecond);
        }
        frame = collector.Capture(CollectorMetrics.Network);
        Assert.AreEqual(0, frame.ProcessCount);
        Assert.IsNull(frame.CpuPercent);
        Assert.IsNull(frame.TotalMemory);
        frame = collector.Capture(CollectorMetrics.Memory);
        Assert.IsNotNull(frame.TotalMemory);
        Assert.AreEqual(0, frame.NetworkCount);
        frame = collector.Capture(CollectorMetrics.None);
        Assert.AreEqual(0, frame.ProcessCount);
        Assert.AreEqual(0, frame.NetworkCount);
        Assert.AreEqual(0, frame.GpuCount);
        Assert.IsNull(frame.TotalMemory);
        Assert.IsNull(frame.ReceiveBytesPerSecond);
    }

    [TestMethod]
    public void DemandTracksVisibleViewsColumnsAndHiddenSortDependencies() {
        var surface = new MonitorSurface { Minimal = true, View = MonitorView.Network };
        VisualSnapshotRenderer.Render(surface, 140, 35);
        Assert.AreEqual(Summaries, surface.RequiredMetrics);
        surface.View = MonitorView.Gpu;
        Assert.AreEqual(Summaries, surface.RequiredMetrics);
        surface.View = MonitorView.Dashboard;
        Assert.AreEqual(CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu, surface.RequiredMetrics);
        surface.View = MonitorView.Processes;
        surface.ColumnSettings.Configure([ProcessColumn.Name, ProcessColumn.Cpu], null, false);
        Assert.AreEqual(Summaries | CollectorMetrics.Processes, surface.RequiredMetrics);
        surface.Model.Sort = ProcessSort.Gpu;
        Assert.AreEqual(Summaries | CollectorMetrics.Processes | CollectorMetrics.ProcessGpu, surface.RequiredMetrics);
        surface.Model.Sort = ProcessSort.Cpu;
        surface.Model.Filter = "alice";
        Assert.AreEqual(Summaries | CollectorMetrics.Processes | CollectorMetrics.Owners, surface.RequiredMetrics);
        surface.Model.Filter = "";
        surface.Minimal = false;
        Assert.AreEqual(CollectorMetrics.Processes | CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu, surface.RequiredMetrics);
        VisualSnapshotRenderer.Render(surface, 30, 10);
        Assert.AreEqual(Summaries, surface.RequiredMetrics);
    }

    [TestMethod]
    public void SwitchingViewsWakesSamplerWithoutWaitingForLongInterval() {
        var source = new RecordingSource();
        using (var app = new MonitorApp(60, 60000, "", ProcessSort.Cpu, MonitorView.Network, source, minimal: true)) {
            var surface = (MonitorSurface)app.Root;
            Assert.IsTrue(SpinWait.SpinUntil(() => source.Requests.Contains(Summaries), 3000));
            Assert.IsFalse(source.Requests.Any(mask => mask.HasFlag(CollectorMetrics.Processes)));
            surface.ColumnSettings.Configure([ProcessColumn.Name, ProcessColumn.Cpu], null, false);
            surface.View = MonitorView.Processes;
            surface.Dirty = true;
            Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return source.Requests.Contains(Summaries | CollectorMetrics.Processes); }, 3000));
        }
        Assert.IsTrue(source.Disposed);
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task PauseReleasesProvidersOnceAndStopsPollingUntilResumed() {
        var source = new RecordingSource();
        using var app = new MonitorApp(60, 20, "", ProcessSort.Cpu, MonitorView.Network, source, minimal: true);
        var surface = (MonitorSurface)app.Root;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(surface);
        try {
            backend.PushEvent(new TerminalTextEvent { Text = "p" });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return source.Requests.Contains(CollectorMetrics.None); }, 5000));
            int count = source.Requests.Count;
            var duration = Stopwatch.StartNew();
            while (duration.ElapsedMilliseconds < 150) {
                Tick();
                Thread.Sleep(5);
            }
            Assert.HasCount(count, source.Requests);
            backend.PushEvent(new TerminalTextEvent { Text = "p" });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return source.Requests.Count > count; }, 5000));
            Assert.AreEqual(Summaries, source.Requests.Last());
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    private sealed class RecordingSource : IMonitorSource {
        public ConcurrentQueue<CollectorMetrics> Requests { get; } = new();
        public string Label => "fixture";
        public bool IsRemote => false;
        public bool Disposed {
            get; private set;
        }
        public void Sample(CollectorFrame destination, CancellationToken token) =>
            throw new InvalidOperationException("Selective overload required");
        public void Sample(CollectorFrame destination, CollectorMetrics metrics, CancellationToken token) =>
            Requests.Enqueue(metrics);
        public void Dispose() =>
            Disposed = true;
    }
}
