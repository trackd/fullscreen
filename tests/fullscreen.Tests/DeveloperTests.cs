using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using fullscreen.Development;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace fullscreen.Tests;

[TestClass]
public sealed partial class DeveloperTests {
    [TestMethod]
    public void ParsesNullDelimitedStatusAndRenamesWithoutQuotingOrPathspecExpansion() {
        var changes = GitBackend.ParseStatus("## main...origin/main\0 M path with spaces.txt\0R  renamed[1].txt\0old\nname.txt\0?? :(glob)*.txt\0");
        Assert.HasCount(3, changes);
        Assert.AreEqual("path with spaces.txt", changes[0].Path);
        Assert.AreEqual("renamed[1].txt", changes[1].Path);
        Assert.AreEqual("old\nname.txt", changes[1].OldPath);
        Assert.AreEqual(":(glob)*.txt", changes[2].Path);
    }
    [TestMethod]
    public async Task GitStagesUnstagesCommitsAndReadsHistoryInAnIsolatedRepository() {
        using var repo = new GitFixture();
        await repo.Init();
        string path = "file [1] -- test.txt";
        File.WriteAllText(Path.Combine(repo.Root, path), "first\n");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        Assert.IsEmpty((await backend.LoadAsync(2, 0, default)).Items);
        var changes = await backend.LoadAsync(0, 0, default);
        Assert.HasCount(1, changes.Items);
        await backend.Operations(0, changes.Items[0]).Single(action => action.Label == "Stage file").Execute("", default);
        var staged = await backend.LoadAsync(1, 0, default);
        Assert.HasCount(1, staged.Items);
        await backend.Operations(1, staged.Items[0]).Single(action => action.Label == "Unstage file").Execute("", default);
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, path)));
        Assert.IsEmpty((await backend.LoadAsync(1, 0, default)).Items);
        changes = await backend.LoadAsync(0, 0, default);
        await backend.Operations(0, changes.Items[0]).Single(action => action.Label == "Stage file").Execute("", default);
        staged = await backend.LoadAsync(1, 0, default);
        await backend.Operations(1, staged.Items[0]).Single(action => action.Label.StartsWith("Commit", StringComparison.Ordinal)).Execute("initial; literal $(text)", default);
        Assert.IsEmpty((await backend.LoadAsync(0, 0, default)).Items);
        var history = await backend.LoadAsync(2, 0, default);
        Assert.HasCount(1, history.Items);
        Assert.Contains("initial; literal $(text)", await backend.DetailAsync(2, history.Items[0], default));
        File.AppendAllText(Path.Combine(repo.Root, path), "second\n");
        changes = await backend.LoadAsync(0, 0, default);
        Assert.Contains("+second", await backend.DetailAsync(0, changes.Items[0], default));
        var commit = backend.Operations(0, changes.Items[0]).Single(action => action.Label.StartsWith("Commit", StringComparison.Ordinal));
        await repo.Git("add", "--", path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commit.Execute("stale index", default));
        Assert.HasCount(1, (await backend.LoadAsync(2, 0, default)).Items);
    }
    [TestMethod]
    public async Task GitBranchCreationAndSwitchKeepChangesAndValidateBranchNames() {
        using var repo = new GitFixture();
        await repo.Init();
        File.WriteAllText(Path.Combine(repo.Root, "a.txt"), "a");
        await repo.Git("add", "a.txt");
        await repo.Git("commit", "-m", "initial");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        await backend.LoadAsync(3, 0, default);
        var create = backend.Operations(3, null).Single(action => action.Label == "Create branch…");
        await create.Execute("feature/test", default);
        var branches = await backend.LoadAsync(3, 0, default);
        Assert.IsTrue(branches.Items.Any(item => item.Key == "feature/test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => create.Execute("--bad", default));
        var main = branches.Items.Single(item => item.Key == "main");
        await backend.Operations(3, main).Single(action => action.Label == "Switch branch").Execute("", default);
        Assert.Contains("main", await repo.Git("branch", "--show-current"));
        await repo.Git("mv", "a.txt", "renamed [2].txt");
        var staged = await backend.LoadAsync(1, 0, default);
        var renamed = staged.Items.Single();
        Assert.AreEqual("a.txt", ((GitChange)renamed.State!).OldPath);
        await backend.Operations(1, renamed).Single(action => action.Label == "Stage file").Execute("", default);
        await backend.Operations(1, renamed).Single(action => action.Label == "Unstage file").Execute("", default);
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, "renamed [2].txt")));
    }
    [TestMethod]
    public async Task GitPushAndPullUseOnlyTheExplicitUpstreamWithoutForce() {
        using var repo = new GitFixture();
        await repo.Init();
        File.WriteAllText(Path.Combine(repo.Root, "a.data"), "a");
        await repo.Git("add", "a.data");
        await repo.Git("commit", "-m", "initial");
        string bare = Path.Combine(repo.Root, "remote.git");
        await repo.Git("init", "--bare", bare);
        await repo.Git("remote", "add", "origin", bare);
        await repo.Git("config", "branch.main.remote", "origin");
        await repo.Git("config", "branch.main.merge", "refs/heads/main");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        await backend.LoadAsync(3, 0, default);
        await backend.Operations(3, null).Single(action => action.Label == "Push").Execute("", default);
        Assert.AreEqual((await repo.Git("rev-parse", "HEAD")).Trim(), (await repo.Git("--git-dir", bare, "rev-parse", "refs/heads/main")).Trim());
        await backend.Operations(3, null).Single(action => action.Label == "Pull (fast-forward only)").Execute("", default);
    }
    [TestMethod]
    public async Task GitHubReviewsBindToTheInspectedHeadAndUseStdinJson() {
        var runner = new GitHubFixtureRunner();
        var backend = new GitHubBackend(Path.GetTempPath(), "owner/repo", runner);
        var page = await backend.LoadAsync(1, 0, default);
        var item = page.Items.Single();
        Assert.IsEmpty(backend.Operations(1, item));
        Assert.Contains("+new line", await backend.DetailAsync(1, item, default));
        var review = backend.Operations(1, item).Single(action => action.Label == "Approve PR…");
        await review.Execute("", default);
        var post = runner.Requests.Single(request => request.Input is not null);
        using var payload = JsonDocument.Parse(post.Input!);
        Assert.AreEqual("abc123", payload.RootElement.GetProperty("commit_id").GetString());
        Assert.AreEqual("APPROVE", payload.RootElement.GetProperty("event").GetString());
        Assert.IsTrue(post.Arguments.Contains("--input"));
        Assert.IsEmpty(backend.Operations(1, item));
    }
    [TestMethod]
    public async Task GitHubRejectsStalePullRequestReviewsAndChangedDeploymentApprovals() {
        var runner = new GitHubFixtureRunner();
        var backend = new GitHubBackend(Path.GetTempPath(), "owner/repo", runner);
        var item = (await backend.LoadAsync(1, 0, default)).Items.Single();
        await backend.DetailAsync(1, item, default);
        var approve = backend.Operations(1, item).Single(action => action.Label == "Approve PR…");
        runner.Head = "new-head";
        await Assert.ThrowsAsync<InvalidOperationException>(() => approve.Execute("", default));
        Assert.IsFalse(runner.Requests.Any(request => request.Input is not null));
        runner.Head = "abc123";
        var run = (await backend.LoadAsync(2, 0, default)).Items.Single();
        await backend.DetailAsync(2, run, default);
        var deployment = backend.Operations(2, run).Single(action => action.Label == "Approve pending environments");
        runner.Environment = 8;
        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.Execute("", default));
        Assert.IsFalse(runner.Requests.Any(request => request.Input is not null));
        runner.Environment = 7;
        await backend.DetailAsync(2, run, default);
        await backend.Operations(2, run).Single(action => action.Label == "Approve pending environments").Execute("", default);
        using var posted = JsonDocument.Parse(runner.Requests.Single(request => request.Input is not null).Input!);
        Assert.AreEqual(7L, posted.RootElement.GetProperty("environment_ids")[0].GetInt64());
        Assert.AreEqual("approved", posted.RootElement.GetProperty("state").GetString());
    }
    [TestMethod]
    public async Task GitHubWorkflowViewsShowStatusJobsArtifactsAndWorkflowHistory() {
        var runner = new GitHubFixtureRunner();
        var backend = new GitHubBackend(Path.GetTempPath(), "owner/repo", runner);
        var runs = await backend.LoadAsync(2, 0, default);
        Assert.AreEqual("✓ success", runs.Items.Single().Cells[3]);
        string runDetail = await backend.DetailAsync(2, runs.Items.Single(), default);
        Assert.Contains("✓ success", runDetail);
        Assert.Contains("✗ failure", runDetail);
        Assert.Contains("✓ success  1. Compile", runDetail);
        Assert.Contains("release-bundle · 1 KB", runDetail);
        Assert.Contains("https://api.github.com/repos/owner/repo/actions/artifacts/3/zip", runDetail);
        var runActions = backend.Operations(2, runs.Items.Single()).Select(action => action.Label).ToArray();
        Assert.Contains("Open run in browser", runActions);
        Assert.Contains("Open job · build", runActions);
        Assert.Contains("Open artifact · release-bundle", runActions);

        var workflows = await backend.LoadAsync(3, 0, default);
        string workflowDetail = await backend.DetailAsync(3, workflows.Items.Single(), default);
        Assert.Contains("WORKFLOW  ·  CI", workflowDetail);
        Assert.Contains("File: .github/workflows/ci.yml", workflowDetail);
        Assert.Contains("Open recent run · Add tests", backend.Operations(3, workflows.Items.Single()).Select(action => action.Label).ToArray());
        Assert.Contains("✓ success  Add tests", workflowDetail);
        Assert.Contains("main · push · ci-bot", workflowDetail);
        Assert.Contains("https://github.com/owner/repo/actions/runs/9", workflowDetail);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task GitHubManagerWiresStatusColorsForRunsStepsWorkflowsAndPullRequests() {
        using var app = new DeveloperApp(new GitHubBackend(Path.GetTempPath(), "owner/repo", new GitHubFixtureRunner()), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 120, 35);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        var row = app.Surface.Model.Current!;
        Assert.AreEqual(app.Surface.Palette.Success, app.Surface.RowAccent!(row));
        Assert.AreEqual(app.Surface.Palette.Success, app.Surface.CellAccent!(row, 3));
        Assert.IsNull(app.Surface.CellAccent(row, 1));
        ui.App.Focus(app.Surface);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Any(surface => surface != app.Surface && surface.Model.Rows.Count == 3));
        var monitor = ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Single(surface => surface != app.Surface);
        Assert.AreEqual(app.Surface.Palette.Error, monitor.CellAccent!(monitor.Model.Rows[2], 2));
        Assert.AreEqual(app.Surface.Palette.Error, app.Surface.DocumentAccent!("  ✗ failure  2. Test"));
        Assert.IsTrue(app.Surface.Palette.TableMuted.TryGetForeground(out var muted));
        Assert.AreEqual(muted, app.Surface.DocumentAccent("  ○ skipped  3. Publish"));
        ui.Key(TerminalKey.Backspace, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Any(surface => surface != app.Surface));
        app.SwitchView(3);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        ui.App.Focus(app.Surface);
        ui.Key(TerminalKey.Enter, () => ui.Output.Contains("RECENT RUNS", StringComparison.Ordinal));
        Assert.AreEqual(app.Surface.Palette.Success, app.Surface.DocumentAccent("✓ success  Add tests"));
        app.SwitchView(1);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        row = app.Surface.Model.Current!;
        Assert.AreEqual("Open", row.Cells[3]);
        Assert.AreEqual(app.Surface.Palette.Success, app.Surface.CellAccent(row, 3));
        Assert.AreEqual(app.Surface.Palette.Purple, app.Surface.DocumentAccent("State: Merged"));
    }

    [TestMethod]
    public async Task GitHubCommentsPreserveQuotesAndNewlinesAndUsesSelectedHost() {
        var runner = new GitHubFixtureRunner();
        var backend = new GitHubBackend(Path.GetTempPath(), "owner/repo", runner, host: "github.example.com");
        var item = (await backend.LoadAsync(0, 0, default)).Items.Single();
        string body = "quote \" and newline\n$(literal)";
        await backend.Operations(0, item).Single().Execute(body, default);
        var post = runner.Requests.Single(request => request.Input is not null);
        using var json = JsonDocument.Parse(post.Input!);
        Assert.AreEqual(body, json.RootElement.GetProperty("body").GetString());
        Assert.Contains("github.example.com", post.Arguments);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.Operations(0, item).Single().Execute(" ", default));
    }
    [TestMethod]
    public async Task RunnerHonorsPreCancellationAndTerminatesAnActiveChild() {
        var runner = new CommandRunner();
        using var before = new CancellationTokenSource();
        before.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(new("git", Path.GetTempPath(), ["--version"]), before.Token));
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(new("pwsh.exe", Path.GetTempPath(), ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"]), during.Token));
        Assert.IsLessThan(10.0, watch.Elapsed.TotalSeconds);
    }
    [TestMethod]
    public async Task RunnerReturnsWhenADetachedGrandchildHoldsThePipes() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var watch = Stopwatch.StartNew();
        // start /b leaves ping running with cmd's inherited stdout after cmd exits.
        var result = await new CommandRunner().RunAsync(new("cmd.exe", Path.GetTempPath(), ["/c", "start /b ping -n 20 127.0.0.1 & echo done"], TimeoutSeconds: 15), default);
        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("done", result.Output);
        Assert.IsLessThan(10.0, watch.Elapsed.TotalSeconds);
    }

    private static readonly int[] expected = [0];

    [TestMethod]
    [DoNotParallelize]
    public async Task PeriodicRefresh_RetainsUnchangedModelAndAppliesExternalChanges() {
        var data = new UiFixtureBackend();
        using var app = new DeveloperApp(data, minimal: true);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        var model = app.Surface.Model;
        var refresh = typeof(DeveloperApp).GetField("_lastRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        refresh.SetValue(app, 0L);
        ui.Tick();
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return data.Loaded.Count >= 2 && !app.Busy; }, 5000));
        Assert.AreSame(model, app.Surface.Model);
        data.Context = "externally changed";
        refresh.SetValue(app, 0L);
        ui.Tick();
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && !ReferenceEquals(model, app.Surface.Model); }, 5000));
        Assert.AreEqual("externally changed", app.Surface.Model.Rows[0].Cells[0]);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task MinimalDeveloperViewSkipsAutomaticDetailsButEnterLoadsThem() {
        var data = new UiFixtureBackend();
        using var app = new DeveloperApp(data, minimal: true);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(140, 34));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(app.Surface);
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return !app.Busy; }, 5000));
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 200) {
                Tick();
                Thread.Sleep(5);
            }
            Assert.AreEqual(0, data.Details);
            CollectionAssert.AreEqual(expected, data.Loaded.ToArray());
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return backend.GetOutText().Contains("+fixture diff", StringComparison.Ordinal); }, 5000));
            Assert.AreEqual(1, data.Details);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    private static readonly int[] expectedArray = [0];

    [TestMethod]
    [DoNotParallelize]
    public async Task DeveloperUiLoadsOnlyActiveTabAndRequiresConfirmationBeforeAnAction() {
        var data = new UiFixtureBackend();
        using var app = new DeveloperApp(data);
        var lightTheme = Themes.ThemeCatalog.Load("Light Modern");
        lightTheme.Apply(app.Surface);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(140, 34));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        void Wait(Func<bool> condition) =>
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return condition(); }, 5000));
        void Send(TerminalEvent ev, Func<bool> condition) {
            backend.PushEvent(ev);
            Wait(condition);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(app.Surface);
        try {
            Wait(() => !app.Busy && backend.GetOutText().Contains("+fixture diff", StringComparison.Ordinal));
            CollectionAssert.AreEqual(expectedArray, data.Loaded.ToArray());
            app.Surface.Model.SortColumn = 0;
            app.Surface.Model.Descending = true;
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Right, X = 5, Y = 4 }, () => backend.GetOutText().Contains("Fixture action", StringComparison.Ordinal));
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Right, X = 5, Y = 4 }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => backend.GetOutText().Contains("Confirm · Fixture action", StringComparison.Ordinal));
            Assert.AreEqual(lightTheme.Background, terminal.FocusedElement!.GetTheme().Background);
            Assert.IsTrue(terminal.FocusedElement is XenoAtom.Terminal.UI.Controls.Button cancel && cancel.Content is XenoAtom.Terminal.UI.Controls.TextBlock label && label.Text == "Cancel",
                "Confirmation should initially focus Cancel; actual focus: " + terminal.FocusedElement.GetType().Name);
            Assert.AreEqual(0, data.Applied);
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => app.Surface.HasFocus);
            Assert.AreEqual(0, data.Applied);
            Send(new TerminalTextEvent { Text = "a" }, () => !app.Surface.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => backend.GetOutText().Contains("Confirm · Fixture action", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => data.Applied == 1);
            Wait(() => !app.Busy);
            Assert.AreEqual(0, app.Surface.Model.SortColumn);
            Assert.IsTrue(app.Surface.Model.Descending);
            Send(new TerminalTextEvent { Text = "a" }, () => !app.Surface.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Down }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => backend.GetOutText().Contains("Message body", StringComparison.Ordinal));
            Send(new TerminalTextEvent { Text = "review text" }, () => backend.GetOutText().Contains("review text", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => backend.GetOutText().Contains("Confirm · Message action", StringComparison.Ordinal));
            Assert.AreEqual(1, data.Applied);
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => true);
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => data.Applied == 2);
            Assert.AreEqual("review text", data.LastText);
            Wait(() => !app.Busy);
            Send(new TerminalTextEvent { Text = "2" }, () => app.View == 1 && !app.Busy);
            Assert.Contains(1, data.Loaded);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }

    private static readonly int[] expectedArray0 = [1, 0];

    [TestMethod]
    [DoNotParallelize]
    public async Task ReadOnlyWorktreeNavigationReloadsTheContextWithoutExposingWrites() {
        var data = new UiFixtureBackend();
        using var app = new DeveloperApp(data, initialView: 1, readOnly: true);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(110, 30));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(app.Surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        void Wait(Func<bool> condition) =>
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return condition(); }, 5000));
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(app.Surface);
        try {
            Wait(() => !app.Busy && backend.GetOutText().Contains("+fixture diff", StringComparison.Ordinal));
            backend.PushEvent(new TerminalTextEvent { Text = "a" });
            Wait(() => !app.Surface.HasFocus);
            Assert.DoesNotContain("Fixture action", backend.GetOutText());
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
            Wait(() => !app.Busy && app.View == 0 && app.Surface.Title.Contains("linked", StringComparison.Ordinal));
            Assert.AreEqual("linked", app.Surface.Model.Current!.Cells[0]);
            Assert.AreEqual(0, data.Applied);
            CollectionAssert.AreEqual(expectedArray0, data.Loaded.ToArray());
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    private sealed class UiFixtureBackend : IDeveloperBackend {
        public int Details;
        public string Context = "fixture";
        public string Title => "DEV FIXTURE · " + Context;
        public string[] Tabs { get; } = ["Files", "History"];
        public List<int> Loaded { get; } = [];
        public int Applied;
        public string LastText = "";
        public Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) {
            Loaded.Add(view);
            return Task.FromResult(new DeveloperPage(["Name"], [new("1", [Context])], "Ready"));
        }
        public Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) {
            Interlocked.Increment(ref Details);
            return Task.FromResult("+fixture diff");
        }
        public DeveloperOperation[] Operations(int view, DeveloperItem? item) =>
            [new("Fixture action", "Apply exactly once", (_, _) => { Applied++; return Task.FromResult("done"); }), new("Message action", "Post the reviewed message", (text, _) => { LastText = text; Applied++; return Task.FromResult("posted"); }, "Message body"), new("Open worktree", "Change inspection context", (_, _) => { Context = "linked"; return Task.FromResult("opened"); }, ReadOnly: true, TargetView: 0)];
    }
    private sealed class GitHubFixtureRunner : ICommandRunner {
        public readonly List<CommandRequest> Requests = [];
        public string Head = "abc123";
        public long Environment = 7;
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken token) {
            Requests.Add(request);
            string path = request.Arguments[request.Arguments.Count - 1];
            string response;
            if (request.Input is not null || request.Arguments.Contains("POST")) {
                response = "{}";
            }
            else if (path.EndsWith("pulls/1", StringComparison.Ordinal) && request.Arguments.Contains("Accept: application/vnd.github.diff")) {
                response = "diff --git a/a b/a\n-old line\n+new line";
            }
            else if (path.EndsWith("pulls/1", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new {
                    number = 1,
                    state = "open",
                    title = "Improve",
                    head = new {
                        sha = Head
                    },
                    html_url = "https://github.com/owner/repo/pull/1"
                });
            }
            else if (path.Contains("pulls?", StringComparison.Ordinal)) {
                response = "[{\"number\":1,\"title\":\"Improve\",\"user\":{\"login\":\"dev\"},\"head\":{\"sha\":\"abc123\",\"ref\":\"main\"}}]";
            }
            else if (path.Contains("check-runs", StringComparison.Ordinal)) {
                response = "{\"check_runs\":[]}";
            }
            else if (path.EndsWith("/status", StringComparison.Ordinal)) {
                response = "{\"contexts\":[]}";
            }
            else if (path.Contains("/reviews?", StringComparison.Ordinal)) {
                response = "[]";
            }
            else if (path.Contains("pending_deployments", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new[] { new { environment = new { id = Environment, name = "production" }, current_user_can_approve = true } });
            }
            else if (path.Contains("/jobs?", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new {
                    jobs = new[] {
                        new {
                            id = 2,
                            name = "build",
                            status = "completed",
                            conclusion = "failure",
                            started_at = "2026-11-03T08:00:00Z",
                            completed_at = "2026-11-03T08:01:00Z",
                            html_url = "https://github.com/owner/repo/actions/runs/9/job/2",
                            steps = new[] {
                                new { number = 1, name = "Compile", status = "completed", conclusion = "success", started_at = "2026-11-03T08:00:00Z", completed_at = "2026-11-03T08:00:30Z" },
                                new { number = 2, name = "Test", status = "completed", conclusion = "failure", started_at = "2026-11-03T08:00:30Z", completed_at = "2026-11-03T08:01:00Z" }
                            }
                        }
                    }
                });
            }
            else if (path.EndsWith("actions/jobs/2/logs", StringComparison.Ordinal)) {
                response = "Compile\nTest\nbuild output";
            }
            else if (path.EndsWith("actions/jobs/2", StringComparison.Ordinal)) {
                response = """{"status":"completed"}""";
            }
            else if (path.Contains("/artifacts?", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { artifacts = new[] { new { name = "release-bundle", size_in_bytes = 1024, expired = false, archive_download_url = "https://api.github.com/repos/owner/repo/actions/artifacts/3/zip" } } });
            }
            else if (path.EndsWith("actions/runs/9", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { id = 9, name = "CI", display_title = "Add tests", head_sha = "abc123", head_branch = "main", @event = "push", actor = new { login = "ci-bot" }, html_url = "https://github.com/owner/repo/actions/runs/9", run_attempt = 1, status = "completed", conclusion = "success", created_at = "2026-11-03T08:00:00Z", updated_at = "2026-11-03T08:02:00Z" });
            }
            else if (path.Contains("actions/workflows/101/runs?", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { workflow_runs = new[] { new { status = "completed", conclusion = "success", display_title = "Add tests", head_branch = "main", @event = "push", actor = new { login = "ci-bot" }, created_at = "2026-11-03T08:00:00Z", updated_at = "2026-11-03T08:02:00Z", html_url = "https://github.com/owner/repo/actions/runs/9" } } });
            }
            else if (path.EndsWith("actions/workflows/101", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { name = "CI", state = "active", path = ".github/workflows/ci.yml", created_at = "2026-10-01T00:00:00Z", updated_at = "2026-11-03T08:00:00Z", html_url = "https://github.com/owner/repo/actions/workflows/101" });
            }
            else if (path.Contains("actions/workflows?", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { workflows = new[] { new { id = 101, name = "CI", state = "active", path = ".github/workflows/ci.yml" } } });
            }
            else if (path.Contains("actions/runs?", StringComparison.Ordinal)) {
                response = JsonSerializer.Serialize(new { workflow_runs = new[] { new { id = 9, head_sha = "abc123", run_attempt = 1, status = "completed", conclusion = "success", display_title = "Add tests", head_branch = "main", html_url = "https://github.com/owner/repo/actions/runs/9" } } });
            }
            else if (path.Contains("issues?", StringComparison.Ordinal)) {
                response = "[{\"number\":2,\"title\":\"Issue\",\"user\":{\"login\":\"dev\"}}]";
            }
            else {
                throw new InvalidOperationException("Unexpected fixture request: " + path);
            }

            return Task.FromResult(new CommandResult(0, response, ""));
        }
    }
    private sealed class GitFixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-Git-" + Guid.NewGuid().ToString("N"));
        public GitFixture() =>
            Directory.CreateDirectory(Root);
        public async Task Init() {
            await Git("init", "--initial-branch=main");
            await Git("config", "user.name", "Fullscreen Test");
            await Git("config", "user.email", "test@example.invalid");
            await Git("config", "commit.gpgsign", "false");
            await Git("config", "core.autocrlf", "false");
            await Git("config", "core.hooksPath", Path.Combine(Root, "no-hooks"));
            await Git("config", "core.excludesFile", Path.Combine(Root, ".git", "info", "exclude"));
        }
        public async Task<string> Git(params string[] arguments) =>
            (await new CommandRunner().RunAsync(new("git", Root, arguments), default)).RequireSuccess();
        public void Dispose() {
            string path = Path.GetFullPath(Root), temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (Path.GetDirectoryName(path) != temp || !Path.GetFileName(path).StartsWith("Fullscreen-Git-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Unsafe fixture cleanup.");
            }

            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, true);
        }
    }
}
