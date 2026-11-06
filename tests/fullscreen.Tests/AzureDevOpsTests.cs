using System.Net;
using System.Text;
using System.Text.Json;
using fullscreen.Development;
using fullscreen.Tables;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AzureDevOpsTests {
    [TestMethod]
    public async Task BacklogAndSprintReviewResolveTheDefaultTeamOnceWhenTeamIsOmitted() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.LoadAsync(6, 0, default);
        await backend.LoadSprintReviewAsync(null, default);
        Assert.AreEqual(1, client.Requests.Count(request => request.Path.StartsWith("_apis/projects/", StringComparison.Ordinal)));
        Assert.IsTrue(client.Requests.Where(request => request.Path.Contains("/_apis/work/", StringComparison.Ordinal))
            .All(request => request.Path.StartsWith("Project/22222222-2222-2222-2222-222222222222/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SprintReviewUsesProcessCategoriesRatherThanEnglishStateNames() {
        var client = new FixtureClient {
            ItemFixtures = [ReviewItem(1, "Custom", "Shipped"), ReviewItem(2, "Custom", "Resolved")],
            WorkflowStates = """{"value":[{"name":"Shipped","category":"Completed"},{"name":"Resolved","category":"Resolved"}]}"""
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var review = await backend.LoadSprintReviewAsync(null, default);
        Assert.AreEqual(2, review.Total);
        Assert.AreEqual(1, review.Completed);
        Assert.AreEqual(0, review.Unknown);
        Assert.AreEqual("2026-10-24T00:00:00Z", review.AsOf);
    }

    [TestMethod]
    public async Task SprintReviewReportsAccessFailureInsteadOfShowingZeroTotals() {
        var client = new FixtureClient { RejectReview = true };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 100, 35);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        ui.App.Focus(app.Surface);
        ui.Text("u", () => !app.Busy && app.Surface.Status.StartsWith("ERROR ·", StringComparison.Ordinal));
        Assert.Contains("Forbidden", app.Surface.Status);
        Assert.IsFalse(ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsTrue(app.Tick());
    }

    [TestMethod]
    public async Task AnalyticsRequestedDuringLoadingOpensWhenThePageIsReady() {
        var client = new FixtureClient { ListGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 80, 30);
        Assert.IsTrue(app.Busy);
        app.Surface.ToolbarCommands.Single(command => command.Label == "U Sprint review").Execute();
        Assert.Contains("Sprint review will open", app.Surface.Status);
        client.ListGate.SetResult();
        Assert.IsTrue(SpinWait.SpinUntil(() => {
            ui.Tick();
            return ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Charts.Chart>().Any();
        }, 5000));
        Assert.Contains("TEAM SPRINT SNAPSHOT", ui.Output);
    }

    [TestMethod]
    public async Task SprintReviewAggregatesAllTypesAndEstimatesWithoutListOrAssigneeFilters() {
        var client = new FixtureClient {
            ItemFixtures = [
                ReviewItem(42, "User Story", "Done", ("Microsoft.VSTS.Scheduling.StoryPoints", 8)),
                ReviewItem(43, "User Story", "Active", ("Microsoft.VSTS.Scheduling.StoryPoints", 3)),
                ReviewItem(44, "User Story", "Active"),
                ReviewItem(45, "Task", "Active", ("Microsoft.VSTS.Scheduling.RemainingWork", 4), ("Microsoft.VSTS.Scheduling.CompletedWork", 2)),
                ReviewItem(46, "Task", "Done", ("Microsoft.VSTS.Scheduling.RemainingWork", 0), ("Microsoft.VSTS.Scheduling.CompletedWork", 6)),
                ReviewItem(47, "User Story", "Removed", ("Microsoft.VSTS.Scheduling.StoryPoints", 5)),
                ReviewItem(48, "Custom", "Unmapped")
            ]
        };
        using var backend = new AzureDevOpsBackend(client, "Project", "Core", pageSize: 1);
        await backend.Operations(2, null).First().Execute("", default);
        var review = await backend.LoadSprintReviewAsync(null, default);
        Assert.AreEqual("Sprint 2", review.Name);
        Assert.AreEqual(6, review.Total);
        Assert.AreEqual(2, review.Completed);
        Assert.AreEqual(1, review.Removed);
        Assert.AreEqual(1, review.Unknown);
        Assert.AreEqual("2026-10-12T00:00:00Z", review.Start);
        Assert.Contains("  story points · 8 completed / 3 not completed · 2/3 estimated", review.Measures);
        Assert.Contains("  Recorded completed hours · 8 · 2/2 reported", review.Measures);
        Assert.Contains("  Recorded remaining hours · 4 · 2/2 reported", review.Measures);
        var query = client.Requests.Single(request => request.Path.Contains("wiql", StringComparison.Ordinal));
        string wiql = query.Body.GetProperty("query").GetString()!;
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", wiql);
        Assert.Contains("[System.AreaPath] UNDER 'Project\\Core'", wiql);
        Assert.DoesNotContain("WorkItemType", wiql);
        Assert.DoesNotContain("@Me", wiql);
        Assert.DoesNotContain("$top", query.Path);
    }

    [TestMethod]
    public async Task SprintReviewUsesBoundedBatchesAndRejectsIncompleteTotals() {
        var client = new FixtureClient {
            ItemFixtures = [.. Enumerable.Range(1, 205).Select(id => ReviewItem(id, "User Story", "Done"))]
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var review = await backend.LoadSprintReviewAsync("Project\\Sprint 1", default);
        Assert.AreEqual(205, review.Total);
        Assert.AreEqual(205, review.Completed);
        var batches = client.Requests.Where(request => request.Path.Contains("workitemsbatch", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[] { 200, 5 }, batches.Select(batch => batch.Body.GetProperty("ids").GetArrayLength()).ToArray());
        Assert.IsTrue(batches.All(batch => batch.Body.GetProperty("asOf").GetString() == "2026-10-24T00:00:00Z"));
        client.IncompleteReview = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.LoadSprintReviewAsync(null, default));
    }

    [TestMethod]
    public async Task ReviewWithoutCurrentSprintRequestsSelectionAndDoesNotInventTotals() {
        var client = new FixtureClient { NoCurrentSprint = true };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var review = await backend.LoadSprintReviewAsync(null, default);
        Assert.AreEqual("", review.Path);
        Assert.HasCount(2, review.Sprints);
        Assert.IsFalse(client.Requests.Any(request => request.Path.Contains("wiql", StringComparison.Ordinal)));
        review = await backend.LoadSprintReviewAsync("Project\\Sprint 1", default);
        Assert.AreEqual("Project\\Sprint 1", review.Path);
    }

    [TestMethod]
    public async Task BacklogUsesConfiguredLevelsPlanningOrderAndExistingConfirmedEdits() {
        var client = new FixtureClient {
            BacklogIds = [9, 42, 7],
            ItemFixtures = [ReviewItem(42, "User Story", "Active"), ReviewItem(7, "User Story", "Active"), ReviewItem(9, "User Story", "Active")]
        };
        using var backend = new AzureDevOpsBackend(client, "Project", "Core", pageSize: 2);
        var first = await backend.LoadAsync(6, 0, default);
        CollectionAssert.AreEqual(new[] { "9", "42" }, first.Items.Select(item => item.Key).ToArray());
        Assert.Contains("all sprints", first.Summary);
        Assert.Contains("team planning order", first.Summary);
        Assert.AreEqual("Stories", backend.BacklogLabel);
        Assert.IsFalse(client.Requests.Any(request => request.Path.Contains("wiql", StringComparison.Ordinal) || request.Path.Contains("iterations", StringComparison.Ordinal)));
        var second = await backend.LoadAsync(6, 1, default);
        Assert.AreEqual("7", second.Items.Single().Key);
        Assert.AreEqual(1, client.Requests.Count(request => request.Path.Contains("/workItems?", StringComparison.Ordinal)));
        await backend.BacklogOperations().Single(operation => operation.Label == "Features").Execute("", default);
        await backend.LoadAsync(6, 0, default);
        Assert.IsTrue(client.Requests.Any(request => request.Path.Contains("backlogs/Microsoft.FeatureCategory/workItems", StringComparison.Ordinal)));
        Assert.IsTrue(backend.Operations(6, null).Any(operation => operation.Label == "Create Feature"));
        var item = first.Items[0];
        await backend.Operations(6, item).Single(operation => operation.Label == "Plan into iteration").Execute("Project\\Sprint 2", default);
        var patch = client.Requests[^1];
        Assert.AreEqual(HttpMethod.Patch, patch.Method);
        Assert.AreEqual("/rev", patch.Body[0].GetProperty("path").GetString());
        Assert.AreEqual("/fields/System.IterationPath", patch.Body[1].GetProperty("path").GetString());
        Assert.AreEqual("Project\\Sprint 2", patch.Body[1].GetProperty("value").GetString());
        await backend.Operations(6, item).Single(operation => operation.Label == "Set priority").Execute("2", default);
        Assert.AreEqual(2, client.Requests[^1].Body[1].GetProperty("value").GetInt32());
        int requests = client.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => backend.Operations(6, item).Single(operation => operation.Label == "Set priority").Execute("NaN", default));
        Assert.HasCount(requests, client.Requests);
        await backend.Operations(6, null).Single(operation => operation.Label == "Create Feature").Execute("Plan feature", default);
        Assert.Contains("wit/workitems/$Feature?", client.Requests[^1].Path);
        Assert.IsTrue(client.Requests[^1].Body.EnumerateArray().Any(operation => operation.GetProperty("path").GetString() == "/fields/System.AreaPath"));
    }

    [TestMethod]
    public async Task BacklogSkipsHiddenLevelsAndReportsAnUnconfiguredTeam() {
        var client = new FixtureClient {
            BacklogLevels = """{"value":[{"id":"hidden","name":"Hidden Stories","type":"requirement","isHidden":true},{"id":"features","name":"Features","type":"portfolio","defaultWorkItemType":{"name":"Feature"}}]}"""
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.LoadAsync(6, 0, default);
        Assert.AreEqual("Features", backend.BacklogLabel);
        Assert.HasCount(1, backend.BacklogOperations());
        Assert.IsTrue(backend.Operations(6, null).Any(operation => operation.Label == "Create Feature"));
        client.BacklogLevels = """{"value":[]}""";
        var empty = await backend.LoadAsync(6, 0, default);
        Assert.Contains("No visible backlog levels", empty.Summary);
        Assert.IsEmpty(empty.Items);
        Assert.IsEmpty(backend.BacklogOperations());
    }

    [TestMethod]
    [DataRow(40)]
    [DataRow(80)]
    [DataRow(140)]
    public async Task NativeToolbarWrapsWithoutHidingCommandsAndKeepsTableFocus(int width) {
        using var app = new DeveloperApp(new AzureDevOpsBackend(new FixtureClient(), "Project"), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), width, 35);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        Assert.IsTrue(app.Surface.HasFocus);
        var buttons = app.Root.EnumerateVisualsDepthFirst().OfType<Button>().ToArray();
        Assert.HasCount(app.Surface.ToolbarCommands.Length, buttons);
        foreach (var button in buttons) {
            Assert.IsGreaterThan(0, button.Bounds.Width);
            Assert.IsGreaterThanOrEqualTo(2, button.Bounds.Y);
            Assert.IsLessThanOrEqualTo(width - 1, button.Bounds.Right);
            Assert.DoesNotContain("[", ((TextBlock)button.Content!).Text!);
        }
        Assert.IsTrue(buttons.GroupBy(button => button.Bounds.Y).Any(row => row.Count() > 1), "Toolbar actions should share rows.");
        var review = buttons.Single(button => button.Content is TextBlock text && text.Text == "U Sprint review");
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Down, XenoAtom.Terminal.TerminalMouseButton.Left, review.Bounds.X + 1, review.Bounds.Y, () => true);
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Up, XenoAtom.Terminal.TerminalMouseButton.Left, review.Bounds.X + 1, review.Bounds.Y,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        ui.Key(XenoAtom.Terminal.TerminalKey.Escape, () => app.Surface.HasFocus);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BacklogDetailsReuseConfirmedMutationsAndRespectReadOnly(bool readOnly) {
        var client = new FixtureClient();
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 6, readOnly: readOnly);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 40);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        Assert.AreEqual(2, app.Surface.StretchColumn);
        ui.App.Focus(app.Surface);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        var detail = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        var create = detail.EnumerateVisualsDepthFirst().OfType<Button>().SingleOrDefault(button => button.Content is TextBlock text && text.Text == "Create User Story");
        if (readOnly) {
            Assert.IsNull(create);
            Assert.IsFalse(detail.EnumerateVisualsDepthFirst().OfType<Button>().Any(button => button.Content is TextBlock text && text.Text == "Plan into iteration"));
        }
        else {
            Assert.IsNotNull(create);
            ui.Activate(create, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<TextBox>().Any());
            var prompt = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
            prompt.EnumerateVisualsDepthFirst().OfType<TextBox>().Single().Text = "Backlog idea";
            ui.Activate(prompt.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "Continue"),
                () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any(dialog => dialog.Title is TextBlock text && text.Text == "Confirm · Create User Story"));
            Assert.IsFalse(client.Requests.Any(request => request.Patch), "Continue must not write before final confirmation.");
            Assert.IsTrue(ui.App.FocusedElement is Button cancel && cancel.Content is TextBlock label && label.Text == "Cancel",
                "Confirmation must initially focus Cancel, not its scrollable preview or the destructive action.");
            ui.Key(XenoAtom.Terminal.TerminalKey.Escape, () => app.Surface.HasFocus);
        }
        Assert.IsFalse(client.Requests.Any(request => request.Patch));
    }

    [TestMethod]
    public async Task BacklogAssignmentFilterKeepsOrderAndDoesNotLeaveEmptyPagesBetweenMatches() {
        var client = new FixtureClient {
            BacklogIds = [9, 42, 7], MineIds = [7, 9],
            ItemFixtures = [ReviewItem(42, "User Story", "Active"), ReviewItem(7, "User Story", "Active"), ReviewItem(9, "User Story", "Active")]
        };
        using var backend = new AzureDevOpsBackend(client, "Project", pageSize: 1);
        await backend.Operations(6, null).First().Execute("", default);
        var first = await backend.LoadAsync(6, 0, default);
        var second = await backend.LoadAsync(6, 1, default);
        Assert.AreEqual("9", first.Items.Single().Key);
        Assert.AreEqual("7", second.Items.Single().Key);
        Assert.IsEmpty((await backend.LoadAsync(6, 2, default)).Items);
    }

    private static JsonElement ReviewItem(int id, string type, string state, params (string Field, double Value)[] estimates) {
        var fields = new Dictionary<string, object> {
            ["System.Title"] = "Review item " + id,
            ["System.WorkItemType"] = type,
            ["System.State"] = state
        };
        foreach (var (Field, Value) in estimates) {
            fields[Field] = Value;
        }
        return JsonSerializer.SerializeToElement(new { id, rev = 3, fields });
    }

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
            Assert.IsFalse(client.DetailStarted, "Overview mode must not fetch details until an item is opened.");
            Assert.IsTrue(app.Surface.Model.Enabled[2]);
            Assert.IsFalse(app.Surface.Model.Enabled[9], "Iteration can be shown through C columns without crowding the default list.");
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Enter });
            Wait(() => client.DetailStarted);
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Enter });
            Wait(() => app.Surface.Status == "Opening details when ready…");
            client.DetailGate.SetResult();
            Wait(() => backend.GetOutText().Contains("ACCEPTANCE CRITERIA", StringComparison.Ordinal));
            Assert.Contains("Hello & welcome", backend.GetOutText());
            Assert.Contains("Parent · #7", backend.GetOutText());
            Assert.Contains("Child · #8", backend.GetOutText());
            Assert.DoesNotContain("System.Description:", backend.GetOutText());
            var detailsDialog = terminal.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
            var relatedButtons = detailsDialog.EnumerateVisualsDepthFirst().OfType<Button>().ToArray();
            Assert.IsTrue(relatedButtons.Any(button => button.Content is TextBlock label && label.Text?.Contains("design.png", StringComparison.Ordinal) == true));
            Assert.IsTrue(relatedButtons.Any(button => button.Content is TextBlock label && label.Text?.Contains("external case", StringComparison.Ordinal) == true));
            var parentLink = relatedButtons.Single(button => button.Content is TextBlock label && label.Text == "Parent · #7");
            terminal.Focus(parentLink);
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Enter });
            Wait(() => backend.GetOutText().Contains("Work item #7", StringComparison.Ordinal));
            Assert.Contains("Back to #42", backend.GetOutText());
            var backLink = terminal.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single()
                .EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock label && label.Text == "Back to #42");
            terminal.Focus(backLink);
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Enter });
            Wait(() => terminal.Root.EnumerateVisualsDepthFirst().OfType<Dialog>()
                .Any(dialog => dialog.Title is TextBlock title && title.Text == "Work item #42"));
            backend.PushEvent(new XenoAtom.Terminal.TerminalKeyEvent { Key = XenoAtom.Terminal.TerminalKey.Escape });
            Wait(() => app.Surface.HasFocus);
            backend.PushEvent(new XenoAtom.Terminal.TerminalTextEvent { Text = "k" });
            Wait(() => backend.GetOutText().Contains("KANBAN", StringComparison.Ordinal));
            Assert.Contains("Active", backend.GetOutText(), "Without a selected board, lanes use work-item states.");
        }
        finally { typeof(XenoAtom.Terminal.UI.TerminalApp).GetMethod("EndRun", flags)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public async Task DeveloperListAndDetailSplitCanBeResizedByKeyboardAndPointer() {
        var surface = new TableSurface(new TableModel(["ID"]), "Developer items");
        surface.SetPreview("DETAILS", "Preview text");
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 140, 40);
        ui.App.Focus(surface);
        int original = surface.SplitPercent;
        ui.Key(XenoAtom.Terminal.TerminalKey.Right, () => surface.SplitPercent > original, XenoAtom.Terminal.TerminalModifiers.Shift);
        int beforeDrag = surface.SplitPercent;
        int dividerX = surface.Bounds.X + surface.Bounds.Width * beforeDrag / 100;
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Down, XenoAtom.Terminal.TerminalMouseButton.Left, dividerX, 10, () => true);
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Move, XenoAtom.Terminal.TerminalMouseButton.Left, dividerX + 12, 10, () => surface.SplitPercent > beforeDrag);
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Up, XenoAtom.Terminal.TerminalMouseButton.Left, dividerX + 12, 10, () => true);
        Assert.IsGreaterThan(beforeDrag, surface.SplitPercent);
    }

    [TestMethod]
    public async Task AssignmentFilterUsesAuthenticatedIdentityInServerQueryAndCanBeCleared() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var filter = backend.Operations(1, null).Single(operation => operation.Label == "Filter list: assigned to me");
        Assert.IsTrue(filter.ReadOnly);
        Assert.AreEqual(1, filter.TargetView);
        Assert.Contains("does not change", filter.Description);
        await filter.Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.Contains("[System.AssignedTo] = @Me", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        Assert.IsFalse(client.Requests.Any(request => request.Method == HttpMethod.Patch), "Filtering must never assign a work item.");
        await backend.Operations(1, null).Single(operation => operation.Label == "Filter list: all assignees").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.DoesNotContain("@Me", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
    }

    [TestMethod]
    public async Task StoriesAndTasksDefaultToCurrentSprintAndShareUserSelection() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project", "Core");
        await backend.LoadAsync(2, 0, default);
        string Query() => client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", Query());
        Assert.Contains("Core/_apis/work/teamsettings/iterations", client.Requests.First(request => request.Path.Contains("iterations", StringComparison.Ordinal)).Path);
        await backend.SprintOperations(2).Single(operation => operation.Label.StartsWith("Sprint 1", StringComparison.Ordinal)).Execute("", default);
        await backend.LoadAsync(3, 0, default);
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 1'", Query());
        await backend.SprintOperations(3).Single(operation => operation.Label == "All sprints").Execute("", default);
        await backend.LoadAsync(2, 0, default);
        Assert.DoesNotContain("IterationPath", Query());
        await backend.SprintOperations(2).First().Execute("", default);
        await backend.LoadAsync(3, 0, default);
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", Query());
    }

    [TestMethod]
    public async Task MissingCurrentSprintIsExplicitAndDoesNotHideTheBacklog() {
        var client = new FixtureClient { NoCurrentSprint = true };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var page = await backend.LoadAsync(2, 0, default);
        Assert.Contains("No current sprint", page.Summary);
        Assert.DoesNotContain("IterationPath", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
    }

    [TestMethod]
    public async Task PullRequestReviewsUseAuthenticatedIdentityAndInspectedCommits() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var pull = (await backend.LoadAsync(4, 0, default)).Items.Single();
        Assert.IsFalse(backend.Operations(4, pull).Any(operation => operation.Label == "Approve"));
        string detail = await backend.DetailAsync(4, pull, default);
        Assert.Contains("src/code.cs", detail);
        Assert.Contains("Please check the error path", detail);
        Assert.Contains("Approved", detail);
        var diff = client.Requests.Single(request => request.Path.Contains("diffs/commits", StringComparison.Ordinal));
        Assert.Contains("baseVersion=target-sha", diff.Path);
        Assert.Contains("targetVersion=source-sha", diff.Path);
        Assert.Contains("diffCommonCommit=true", diff.Path);
        await backend.Operations(4, pull).Single(operation => operation.Label == "Approve").Execute("", default);
        var vote = client.Requests.Last();
        Assert.AreEqual(HttpMethod.Put, vote.Method);
        Assert.Contains("/reviewers/11111111-1111-1111-1111-111111111111", vote.Path);
        Assert.AreEqual(10, vote.Body.GetProperty("vote").GetInt32());
        await backend.Operations(4, pull).Single(operation => operation.Label == "Add PR comment").Execute("Review\ncomment", default);
        var comment = client.Requests.Last();
        Assert.AreEqual(HttpMethod.Post, comment.Method);
        Assert.Contains("/threads?", comment.Path);
        Assert.AreEqual("Review\ncomment", comment.Body.GetProperty("comments")[0].GetProperty("content").GetString());
    }

    [TestMethod]
    public async Task PullRequestDiscussionExcludesDeletedThreadsAndComments() {
        var client = new FixtureClient {
            PullThreads = """{"value":[{"isDeleted":true,"comments":[{"content":"deleted thread"}]},{"isDeleted":false,"comments":[{"isDeleted":true,"content":"deleted comment"},{"isDeleted":false,"content":"visible comment"},{"content":"legacy comment"}]},{"comments":[{"content":"legacy thread"}]}]}"""
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var pull = (await backend.LoadAsync(4, 0, default)).Items.Single();
        string detail = await backend.DetailAsync(4, pull, default);
        Assert.DoesNotContain("deleted thread", detail);
        Assert.DoesNotContain("deleted comment", detail);
        Assert.Contains("visible comment", detail);
        Assert.Contains("legacy comment", detail);
        Assert.Contains("legacy thread", detail);
    }

    [TestMethod]
    public async Task ChangedPullRequestCannotBeApprovedOrCommentedOn() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var pull = (await backend.LoadAsync(4, 0, default)).Items.Single();
        await backend.DetailAsync(4, pull, default);
        var approve = backend.Operations(4, pull).Single(operation => operation.Label == "Approve");
        var comment = backend.Operations(4, pull).Single(operation => operation.Label == "Add PR comment");
        client.ChangedPull = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => approve.Execute("", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => comment.Execute("Comment", default));
        Assert.IsTrue(client.Requests.All(request => request.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task PipelinesShowStatusDurationStepsAndArtifactActions() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var build = (await backend.LoadAsync(5, 0, default)).Items.Single();
        Assert.Contains("✗", build.Cells[2]);
        string detail = await backend.DetailAsync(5, build, default);
        Assert.Contains("✗ failed", detail);
        Assert.Contains("2m 0s", detail);
        Assert.Contains("Compile", detail);
        Assert.Contains("package", detail);
        Assert.IsTrue(backend.Operations(5, build).Any(operation => operation.Label == "Open artifact · package"));
        Assert.IsTrue(backend.Operations(5, build).All(operation => operation.ReadOnly));
    }

    [TestMethod]
    public async Task OverviewToolbarSupportsNativeMousePreviewToggleAndSprintReview() {
        using var app = new DeveloperApp(new AzureDevOpsBackend(new FixtureClient(), "Project"), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 140, 45);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        var preview = app.Root.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock text && text.Text == "V Preview ☐");
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Down, XenoAtom.Terminal.TerminalMouseButton.Left, preview.Bounds.X + 1, preview.Bounds.Y, () => true);
        ui.Mouse(XenoAtom.Terminal.TerminalMouseKind.Up, XenoAtom.Terminal.TerminalMouseButton.Left, preview.Bounds.X + 1, preview.Bounds.Y,
            () => app.Surface.ToolbarCommands.Any(command => command.Label == "V Preview ☑"));
        Assert.IsTrue(app.Surface.ToolbarCommands.Any(command => command.Label == "V Preview ☑"));
        app.Surface.ToolbarCommands.Single(command => command.Label.StartsWith("V Preview", StringComparison.Ordinal)).Execute();
        ui.App.Focus(app.Surface);
        ui.Text("u", () => ui.Output.Contains("TEAM SPRINT SNAPSHOT", StringComparison.Ordinal));
        Assert.Contains("Sprint work by current state", ui.Output);
        var chart = ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Charts.Chart>().Single();
        Assert.IsGreaterThan(60, chart.Bounds.Width);
        Assert.IsGreaterThanOrEqualTo(10, chart.Bounds.Height);
        Assert.IsGreaterThan(0, chart.PlotBounds.Height);
        Assert.Contains("█", ui.Output, "Analytics must draw bars, not only attach a chart to the dialog.");
    }

    [TestMethod]
    [DataRow(80, 30)]
    [DataRow(140, 45)]
    public async Task WorkItemDialogUsesAvailableWidthAndWrapsLongContent(int width, int height) {
        using var app = new DeveloperApp(new AzureDevOpsBackend(new FixtureClient { LongDescription = true }, "Project"), initialView: 2);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), width, height);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        ui.App.Focus(app.Surface);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        Assert.IsGreaterThanOrEqualTo(width - 6, dialog.Bounds.Width);
        var description = dialog.EnumerateVisualsDepthFirst().OfType<TextBlock>()
            .Single(block => block.Text?.Contains("Long description", StringComparison.Ordinal) == true);
        Assert.IsTrue(description.Wrap);
        Assert.IsLessThanOrEqualTo(dialog.Bounds.Width, description.Bounds.Width);
        Assert.IsGreaterThan(1, description.Bounds.Height);
        Assert.IsTrue(dialog.EnumerateVisualsDepthFirst().OfType<Grid>().Any(), "The work item should use a unified content grid.");
        var buttons = dialog.EnumerateVisualsDepthFirst().OfType<Button>().ToArray();
        Assert.IsTrue(buttons.GroupBy(button => button.Bounds.Y).Any(row => row.Count() > 1), "Related actions should be arranged horizontally.");
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
        CollectionAssert.AreEqual(new[] { "New", "Doing", "Done" }, backend.BoardColumns);
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
        public TaskCompletionSource? ListGate;
        public volatile bool DetailStarted;
        public bool NoCurrentSprint;
        public bool ChangedPull;
        public string? PullThreads;
        public bool LongDescription;
        public JsonElement[]? ItemFixtures;
        public int[]? MineIds;
        public int[] BacklogIds = [42];
        public string? BacklogLevels;
        public string? WorkflowStates;
        public bool RejectReview;
        public bool IncompleteReview;
        public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken token, bool patch = false) {
            token.ThrowIfCancellationRequested();
            Requests.Add(new(method, path, JsonSerializer.SerializeToElement(body), patch));
            if (RejectReview && path.Contains("/states?", StringComparison.Ordinal)) {
                throw new HttpRequestException("Azure DevOps Forbidden (403).", null, HttpStatusCode.Forbidden);
            }
            if (ListGate is not null && path.Contains("wiql", StringComparison.Ordinal)) {
                await ListGate.Task.WaitAsync(token);
            }
            if (DetailGate is not null && path.Contains("wit/workitems/", StringComparison.Ordinal) && !path.Contains("/comments?", StringComparison.Ordinal)) {
                DetailStarted = true;
                await DetailGate.Task.WaitAsync(token);
            }
            string json = path.StartsWith("_apis/projects/", StringComparison.Ordinal) ? """{"defaultTeam":{"id":"22222222-2222-2222-2222-222222222222","name":"Default team"}}"""
                : path.Contains("connectionData", StringComparison.Ordinal) ? """{"authenticatedUser":{"id":"11111111-1111-1111-1111-111111111111"}}"""
                : path.Contains("/reviewers/", StringComparison.Ordinal) ? "{}"
                : path.Contains("/threads", StringComparison.Ordinal) ? PullThreads ?? """{"value":[{"comments":[{"content":"Please check the error path","author":{"displayName":"Sam"}}]}]}"""
                : path.Contains("diffs/commits", StringComparison.Ordinal) ? """{"changes":[{"changeType":"edit","item":{"path":"/src/code.cs"}}]}"""
                : path.Contains("pullrequests", StringComparison.Ordinal) ? path.Contains("searchCriteria", StringComparison.Ordinal) ? "{\"value\":[" + Pull + "]}" : ChangedPull ? Pull.Replace("source-sha", "new-source", StringComparison.Ordinal) : Pull
                : path.Contains("/timeline", StringComparison.Ordinal) ? """{"records":[{"name":"Compile","type":"Job","state":"completed","result":"failed","order":1,"startTime":"2026-10-24T10:00:00Z","finishTime":"2026-10-24T10:02:00Z"}]}"""
                : path.Contains("/artifacts", StringComparison.Ordinal) ? """{"value":[{"name":"package","resource":{"downloadUrl":"https://dev.azure.com/fixture/Project/_apis/build/builds/99/artifacts?artifactName=package"}}]}"""
                : path.Contains("build/builds", StringComparison.Ordinal) ? path.Contains("builds?", StringComparison.Ordinal) ? "{\"value\":[" + Build + "]}" : Build
                : path.Contains("$timeframe=current", StringComparison.Ordinal) ? NoCurrentSprint ? """{"value":[]}""" : """{"value":[{"name":"Sprint 2","path":"Project\\Sprint 2"}]}"""
                : path.Contains("teamsettings/iterations", StringComparison.Ordinal) ? """{"value":[{"name":"Sprint 1","path":"Project\\Sprint 1"},{"name":"Sprint 2","path":"Project\\Sprint 2","attributes":{"startDate":"2026-10-12T00:00:00Z","finishDate":"2026-10-25T00:00:00Z"}}]}"""
                : path.Contains("teamfieldvalues", StringComparison.Ordinal) ? """{"defaultValue":"Project\\Core","values":[{"value":"Project\\Core","includeChildren":true}]}"""
                : path.Contains("/backlogs/", StringComparison.Ordinal) ? JsonSerializer.Serialize(new { workItems = BacklogIds.Select(id => new { target = new { id } }) })
                : path.Contains("/backlogs?", StringComparison.Ordinal) ? BacklogLevels ?? """{"value":[{"id":"Microsoft.FeatureCategory","name":"Features","type":"portfolio","workItemTypes":[{"name":"Feature"}]},{"id":"Microsoft.RequirementCategory","name":"Stories","type":"requirement","workItemTypes":[{"name":"User Story"},{"name":"Bug"}]}]}"""
                : path.Contains("/states?", StringComparison.Ordinal) ? WorkflowStates ?? """{"value":[{"name":"Active","category":"InProgress"},{"name":"Done","category":"Completed"},{"name":"Removed","category":"Removed"}]}"""
                : path.Contains("wiql", StringComparison.Ordinal) ? """{"asOf":"2026-10-24T00:00:00Z","workItems":[{"id":42}]}"""
                : path.Contains("boards/", StringComparison.Ordinal) ? """{"columns":[{"name":"New","stateMappings":{"User Story":"New"}},{"name":"Doing","stateMappings":{"User Story":"Active"}},{"name":"Done","stateMappings":{"User Story":"Done"}}],"fields":{"columnField":{"referenceName":"WEF_board_Kanban.Column"}}}"""
                : path.Contains("boards", StringComparison.Ordinal) ? """{"value":[{"id":"board-1","name":"Stories"}]}"""
                : path.Contains("workitemsbatch", StringComparison.Ordinal) ? "{\"value\":[" + Item + "]}"
                : path.Contains("/comments?", StringComparison.Ordinal) ? """{"comments":[{"text":"Comment fixture","createdBy":{"displayName":"Sam"}}]}"""
                : path.Contains("wit/workitems/", StringComparison.Ordinal) ? Item : """{"id":99,"value":[]}""";
            if (LongDescription && path.Contains("wit/workitems/", StringComparison.Ordinal) && !path.Contains("/comments?", StringComparison.Ordinal)) {
                json = json.Replace("Hello &amp; welcome", "Long description " + string.Join(" ", Enumerable.Repeat("readable story content", 35)), StringComparison.Ordinal);
            }
            if (ItemFixtures is not null && path.Contains("wiql", StringComparison.Ordinal)) {
                IEnumerable<int> ids = ItemFixtures.Select(item => item.GetProperty("id").GetInt32());
                if (MineIds is not null && Requests[^1].Body.GetProperty("query").GetString()!.Contains("@Me", StringComparison.Ordinal)) {
                    ids = ids.Where(MineIds.Contains);
                }
                json = JsonSerializer.Serialize(new { asOf = "2026-10-24T00:00:00Z", workItems = ids.Select(id => new { id }) });
            }
            if (ItemFixtures is not null && path.Contains("workitemsbatch", StringComparison.Ordinal)) {
                int[] ids = [.. Requests[^1].Body.GetProperty("ids").EnumerateArray().Select(id => id.GetInt32())];
                var matching = ItemFixtures.Where(item => ids.Contains(item.GetProperty("id").GetInt32()));
                if (IncompleteReview) {
                    matching = matching.Take(Math.Max(0, ids.Length - 1));
                }
                json = JsonSerializer.Serialize(new { value = matching });
            }
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        public void Dispose() =>
            Disposed = true;
        private const string Pull = """{"pullRequestId":12,"title":"Fix collector","status":"active","repository":{"id":"repo-id","name":"Repo"},"lastMergeSourceCommit":{"commitId":"source-sha"},"lastMergeTargetCommit":{"commitId":"target-sha"},"reviewers":[{"displayName":"Sam","vote":10}]}""";
        private const string Build = """{"id":99,"buildNumber":"20261024.1","definition":{"name":"CI"},"status":"completed","result":"failed","startTime":"2026-10-24T10:00:00Z","finishTime":"2026-10-24T10:02:00Z"}""";
        private const string Item = """{"id":42,"rev":3,"fields":{"System.Title":"Implement collector","System.WorkItemType":"User Story","System.State":"Active","System.Description":"<p>Hello &amp; welcome</p>","System.AssignedTo":{"displayName":"Sam"},"WEF_board_Kanban.Column":"Doing"},"relations":[{"rel":"System.LinkTypes.Hierarchy-Reverse","url":"https://dev.azure.com/fixture/_apis/wit/workItems/7"},{"rel":"System.LinkTypes.Hierarchy-Forward","url":"https://dev.azure.com/fixture/_apis/wit/workItems/8"},{"rel":"AttachedFile","url":"https://dev.azure.com/fixture/_apis/wit/attachments/abc?fileName=design.png","attributes":{"name":"design.png"}},{"rel":"Hyperlink","url":"https://example.test/case/123","attributes":{"name":"external case"}}]}""";
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
