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
    public async Task BoardLevelDefaultsToRequirementsAndExplicitPortfolioSelectionUsesItsOwnMappings() {
        var client = new FixtureClient {
            Response = path => path.Contains("/boards?", StringComparison.Ordinal)
                ? """{"value":[{"id":"epics","name":"Epics"},{"id":"features","name":"Features"},{"id":"stories","name":"Stories"}]}"""
                : path.Contains("/boards/features?", StringComparison.Ordinal)
                    ? """{"columns":[{"name":"Feature queue","stateMappings":{"Feature":"New"}},{"name":"Delivered","stateMappings":{"Feature":"Done"}}],"fields":{"columnField":{"referenceName":"WEF_features.Column"}}}"""
                    : null
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var boards = await backend.LoadAsync(0, 0, default);
        Assert.AreEqual("Stories", boards.Items.Single().Cells[0]);
        CollectionAssert.AreEqual(new[] { "Epics", "Features", "Stories" }, backend.BoardLevelOperations().Select(operation => operation.Label).ToArray());
        await backend.BoardLevelOperations().Single(operation => operation.Label == "Features").Execute("", default);
        boards = await backend.LoadAsync(0, 0, default);
        Assert.AreEqual("Features", boards.Items.Single().Cells[0]);
        await backend.Operations(0, boards.Items[0]).Single().Execute("", default);
        await backend.LoadAsync(1, 0, default);
        string wiql = client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
        Assert.Contains("[System.WorkItemType] IN ('Feature')", wiql);
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", wiql);
        CollectionAssert.AreEqual(new[] { "Feature queue", "Delivered" }, backend.BoardColumns);
        Assert.IsTrue(backend.Operations(1, null).Any(operation => operation.Label == "Create Feature"));
        Assert.IsFalse(backend.Operations(1, null).Any(operation => operation.Label == "Create User Story"));
        Assert.IsFalse(client.Requests.Any(request => request.Method != HttpMethod.Get && !request.Path.Contains("wiql", StringComparison.Ordinal)
            && !request.Path.Contains("workitemsbatch", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task WorkItemTypeFilterDefaultsToStoriesAndAllTypesPreservesOtherFilters() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project", "Core");
        await backend.LoadAsync(1, 0, default);
        Assert.Contains("[System.WorkItemType] = 'User Story'", Query());
        CollectionAssert.AreEquivalent(new[] { "All types", "User Story", "Feature", "Epic", "Task", "Bug" },
            backend.WorkItemTypeOperations().Select(operation => operation.Label).ToArray());
        await backend.Operations(1, null).First().Execute("", default);
        await backend.WorkItemTypeOperations().Single(operation => operation.Label == "Epic").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.Contains("[System.WorkItemType] = 'Epic'", Query());
        Assert.Contains("[System.AssignedTo] = @Me", Query());
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", Query());
        Assert.IsTrue(backend.Operations(1, null).Any(operation => operation.Label == "Create Epic"));
        await backend.WorkItemTypeOperations().Single(operation => operation.Label == "All types").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.DoesNotContain("[System.WorkItemType]", Query());
        Assert.Contains("[System.AssignedTo] = @Me", Query());
        Assert.Contains("[System.IterationPath]", Query());
        Assert.Contains("[System.AreaPath]", Query());
        await backend.SprintOperations(1).Single(operation => operation.Label == "All sprints").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.DoesNotContain("[System.IterationPath]", Query());
        Assert.AreEqual("All types", backend.WorkItemTypeLabel);
        Assert.AreEqual(1, client.Requests.Count(request => request.Path.Contains("workitemtypes?", StringComparison.Ordinal)));
        string Query() => client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
    }

    [TestMethod]
    public async Task TypeFilterClearsBoardColumnsAndQuotesCustomProcessTypes() {
        var client = new FixtureClient {
            Response = path => path.Contains("workitemtypes?", StringComparison.Ordinal)
                ? """{"value":[{"name":"User Story"},{"name":"Owner's Request"},{"name":"Disabled","isDisabled":true}]}""" : null
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var board = (await backend.LoadAsync(0, 0, default)).Items.Single();
        await backend.Operations(0, board).Single().Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.IsTrue(backend.BoardOpen);
        Assert.IsFalse(backend.WorkItemTypeOperations().Any(operation => operation.Label == "Disabled"));
        await backend.WorkItemTypeOperations().Single(operation => operation.Label == "Owner's Request").Execute("", default);
        await backend.LoadAsync(1, 0, default);
        Assert.IsFalse(backend.BoardOpen);
        Assert.IsEmpty(backend.BoardColumns);
        string wiql = client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
        Assert.Contains("[System.WorkItemType] = 'Owner''s Request'", wiql);
        Assert.Contains("[System.AreaPath]", wiql);
    }

    [TestMethod]
    public async Task ScrumProcessDefaultsToConfiguredRequirementTypeAndLevelRatherThanEnglishStories() {
        var client = new FixtureClient {
            BacklogLevels = """{"value":[{"id":"requirements","name":"Backlog items","type":"requirement","workItemTypes":[{"name":"Product Backlog Item"}]}]}""",
            Response = path => path.Contains("workitemtypes?", StringComparison.Ordinal) ? """{"value":[{"name":"Product Backlog Item"},{"name":"Bug"}]}"""
                : path.Contains("processconfiguration?", StringComparison.Ordinal) ? """{"requirementBacklog":{"workItemTypes":[{"name":"Product Backlog Item"}]}}"""
                : path.Contains("/boards?", StringComparison.Ordinal) ? """{"value":[{"id":"features","name":"Features"},{"id":"requirements","name":"Backlog items"}]}""" : null
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        Assert.AreEqual("Backlog items", (await backend.LoadAsync(0, 0, default)).Items.Single().Cells[0]);
        await backend.LoadAsync(1, 0, default);
        Assert.AreEqual("Product Backlog Item", backend.WorkItemTypeLabel);
        Assert.Contains("[System.WorkItemType] = 'Product Backlog Item'", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"value\":[]}")]
    public async Task MissingProcessTypesReportAnErrorRatherThanSilentlyLoadingAllTypes(string response) {
        var client = new FixtureClient { Response = path => path.Contains("workitemtypes?", StringComparison.Ordinal) ? response : null };
        using var backend = new AzureDevOpsBackend(client, "Project");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => backend.LoadAsync(1, 0, default));
        Assert.IsFalse(client.Requests.Any(request => request.Path.Contains("wiql", StringComparison.Ordinal)));
        Assert.IsFalse(backend.AllWorkItemTypes);
    }

    [TestMethod]
    [DataRow("asTasks", false)]
    [DataRow("off", false)]
    [DataRow("asRequirements", true)]
    public async Task StoryBoardRespectsTeamBugBehaviorAndCustomBugCategory(string behavior, bool includesBugs) {
        var client = new FixtureClient {
            Response = path => path.Contains("teamsettings?", StringComparison.Ordinal) ? JsonSerializer.Serialize(new { bugsBehavior = behavior })
                : path.Contains("/boards/board-1?", StringComparison.Ordinal)
                    ? """{"columns":[{"name":"New","stateMappings":{"User Story":"New","Defect":"New"}}]}"""
                    : path.Contains("workitemtypecategories/Microsoft.BugCategory", StringComparison.Ordinal)
                        ? """{"workItemTypes":[{"name":"Defect"}]}""" : null
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        var board = (await backend.LoadAsync(0, 0, default)).Items.Single();
        await backend.Operations(0, board).Single().Execute("", default);
        await backend.LoadAsync(1, 0, default);
        string wiql = client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!;
        Assert.AreEqual(includesBugs, wiql.Contains("'Defect'", StringComparison.Ordinal));
        Assert.Contains("'User Story'", wiql);
    }

    [TestMethod]
    public async Task TypeAndLevelDropdownsAreKeyboardAccessibleAndKeepSelectionsAcrossRefresh() {
        var client = new FixtureClient {
            Response = path => path.Contains("/boards?", StringComparison.Ordinal)
                ? """{"value":[{"id":"epics","name":"Epics"},{"id":"features","name":"Features"},{"id":"stories","name":"Stories"}]}""" : null
        };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), readOnly: true);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 45);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        ui.App.Focus(app.Surface);
        ui.Text("l", () => ui.App.FocusedElement?.GetType().Name == "ContextMenuList");
        Assert.Contains("Features", ui.Output);
        ui.Key(XenoAtom.Terminal.TerminalKey.Home, () => true);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => !app.Busy && app.Surface.Model.Rows.Single().Cells[0] == "Epics");
        app.SwitchView(1);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        ui.App.Focus(app.Surface);
        ui.Text("t", () => ui.App.FocusedElement?.GetType().Name == "ContextMenuList");
        Assert.Contains("All types", ui.Output);
        ui.Key(XenoAtom.Terminal.TerminalKey.Home, () => true);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => !app.Busy && app.Surface.ToolbarCommands.Any(command => command.Label == "T Type: All types"));
        Assert.IsTrue(app.Surface.ToolbarCommands.Any(command => command.Label.Contains("Project\\Sprint 2", StringComparison.Ordinal)));
        ui.App.Focus(app.Surface);
        ui.Text("k", () => app.Surface.Status.Contains("list-only", StringComparison.Ordinal));
        Assert.IsFalse(ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.IsFalse(app.Surface.ToolbarCommands.Any(command => command.Label == "K Kanban"));
        app.SwitchView(0);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        Assert.AreEqual("Epics", app.Surface.Model.Rows.Single().Cells[0]);
        Assert.IsFalse(client.Requests.Any(request => request.Patch));
    }

    [TestMethod]
    public async Task WorkItemsDefaultToCurrentSprintAndCanClearItWithoutFilteringBacklog() {
        var client = new FixtureClient();
        using var backend = new AzureDevOpsBackend(client, "Project");
        var page = await backend.LoadAsync(1, 0, default);
        Assert.Contains("Project\\Sprint 2", page.Summary);
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        await backend.SprintOperations(1).Single(operation => operation.Label == "All sprints").Execute("", default);
        foreach (int view in new[] { 1, 2, 3 }) {
            page = await backend.LoadAsync(view, 0, default);
            Assert.Contains("All sprints", page.Summary);
            Assert.DoesNotContain("[System.IterationPath]", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
        }
        page = await backend.LoadAsync(6, 0, default);
        Assert.Contains("all sprints", page.Summary);
        Assert.AreEqual("Order", page.Columns[^1]);
        Assert.AreEqual("1", page.Items[0].Cells[^1]);
        await backend.SprintOperations(1).First().Execute("", default);
        await backend.LoadAsync(3, 0, default);
        Assert.Contains("[System.IterationPath] = 'Project\\Sprint 2'", client.Requests.Last(request => request.Path.Contains("wiql", StringComparison.Ordinal)).Body.GetProperty("query").GetString()!);
    }

    [TestMethod]
    public async Task VisibleTabsMapNumbersToCanonicalViewsAndBacklogDefaultsToNumericOrder() {
        var client = new FixtureClient { BacklogIds = [42, 9], ItemFixtures = [ReviewItem(42, "User Story", "Active"), ReviewItem(9, "User Story", "Active")] };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 1, excludeTabs: ["Tasks"]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 40);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        CollectionAssert.AreEqual(new[] { "Boards", "Work items", "Stories", "Backlog", "Pull requests", "Pipelines" }, app.Surface.Tabs);
        ui.App.Focus(app.Surface);
        ui.Text("4", () => !app.Busy && app.View == 6);
        Assert.AreEqual("Order", app.Surface.Model.Columns[app.Surface.Model.SortColumn]);
        Assert.IsFalse(app.Surface.Model.Descending);
        CollectionAssert.AreEqual(new[] { "42", "9" }, app.Surface.Model.Visible.Select(row => ((DeveloperItem)row.Original).Key).ToArray());
        int requests = client.Requests.Count;
        app.SwitchView(3);
        Assert.HasCount(requests, client.Requests);
        Assert.Contains("excluded", app.Surface.Status);
    }

    [TestMethod]
    public void ExcludedInitialTabFallsBackAndAllExcludedIsRejected() {
        var client = new FixtureClient();
        using (var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"), initialView: 3, excludeTabs: ["Boards", "Tasks"])) {
            Assert.AreEqual(1, app.View);
        }
        using var backend = new AzureDevOpsBackend(new FixtureClient(), "Project");
        Assert.ThrowsExactly<ArgumentException>(() => new DeveloperApp(backend, excludeTabs: ["Boards", "WorkItems", "Stories", "Tasks", "Backlog", "PullRequests", "Pipelines"]));
    }

    [TestMethod]
    public async Task BoardsLandingDashboardRendersNativeBarsAndDoesNotRequireAnalytics() {
        var client = new FixtureClient();
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"));
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 45);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        Assert.Contains("TEAM DASHBOARD", ui.Output);
        Assert.Contains("Current sprint by state", ui.Output);
        Assert.Contains("█", ui.Output);
        Assert.HasCount(0, client.AnalyticsQueries);
        Assert.IsTrue(app.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Charts.Chart>().Single().PreferTerminalCells);
    }

    [TestMethod]
    public async Task BoardsAnalyticsOpensFullHeightAndCanSwitchRangeWithoutACurrentSprint() {
        var client = new FixtureClient { NoCurrentSprint = true };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"));
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 45);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        app.Surface.ToolbarCommands.Single(command => command.Label == "Y Boards analytics").Execute();
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        var dialog = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        Assert.IsGreaterThan(30, dialog.Bounds.Height);
        Assert.Contains("No dated current sprint", ui.Output);
        var range = dialog.EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock { Text: "Last 30 days" });
        ui.App.Focus(range);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => !app.Busy && client.AnalyticsQueries.Count > 0);
        Assert.Contains("Last 30 days", ui.Output);
        ui.Key(XenoAtom.Terminal.TerminalKey.Escape, () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Assert.AreEqual(0, app.View);
    }

    [TestMethod]
    public async Task CumulativeFlowRendersCellsAndLabelsInsideTheAnalyticsDialog() {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var client = new FixtureClient {
            Analytics = query => query.Contains("StateCategory", StringComparison.Ordinal) ? """{"value":[]}""" :
                JsonSerializer.Serialize(new { value = new[] {
                    new { DateValue = today, ColumnName = "New", Count = 5 },
                    new { DateValue = today, ColumnName = "Done", Count = 3 }
                } })
        };
        using var app = new DeveloperApp(new AzureDevOpsBackend(client, "Project"));
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 110, 45);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        int before = ui.Output.Length;
        app.Surface.ToolbarCommands.Single(command => command.Label == "Y Boards analytics").Execute();
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy; }, 5000));
        string rendered = ui.Output[before..];
        Assert.Contains("Cumulative flow", rendered);
        Assert.Contains("Done", rendered);
        Assert.Contains("█", rendered);
        var chart = ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single().EnumerateVisualsDepthFirst().OfType<fullscreen.Charts.Chart>().Single();
        Assert.IsTrue(chart.ShowLegend);
        Assert.AreEqual(2, chart.Series.Count);
    }

    [TestMethod]
    public async Task AnalyticsScopesTeamBoardRangeAndUsesOnlyValidSprintBaselines() {
        var client = new FixtureClient();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly start = today.AddDays(-5);
        string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        client.Response = path => path.Contains("teamsettings/iterations", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { value = new[] { new { id = "sprint-id", name = "Current", path = "Project\\Current", attributes = new { startDate = Iso(start) + "T00:00:00Z", finishDate = Iso(today.AddDays(5)) + "T00:00:00Z" } } } })
            : null;
        client.Analytics = query => query.Contains("StateCategory", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { value = new[] {
                new { DateValue = Iso(start), Iteration = new { IterationPath = "Project\\Current" }, StateCategory = "Proposed", Count = 5 },
                new { DateValue = Iso(today), Iteration = new { IterationPath = "Project\\Current" }, StateCategory = "Completed", Count = 3 },
                new { DateValue = Iso(today), Iteration = new { IterationPath = "Project\\Current" }, StateCategory = "InProgress", Count = 4 }
            } })
            : JsonSerializer.Serialize(new { value = new[] {
                new { DateValue = Iso(today), ColumnName = "Done", Count = 3 },
                new { DateValue = Iso(today), ColumnName = "New", Count = 4 }
            } });
        using var backend = new AzureDevOpsBackend(client, "Project");
        var current = await backend.LoadBoardsAnalyticsAsync(false, null, default);
        Assert.HasCount(1, current.Velocity);
        Assert.AreEqual(new SprintVelocity("Current", 5, 3, 4), current.Velocity[0]);
        CollectionAssert.AreEqual(new[] { "New", "Done" }, current.Flow.Select(row => row.Column).ToArray());
        Assert.Contains("Team/TeamId eq 22222222-2222-2222-2222-222222222222", client.AnalyticsQueries[0]);
        Assert.Contains("BoardName eq 'Stories'", client.AnalyticsQueries[0]);
        Assert.Contains("Iteration/IterationPath eq 'Project\\Current'", client.AnalyticsQueries[0]);
        Assert.Contains("completed-late", current.Notice);
        client.AnalyticsQueries.Clear();
        await backend.LoadBoardsAnalyticsAsync(true, null, default);
        Assert.DoesNotContain("Iteration/IterationPath eq", client.AnalyticsQueries[0]);
        Assert.Contains(Iso(today.AddDays(-29)) + "Z", client.AnalyticsQueries[0]);
        client.Analytics = query => query.Contains("StateCategory", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { value = new[] { new { DateValue = Iso(today), Iteration = new { IterationPath = "Project\\Current" }, StateCategory = "Completed", Count = 3 } } })
            : """{"value":[]}""";
        Assert.HasCount(0, (await backend.LoadBoardsAnalyticsAsync(true, null, default)).Velocity);
        client.Analytics = _ => "{}";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => backend.LoadBoardsAnalyticsAsync(true, null, default));
    }

    [TestMethod]
    public async Task CapacityUsesWorkingDaysAndUnionOfDaysOffWithoutConvertingPoints() {
        var client = new FixtureClient();
        client.Response = path => path.Contains("teamsettings/iterations", StringComparison.Ordinal)
            ? path.Contains("/capacities", StringComparison.Ordinal)
                ? """{"teamMembers":[{"activities":[{"capacityPerDay":6},{"capacityPerDay":1}],"daysOff":[{"start":"2026-10-14","end":"2026-10-15"}]}]}"""
                : path.Contains("/teamdaysoff", StringComparison.Ordinal)
                    ? """{"daysOff":[{"start":"2026-10-13","end":"2026-10-14"}]}"""
                    : """{"value":[{"id":"sprint-id","name":"Sprint","path":"Project\\Sprint","attributes":{"startDate":"2026-10-12","finishDate":"2026-10-18"}}]}"""
            : path.Contains("teamsettings?", StringComparison.Ordinal)
                ? """{"workingDays":["monday","tuesday","wednesday","thursday","friday"]}"""
                : path.Contains("workitemsbatch", StringComparison.Ordinal)
                    ? """{"value":[{"id":42,"fields":{"System.WorkItemType":"User Story","Microsoft.VSTS.Scheduling.StoryPoints":13,"Microsoft.VSTS.Scheduling.OriginalEstimate":8}}]}"""
                    : null;
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.LoadAsync(2, 0, default);
        var capacity = await backend.LoadCapacityAsync("Project\\Sprint", default);
        Assert.AreEqual(14d, capacity.Hours);
        Assert.AreEqual(8d, capacity.Planned.Single().Hours);
        Assert.AreEqual(1, capacity.Planned.Single().Reported);
        Assert.Contains("Story points are not hours", capacity.Notice);
        client.Response = path => path.Contains("teamsettings?", StringComparison.Ordinal) ? """{"workingDays":[]}""" : null;
        Assert.IsNull((await backend.LoadCapacityAsync("Project\\Sprint", default)).Hours);
    }

    [TestMethod]
    [DataRow("https://evil.test/value")]
    [DataRow("https://analytics.dev.azure.com/another/Project/_odata/v3.0-preview/next")]
    [DataRow("http://analytics.dev.azure.com/fixture/Project/_odata/v3.0-preview/next")]
    [DataRow("https://analytics.dev.azure.com/fixture/Project/_odata/v3.0-preview/WorkItemBoardSnapshot")]
    public async Task AnalyticsTransportRejectsUnsafeOrRepeatedContinuationsBeforeSendingCredentials(string next) {
        int requests = 0;
        using var handler = new FixtureHandler {
            Response = _ => {
                requests++;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = Array.Empty<object>(), ["@odata.nextLink"] = next })) };
            }
        };
        using var client = new AzureDevOpsClient("fixture", "fixture-token", true, handler);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.ReadAnalyticsAsync("Project", "WorkItemBoardSnapshot", default));
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task AnalyticsTransportAggregatesPagesAuthenticatesAndReportsPermissionFailures() {
        int requests = 0;
        using var handler = new FixtureHandler {
            Response = request => {
                requests++;
                Assert.AreEqual("Bearer fixture-token", request.Headers.Authorization?.ToString());
                return new(HttpStatusCode.OK) { Content = new StringContent(requests == 1
                    ? """{"value":[{"Count":1}],"@odata.nextLink":"next"}"""
                    : """{"value":[{"Count":2}]}""") };
            }
        };
        using var client = new AzureDevOpsClient("fixture", "fixture-token", true, handler);
        var response = await client.ReadAnalyticsAsync("Project", "WorkItemBoardSnapshot", default);
        Assert.AreEqual(2, response.GetProperty("value").GetArrayLength());
        handler.Response = _ => new(HttpStatusCode.Forbidden) { Content = new StringContent("private-server-detail") };
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.ReadAnalyticsAsync("Project", "WorkItemBoardSnapshot", default));
        Assert.Contains("Analytics returned 403", error.Message);
        Assert.DoesNotContain("private-server-detail", error.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ReadAnalyticsAsync("Project", "WorkItemBoardSnapshot", cancelled.Token));
    }

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
    public async Task BacklogOffersHiddenLevelsExplicitlyWithoutChangingTeamSettings() {
        var client = new FixtureClient {
            BacklogLevels = """{"value":[{"id":"hidden","name":"Hidden Stories","type":"requirement","isHidden":true},{"id":"features","name":"Features","type":"portfolio","defaultWorkItemType":{"name":"Feature"}}]}"""
        };
        using var backend = new AzureDevOpsBackend(client, "Project");
        await backend.LoadAsync(6, 0, default);
        Assert.AreEqual("Hidden Stories", backend.BacklogLabel);
        Assert.HasCount(2, backend.BacklogOperations());
        Assert.IsTrue(backend.BacklogOperations().Any(operation => operation.Label.Contains("hidden in team navigation", StringComparison.Ordinal)));
        await backend.BacklogOperations().Single(operation => operation.Label == "Features").Execute("", default);
        await backend.LoadAsync(6, 0, default);
        Assert.IsTrue(backend.Operations(6, null).Any(operation => operation.Label == "Create Feature"));
        client.BacklogLevels = """{"value":[]}""";
        var empty = await backend.LoadAsync(6, 0, default);
        Assert.Contains("No backlog levels", empty.Summary);
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
    public async Task PipelineMonitorRetainsResultColorsAndReturnsToManager() {
        using var app = new DeveloperApp(new AzureDevOpsBackend(new FixtureClient(), "Project"), initialView: 5);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick(), 120, 40);
        Assert.IsTrue(SpinWait.SpinUntil(() => { ui.Tick(); return !app.Busy && app.Surface.Model.Rows.Count > 0; }, 5000));
        Assert.AreEqual(app.Surface.Palette.Error, app.Surface.RowAccent!(app.Surface.Model.Current!));
        ui.App.Focus(app.Surface);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter,
            () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Any(surface => surface != app.Surface && surface.Model.Rows.Count > 0));
        var monitor = ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Single(surface => surface != app.Surface);
        Assert.AreEqual(app.Surface.Palette.Error, monitor.CellAccent!(monitor.Model.Current!, 2));
        Assert.Contains("Compile", monitor.Model.Current!.Cells[1]);
        ui.Key(XenoAtom.Terminal.TerminalKey.Backspace,
            () => !ui.App.Root.EnumerateVisualsDepthFirst().OfType<fullscreen.Tables.TableSurface>().Any(surface => surface != app.Surface));
        Assert.IsFalse(app.Surface.ExitRequested);
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
            Assert.IsFalse(client.Requests.Any(x => x.Path.Contains("pullrequests", StringComparison.Ordinal)
                || x.Path.Contains("build/builds", StringComparison.Ordinal) || x.Path.Contains("/workItems?", StringComparison.Ordinal)));
            Assert.IsTrue(client.Requests.Any(x => x.Path.Contains("boards", StringComparison.Ordinal)));
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
        public Func<string, string?>? Response;
        public Func<string, string>? Analytics;
        public List<string> AnalyticsQueries { get; } = [];
        public Task<JsonElement> ReadAnalyticsAsync(string project, string query, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            query = Uri.UnescapeDataString(query);
            AnalyticsQueries.Add(query);
            return Task.FromResult(JsonSerializer.Deserialize<JsonElement>(Analytics?.Invoke(query) ?? """{"value":[]}"""));
        }
        public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken token, bool patch = false) {
            token.ThrowIfCancellationRequested();
            Requests.Add(new(method, path, JsonSerializer.SerializeToElement(body), patch));
            if (Response?.Invoke(path) is { } overridden) {
                return JsonSerializer.Deserialize<JsonElement>(overridden);
            }
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
                : path.Contains("teamsettings?", StringComparison.Ordinal) ? """{"bugsBehavior":"asTasks","workingDays":[]}"""
                : path.Contains("workitemtypecategories/Microsoft.BugCategory", StringComparison.Ordinal) ? """{"workItemTypes":[{"name":"Bug"}]}"""
                : path.Contains("workitemtypes?", StringComparison.Ordinal) ? """{"value":[{"name":"User Story"},{"name":"Feature"},{"name":"Epic"},{"name":"Task"},{"name":"Bug"}]}"""
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
        public Func<HttpRequestMessage, HttpResponseMessage>? Response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization = request.Headers.Authorization?.ToString();
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Address = request.RequestUri?.AbsoluteUri;
            if (Response is not null) {
                return Task.FromResult(Response(request));
            }
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
