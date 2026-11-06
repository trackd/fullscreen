using System.Net;
using System.Reflection;
using System.Text.Json;
using fullscreen.Development;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PipelineTests {
    private static readonly DeveloperItem Run = new("9", []);
    private static readonly PipelineNode TaskNode = new("task", "job", "Task", "Compile", "inProgress", "2s", "7");
    private static readonly PipelineSnapshot Snapshot = new("CI", "inProgress", [
        new("stage", null, "Stage", "Build", "inProgress", "2s"),
        new("job", "stage", "Job", "Linux", "inProgress", "2s", "summary"),
        TaskNode,
        new("done", "job", "Task", "Checkout", "succeeded", "1s", "6")
    ], [new("bundle", "bundle", "100 B")], "Published logs");

    [TestMethod]
    public async Task GitHubPipelineReadsCurrentAttemptAndPaginatesJobs() {
        var requests = new List<string>();
        var runner = new Runner(path => {
            requests.Add(path);
            return path.Contains("/jobs?", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { total_count = 101, jobs = Enumerable.Range(path.EndsWith("page=1", StringComparison.Ordinal) ? 1 : 101,
                    path.EndsWith("page=1", StringComparison.Ordinal) ? 100 : 1).Select(id => new { id, name = "job " + id, status = "completed", conclusion = "success", steps = new[] { new { number = 1, name = "Compile", status = "completed", conclusion = "success" } } }) })
                : path.Contains("/artifacts?", StringComparison.Ordinal) ? """{"artifacts":[{"id":10,"name":"bundle","size_in_bytes":1024,"expired":true}]}"""
                : """{"name":"CI","run_attempt":3,"status":"in_progress"}""";
        });
        var backend = new GitHubBackend(Environment.CurrentDirectory, "owner/repo", runner);
        var pipeline = (IPipelineBackend)backend;
        var snapshot = await pipeline.ReadPipelineAsync(Run, default);
        Assert.HasCount(202, snapshot.Nodes);
        Assert.AreEqual("101", snapshot.Nodes[^1].ParentId);
        Assert.Contains("attempt 3", snapshot.Title);
        Assert.IsTrue(snapshot.Artifacts[0].Expired);
        Assert.Contains("repos/owner/repo/actions/runs/9/attempts/3/jobs?per_page=100&page=2", requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.DownloadPipelineArtifactAsync(Run, snapshot.Artifacts[0], "unused.zip", default));
        Assert.IsFalse(runner.Downloaded);
    }

    [TestMethod]
    public async Task GitHubPipelineReportsUnpublishedLogsThenCachesCompletedLog() {
        bool completed = false;
        var runner = new Runner(path => path.EndsWith("/logs", StringComparison.Ordinal) ? "step heading\noutput" : completed ? """{"status":"completed"}""" : """{"status":"in_progress"}""");
        var pipeline = (IPipelineBackend)new GitHubBackend(Environment.CurrentDirectory, "owner/repo", runner);
        var node = new PipelineNode("job", null, "Job", "Build", "in_progress", "", "2");
        var pending = await pipeline.ReadPipelineLogAsync(Run, node, default);
        Assert.Contains("not published", pending.Notice);
        Assert.IsEmpty(pending.Text);
        completed = true;
        var log = await pipeline.ReadPipelineLogAsync(Run, node, default);
        Assert.Contains("output", log.Text);
        int requests = runner.Requests;
        Assert.AreEqual(log, await pipeline.ReadPipelineLogAsync(Run, node, default));
        Assert.AreEqual(requests, runner.Requests);
    }

    [TestMethod]
    public async Task AzurePipelineFollowsNestedTimelinesAndBoundsTheLogTail() {
        var client = new Client(path => path.Contains("/timeline/nested?", StringComparison.Ordinal)
            ? """{"records":[{"id":"task","parentId":"job","type":"Task","name":"Compile","state":"inProgress","attempt":2,"log":{"id":7}}]}"""
            : path.Contains("/timeline?", StringComparison.Ordinal)
            ? """{"id":"main","records":[{"id":"stage","type":"Stage","name":"Build","order":1},{"id":"job","parentId":"stage","type":"Job","name":"Linux","details":{"id":"nested"}}]}"""
            : path.Contains("/logs/7?", StringComparison.Ordinal) ? """["line 1002","last line"]"""
            : path.Contains("/logs?", StringComparison.Ordinal) ? """{"value":[{"id":7,"lineCount":3001}]}"""
            : path.Contains("/artifacts?", StringComparison.Ordinal) ? """{"value":[{"name":"bundle","resource":{"properties":{"artifactsize":"100"}}}]}"""
            : """{"buildNumber":"42","definition":{"name":"CI"},"status":"inProgress"}""");
        using var backend = new AzureDevOpsBackend(client, "A project");
        var pipeline = (IPipelineBackend)backend;
        var snapshot = await pipeline.ReadPipelineAsync(Run, default);
        Assert.HasCount(3, snapshot.Nodes);
        var task = snapshot.Nodes.Single(node => node.Id == "task");
        Assert.AreEqual("job", task.ParentId);
        Assert.IsNull(snapshot.Nodes.Single(node => node.Id == "stage").ParentId);
        var log = await pipeline.ReadPipelineLogAsync(Run, task, default);
        Assert.Contains("last line", log.Text);
        Assert.Contains("starting at line 1002", log.Notice);
        Assert.Contains("A%20project/_apis/build/builds/9/logs/7?startLine=1001&endLine=3000&api-version=7.1", client.Requests);
        await pipeline.DownloadPipelineArtifactAsync(Run, snapshot.Artifacts[0], "new.zip", default);
        Assert.AreEqual("A%20project/_apis/build/builds/9/artifacts?artifactName=bundle&$format=zip&api-version=7.1", client.Download);
    }

    [TestMethod]
    public async Task AzurePipelineHandlesQueuedBuildWithoutTimeline() {
        var client = new Client(path => path.Contains("/timeline?", StringComparison.Ordinal)
            ? throw new HttpRequestException("Not found", null, HttpStatusCode.NotFound)
            : path.Contains("/artifacts?", StringComparison.Ordinal) ? """{"value":[]}"""
            : """{"buildNumber":"42","status":"notStarted","definition":{"name":"CI"}}""");
        using var backend = new AzureDevOpsBackend(client, "Project");
        var snapshot = await ((IPipelineBackend)backend).ReadPipelineAsync(Run, default);
        Assert.IsEmpty(snapshot.Nodes);
        Assert.Contains("notStarted", snapshot.Status);
    }

    [TestMethod]
    public async Task UnchangedPoll_RetainsTableWhileChangedLogsAndNodesStillUpdate() {
        var backend = new MonitorBackend();
        using var app = new DeveloperApp(backend);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        var monitor = Open(ui, app);
        Wait(ui, () => monitor.Model.Rows.Count == Snapshot.Nodes.Length && backend.LogNodes.Count > 0);
        var model = monitor.Model;
        int polls = backend.Snapshots;
        monitor.ToolbarCommands.Single(command => command.Label == "F5 Refresh").Execute();
        Wait(ui, () => backend.Snapshots > polls && monitor.Status.Contains("Updated", StringComparison.Ordinal));
        for (int tick = 0; tick < 10; tick++) { ui.Tick(); }
        Assert.AreSame(model, monitor.Model, "Identical polling responses must not rebuild the table.");
        backend.LogText = "new log text";
        polls = backend.Snapshots;
        monitor.ToolbarCommands.Single(command => command.Label == "F5 Refresh").Execute();
        Wait(ui, () => backend.Snapshots > polls && ui.Output.Contains("new log text", StringComparison.Ordinal));
        Assert.AreSame(model, monitor.Model, "Appending logs must not rebuild unchanged job rows.");
        backend.CurrentSnapshot = Snapshot with { Nodes = [.. Snapshot.Nodes.Select(node => node.Id == "task" ? node with { Status = "succeeded" } : node)] };
        polls = backend.Snapshots;
        monitor.ToolbarCommands.Single(command => command.Label == "F5 Refresh").Execute();
        Wait(ui, () => backend.Snapshots > polls && !ReferenceEquals(model, monitor.Model));
        Assert.AreEqual("succeeded", ((PipelineNode)monitor.Model.Rows.Single(row => ((PipelineNode)row.Original).Id == "task").Original).Status);
    }

    [TestMethod]
    public async Task MonitorFollowsActiveDescendantPreservesSelectionAndPauseControls() {
        var backend = new MonitorBackend();
        using var app = new DeveloperApp(backend);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        var monitor = Open(ui, app);
        Wait(ui, () => backend.LogNodes.Contains("task"));
        Assert.AreEqual("stage", ((PipelineNode)monitor.Model.Current!.Original!).Id);
        monitor.Model.Select(1, 20);
        Wait(ui, () => backend.LogNodes.Count >= 2);
        Assert.AreEqual("task", backend.LogNodes[^1], "A job's summary log must not hide its active task log.");
        monitor.ToolbarCommands.Single(command => command.Label == "F5 Refresh").Execute();
        Wait(ui, () => backend.Snapshots >= 4);
        Assert.AreEqual("job", ((PipelineNode)monitor.Model.Current!.Original!).Id);
        monitor.ToolbarCommands.Single(command => command.Label == "P Pause refresh").Execute();
        int count = backend.Snapshots;
        for (int i = 0; i < 10; i++) {
            ui.Tick();
        }
        Assert.AreEqual(count, backend.Snapshots);
        Assert.StartsWith("PAUSED", monitor.Status);
        monitor.ToolbarCommands.Single(command => command.Label == "F Follow tail").Execute();
        Assert.Contains("Follow tail OFF", monitor.Status);
        ui.Key(TerminalKey.Backspace, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Any(surface => surface != app.Surface));
        Assert.IsFalse(app.Surface.ExitRequested);
    }

    [TestMethod]
    public async Task MonitorDiscardsStaleLogsAndCancelsPendingRequestsOnClose() {
        var backend = new MonitorBackend { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var app = new DeveloperApp(backend);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        var monitor = Open(ui, app);
        Wait(ui, () => backend.LogNodes.Count == 1);
        var superseded = backend.LastToken;
        monitor.Model.Select(3, 20);
        Wait(ui, () => backend.LogNodes.Count >= 2);
        Assert.IsTrue(superseded.IsCancellationRequested);
        backend.Gate.SetResult();
        Wait(ui, () => string.Join('\n', Field<string[]>(monitor, "_preview") ?? []).Contains("log for done", StringComparison.Ordinal));
        Assert.AreEqual("done", ((PipelineNode)monitor.Model.Current!.Original!).Id);
        Assert.DoesNotContain("log for task", string.Join('\n', Field<string[]>(monitor, "_preview") ?? []));
        backend.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.ToolbarCommands.Single(command => command.Label == "F5 Refresh").Execute();
        Wait(ui, () => backend.LogNodes.Count >= 3);
        var closing = backend.LastToken;
        Assert.IsFalse(closing.IsCancellationRequested);
        ui.Key(TerminalKey.Backspace, () => closing.IsCancellationRequested);
        Assert.IsFalse(app.Surface.ExitRequested);
    }

    [TestMethod]
    public async Task LiveDocumentRetainsScrollAndSearchWhenFollowIsOff() {
        var surface = new TableSurface(new(["Column"]), "Log");
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 90, 24);
        surface.SetDocument(string.Join('\n', Enumerable.Range(1, 200).Select(index => "line " + index)));
        ui.App.Focus(surface);
        ui.Key(TerminalKey.PageDown, () => Field<int>(surface, "_documentOffset") > 0);
        int offset = Field<int>(surface, "_documentOffset");
        surface.UpdateLiveDocument(string.Join('\n', Enumerable.Range(1, 210).Select(index => "line " + index)), false);
        Assert.AreEqual(offset, Field<int>(surface, "_documentOffset"));
        ui.Text("/", () => Field<bool>(surface, "_documentSearching"));
        ui.Text("line 100", () => Field<string>(surface, "_documentSearch") == "line 100");
        surface.UpdateLiveDocument(string.Join('\n', Enumerable.Range(1, 220).Select(index => "line " + index)), false);
        Assert.AreEqual("line 100", Field<string>(surface, "_documentSearch"));
        Assert.IsTrue(Field<bool>(surface, "_documentSearching"));
        surface.UpdateLiveDocument(string.Join('\n', Enumerable.Range(1, 230).Select(index => "line " + index)), true);
        Assert.IsGreaterThan(200, Field<int>(surface, "_documentOffset"));
        surface.SetPreview("Log", string.Join('\n', Enumerable.Range(1, 200)));
        surface.UpdateLivePreview("Log", string.Join('\n', Enumerable.Range(1, 210)), true);
        Assert.IsGreaterThan(180, Field<int>(surface, "_previewOffset"));
        int previewOffset = Field<int>(surface, "_previewOffset");
        surface.UpdateLivePreview("Log", string.Join('\n', Enumerable.Range(1, 220)), false);
        Assert.AreEqual(previewOffset, Field<int>(surface, "_previewOffset"));
    }

    [TestMethod]
    public async Task MonitorDownloadsOnlyAfterDestinationConfirmationEvenInRemoteReadOnlyMode() {
        using var files = new Files();
        var backend = new MonitorBackend();
        using var app = new DeveloperApp(backend, readOnly: true);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        var monitor = Open(ui, app);
        ui.Text("2", () => monitor.ActiveTab == 1 && monitor.Model.Rows.Count == 1);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Any());
        Assert.IsNull(backend.DownloadPath);
        var path = Assert.ContainsSingle(ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBox>());
        path.Text = Path.Combine(files.Root, "chosen.zip");
        var button = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Download");
        ui.Activate(button, () => backend.DownloadPath is not null);
        Wait(ui, () => monitor.Status.StartsWith("Downloaded archive:", StringComparison.Ordinal));
        Assert.AreEqual(Path.Combine(files.Root, "chosen.zip"), backend.DownloadPath);
        Assert.IsNotNull(backend.DownloadPath);
        Assert.AreEqual("archive", File.ReadAllText(backend.DownloadPath));
        ui.App.Focus(monitor);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Any());
        Assert.ContainsSingle(ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBox>()).Text = backend.DownloadPath;
        var again = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Download");
        ui.Activate(again, () => monitor.Status.Contains("already exists", StringComparison.Ordinal));
        Assert.AreEqual(1, backend.DownloadCount);
    }

    [TestMethod]
    [DataRow(@"\\example.invalid\artifact")]
    [DataRow("../other/artifact")]
    [DataRow("/other/artifact")]
    [DataRow("https://example.invalid/artifact")]
    public async Task ArtifactDownloadRejectsNonOrganizationPathsBeforeSendingCredentials(string path) {
        int calls = 0;
        using var client = new AzureDevOpsClient("fixture", "fixture-token", true, new Handler(_ => {
            calls++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.DownloadAsync(path, "unused.zip", default));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task ArtifactDownloadRejectsDeclaredOversizeBeforeCreatingTheFile() {
        using var files = new Files();
        using var client = new AzureDevOpsClient("fixture", "fixture-token", true, new Handler(_ => {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = PipelineFiles.MaximumBytes + 1;
            return new(HttpStatusCode.OK) { Content = content };
        }));
        string destination = Path.Combine(files.Root, "large.zip");
        var error = await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync("Project/_apis/artifacts", destination, default));
        Assert.Contains("1 GiB", error.Message);
        Assert.IsFalse(File.Exists(destination));
    }

    [TestMethod]
    [DataRow("https://store.vsblob.vsassets.io/artifact?sig=private", true)]
    [DataRow("https://store.blob.core.windows.net/artifact?sig=private", true)]
    [DataRow("https://dev.azure.com/fixture/Project/new", true)]
    [DataRow("https://dev.azure.com/another/Project/new", false)]
    [DataRow("https://example.invalid/artifact", false)]
    [DataRow("http://store.vsblob.vsassets.io/artifact", false)]
    [DataRow("https://store.vsblob.vsassets.io:444/artifact", false)]
    public async Task ArtifactRedirectsAreRestrictedAndNeverSendCredentialsToStorage(string redirect, bool allowed) {
        using var files = new Files();
        int count = 0;
        var handler = new Handler(request => {
            count++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/artifacts", StringComparison.Ordinal)) {
                Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new(redirect);
                return response;
            }
            Assert.AreEqual(redirect.StartsWith("https://dev.azure.com/fixture/", StringComparison.Ordinal), request.Headers.Authorization is not null);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([0, 1, 255, 13, 10]) };
        });
        using var client = new AzureDevOpsClient("fixture", "fixture-token", true, handler);
        string destination = Path.Combine(files.Root, "artifact.zip");
        if (allowed) {
            await client.DownloadAsync("Project/_apis/build/builds/9/artifacts?api-version=7.1", destination, default);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 255, 13, 10 }, await File.ReadAllBytesAsync(destination));
            await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync("Project/_apis/build/builds/9/artifacts", destination, default));
        }
        else {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync("Project/_apis/build/builds/9/artifacts", destination, default));
            Assert.DoesNotContain("sig=", error.Message);
            Assert.AreEqual(1, count);
            Assert.IsFalse(File.Exists(destination));
        }
    }

    [TestMethod]
    public async Task ArtifactCopyCancelsWithoutLeavingPartialFileOrOverwriting() {
        using var files = new Files();
        string path = Path.Combine(files.Root, "artifact.zip");
        await File.WriteAllTextAsync(path, "existing");
        await Assert.ThrowsAsync<IOException>(() => PipelineFiles.CopyNewAsync(new MemoryStream([1, 2]), path, default));
        Assert.AreEqual("existing", await File.ReadAllTextAsync(path));
        File.Delete(path);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => PipelineFiles.CopyNewAsync(new MemoryStream([1, 2]), path, stop.Token));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task CommandArtifactDownloadIsBinaryAndFailureLeavesNoFile() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        using var files = new Files();
        string destination = Path.Combine(files.Root, "artifact.zip");
        var runner = new CommandRunner();
        await runner.DownloadAsync(new("cmd.exe", files.Root, ["/c", "echo archive"]), destination, default);
        Assert.AreEqual("archive\r\n", await File.ReadAllTextAsync(destination));
        await Assert.ThrowsAsync<IOException>(() => runner.DownloadAsync(new("cmd.exe", files.Root, ["/c", "echo replacement"]), destination, default));
        Assert.AreEqual("archive\r\n", await File.ReadAllTextAsync(destination));
        File.Delete(destination);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.DownloadAsync(new("cmd.exe", files.Root, ["/c", "echo partial & exit /b 1"]), destination, default));
        Assert.IsEmpty(Directory.GetFiles(files.Root));
    }

    private static T? Field<T>(object instance, string name) =>
        (T?)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static TableSurface Open(DesignerUxTests.UiSession ui, DeveloperApp app) {
        Wait(ui, () => !app.Busy && app.Surface.Model.Rows.Count > 0);
        ui.App.Focus(app.Surface);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Any(surface => surface != app.Surface && surface.Model.Rows.Count == 4));
        return ui.App.Root.EnumerateVisualsDepthFirst().OfType<TableSurface>().Single(surface => surface != app.Surface);
    }
    private static void Wait(DesignerUxTests.UiSession ui, Func<bool> ready) =>
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return ready(); }, 5000));

    private sealed class Runner(Func<string, string> response) : ICommandRunner {
        internal int Requests;
        internal bool Downloaded;
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken token) {
            Requests++;
            return Task.FromResult(new CommandResult(0, response(request.Arguments.First(argument => argument.StartsWith("repos/", StringComparison.Ordinal))), ""));
        }
        public Task DownloadAsync(CommandRequest request, string destination, CancellationToken token) {
            Downloaded = true;
            return Task.CompletedTask;
        }
    }
    private sealed class Client(Func<string, string> response) : IAzureDevOpsClient {
        public string OrganizationUrl => "https://dev.azure.com/fixture";
        internal readonly List<string> Requests = [];
        internal string? Download;
        public Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken token, bool patch = false) {
            Requests.Add(path);
            return Task.FromResult(JsonDocument.Parse(response(path)).RootElement.Clone());
        }
        public Task DownloadAsync(string path, string destination, CancellationToken token) {
            Download = path;
            return Task.CompletedTask;
        }
    }
    private sealed class MonitorBackend : IDeveloperBackend, IPipelineBackend {
        public string Title => "Fixture";
        public string[] Tabs => ["Runs"];
        internal readonly List<string> LogNodes = [];
        internal int Snapshots;
        internal TaskCompletionSource? Gate;
        internal CancellationToken LastToken;
        internal string? DownloadPath;
        internal int DownloadCount;
        internal PipelineSnapshot CurrentSnapshot = Snapshot;
        internal string? LogText;
        public Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) =>
            Task.FromResult(new DeveloperPage(["Run"], [new("9", ["CI"])], ""));
        public Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) => Task.FromResult("Overview");
        public DeveloperOperation[] Operations(int view, DeveloperItem? item) => [];
        public bool IsPipelineView(int view) => true;
        public Task<PipelineSnapshot> ReadPipelineAsync(DeveloperItem run, CancellationToken token) {
            Snapshots++;
            LastToken = token;
            return Task.FromResult(CurrentSnapshot);
        }
        public async Task<PipelineLog> ReadPipelineLogAsync(DeveloperItem run, PipelineNode node, CancellationToken token) {
            LogNodes.Add(node.Id);
            if (Gate is not null) {
                await Gate.Task.WaitAsync(token);
            }
            return new(LogText ?? "log for " + node.Id);
        }
        public async Task DownloadPipelineArtifactAsync(DeveloperItem run, PipelineArtifact artifact, string destination, CancellationToken token) {
            DownloadPath = destination;
            DownloadCount++;
            await File.WriteAllTextAsync(destination, "archive", token);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class Files : IDisposable {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-Pipeline-" + Guid.NewGuid().ToString("N"));
        internal Files() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
