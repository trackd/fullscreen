using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class UserViewTests {
    [TestMethod]
    [DataRow(ProcessSort.Cpu, false, 12)]
    [DataRow(ProcessSort.Cpu, true, 11)]
    [DataRow(ProcessSort.Memory, false, 12)]
    [DataRow(ProcessSort.Memory, true, 11)]
    [DataRow(ProcessSort.Name, true, 11)]
    [DataRow(ProcessSort.Id, true, 11)]
    public void ExpandedProcessesSortInsideTheirAccounts(ProcessSort sort, bool ascending, int firstId) {
        var frame = Frame();
        var view = new UserView { Sort = sort, Ascending = ascending };
        view.Update(frame);
        view.Expand(true);
        view.SelectedKey = "user:HOST\\Bob";
        view.Expand(true);
        view.Update(frame);
        Assert.AreEqual("HOST\\Ada", view.Rows[0].UserName);
        Assert.AreEqual(firstId, view.Rows[1].Process!.Value.Id);
        Assert.AreEqual("HOST\\Bob", view.Rows[3].UserName);
        Assert.IsNull(view.Rows[3].Process);
        Assert.AreEqual(13, view.Rows[4].Process!.Value.Id);
        Assert.AreEqual(5d, view.Rows[0].CpuPercent);
        Assert.AreEqual(2, view.Rows[0].ProcessCount);
    }

    [TestMethod]
    public void UserProcessTreePreservesParentsBranchesTotalsAndAccountBoundaries() {
        var frame = Frame();
        frame.Processes = [
            frame.Processes[0],
            frame.Processes[1] with { ParentId = 11 },
            frame.Processes[2] with { ParentId = 11 },
            new(14, "nested", 8, 4096, 1, ParentId: 12, UserName: "HOST\\Ada", StartTimeTicks: 114)
        ];
        frame.ProcessCount = 4;
        var view = new UserView { Tree = true };
        view.Update(frame);
        view.Expand(true);
        view.SelectedKey = "user:HOST\\Bob";
        view.Expand(true);
        view.Update(frame);
        CollectionAssert.AreEqual(new[] { 11, 12, 14, 13 },
            view.Rows.Where(row => row.Process.HasValue).Select(row => row.Process!.Value.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 },
            view.Rows.Where(row => row.Process.HasValue).Select(row => row.Depth).ToArray());
        view.SelectedKey = "user:HOST\\Ada:12:112";
        view.Expand(false);
        view.Update(frame);
        Assert.IsFalse(view.Rows.Any(row => row.Process?.Id == 14));
        Assert.IsTrue(view.Selected!.Collapsed);
        Assert.AreEqual(3, view.Rows[0].ProcessCount);
        Assert.AreEqual(13d, view.Rows[0].CpuPercent);
        view.Expand(true);
        view.Update(frame);
        Assert.IsTrue(view.Rows.Any(row => row.Process?.Id == 14));
        view.Filter = "nested";
        view.Update(frame);
        CollectionAssert.AreEqual(new[] { 11, 12, 14 },
            view.Rows.Where(row => row.Process.HasValue).Select(row => row.Process!.Value.Id).ToArray());
    }

    [TestMethod]
    public void EmptySessionColumnsReleaseSpaceForAccountNamesAndSeparateProcessCounts() {
        var frame = Frame();
        const string account = "LONG-HOST\\Andree.Administrator";
        for (int index = 0; index < frame.UserCount; index++) {
            frame.Users[index] = frame.Users[index] with { UserName = account, LogonTime = DateTimeOffset.UnixEpoch };
        }
        for (int index = 0; index < frame.ProcessCount; index++) {
            frame.Processes[index] = frame.Processes[index] with { UserName = account };
        }
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame };
        var lines = VisualSnapshotRenderer.Render(surface, 130, 40).ToMarkupLines();
        Assert.Contains(account, lines[4]);
        Assert.Contains("PID/#", lines[3]);
        Assert.Contains("CPU %", lines[3]);
        Assert.Contains("LOGON TIME", lines[3]);
        Assert.DoesNotContain("CLIENT", lines[3]);
        Assert.DoesNotContain("IDLE TIME", lines[3]);
        Assert.DoesNotContain("(3", lines[4]);
    }

    [TestMethod]
    public async Task UserHeaderSortingAndTreeShortcutsKeepUsernameGroups() {
        var frame = Frame();
        frame.Processes[1] = frame.Processes[1] with { ParentId = 11 };
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame };
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 130, 40);
        surface.Users.Expand(true);
        surface.Refresh();
        ui.Tick();
        ui.Text("t", () => surface.Users.Tree);
        Assert.AreEqual(11, surface.Users.Rows[1].Process!.Value.Id);
        Assert.AreEqual(1, surface.Users.Rows[2].Depth);
        ui.Key(TerminalKey.F6, () => surface.Model.Sort == ProcessSort.Memory);
        Assert.AreEqual(11, surface.Users.Rows[1].Process!.Value.Id, "Tree sorting keeps parents before children.");
        ui.Text("t", () => !surface.Users.Tree);
        Assert.AreEqual(12, surface.Users.Rows[1].Process!.Value.Id);
        ui.Text("r", () => surface.Model.Ascending);
        Assert.AreEqual(11, surface.Users.Rows[1].Process!.Value.Id);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 3, 3,
            () => surface.Model.Sort == ProcessSort.Name);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 3, 3, () => true);
        Assert.AreEqual("HOST\\Ada", surface.Users.Rows[0].UserName);
        Assert.AreEqual("HOST\\Bob", surface.Users.Rows[^1].UserName);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UserMenuLogsOffOnlyTheChosenSessionAfterConfirmation(bool remote) {
        var frame = Frame();
        var logon = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
        frame.Users[0] = frame.Users[0] with {
            LogonTime = logon
        };
        frame.Users[1] = frame.Users[1] with {
            LogonTime = logon.AddHours(1)
        };
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = frame, ActionsEnabled = true, IsRemote = remote, SourceLabel = "fixture" };
        MonitorActionRequest? requested = null;
        surface.ActionRequested += request => requested = request;
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        if (remote) {
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Right, 10, 4, () => ui.Output.Contains("Log off RDP #2", StringComparison.Ordinal));
        }
        else {
            ui.Text("a", () => ui.Output.Contains("Log off RDP #2", StringComparison.Ordinal));
        }
        Assert.IsNull(requested);
        ui.Key(TerminalKey.Escape, () => ui.App.FocusedElement == surface);
        var actions = surface.UserSessionActions(surface.Users.Selected!);
        Assert.HasCount(2, actions);
        Assert.IsTrue(actions[1].IsEnabled);
        actions[1].Action.Invoke!();
        ui.Tick();
        Assert.IsNull(requested, "Opening confirmation must not log anyone off.");
        Assert.Contains("Unsaved work", ui.Output);
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        var confirm = dialog.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Confirm");
        ui.Activate(confirm, () => requested is not null);
        Assert.AreEqual(MonitorAction.LogoffSession, requested!.Action);
        Assert.AreEqual("HOST\\Ada", requested.Name);
        Assert.AreEqual(2, requested.SessionId);
        Assert.AreEqual(logon.AddHours(1).ToFileTime(), requested.SessionLogonTime);
    }
    [TestMethod]
    public void LogoffActionsRequireWindowsIdentityAndAvailableActions() {
        var frame = Frame();
        frame.Users[0] = frame.Users[0] with {
            LogonTime = DateTimeOffset.UtcNow
        };
        var surface = new MonitorSurface { Frame = frame, ActionsEnabled = true };
        var view = new UserView();
        view.Update(frame);
        Assert.IsTrue(surface.UserSessionActions(view.Selected!)[0].IsEnabled);
        Assert.IsFalse(surface.UserSessionActions(view.Selected!)[1].IsEnabled, "Unknown logon times cannot identify a session safely.");
        surface.ActionBusy = true;
        Assert.IsFalse(surface.UserSessionActions(view.Selected!)[0].IsEnabled);
        surface.ActionBusy = false;
        surface.ActionsEnabled = false;
        Assert.IsFalse(surface.UserSessionActions(view.Selected!)[0].IsEnabled);
        surface.ActionsEnabled = true;
        frame.Platform = "Linux";
        Assert.IsFalse(surface.UserSessionActions(view.Selected!)[0].IsEnabled);
    }
    [TestMethod]
    public void SessionIdentityRejectsReusedIdsAccountsAndUnknownLogonTimes() {
        var logon = DateTimeOffset.UtcNow;
        var session = new UserSessionRow("HOST\\Ada", "RDP", "Active", "", "", 4, LogonTime: logon);
        Assert.IsTrue(UserSessionSampler.MatchesSession(session, 4, "host\\ada", logon.ToFileTime()));
        Assert.IsFalse(UserSessionSampler.MatchesSession(session, 5, session.UserName, logon.ToFileTime()));
        Assert.IsFalse(UserSessionSampler.MatchesSession(session, 4, "HOST\\Bob", logon.ToFileTime()));
        Assert.IsFalse(UserSessionSampler.MatchesSession(session, 4, session.UserName, logon.AddMinutes(1).ToFileTime()));
        Assert.IsFalse(UserSessionSampler.MatchesSession(session with {
            LogonTime = null
        }, 4, session.UserName, logon.ToFileTime()));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LogoffExecutionRejectsAnAbsentSessionOnTheSelectedSource(bool remote) {
        // This ID cannot identify a real WTS session; exercise dispatch and the
        // native inventory check without ever reaching WTSLogoffSession.
        var request = new MonitorActionRequest(MonitorAction.LogoffSession, "fixture-does-not-exist") {
            SessionId = int.MaxValue,
            SessionLogonTime = DateTimeOffset.UtcNow.ToFileTime()
        };
        string expected = OperatingSystem.IsWindows() ? "Session identity changed" : "Windows targets only";
        if (!remote) {
            if (OperatingSystem.IsWindows()) {
                var error = Assert.ThrowsExactly<InvalidOperationException>(() => MonitorActions.ExecuteLocal(request, CancellationToken.None));
                Assert.Contains(expected, error.Message);
            }
            else {
                Assert.ThrowsExactly<PlatformNotSupportedException>(() => MonitorActions.ExecuteLocal(request, CancellationToken.None));
            }
            return;
        }
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2());
        runspace.Open();
        using var source = new RemoteMonitorSource(runspace, "fixture-target");
        source.Sample(new CollectorFrame(), CollectorMetrics.None, CancellationToken.None);
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => source.Execute(request, CancellationToken.None));
        Assert.Contains(expected, failure.Message);
    }
    [TestMethod]
    [DataRow(false, 120)]
    [DataRow(true, 160)]
    [DataRow(true, 120)]
    public void UserTableShowsClientIdleAndLogonWithoutOpeningDetails(bool processes, int width) {
        var frame = Frame();
        var logon = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
        frame.Users[0] = frame.Users[0] with {
            ClientName = "laptop",
            IdleTime = TimeSpan.FromMinutes(17),
            LogonTime = logon
        };
        var surface = new MonitorSurface { View = MonitorView.Users, IncludeUserProcesses = processes, Frame = frame, MinHeight = 20, MaxHeight = 20 };
        string output = string.Join('\n', VisualSnapshotRenderer.Render(surface, width, 20).ToMarkupLines());
        Assert.Contains("CLIENT", output);
        Assert.Contains("IDLE TIME", output);
        Assert.Contains("LOGON TIME", output);
        Assert.Contains("laptop", output);
        Assert.Contains("0.00:17:00", output);
        Assert.Contains(logon.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), output);
    }
    [TestMethod]
    public void SessionOnlyUsersIgnoreStaleProcessesAndSearchClientDetails() {
        var frame = Frame();
        frame.Users[0] = frame.Users[0] with {
            ClientName = "workstation-7",
            LogonTime = DateTimeOffset.Parse("2026-10-29T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            IdleTime = TimeSpan.FromMinutes(17)
        };
        var view = new UserView { IncludeProcesses = false };
        view.Update(frame);
        Assert.HasCount(1, view.Rows);
        Assert.AreEqual(0, view.Selected!.ProcessCount);
        Assert.AreEqual(0, view.Selected.CpuPercent);
        Assert.HasCount(2, view.Selected.Logins);
        view.Expand(true);
        view.Update(frame);
        Assert.HasCount(1, view.Rows);
        view.Filter = "workstation-7";
        view.Update(frame);
        Assert.HasCount(1, view.Rows);
        Assert.AreEqual(TimeSpan.FromMinutes(17), view.Selected!.Logins[0].IdleTime);
        view.IncludeProcesses = true;
        view.Filter = "";
        view.Update(frame);
        view.Expand(true);
        view.Update(frame);
        Assert.HasCount(4, view.Rows);
        view.IncludeProcesses = false;
        view.Update(frame);
        Assert.HasCount(1, view.Rows);
        Assert.IsNull(view.Selected!.Process);
    }
    [TestMethod]
    public void DetailedSessionTimesSurviveRemoteTransportIncludingUnknownValues() {
        var original = Frame();
        var utc = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        original.Users[0] = original.Users[0] with {
            LogonTime = utc,
            IdleTime = TimeSpan.Zero,
            LastInputTime = utc.AddHours(1),
            ConnectTime = utc.AddMinutes(5),
            DisconnectTime = utc.AddMinutes(45),
            ClientName = "laptop",
            Protocol = "RDP"
        };
        var copy = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(original), copy);
        Assert.AreEqual(original.Users[0], copy.Users[0]);
        Assert.AreEqual(original.Users[1], copy.Users[1]);
        Assert.AreEqual(TimeSpan.Zero, copy.Users[0].IdleTime);
        Assert.IsNull(copy.Users[1].IdleTime);
        Assert.IsNull(copy.Users[1].LogonTime);
        Assert.IsNull(UserSessionSampler.FileTime(0));
        Assert.IsNull(UserSessionSampler.FileTime(long.MaxValue));
        Assert.AreEqual(utc, UserSessionSampler.FileTime(utc.ToFileTime()));
        Assert.IsNull(UserSessionSampler.IdleDuration(null, utc));
        Assert.IsNull(UserSessionSampler.IdleDuration(utc.AddSeconds(1), utc));
        Assert.AreEqual(TimeSpan.FromMinutes(5), UserSessionSampler.IdleDuration(utc, utc.AddMinutes(5)));
    }
    [TestMethod]
    public void UnixDetailedSessionsKeepAmbiguousDatesAndUnknownIdleHonest() {
        var frame = new CollectorFrame();
        UserSessionSampler.ParseWho("ada pts/1 2026-10-29 10:20 01:17 1234 (remote)\nada ttys002 Oct 29 10:21 . 5678\nbob pts/2 2026-10-29 08:10 old 99\nbroken\n", frame, detailed: true);
        Assert.AreEqual(3, frame.UserCount);
        Assert.AreEqual("remote", frame.Users[0].Host);
        Assert.AreEqual(TimeSpan.FromMinutes(77), frame.Users[0].IdleTime);
        Assert.AreEqual("2026-10-29 10:20", frame.Users[0].LoginTime);
        Assert.AreEqual("Oct 29 10:21", frame.Users[1].LoginTime);
        Assert.IsNull(frame.Users[1].LogonTime);
        Assert.AreEqual(TimeSpan.Zero, frame.Users[1].IdleTime);
        Assert.IsNull(frame.Users[2].IdleTime);
    }
    [TestMethod]
    public async Task StandaloneUsersToggleProcessDemandAndOpenSessionDetailsWithRealInput() {
        var surface = new MonitorSurface { UsersOnly = true, IncludeUserProcesses = false, Frame = Frame() };
        surface.Frame.Users[0] = surface.Frame.Users[0] with {
            ClientName = "test-laptop",
            Protocol = "RDP",
            IdleTime = TimeSpan.FromMinutes(17)
        };
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        Assert.AreEqual(CollectorMetrics.Users, surface.RequiredMetrics);
        Assert.Contains("test-laptop", ui.Output);
        ui.Text("1", () => surface.View == MonitorView.Users);
        Assert.AreEqual(CollectorMetrics.Users, surface.RequiredMetrics);
        ui.Text("d", () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.Contains("Last input", ui.Output);
        Assert.Contains("0.00:17:00", ui.Output);
        var close = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Close");
        ui.Activate(close, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        ui.App.Focus(surface);
        ui.Text("l", () => surface.IncludeUserProcesses);
        Assert.AreEqual(CollectorMetrics.Users | CollectorMetrics.Processes | CollectorMetrics.Owners, surface.RequiredMetrics);
        ui.Text("+", () => surface.Users.IsExpanded("user:HOST\\Ada"));
        ui.Key(TerminalKey.Down, () => surface.Users.Selected?.Process.HasValue == true);
        ui.Text("l", () => !surface.IncludeUserProcesses);
        Assert.AreEqual(CollectorMetrics.Users, surface.RequiredMetrics);
        Assert.IsNull(surface.Users.Selected!.Process);
    }
    [TestMethod]
    public void ExpandedUsersRenderProcessRowsWithoutBackgroundCharts() {
        var surface = new MonitorSurface { View = MonitorView.Users, Frame = Frame(), MinHeight = 30, MaxHeight = 30 };
        VisualSnapshotRenderer.Render(surface, 120, 30);
        surface.Users.Expand(true);
        surface.Refresh();
        string snapshot = string.Join('\n', VisualSnapshotRenderer.Render(surface, 120, 30).ToMarkupLines());
        Assert.Contains("HOST\\Ada", snapshot);
        Assert.Contains("editor", snapshot);
        Assert.Contains("shell", snapshot);
        Assert.IsFalse(surface.EnumerateVisualsDepthFirst().OfType<Charts.BuiltInTrend>().Any(chart => chart.Bounds.Width > 0 && chart.Bounds.Height > 0));
    }
    [TestMethod]
    public void CollectorHandlesAreIndependentIdempotentAndExplicitlyDisposed() {
        string first = Guid.NewGuid().ToString("N"), second = Guid.NewGuid().ToString("N");
        try {
            CollectorSessions.Open(first);
            CollectorSessions.Open(second);
            CollectorSessions.Open(first);
            Assert.IsTrue(CollectorSessions.IsOpen(first));
            Assert.IsTrue(CollectorSessions.IsOpen(second));
            var frame = new CollectorFrame();
            FrameCodec.Decode(Convert.FromBase64String(CollectorSessions.CaptureBase64(first, 0)), frame);
            Assert.AreEqual(0, frame.ProcessCount);
            Assert.AreEqual(0, frame.UserCount);
            CollectorSessions.Close(first);
            CollectorSessions.Close(first);
            Assert.IsFalse(CollectorSessions.IsOpen(first));
            Assert.IsTrue(CollectorSessions.IsOpen(second));
            Assert.ThrowsExactly<InvalidOperationException>(() => CollectorSessions.CaptureBase64(first, 0));
            FrameCodec.Decode(Convert.FromBase64String(CollectorSessions.CaptureBase64(second, 0)), frame);
            Assert.AreEqual(Environment.MachineName, frame.MachineName);
            Assert.ThrowsExactly<ArgumentException>(() => CollectorSessions.Open("not-a-handle"));
        }
        finally { CollectorSessions.Close(first); CollectorSessions.Close(second); }
        Assert.IsFalse(CollectorSessions.IsOpen(second));
    }
    [TestMethod]
    public void SelectingAndLeavingUsersChangesCollectorDemandOnTheSourceWorker() {
        var source = new UserSource();
        using var app = new MonitorApp(60, 60000, "", ProcessSort.Cpu, MonitorView.Network, source, minimal: true);
        var surface = (MonitorSurface)app.Root;
        Assert.IsTrue(SpinWait.SpinUntil(() => source.Requests.Contains(CollectorMetrics.Network), 3000));
        Assert.IsFalse(source.Requests.Any(mask => mask.HasFlag(CollectorMetrics.Users)));
        surface.ShowUserSessions();
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.Frame.UserCount == 2; }, 3000));
        Assert.AreEqual("fixture target", surface.Frame.MachineName);
        Assert.AreEqual("HOST\\Ada", surface.Frame.Users[0].UserName);
        surface.View = MonitorView.Network;
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return source.Requests.Last() == CollectorMetrics.Network && surface.Frame.UserCount == 0; }, 3000));
    }
    private sealed class UserSource : IMonitorSource {
        public System.Collections.Concurrent.ConcurrentQueue<CollectorMetrics> Requests { get; } = new();
        public string Label => "fixture target";
        public bool IsRemote => true;
        public void Sample(CollectorFrame destination, CancellationToken cancellationToken) =>
            Sample(destination, CollectorMetrics.All, cancellationToken);
        public void Sample(CollectorFrame destination, CollectorMetrics metrics, CancellationToken cancellationToken) {
            Requests.Enqueue(metrics);
            destination.CopyFrom(metrics.HasFlag(CollectorMetrics.Users) ? Frame() : new CollectorFrame());
            destination.MachineName = Label;
        }
        public void Dispose() {
        }
    }
    private static CollectorFrame Frame() =>
        new() {
            Platform = "Windows",
            UserCount = 2,
            ProcessCount = 3,
            Users = [new("HOST\\Ada", "Console", "Active", "", "", 1), new("HOST\\Ada", "RDP", "Disconnected", "", "", 2)],
            Processes = [new(11, "editor", 2, 1024, 1, UserName: "HOST\\Ada", StartTimeTicks: 111),
            new(12, "shell", 3, 2048, 1, UserName: "host\\ada", StartTimeTicks: 112), new(13, "other", 1, 512, 1, UserName: "HOST\\Bob", StartTimeTicks: 113)]
        };
    [TestMethod]
    public void AccountsAggregateSessionsAndExpandOnlyTheirOwnProcessesWithStableSelection() {
        var frame = Frame();
        var view = new UserView();
        view.Update(frame);
        Assert.HasCount(2, view.Rows);
        var ada = view.Rows[0];
        Assert.AreEqual(2, ada.ProcessCount);
        Assert.AreEqual(5, ada.CpuPercent);
        Assert.AreEqual(3072, ada.WorkingSet);
        Assert.Contains("Console #1", ada.Sessions);
        Assert.Contains("RDP #2", ada.Sessions);
        view.Expand(true);
        view.Update(frame);
        Assert.HasCount(4, view.Rows);
        Assert.AreEqual(12, view.Rows[1].Process!.Value.Id);
        view.Move(1, 10);
        string? selected = view.SelectedKey;
        frame.Processes[1] = frame.Processes[1] with {
            CpuPercent = 1
        };
        view.Update(frame);
        Assert.AreEqual(selected, view.SelectedKey);
        view.Expand(false);
        view.Update(frame);
        Assert.AreEqual(ada.Key, view.SelectedKey);
        Assert.HasCount(2, view.Rows);
        view.Filter = "editor";
        view.Update(frame);
        Assert.HasCount(2, view.Rows);
        Assert.AreEqual(11, view.Rows[1].Process!.Value.Id);
        view.Filter = "missing";
        view.Update(frame);
        Assert.HasCount(0, view.Rows);
    }
    [TestMethod]
    public void SessionRecordsRoundTripThroughRemoteFramesAndClearWhenNotRequested() {
        var original = Frame();
        original.UserStatus = "fixture target";
        var copy = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(original), copy);
        Assert.AreEqual(original.Users[1], copy.Users[1]);
        Assert.AreEqual(2, copy.UserCount);
        var snapshot = new CollectorFrame();
        snapshot.CopyFrom(copy);
        Assert.AreEqual(original.Users[0], snapshot.Users[0]);
        Assert.AreEqual("fixture target", snapshot.UserStatus);
        copy.CopyFrom(new CollectorFrame());
        Assert.AreEqual(0, copy.UserCount);
        using var collector = new PerformanceCollector();
        var idle = collector.Capture(CollectorMetrics.None);
        Assert.AreEqual(0, idle.UserCount);
        Assert.Contains("Not requested", idle.UserStatus);
    }
    [TestMethod]
    public void WhoReturnsStructuredFieldsWithoutGuessingLocaleSpecificDates() {
        var frame = new CollectorFrame();
        UserSessionSampler.ParseWho("ada pts/1 2026-10-29 10:20 (remote)\nada ttys002 Oct 29 10:21\nmalformed\n", frame);
        Assert.AreEqual(2, frame.UserCount);
        Assert.AreEqual(new UserSessionRow("ada", "pts/1", "Logged in", "remote", "2026-10-29 10:20"), frame.Users[0]);
        Assert.AreEqual("Oct 29 10:21", frame.Users[1].LoginTime);
        Assert.IsNull(frame.Users[1].SessionId);
        var empty = new CollectorFrame();
        UserSessionSampler.ParseWho("\n", empty);
        Assert.AreEqual(0, empty.UserCount);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UsersLoadOnlyOnSelectionAndExpandedProcessesReuseConfirmedActions(bool remote) {
        var surface = new MonitorSurface { IsRemote = remote, ActionsEnabled = true, SourceLabel = "fixture", Frame = Frame() };
        Assert.AreEqual(CollectorMetrics.Processes, new MonitorSurface { Minimal = true }.RequiredMetrics);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Key(TerminalKey.F8, () => surface.View == MonitorView.Users);
        Assert.AreEqual(CollectorMetrics.Users | CollectorMetrics.Processes | CollectorMetrics.Owners, surface.RequiredMetrics);
        surface.Users.Expand(true);
        ui.Tick();
        ui.Key(TerminalKey.Down, () => surface.Users.Selected?.Process.HasValue == true);
        var selected = surface.Users.Selected!.Process!.Value;
        MonitorActionRequest? requested = null;
        surface.ActionRequested += request => requested = request;
        ui.Key(TerminalKey.Enter, () => ui.Output.Contains("Stop process", StringComparison.Ordinal));
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsNull(requested, "Opening confirmation must not stop a process.");
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        var confirm = dialog.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Confirm");
        ui.Activate(confirm, () => requested is not null);
        Assert.AreEqual(selected.Id, requested!.ProcessId);
        Assert.AreEqual(selected.StartTimeTicks, requested.StartTimeTicks);
        surface.ActionsEnabled = false;
        Assert.IsFalse(surface.ProcessActions(selected).Single().IsEnabled);
    }
}
