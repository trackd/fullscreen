using System.Net;
using System.Text;
using System.Text.Json;
using fullscreen.Development;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AzureDevOpsTests {
    [TestMethod]
    public async Task WorkItemPanelsAndKanbanRenderFromTheSelectedItem() {
        var client = new FixtureClient { DetailGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 1);
        using var backend = new XenoAtom.Terminal.Backends.InMemoryTerminalBackend(new XenoAtom.Terminal.TerminalSize(140, 45));
        using var session = XenoAtom.Terminal.Terminal.Open(backend, new XenoAtom.Terminal.TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new XenoAtom.Terminal.UI.TerminalApp(app.Root, session.Instance, new XenoAtom.Terminal.UI.TerminalAppOptions { HostKind = XenoAtom.Terminal.UI.Hosting.TerminalHostKind.Fullscreen });
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        void Tick() {
            app.Tick();
            timestamp += System.Diagnostics.Stopwatch.Frequency / 100;
            typeof(XenoAtom.Terminal.UI.TerminalApp).GetMethod("Tick", flags)!.Invoke(terminal, [timestamp]);
        }
        void Wait(Func<bool> predicate) =>
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return predicate(); }, 5000));
        typeof(XenoAtom.Terminal.UI.TerminalApp).GetMethod("BeginRun", flags)!.Invoke(terminal, null);
        try {
            terminal.Focus(app.Surface);
            Wait(() => !app.Busy && app.Surface.Model.Rows.Count == 1);
            Wait(() => client.DetailStarted);
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Enter });
            Wait(() => app.Surface.Status == "Opening details when ready…");
            client.DetailGate.SetResult();
            Wait(() => backend.GetOutText().Contains("ACCEPTANCE CRITERIA", StringComparison.Ordinal));
            Assert.Contains("Hello & welcome", backend.GetOutText());
            Assert.DoesNotContain("System.Description:", backend.GetOutText());
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Escape });
            Wait(() => app.Surface.HasFocus);
            backend.PushEvent(new XenoAtom.Terminal.TerminalTextEvent { Text = "k" });
            Wait(() => backend.GetOutText().Contains("KANBAN", StringComparison.Ordinal));
            Assert.Contains("Active", backend.GetOutText(), "Without a selected board, lanes use work-item states.");
        }
        finally { typeof(XenoAtom.Terminal.UI.TerminalApp).GetMethod("EndRun", flags)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public async Task AssignmentFilterUsesAuthenticatedIdentityInServerQueryAndCanBeCleared() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.Operations(1, null).Single(operation => operation.Label == "Assigned to me").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.Contains("[System.AssignedTo] = @Me", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        await backend.Operations(1, null).Single(operation => operation.Label == "Show everyone's items").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.DoesNotContain("@Me", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
    }
    [TestMethod]
    public void AppLoadsOnlyInitialTabAndDisposesOwnedClientAfterWorkerStops() {
        var client = new FixtureClient();
        var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"));
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return app.Surface.Model.Rows.Count != 0; }, 5000));
            Assert.IsTrue(client.Requests.All(x => x.Path.Contains("boards", StringComparison.Ordinal)));
        }
        finally { app.Dispose(); }
        Assert.IsTrue(client.Disposed);
    }

    [TestMethod]
    public async Task TransportBoundsResponsesAndPropagatesCancellation() {
        using var oversized = new AzureDevOpsClient("fixture", "token", true, new FixtureHandler { Payload = new string('x', 4 * 1024 * 1024 + 1) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => oversized.SendAsync(HttpMethod.Get, "Project/_apis/work/boards", null, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var client = new AzureDevOpsClient("fixture", "token", true, new FixtureHandler());
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.SendAsync(HttpMethod.Get, "Project/_apis/work/boards", null, cancelled.Token));
    }
    [TestMethod]
    public async Task StoriesUseBoundedBatchTeamScopeTypedParentAndStableIdPaging() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project 'quoted'", "Core", 2, "Product Backlog Item");
        Assert.IsEmpty(client.Requests);
        var page = await backend.LoadAsync(2, 0, default);
        Assert.HasCount(1, page.Items);
        Assert.AreEqual("7", page.Items[0].Cells[6]);
        Assert.AreEqual("Sam", page.Items[0].Cells[5]);
        var wiql = client.Requests.Single(x => x.Path.Contains("wiql", StringComparison.Ordinal));
        string query = wiql.Body.GetProperty("query").GetString()!;
        Assert.Contains("[System.WorkItemType] = 'Product Backlog Item'", query);
        Assert.Contains("[System.AreaPath] UNDER 'Project\\Core'", query);
        Assert.Contains("$top=2", wiql.Path);
        var batch = client.Requests.Single(x => x.Path.Contains("workitemsbatch", StringComparison.Ordinal));
        Assert.AreEqual("Relations", batch.Body.GetProperty("$expand").GetString());
        Assert.AreEqual("2026-10-24T00:00:00Z", batch.Body.GetProperty("asOf").GetString());
        await backend.LoadAsync(2, 1, default);
        Assert.Contains("[System.Id] < 42", client.Requests.Last(x => x.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        Assert.IsFalse(client.Requests.Any(x => x.Path.Contains("build", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task EditsUseAtomicRevisionTestAndChildCreationUsesParentRelation() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var item = (await backend.LoadAsync(1, 0, default)).Items[0];
        await backend.Operations(1, item).Single(x => x.Label == "Rename").Execute("literal 'quote'\n$(text)", default);
        var request = client.Requests[^1];
        Assert.AreEqual(HttpMethod.Patch, request.Method);
        Assert.IsTrue(request.Patch);
        Assert.AreEqual("/rev", request.Body[0].GetProperty("path").GetString());
        Assert.AreEqual(3, request.Body[0].GetProperty("value").GetInt32());
        Assert.AreEqual("literal 'quote'\n$(text)", request.Body[1].GetProperty("value").GetString());
        await backend.Operations(1, item).Single(x => x.Label == "Add child task").Execute("Child", default);
        request = client.Requests[^1];
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("System.LinkTypes.Hierarchy-Reverse", request.Body[1].GetProperty("value").GetProperty("rel").GetString());
        Assert.AreEqual("https://dev.azure.com/fixture/Project/_apis/wit/workItems/42", request.Body[1].GetProperty("value").GetProperty("url").GetString());
        int count = client.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => backend.Operations(1, item).Single(x => x.Label == "Set remaining work").Execute("NaN", default));
        Assert.HasCount(count, client.Requests);
    }

    [TestMethod]
    public async Task BoardsNavigateReadOnlyAndUseBoardSpecificColumnAndDefaultTeamAreas() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var board = (await backend.LoadAsync(0, 0, default)).Items[0];
        var open = backend.Operations(0, board).Single();
        Assert.IsTrue(open.ReadOnly);
        Assert.AreEqual(1, open.TargetView);
        await open.Execute("", default);
        var cards = await backend.LoadAsync(1, 0, default);
        Assert.AreEqual("Doing", cards.Items[0].Cells[4]);
        Assert.Contains("'User Story'", client.Requests.Last(x => x.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        Assert.IsTrue(client.Requests.Any(x => x.Path.Contains("teamfieldvalues", StringComparison.Ordinal)));
        await backend.Operations(1, null).Single(x => x.Label == "Show all work items").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        string query = client.Requests.Last(x => x.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
        Assert.IsFalse(query.Contains("AreaPath", StringComparison.Ordinal));
        string details = await backend.DetailAsync(1, cards.Items[0], default);
        Assert.Contains("Hello & welcome", details);
        Assert.IsFalse(details.Contains("<p>", StringComparison.Ordinal));
        Assert.Contains("Comment fixture", backend.Discussions["42"]);
        Assert.IsFalse(details.Contains("System.Title:", StringComparison.Ordinal), "Work-item details should use readable labels rather than raw field dumps.");
    }

    [TestMethod]
    public async Task PipelinePagingUsesOpaqueContinuationAndPullRequestsAreProjectWide() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.LoadAsync(5, 0, default);
        await backend.LoadAsync(5, 1, default);
        Assert.Contains("continuationToken=opaque%2B%2F%3D", client.Requests[^1].Path);
        await backend.LoadAsync(4, 2, default);
        Assert.Contains("$skip=100", client.Requests[^1].Path);
        Assert.Contains("git/pullrequests", client.Requests[^1].Path);
    }

    [TestMethod]
    public async Task TransportEncodesAuthUsesPatchMediaTypeAndDisposesHandler() {
        var handler = new FixtureHandler();
        using (var client = new AzureDevOpsClient("fixture", "secret", false, handler)) {
            await client.SendAsync(HttpMethod.Patch, "Project/_apis/wit/workitems/42?api-version=7.1", new[] { new { op = "test", path = "/rev", value = 3 } }, default, true);
            Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":secret")), handler.Authorization);
            Assert.AreEqual("application/json-patch+json", handler.MediaType);
            Assert.AreEqual("https://dev.azure.com/fixture/Project/_apis/wit/workitems/42?api-version=7.1", handler.Address);
            Assert.AreEqual("opaque", client.ContinuationToken);
        }
        Assert.IsTrue(handler.Disposed);
        using var bearer = new AzureDevOpsClient("https://fixture.visualstudio.com", "token", true, handler = new FixtureHandler());
        await bearer.SendAsync(HttpMethod.Get, "Project/_apis/work/boards", null, default);
        Assert.AreEqual("Bearer token", handler.Authorization);
    }

    [TestMethod]
    public async Task TransportRejectsUntrustedOriginsAndDoesNotEchoServerSecrets() {
        foreach (string address in new[] { "http://dev.azure.com/org", "https://evil.example/org", "https://dev.azure.com/org?secret=1", "https://user:pass@dev.azure.com/org", "https://dev.azure.com/org/extra" }) {
            Assert.Throws<ArgumentException>(() => new AzureDevOpsClient(address, "secret", false));
        }

        using var client = new AzureDevOpsClient("fixture", "secret", false, new FixtureHandler { Status = HttpStatusCode.Conflict });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(HttpMethod.Get, "Project/_apis/wit/workitems/42", null, default));
        Assert.IsFalse(error.Message.Contains("server-secret", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
    }

    private sealed record Request(HttpMethod Method, string Path, JsonElement Body, bool Patch);
    private sealed class FixtureClient : IAzureDevOpsClient, IDisposable {
        public string OrganizationUrl => "https://dev.azure.com/fixture";
        public string? ContinuationToken => "opaque+/=";
        public List<Request> Requests { get; } = [];
        public bool Disposed {
            get; private set;
        }
        public TaskCompletionSource? DetailGate;
        public volatile bool DetailStarted;
        public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken token, bool patch = false) {
            token.ThrowIfCancellationRequested();
            Requests.Add(new(method, path, JsonSerializer.SerializeToElement(body), patch));
            if (DetailGate is not null && path.Contains("wit/workitems/", StringComparison.Ordinal) && !path.Contains("/comments?", StringComparison.Ordinal)) {
                DetailStarted = true;
                await DetailGate.Task.WaitAsync(token);
            }
            string json = path.Contains("teamfieldvalues", StringComparison.Ordinal) ? """{"values":[{"value":"Project\\Core","includeChildren":true}]}"""
                : path.Contains("wiql", StringComparison.Ordinal) ? """{"asOf":"2026-10-24T00:00:00Z","workItems":[{"id":42}]}"""
                : path.Contains("boards/", StringComparison.Ordinal) ? """{"allowedMappings":{"User Story":{}},"fields":{"columnField":{"referenceName":"WEF_board_Kanban.Column"}}}"""
                : path.Contains("boards", StringComparison.Ordinal) ? """{"value":[{"id":"board-1","name":"Stories"}]}"""
                : path.Contains("workitemsbatch", StringComparison.Ordinal) ? "{\"value\":[" + Item + "]}"
                : path.Contains("/comments?", StringComparison.Ordinal) ? """{"comments":[{"text":"Comment fixture","createdBy":{"displayName":"Sam"}}]}"""
                : path.Contains("wit/workitems/", StringComparison.Ordinal) ? Item : """{"id":99,"value":[]}""";
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        public void Dispose() =>
            Disposed = true;
        private const string Item = """{"id":42,"rev":3,"fields":{"System.Title":"Implement collector","System.WorkItemType":"User Story","System.State":"Active","System.Description":"<p>Hello &amp; welcome</p>","System.AssignedTo":{"displayName":"Sam"},"WEF_board_Kanban.Column":"Doing"},"relations":[{"rel":"System.LinkTypes.Hierarchy-Reverse","url":"https://dev.azure.com/fixture/_apis/wit/workItems/7"}]}""";
    }
    private sealed class FixtureHandler : HttpMessageHandler {
        public string? Authorization, MediaType, Address;
        public bool Disposed;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Payload;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization = request.Headers.Authorization?.ToString();
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Address = request.RequestUri?.AbsoluteUri;
            var response = new HttpResponseMessage(Status) { Content = new StringContent(Payload ?? (Status == HttpStatusCode.OK ? "{}" : "server-secret")) };
            response.Headers.Add("x-ms-continuationtoken", "opaque");
            return Task.FromResult(response);
        }
        protected override void Dispose(bool disposing) {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
