using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using FullscreenHost = fullscreen.Hosting.FullscreenHost;
using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorControlTests {
    [TestMethod]
    public void NavigationStartsWithProcessesAndSixelMetricsCanFallBackWithoutEnablingUnsupportedTerminals() {
        Assert.AreEqual(MonitorView.Processes, new MonitorSurface().View);
        CollectionAssert.AreEqual(new[] { MonitorView.Processes, MonitorView.Services, MonitorView.Network, MonitorView.Gpu, MonitorView.Users, MonitorView.Dashboard }, MonitorSurface.NavigationViews);
        var supported = new TerminalGraphicsCapabilities { SupportState = TerminalGraphicsSupportState.Forced, SupportedProtocols = [TerminalGraphicsProtocol.Sixel] };
        var fallback = FullscreenHost.ResolveChartMetrics(supported, null, new TerminalSize(100, 30));
        Assert.AreEqual(8, fallback!.Value.CellPixelWidth);
        Assert.AreEqual(16, fallback.Value.CellPixelHeight);
        Assert.IsNull(FullscreenHost.ResolveChartMetrics(TerminalGraphicsCapabilities.None, null, new TerminalSize(100, 30)));
    }
    [TestMethod]
    [DataRow(MonitorAction.StartService, false)]
    [DataRow(MonitorAction.StopService, false)]
    [DataRow(MonitorAction.RestartService, false)]
    [DataRow(MonitorAction.ForceStopService, false)]
    [DataRow(MonitorAction.StartService, true)]
    public async Task ServiceActionsRequireConfirmationAndExecuteOnceOnTheSourceWorker(MonitorAction action, bool fails) {
        var source = new ControlSource { Fails = fails };
        using var monitor = new MonitorApp(60, 60000, "", ProcessSort.Cpu, MonitorView.Services, source);
        var surface = (MonitorSurface)monitor.Root;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            monitor.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.Frame.ServiceCount == 1; }, 3000));
            surface.ServiceActions(surface.Frame.Services[0])[(int)action - 1].Action.Invoke!();
            Tick();
            Assert.IsEmpty(source.Actions);
            Assert.Contains("fixture-target", backend.GetOutText());
            var confirm = terminal.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock label && label.Text == "Confirm");
            terminal.Focus(confirm);
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return source.Actions.Count == 1 && !surface.ActionBusy; }, 3000));
            Assert.AreEqual(action, source.Actions.Single().Action);
            Assert.AreEqual("fixture.service", source.Actions.Single().Name);
            Assert.AreNotEqual(Environment.CurrentManagedThreadId, source.ThreadId);
            Assert.Contains(fails ? "Action failed · fixture permission denied" : "requested", surface.ActionMessage);
            for (int i = 0; i < 10; i++) {
                Tick();
            }

            Assert.HasCount(1, source.Actions, "Actions must never be retried automatically after a failure.");
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    private sealed class ControlSource : IMonitorSource, IMonitorControlSource {
        public string Label => "fixture-target";
        public bool IsRemote => true;
        public int ThreadId {
            get; private set;
        }
        public bool Fails {
            get; init;
        }
        public ConcurrentQueue<MonitorActionRequest> Actions { get; } = new();
        public void Execute(MonitorActionRequest request, CancellationToken token) {
            ThreadId = Environment.CurrentManagedThreadId;
            Actions.Enqueue(request);
            if (Fails) {
                throw new UnauthorizedAccessException("fixture permission denied");
            }
        }
        public void Sample(CollectorFrame frame, CancellationToken token) {
            frame.ServiceCount = 1;
            frame.Services[0] = new("fixture.service", "Fixture", "running", "", 10);
        }
        public void Dispose() {
        }
    }
}
