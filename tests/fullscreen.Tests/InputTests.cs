using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using System.Reflection;
using System.Diagnostics;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InputTests {
    [TestMethod]
    public async Task KeyboardMousePasteAndResizeUseFrameworkRouting() {
        var surface = new MonitorSurface();
        surface.ColumnSettings.Configure([ProcessColumn.Id, ProcessColumn.Cpu, ProcessColumn.Memory, ProcessColumn.Name], null, false);
        surface.Model.Update([new(1, "pwsh", 50, 100, 1), new(2, "dotnet", 10, 200, 2)]);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(100, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var app = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true, InitialFocusMode = InitialFocusMode.FirstFocusable });
        // XenoAtom's deterministic test entry points are internal. Reflection stays in this test adapter.
        void Begin() =>
            typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [timestamp]);
        }
        void End() =>
            typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        Begin();
        app.Focus(surface);
        Assert.IsTrue(surface.HasFocus);
        try {
            void Send(TerminalEvent ev, Func<bool>? observed = null) {
                backend.PushEvent(ev);
                Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed?.Invoke() ?? true; }, 2000), "Input was not delivered by the framework reader.");
            }
            Tick();
            Assert.Contains("5 Users", backend.GetOutText());
            Assert.Contains("6 Dashboard", backend.GetOutText());
            Assert.AreEqual(MonitorView.Processes, surface.View);
            Send(new TerminalTextEvent { Text = "6" }, () => surface.View == MonitorView.Dashboard);
            Assert.AreEqual(MonitorView.Dashboard, surface.View);
            Send(new TerminalTextEvent { Text = "g" }, () => surface.GraphType == Charts.GraphKind.Bars);
            Assert.AreEqual(Charts.GraphKind.Bars, surface.GraphType);
            Send(new TerminalTextEvent { Text = "1" }, () => surface.View == MonitorView.Processes);
            Assert.AreEqual(MonitorView.Processes, surface.View);
            Send(new TerminalKeyEvent { Key = TerminalKey.Down }, () => surface.Model.SelectedId == 2);
            Assert.AreEqual(2, surface.Model.SelectedId);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 18, Y = 10 }, () => surface.Model.Sort == ProcessSort.Memory);
            Assert.AreEqual(ProcessSort.Memory, surface.Model.Sort);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 18, Y = 10 });
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 6, Y = 10 }, () => backend.GetOutText().Contains("Clear this filter", StringComparison.Ordinal));
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 6, Y = 10 });
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.HasFocus);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Right, X = 10, Y = 12 }, () => backend.GetOutText().Contains("Filter this name", StringComparison.Ordinal));
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Right, X = 10, Y = 12 });
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.HasFocus);
            Send(new TerminalTextEvent { Text = "t" }, () => surface.Model.Tree);
            Send(new TerminalTextEvent { Text = "c" }, () => surface.ColumnsOpen);
            Send(new TerminalKeyEvent { Key = TerminalKey.Down });
            Send(new TerminalTextEvent { Text = " " }, () => surface.ColumnSettings.Contains(ProcessColumn.ParentId));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => !surface.ColumnsOpen);
            Send(new TerminalTextEvent { Text = "/" });
            Send(new TerminalPasteEvent { Text = "pwsh" }, () => surface.Model.Filter == "pwsh");
            Assert.AreEqual("pwsh", surface.Model.Filter);
            Send(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = TerminalChar.CtrlU, Modifiers = TerminalModifiers.Ctrl }, () => surface.Model.Filter.Length == 0);
            Send(new TerminalPasteEvent { Text = "dotnet" }, () => surface.Model.Filter == "dotnet");
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.Model.Filter.Length == 0);
            surface.Model.Filter = "pwsh";
            surface.Model.ColumnFilters[ProcessColumn.Name] = "pwsh";
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.Model.Filter.Length == 0 && surface.Model.ColumnFilters.Count == 0);
            Send(new TerminalTextEvent { Text = "p" }, () => surface.Paused);
            Assert.IsTrue(surface.Paused);
            Send(new TerminalTextEvent { Text = "2" }, () => surface.View == MonitorView.Services);
            Assert.AreEqual(CollectorMetrics.None, surface.RequiredMetrics); // Paused service view performs no work.
            Send(new TerminalTextEvent { Text = "3" }, () => surface.View == MonitorView.Network);
            Send(new TerminalTextEvent { Text = "c" }, () => surface.ColumnsOpen);
            Send(new TerminalTextEvent { Text = " " }, () => !surface.IsColumnVisible(NetworkColumn.Interface));
            Assert.IsTrue(surface.ColumnSettings.Contains(ProcessColumn.Name));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => !surface.ColumnsOpen);
            Send(new TerminalTextEvent { Text = "4" }, () => surface.View == MonitorView.Gpu);
            Send(new TerminalTextEvent { Text = "c" }, () => surface.ColumnsOpen);
            Send(new TerminalTextEvent { Text = " " }, () => !surface.IsColumnVisible(GpuColumn.Device));
            Assert.IsTrue(surface.IsColumnVisible(NetworkColumn.Receive));
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => !surface.ColumnsOpen);
            Send(new TerminalResizeEvent { Size = new TerminalSize(35, 12) });
            Send(new TerminalTextEvent { Text = "q" }, () => surface.ExitRequested);
            Assert.IsTrue(surface.ExitRequested);
        }
        finally { End(); }
    }
}
