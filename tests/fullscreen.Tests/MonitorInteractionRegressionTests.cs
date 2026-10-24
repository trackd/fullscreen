using System.Diagnostics;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorInteractionRegressionTests {
    [TestMethod]
    public void ServiceDescriptionShowsDisplayNameAndOnlyShowsDetailsWhenUseful() {
        var frame = new CollectorFrame { ServiceCount = 1 };
        frame.Services[0] = new("fixture", "Useful service display name", "running", "—", 123);
        var surface = new MonitorSurface { View = MonitorView.Services, MinHeight = 30, MaxHeight = 30 };
        surface.SetFrame(frame);
        string healthy = string.Join('\n', XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(surface, 140, 30).ToMarkupLines());
        Assert.Contains("Useful service display name", healthy);
        Assert.DoesNotContain("DETAIL", healthy);
        frame.Services[0] = frame.Services[0] with {
            Detail = "error 1066 / service 42"
        };
        surface.SetFrame(frame);
        string failed = string.Join('\n', XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer.Render(surface, 140, 30).ToMarkupLines());
        Assert.Contains("DETAIL", failed);
        Assert.Contains("error 1066", failed);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExclusionsHideAllMatchingNamesAndRestoreChildrenAfterExcludedParents(bool tree) {
        ProcessRow[] rows = [new(1, "worker", 1, 1, 1), new(2, "WORKER", 2, 2, 1), new(3, "child", 3, 3, 1, ParentId: 1)];
        var model = new ProcessView { Tree = tree };
        model.ExcludedNames.Add("worker");
        model.Update(rows);
        Assert.AreEqual(1, model.Count);
        Assert.AreEqual(3, model.Rows[0].Id);
        model.ExcludedNames.Clear();
        model.Update(rows);
        Assert.AreEqual(3, model.Count);
    }
    [TestMethod]
    [DataRow(MonitorView.Network)]
    [DataRow(MonitorView.Gpu)]
    [DataRow(MonitorView.Dashboard)]
    public async Task EveryPlotAcceptsClicksAtTheCorrectColumnThroughChildEventRouting(MonitorView view) {
        var surface = new MonitorSurface { View = view };
        surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch, CpuPercent = 50 });
        using var backend = new InMemoryTerminalBackend(new TerminalSize(140, 44));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long time = Stopwatch.GetTimestamp();
        void Tick() {
            time += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [time]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        try {
            Tick();
            var plots = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.Bounds.Width > 0).ToArray();
            Assert.HasCount(view == MonitorView.Dashboard ? 5 : 2, plots);
            foreach (var plot in plots) {
                int column = plot.Bounds.Width * 2 / 3;
                backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = plot.Bounds.X + column, Y = plot.Bounds.Y + plot.Bounds.Height / 2 });
                Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return plot.InspectionColumn == column; }, 2000), $"Plot {plot.Bounds} did not receive column {column}.");
                backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = plot.Bounds.X + column, Y = plot.Bounds.Y + plot.Bounds.Height / 2 });
                Tick();
            }
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null); }
    }
}
