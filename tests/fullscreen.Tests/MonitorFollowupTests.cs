using System.Collections.Concurrent;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorFollowupTests {
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static TimeSeries History(MonitorSurface surface, string name) =>
        (TimeSeries)typeof(MonitorSurface).GetField(name, Private)!.GetValue(surface)!;
    private static Button Button(DesignerUxTests.UiSession ui, string label) =>
        ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single()
            .EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock block && block.Text == label);
    private static bool Wait(DesignerUxTests.UiSession ui, Func<bool> predicate) =>
        SpinWait.SpinUntil(() => { ui.Tick(); return predicate(); }, 5000);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DashboardBackgroundSamplingKeepsHistoriesCurrentAndStillPauses(bool enabled) {
        var source = new FixtureSource();
        using var app = new MonitorApp(60, 100, "", ProcessSort.Cpu, MonitorView.Services,
            source, minimal: true, backgroundSampling: enabled);
        var surface = (MonitorSurface)app.Root;
        await using var ui = new DesignerUxTests.UiSession(surface, () => app.Tick());
        Assert.IsTrue(Wait(ui, () => source.Samples.Count >= 3 && surface.Frame.ServiceCount == 1));
        const CollectorMetrics summary = CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu;
        Assert.AreEqual(enabled ? CollectorMetrics.Services | summary : CollectorMetrics.Services, surface.RequiredMetrics);
        foreach (string name in new[] { "_cpuHistory", "_memoryHistory", "_receiveHistory", "_sendHistory", "_gpuHistory", "_vramHistory" }) {
            var history = History(surface, name);
            Assert.AreEqual(enabled, history.Count >= 2, name);
            if (enabled) {
                Assert.IsNotNull(history.ValueAt(history.Count - 1), name);
            }
        }
        Assert.IsTrue(source.Samples.All(metrics => (metrics & (CollectorMetrics.Processes | CollectorMetrics.Users | CollectorMetrics.Owners)) == 0));
        ui.App.Focus(surface);
        ui.Text("p", () => surface.Paused);
        Assert.AreEqual(CollectorMetrics.None, surface.RequiredMetrics);
        int count = History(surface, "_cpuHistory").Count;
        for (int tick = 0; tick < 30; tick++) { ui.Tick(); }
        Assert.AreEqual(count, History(surface, "_cpuHistory").Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EveryProcessColumnIsReachableAndWidthsIgnoreSampleContents(bool tree) {
        var surface = new MonitorSurface { Minimal = true };
        surface.ColumnSettings.Configure(Enum.GetValues<ProcessColumn>(), null, tree);
        surface.Model.Tree = tree;
        surface.Frame.ProcessCount = 1;
        surface.Frame.Processes[0] = new(7, "short", 4, 2L << 30, 2, UserName: "Ada");
        surface.Model.Update(surface.Frame.Processes.AsSpan(0, 1));
        string Layout() => string.Join("\n", ((Array)typeof(MonitorSurface).GetField("_columns", Private)!.GetValue(surface)!).Cast<object>());
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 80, 30);
        string original = Layout();
        surface.SetFrame(new CollectorFrame { ProcessCount = 1, Processes = [
            new(9999999, new string('n', 200), 99, 900L << 30, 1000, UserName: new string('u', 100),
                NetworkBytesPerSecond: 1000000000)
        ] });
        surface.Model.Update(surface.Frame.Processes.AsSpan(0, 1));
        ui.Tick();
        Assert.AreEqual(original, Layout(), "Refresh must not change column widths or hide missing counters.");
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 79, 0, () => surface.FirstProcessColumn == 1);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 79, 0, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 0, 0, () => surface.FirstProcessColumn == 0);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 0, 0, () => true);
        ui.App.Focus(surface);
        for (int index = 1; index < Enum.GetValues<ProcessColumn>().Length; index++) {
            int expected = index;
            ui.Key(TerminalKey.Right, () => surface.FirstProcessColumn == expected, tree ? TerminalModifiers.Shift : default);
        }
        Assert.Contains("NET/s", ui.Output);
        ui.Key(TerminalKey.Left, () => surface.FirstProcessColumn == Enum.GetValues<ProcessColumn>().Length - 2,
            tree ? TerminalModifiers.Shift : default);
    }

    [TestMethod]
    public void ProcessDefaultsAndScaledUnitsFitTheRequestedOrder() {
        CollectionAssert.AreEqual(new[] {
            ProcessColumn.Name, ProcessColumn.UserName, ProcessColumn.Cpu, ProcessColumn.Memory,
            ProcessColumn.Network, ProcessColumn.Gpu, ProcessColumn.Threads, ProcessColumn.Id
        }, ProcessColumns.Defaults.ToArray());
        var surface = new MonitorSurface { Minimal = true };
        surface.Model.Update([new(42, "fixture", 10, 8L << 30, 4, NetworkBytesPerSecond: 12L << 20)]);
        string output = string.Join("\n", VisualSnapshotRenderer.Render(surface, 140, 30).ToMarkupLines());
        Assert.Contains("8.0 GiB", output);
        Assert.Contains("12.0 MiB/s", output);
        Assert.DoesNotContain("STATUS", output);
        Assert.Contains("PID", output);
    }

    [TestMethod]
    public async Task MarkAllAndPerSessionSelectionRequireConfirmationAndReportIndividualFailures() {
        var source = new FixtureSource { FailFirstLogoff = true };
        using var app = new MonitorApp(60, 60000, "", ProcessSort.Cpu, MonitorView.Users, source);
        var surface = (MonitorSurface)app.Root;
        await using var ui = new DesignerUxTests.UiSession(surface, () => app.Tick(), 120, 40);
        Assert.IsTrue(Wait(ui, () => surface.Users.Rows.Count == 1));
        ui.App.Focus(surface);
        ui.Text(" ", () => surface.MarkedSessionCount == 2);
        ui.Text("m", () => surface.MarkedSessionCount == 0);
        ui.Text("x", () => ui.Output.Contains("Select sessions to log off", StringComparison.Ordinal));
        Assert.IsEmpty(source.Actions);
        ui.Activate(Button(ui, "Mark all"), () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<CheckBox>().All(check => check.IsChecked));
        var checks = ui.App.Root.EnumerateVisualsDepthFirst().OfType<CheckBox>().ToArray();
        Assert.HasCount(2, checks);
        checks[0].IsChecked = false;
        ui.Activate(Button(ui, "Log off selected…"), () => ui.Output.Contains("Confirm logoff · 1 sessions", StringComparison.Ordinal));
        Assert.IsEmpty(source.Actions);
        Assert.Contains("fixture-target", ui.Output);
        Assert.Contains("Ada", ui.Output);
        ui.Key(TerminalKey.Escape, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsEmpty(source.Actions, "Cancel must never log off a session.");
        surface.ShowSessionSelection();
        ui.Tick();
        ui.Activate(Button(ui, "Mark all"), () => true);
        ui.Activate(Button(ui, "Log off selected…"), () => ui.Output.Contains("Confirm logoff · 2 sessions", StringComparison.Ordinal));
        Assert.IsEmpty(source.Actions);
        ui.Activate(Button(ui, "Confirm logoff"), () => source.Actions.Count == 2 && !surface.ActionBusy);
        Assert.IsTrue(source.Actions.All(request => request.Action == MonitorAction.LogoffSession && request.SessionLogonTime > 0));
        Assert.Contains("fixture stale identity", surface.ActionMessage);
        Assert.Contains("LogoffSession requested · Ada #8", surface.ActionMessage);
        Assert.Contains("Session logoff results", ui.Output);
        for (int i = 0; i < 10; i++) { ui.Tick(); }
        Assert.HasCount(2, source.Actions, "Failed logoffs are not retried.");
    }

    [TestMethod]
    public async Task SessionMarksAreRemovedWhenTheLogonIdentityChanges() {
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = new CollectorFrame { Platform = "Windows", UserCount = 1 } };
        surface.Frame.Users[0] = new("Ada", "Console", "Active", "", "", 7, DateTimeOffset.UnixEpoch);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        surface.MarkAllSessions();
        Assert.AreEqual(1, surface.MarkedSessionCount);
        surface.Frame.Users[0] = surface.Frame.Users[0] with { LogonTime = DateTimeOffset.UnixEpoch.AddDays(1) };
        surface.Refresh();
        ui.Tick();
        Assert.AreEqual(0, surface.MarkedSessionCount);
    }

    [TestMethod]
    [DataRow(80)]
    [DataRow(140)]
    public void UserColumnLayoutUsesOnlyVisibleAccountMetadata(int width) {
        var frame = new CollectorFrame { Platform = "Windows", UserCount = 2 };
        frame.Users[0] = new("Ada", "Console", "Active", "client-host", "", 7,
            DateTimeOffset.UnixEpoch, TimeSpan.Zero);
        frame.Users[1] = new("Bob", "RDP", "Disconnected", "", "", 8);
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame };
        surface.Model.Filter = "Bob";
        VisualSnapshotRenderer.Render(surface, width, 30);
        var layout = typeof(MonitorSurface).GetMethod("UserColumnLayout", Private)!;
        void CheckMetadata(bool shown) {
            var columns = layout.Invoke(surface, null)!;
            foreach (string name in new[] { "Logon", "Idle", "Client" }) {
                int position = (int)columns.GetType().GetProperty(name)!.GetValue(columns)!;
                Assert.AreEqual(shown, position >= 0, name);
            }
        }
        CheckMetadata(false);
        surface.Model.Filter = "";
        surface.Refresh();
        VisualSnapshotRenderer.Render(surface, width, 30);
        CheckMetadata(width >= 110);
        surface.IncludeUserProcesses = false;
        surface.Refresh();
        VisualSnapshotRenderer.Render(surface, width, 30);
        CheckMetadata(width >= 110);
    }

    [TestMethod]
    public void SessionMarkPruningDoesNotAllocateWhenNothingIsMarked() {
        var surface = new MonitorSurface { Frame = new CollectorFrame { Platform = "Windows", UserCount = 1 } };
        surface.Frame.Users[0] = new("Ada", "Console", "Active", "", "", 7, DateTimeOffset.UnixEpoch);
        var prune = typeof(MonitorSurface).GetMethod("PruneSessionMarks", Private)!.CreateDelegate<Action>(surface);
        prune();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) {
            prune();
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - start);
    }

    [TestMethod]
    [DataRow(40, 12)]
    [DataRow(80, 18)]
    public async Task SessionPickerAndConfirmationKeepActionButtonsInsideShortTerminals(int width, int height) {
        var source = new FixtureSource();
        var frame = new CollectorFrame();
        source.Sample(frame, CancellationToken.None);
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame, ActionsEnabled = true };
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, width, height);
        surface.ShowSessionSelection();
        ui.Tick();
        foreach (var check in ui.App.Root.EnumerateVisualsDepthFirst().OfType<CheckBox>()) { check.IsChecked = true; }
        var select = Button(ui, "Log off selected…");
        Assert.IsLessThanOrEqualTo(height, select.Bounds.Bottom);
        ui.Activate(select, () => Button(ui, "Confirm logoff").Bounds.Width > 0);
        var confirm = Button(ui, "Confirm logoff");
        Assert.IsGreaterThanOrEqualTo(0, confirm.Bounds.Y);
        Assert.IsLessThanOrEqualTo(height, confirm.Bounds.Bottom);
    }

    [TestMethod]
    public async Task ClosedProcessDialogsIgnoreStaleResultsAndSanitizeTerminalControls() {
        var requests = new List<long>();
        var surface = new MonitorSurface();
        surface.ProcessDetailRequested += (id, _) => requests.Add(id);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        var row = new ProcessRow(42, "fixture", 0, 1024, 1);
        surface.ShowProcessDetails(row);
        ui.Tick();
        ui.Key(TerminalKey.Escape, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        surface.ShowProcessDetails(row);
        ui.Tick();
        surface.CompleteProcessDetails(requests[0], "stale");
        var text = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single(block => block.Text == "Loading command line…");
        surface.CompleteProcessDetails(requests[1], "fixture.exe --argument value\u001b[31m");
        Assert.Contains("--argument value", text.Text!);
        Assert.DoesNotContain("\u001b", text.Text!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProcessDetailsLoadOnWorkerAndHandleDeniedOrLongCommandLines(bool denied) {
        var source = new FixtureSource { DenyDetails = denied };
        using var app = new MonitorApp(60, 60000, "", ProcessSort.Cpu, MonitorView.Processes, source);
        var surface = (MonitorSurface)app.Root;
        await using var ui = new DesignerUxTests.UiSession(surface, () => app.Tick(), 80, 30);
        Assert.IsTrue(Wait(ui, () => surface.Model.Count == 1));
        ui.App.Focus(surface);
        ui.Text("d", () => ui.Output.Contains("COMMAND LINE", StringComparison.Ordinal));
        Assert.IsTrue(Wait(ui, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>()
            .Any(block => block.Text?.Contains(denied ? "fixture permission denied" : "[Truncated", StringComparison.Ordinal) == true)));
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, source.DetailThread);
        var command = ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBlock>()
            .Single(block => block.Text?.Contains(denied ? "fixture permission denied" : "[Truncated", StringComparison.Ordinal) == true);
        Assert.IsTrue(command.Wrap);
        Assert.IsLessThanOrEqualTo(32820, command.Text!.Length);
        ui.Text("q", () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsFalse(surface.ExitRequested);
        Assert.IsTrue(app.Tick());
        Assert.IsEmpty(source.Actions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CommandLineLookupUsesTheCollectorAndRejectsChangedOrUnverifiedIdentities(bool remote) {
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(
            System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2());
        runspace.Open();
        using IMonitorSource source = remote ? new RemoteMonitorSource(runspace, "in-process") : new LocalMonitorSource();
        var frame = new CollectorFrame();
        source.Sample(frame, CollectorMetrics.Processes, CancellationToken.None);
        var row = frame.Processes.Take(frame.ProcessCount).Single(process => process.Id == Environment.ProcessId);
        var details = (IMonitorProcessDetailSource)source;
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Contains(current.ProcessName, details.ReadCommandLine(row, CancellationToken.None));
        var changed = Assert.Throws<Exception>(() => details.ReadCommandLine(row with { StartTimeTicks = row.StartTimeTicks + 1 }, CancellationToken.None));
        Assert.Contains("identity changed", changed.Message);
        var missing = Assert.Throws<Exception>(() => details.ReadCommandLine(row with { StartTimeTicks = 0 }, CancellationToken.None));
        Assert.Contains("Process start identity is unavailable", missing.Message);
    }

    [TestMethod]
    public void MacArgumentsExcludeEnvironmentAndPreserveEmptyArguments() {
        var data = BitConverter.GetBytes(4).Concat(System.Text.Encoding.UTF8.GetBytes(
            "/usr/bin/fixture\0\0fixture\0--name\0\0two words\0TOKEN=not-an-argument\0")).ToArray();
        string commandLine = ProcessOwnerReader.ParseMacCommandLine(data);
        Assert.AreEqual("fixture --name  two words", commandLine);
        Assert.DoesNotContain("TOKEN", commandLine);
        Assert.Throws<InvalidDataException>(() => ProcessOwnerReader.ParseMacCommandLine(data[..12]));
    }

    private sealed class FixtureSource : IMonitorSource, IMonitorControlSource, IMonitorProcessDetailSource {
        public string Label => "fixture-target";
        public bool IsRemote => true;
        public bool FailFirstLogoff, DenyDetails;
        public int DetailThread;
        private int _sample;
        public ConcurrentQueue<MonitorActionRequest> Actions { get; } = new();
        public ConcurrentQueue<CollectorMetrics> Samples { get; } = new();
        public void Sample(CollectorFrame frame, CancellationToken token) => Sample(frame, CollectorMetrics.All, token);
        public void Sample(CollectorFrame frame, CollectorMetrics metrics, CancellationToken token) {
            Samples.Enqueue(metrics);
            frame.CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref _sample));
            frame.Platform = "Windows";
            frame.CpuPercent = 20;
            frame.TotalMemory = 16UL << 30;
            frame.AvailableMemory = 8UL << 30;
            frame.ReceiveBytesPerSecond = 1024;
            frame.SendBytesPerSecond = 512;
            frame.GpuCount = 1;
            frame.Gpus[0] = new("Fixture GPU", "fixture", 20, 1000, 2000, null);
            frame.ServiceCount = 1;
            frame.Services[0] = new("service", "Fixture", "Running", "", 0);
            frame.UserCount = 2;
            frame.Users[0] = new("Ada", "Console", "Active", "", "", 7, DateTimeOffset.UnixEpoch);
            frame.Users[1] = new("Ada", "RDP", "Disconnected", "", "", 8, DateTimeOffset.UnixEpoch.AddDays(1));
            frame.ProcessCount = 1;
            frame.Processes[0] = new(42, "fixture", 20, 1024, 2, UserName: "Ada", StartTimeTicks: 99);
        }
        public void Execute(MonitorActionRequest request, CancellationToken token) {
            Actions.Enqueue(request);
            if (FailFirstLogoff && request.SessionId == 7) {
                throw new InvalidOperationException("fixture stale identity");
            }
        }
        public string ReadCommandLine(ProcessRow process, CancellationToken token) {
            DetailThread = Environment.CurrentManagedThreadId;
            if (DenyDetails) {
                throw new UnauthorizedAccessException("fixture permission denied");
            }
            return "fixture.exe --argument value " + new string('x', 40000);
        }
        public void Dispose() { }
    }
}
