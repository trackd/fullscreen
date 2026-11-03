using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace fullscreen.Development;
/// <summary>Loads Azure DevOps resources and actions for the developer TUI.</summary>
public sealed partial class AzureDevOpsBackend : IDeveloperBackend, IDisposable {
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement> Details = new();
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Discussions = new();
    private bool _mine;
    internal bool MineOnly => _mine;

    private readonly IAzureDevOpsClient _client;
    private readonly string _project;
    private readonly string _team;
    private string _defaultTeam = "";
    private readonly string _storyType;
    private readonly int _pageSize;
    private readonly Dictionary<(int View, int Page), int> _cursors = [];
    private readonly Dictionary<int, string?> _buildCursors = [];
    private string _boardColumn = "System.BoardColumn";
    private string? _board;
    private string[] _boardTypes = [];
    private string[] _boardColumns = [];
    internal string[] BoardColumns => _boardColumns;
    internal bool BoardOpen => _board is not null;
    private string[] _areas = [];
    private bool _areasLoaded;
    private string _defaultArea = "";
    private string? _iterationSelection;
    private string? _currentIteration;
    private (string Name, string Path)[] _iterations = [];
    private readonly Dictionary<string, JsonElement> _iterationDetails = new(StringComparer.OrdinalIgnoreCase);
    internal string SprintLabel => _iterationSelection == "" ? "All sprints"
        : _iterationSelection ?? _currentIteration ?? "No current sprint · all items";
    /// <summary>Gets the displayed heading.</summary>
    public string Title => $"AZURE DEVOPS · {_project}" + (_board is null ? "" : $" · {_board}") + (_mine ? " · ASSIGNED TO ME" : "");
    /// <summary>Gets the available tab headings.</summary>
    public string[] Tabs { get; } = ["Boards", "Work items", "Stories", "Tasks", "Pull requests", "Pipelines", "Backlog"];

    internal static bool IsWorkItemView(int view) => view is >= 1 and <= 3 or 6;

    /// <summary>Initializes a new AzureDevOpsBackend instance.</summary>
    /// <param name="client">The Azure DevOps request client.</param>
    /// <param name="project">The Azure DevOps project name.</param>
    /// <param name="team">The Azure DevOps team name. Defaults to null.</param>
    /// <param name="pageSize">The number of entries that fit in the visible page. Defaults to 50.</param>
    /// <param name="storyType">The work item type treated as a story. Defaults to &quot;User
    /// Story&quot;.</param>
    public AzureDevOpsBackend(
        IAzureDevOpsClient client,
        string project,
        string? team = null,
        int pageSize = 50,
        string storyType = "User Story"
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyType);
        if (pageSize is < 1 or > 100) {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        _client = client;
        _project = project;
        _team = team ?? "";
        _pageSize = pageSize;
        _storyType = storyType;
    }

    private static string E(string text) =>
        Uri.EscapeDataString(text);
    private static string Q(string text) =>
        "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
    private string Api(string resource) =>
        E(_project) + "/_apis/" + resource;
    private string TeamApi(string resource) {
        string team = _team.Length == 0 ? _defaultTeam : _team;
        return E(_project) + "/" + (team.Length == 0 ? "" : E(team) + "/") + "_apis/work/" + resource;
    }

    private async Task EnsureTeam(CancellationToken token) {
        if (_team.Length != 0 || _defaultTeam.Length != 0) {
            return;
        }
        var project = await Get("_apis/projects/" + E(_project), token).ConfigureAwait(false);
        _defaultTeam = S(P(project, "defaultTeam"), "id");
        if (_defaultTeam.Length == 0) {
            throw new InvalidOperationException("The project's default team could not be resolved. Specify -Team and retry.");
        }
    }
    private Task<JsonElement> Get(
        string path,
        CancellationToken token
    ) =>
        _client.SendAsync(
            HttpMethod.Get,
            path + (path.Contains('?') ? "&" : "?") +
        "api-version=7.1",
            null,
            token
        );
    private static string S(
        JsonElement value,
        string name
    ) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString() : "";
    private static JsonElement P(
        JsonElement value,
        string name
    ) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;
    private static JsonElement.ArrayEnumerator Values(
        JsonElement value,
        string name = "value"
    ) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result)
        && result.ValueKind == JsonValueKind.Array ? result.EnumerateArray() : default;
    private static string Identity(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? S(value, "displayName") : value.ToString();
    private static int Number(
        JsonElement value,
        string name
    ) =>
        int.TryParse(S(value, name), CultureInfo.InvariantCulture, out int result) ? result : 0;
    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.LoadAsync"/>
    public async Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) {
        if (page < 0 || page > 10000) {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (view == 0) {
            var boards = await Get(TeamApi("boards"), token).ConfigureAwait(false);
            return new(
                [
                    "Board",
                    "ID"
                ],
                [..Values(boards).Skip(page * _pageSize).Take(_pageSize).Select(x => new DeveloperItem(S(x, "id"), [
                    S(x, "name"),
                    S(x, "id")
                ], x.Clone()))],
                "Team boards · A opens board cards"
            );
        }

        if (view is >= 1 and <= 3) {
            return await WorkItems(view, page, token).ConfigureAwait(false);
        }
        if (view == 6) {
            return await Backlog(page, token).ConfigureAwait(false);
        }

        if (view == 4) {
            var prs = await Get(
                Api($"git/pullrequests?searchCriteria.status=active&$top={_pageSize}&$skip={page * _pageSize}"),
                token
            ).ConfigureAwait(false);
            return new(
                [
                    "ID",
                    "Title",
                    "Repository",
                    "Author",
                    "Source",
                    "Target",
                    "Draft"
                ],
                [..Values(prs).Select(x => new DeveloperItem(
                    S(x, "pullRequestId"),
                    [
                        S(x, "pullRequestId"),
                        S(x, "title"),
                        S(P(x, "repository"), "name"),
                        Identity(P(x, "createdBy")),
                        S(x, "sourceRefName"),
                        S(x, "targetRefName"),
                        S(x, "isDraft")
                    ],
                    x.Clone()
                ))],
                "Active project pull requests · inspect for description and reviewers"
            );
        }

        if (view == 5) {
            if (page == 0) {
                _buildCursors.Clear();
            }
            else if (!_buildCursors.TryGetValue(page, out var next) || next is null) {
                return new(
                    [
                        "Run",
                        "Pipeline",
                        "Status",
                        "Result",
                        "Branch",
                        "Started"
                    ],
                    [],
                    "No next page; visit the previous page first."
                );
            }

            string cursor = page == 0 ? "" : "&continuationToken=" + E(_buildCursors[page]!);
            var builds = await Get(Api($"build/builds?$top={_pageSize}&queryOrder=queueTimeDescending{cursor}"), token).ConfigureAwait(false);
            _buildCursors[page + 1] = _client.ContinuationToken;
            return new(
                [
                    "Run",
                    "Pipeline",
                    "Status",
                    "Result",
                    "Branch",
                    "Started"
                ],
                [..Values(builds).Select(x => new DeveloperItem(
                    S(x, "id"),
                    [
                        S(x, "buildNumber"),
                        S(P(x, "definition"), "name"),
                        GitHubBackend.StatusLabel(S(x, "status"), S(x, "result")),
                        S(x, "result"),
                        S(x, "sourceBranch"),
                        S(x, "startTime")
                    ],
                    x.Clone()
                ))],
                "Latest pipeline runs · inspect jobs and steps"
            );
        }

        throw new ArgumentOutOfRangeException(nameof(view));
    }

    private async Task<DeveloperPage> WorkItems(int view, int page, CancellationToken token) {
        if (view is 2 or 3 && page == 0) {
            await LoadIterations(token).ConfigureAwait(false);
        }
        if (!_areasLoaded && (_team.Length != 0 || _board is not null)) {
            var settings = await Get(TeamApi("teamsettings/teamfieldvalues"), token).ConfigureAwait(false);
            _defaultArea = S(settings, "defaultValue");
            _areas = [.. Values(settings, "values").Select(x => "[System.AreaPath] " + (S(x, "includeChildren") == "True" || S(x, "includeChildren") == "true" ? "UNDER " : "= ") + Q(S(x, "value")))];
            _areasLoaded = true;
        }

        string where = "[System.TeamProject] = @project";
        string? iteration = _iterationSelection ?? _currentIteration;
        if (view is 2 or 3 && !string.IsNullOrEmpty(iteration)) {
            where += " AND [System.IterationPath] = " + Q(iteration);
        }
        if (_mine) {
            where += " AND [System.AssignedTo] = @Me";
        }

        if (_areasLoaded && _areas.Length == 0) {
            where += " AND [System.Id] = 0";
        }

        if (_areas.Length != 0) {
            where += " AND (" + string.Join(" OR ", _areas) + ")";
        }

        if (view == 2) {
            where += " AND [System.WorkItemType] = " + Q(_storyType);
        }

        if (view == 3) {
            where += " AND [System.WorkItemType] = 'Task'";
        }

        if (view == 1 && _boardTypes.Length != 0) {
            where += " AND [System.WorkItemType] IN (" + string.Join(",", _boardTypes.Select(Q)) + ")";
        }

        if (page == 0) {
            foreach (var key in _cursors.Keys.Where(x => x.View == view).ToArray()) {
                _cursors.Remove(key);
            }
        }
        else if (!_cursors.TryGetValue((view, page), out int cursor)) {
            return new(WorkColumns, [], "Visit the previous page first; paging resets on first-page refresh.");
        }
        else {
            where += " AND [System.Id] < " + cursor.ToString(CultureInfo.InvariantCulture);
        }

        var query = await _client.SendAsync(
            HttpMethod.Post,
            Api($"wit/wiql?$top={_pageSize}&api-version=7.1"),
            new {
                query = "SELECT [System.Id] FROM WorkItems WHERE " + where +
            " ORDER BY [System.Id] DESC"
            },
            token
        ).ConfigureAwait(false);
        int[] ids = [.. Values(query, "workItems").Select(x => Number(x, "id"))];
        if (ids.Length == 0) {
            return new(WorkColumns, [], "No work items in this page." + (view is 2 or 3 ? " · " + SprintLabel + " · I choose sprint" : ""));
        }

        _cursors[(view, page + 1)] = ids[^1];
        var rows = await LoadWorkItemRows(ids, _boardColumn, token, S(query, "asOf")).ConfigureAwait(false);
        return new(
            WorkColumns,
            rows,
            (_board is null ? "Project work items" : "Board · " + _board) +
            (view is 2 or 3 ? " · " + SprintLabel : "") + " · descending ID · C columns · / filters",
            WorkWidths
        );
    }

    private async Task<DeveloperItem[]> LoadWorkItemRows(int[] ids, string column, CancellationToken token, string? asOf = null) {
        var request = new Dictionary<string, object> { ["ids"] = ids, ["$expand"] = "Relations" };
        if (!string.IsNullOrEmpty(asOf)) {
            request["asOf"] = asOf;
        }
        var batch = await _client.SendAsync(
            HttpMethod.Post,
            Api("wit/workitemsbatch?api-version=7.1"),
            request,
            token
        ).ConfigureAwait(false);
        var indexed = Values(batch).ToDictionary(x => Number(x, "id"));
        return [.. ids.Where(indexed.ContainsKey).Select(id => {
            var item = indexed[id];
            var fields = P(item, "fields");
            string parent = Values(item, "relations").Where(x => S(x, "rel") == "System.LinkTypes.Hierarchy-Reverse").Select(x => S(x, "url").Split('/').Last()).FirstOrDefault() ?? "";
            return new DeveloperItem(
                id.ToString(CultureInfo.InvariantCulture),
                [
                    id.ToString(CultureInfo.InvariantCulture),
                    S(fields, "System.WorkItemType"),
                    S(fields, "System.Title"),
                    S(fields, "System.State"),
                    S(fields, column),
                    Identity(P(fields, "System.AssignedTo")),
                    parent,
                    S(fields, "Microsoft.VSTS.Common.Priority"),
                    S(fields, "Microsoft.VSTS.Scheduling.RemainingWork"),
                    S(fields, "System.IterationPath"),
                    S(fields, "System.Tags")
                ],
                item.Clone()
            );
        })];
    }

    private static readonly int[] WorkWidths = [8, 18, 40, 14, 18, 20, 9, 10, 12, 25, 20];
    private static readonly string[] WorkColumns = [
        "ID",
        "Type",
        "Title",
        "State",
        "Board column",
        "Assigned to",
        "Parent",
        "Priority",
        "Remaining",
        "Iteration",
        "Tags"
    ];

    private async Task LoadIterations(CancellationToken token) {
        var iterations = await Get(TeamApi("teamsettings/iterations"), token).ConfigureAwait(false);
        _iterations = [.. Values(iterations).Where(iteration => S(iteration, "path").Length > 0)
            .Select(iteration => (S(iteration, "name"), S(iteration, "path")))];
        _iterationDetails.Clear();
        foreach (var iteration in Values(iterations)) {
            _iterationDetails[S(iteration, "path")] = iteration.Clone();
        }
        var current = await Get(TeamApi("teamsettings/iterations?$timeframe=current"), token).ConfigureAwait(false);
        _currentIteration = Values(current).Select(iteration => S(iteration, "path")).FirstOrDefault(path => path.Length > 0);
    }

    internal DeveloperOperation[] SprintOperations(int view) {
        DeveloperOperation Select(string label, string? path) => new(label,
            "Filter stories and tasks by the selected team sprint.", (_, _) => {
                _iterationSelection = path;
                _cursors.Clear();
                return Task.FromResult("Sprint changed");
            }, ReadOnly: true, TargetView: view);
        return [
            Select("Current sprint" + (_currentIteration is null ? " · not configured" : " · " + _currentIteration), null),
            Select("All sprints", ""),
            .. _iterations.Select(iteration => Select(iteration.Name + " · " + iteration.Path, iteration.Path))
        ];
    }
    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.DetailAsync"/>
    public async Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) {
        var state = (JsonElement)item.State!;
        if (view == 0) {
            var board = await Get(TeamApi("boards/" + E(item.Key)), token).ConfigureAwait(false);
            return S(board, "name") +
                "\n\nCOLUMNS\n" + string.Join(
                    '\n',
                    Values(board, "columns").Select(column => "  " + S(column, "name") +
                " · WIP limit " + S(column, "itemLimit"))
                ) +
                "\n\nA → Open board cards to browse and move work items.";
        }

        if (IsWorkItemView(view)) {
            var detail = await Get(Api("wit/workitems/" + E(item.Key) + "?$expand=all"), token).ConfigureAwait(false);
            var fields = P(detail, "fields");
            Details[item.Key] = detail.Clone();
            try {
                var comments = await _client.SendAsync(
                    HttpMethod.Get,
                    Api("wit/workitems/" + E(item.Key) +
                    "/comments?$top=50&order=desc&api-version=7.1-preview.4"),
                    null,
                    token
                ).ConfigureAwait(false);
                Discussions[item.Key] = string.Join(
                    "\n\n",
                    Values(comments, "comments").Select(comment => Identity(P(comment, "createdBy")) +
                    " · " + S(comment, "createdDate") +
                    "\n" + Plain(S(comment, "text")))
                );
                if (Discussions[item.Key].Length == 0) {
                    Discussions[item.Key] = "No comments.";
                }

                if (S(comments, "continuationToken").Length > 0) {
                    Discussions[item.Key] += "\n\nLatest 50 comments shown.";
                }
            }
            catch (HttpRequestException error) {
                Discussions[item.Key] = "Discussion unavailable · " + error.Message;
            }

            if (Details.Count > 64) {
                foreach (string key in Details.Keys.Where(key => key != item.Key).Take(Details.Count - 64)) {
                    Details.TryRemove(key, out _);
                    Discussions.TryRemove(key, out _);
                }
            }

            var text = new StringBuilder($"#{item.Key} · {S(fields, "System.WorkItemType")} · revision {S(detail, "rev")}\n{S(fields, "System.Title")}\n\n");
            foreach (string field in new[]
            {
                "System.State",
                "System.AssignedTo",
                "System.AreaPath",
                "System.IterationPath",
                "Microsoft.VSTS.Common.Priority",
                "Microsoft.VSTS.Scheduling.RemainingWork",
                "System.Tags",
                "System.Description",
                "Microsoft.VSTS.Common.AcceptanceCriteria"
            }

            ) {
                string value = Plain(Identity(P(fields, field)));
                if (value.Length > 0) {
                    text.Append(field[(field.LastIndexOf('.') + 1)..]).Append(": ").AppendLine(value);
                }
            }

            text.AppendLine("\nLINKS (parent / children / related)");
            foreach (var link in Values(detail, "relations")) {
                text.Append(S(link, "rel")).Append(" · ").AppendLine(S(link, "url"));
            }

            return text.ToString();
        }

        if (view == 4) {
            return await PullRequestDetail(item, state, token).ConfigureAwait(false);
        }

        return await PipelineDetail(item, token).ConfigureAwait(false);
    }

    private static string Plain(string text) =>
        WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.Operations"/>
    public DeveloperOperation[] Operations(int view, DeveloperItem? item) {
        if (view == 4) {
            return PullRequestOperations(item);
        }
        if (view == 5) {
            return PipelineOperations(item);
        }
        if (view == 0 && item?.State is JsonElement board) {
            return [new("Open board cards", "Show this board's work item types and team area paths.", async (
                _,
                token
            ) =>
            {
                var detail = await Get(TeamApi("boards/" + E(item.Key)), token).ConfigureAwait(false);
                _boardColumns = [.. Values(detail, "columns").Select(column => S(column, "name")).Where(name => name.Length > 0)];
                _boardTypes = [.. Values(detail, "columns")
                    .SelectMany(column => {
                        var mappings = P(column, "stateMappings");
                        return mappings.ValueKind == JsonValueKind.Object
                            ? mappings.EnumerateObject().Select(mapping => mapping.Name)
                            : [];
                    })
                    .Concat(Values(detail, "workItemTypes").Select(type => S(type, "name")))
                    .Where(type => type.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
                if (_boardTypes.Length == 0) {
                    throw new InvalidOperationException("This board has no configured work-item type mappings in its columns.");
                }

                _boardColumn = S(P(P(detail, "fields"), "columnField"), "referenceName");
                if (_boardColumn.Length == 0)
                {
                    _boardColumn = "System.BoardColumn";
                }

                _board = S(board, "name");
                _cursors.Clear();
                return "Board opened";
            }, ReadOnly: true, TargetView: 1)];
        }

        if (!IsWorkItemView(view)) {
            return [];
        }

        var actions = new List<DeveloperOperation>
        {
            new(_mine ? "Filter list: all assignees" : "Filter list: assigned to me", "Return to the list and filter by the signed-in user. This does not change any work item's assignment.", (
                _,
                _
            ) =>
            {
                _mine = !_mine;
                _cursors.Clear();
                _backlogIds = null;
                return Task.FromResult("Assignment filter changed");
            }, ReadOnly: true, TargetView: view)
        };
        if (view == 6) {
            actions.AddRange(_backlogTypes.Select(type => Create("Create " + type, type, null)));
        }
        else {
            actions.Add(Create("Create " + _storyType, _storyType, null));
            actions.Add(Create("Create task", "Task", null));
        }
        if (view != 6 && _board is not null) {
            actions.Add(new("Show all work items", "Clear the selected board type filter.", (_, _) => {
                _board = null;
                _boardTypes = [];
                _boardColumns = [];
                _boardColumn = "System.BoardColumn";
                if (_team.Length == 0) {
                    _areas = [];
                    _areasLoaded = false;
                    _defaultArea = "";
                }

                _cursors.Clear();
                return Task.FromResult("Board filter cleared");
            }, ReadOnly: true, TargetView: 1));
        }

        if (item?.State is JsonElement initial) {
            var state = Details.TryGetValue(item.Key, out var loaded) ? loaded : initial;
            int revision = Number(state, "rev");
            actions.Add(Create("Add child task", "Task", item.Key, P(state, "fields")));
            actions.Add(Edit("Rename", "System.Title", "Title", item, revision));
            actions.Add(Edit("Change state", "System.State", "State (exact process state name)", item, revision));
            if (view != 6 && _board is not null) {
                actions.Add(Edit(
                    "Move to board column",
                    _boardColumn,
                    "Board column (exact column name)",
                    item,
                    revision
                ));
            }

            actions.Add(Edit("Assign", "System.AssignedTo", "Identity email or unique name", item, revision));
            actions.Add(Edit("Plan into iteration", "System.IterationPath", "Iteration path", item, revision));
            actions.Add(Edit("Set area", "System.AreaPath", "Area path", item, revision));
            actions.Add(Edit("Set priority", "Microsoft.VSTS.Common.Priority", "Priority (1 highest – 4 lowest)", item, revision));
            actions.Add(Edit(
                "Set remaining work",
                "Microsoft.VSTS.Scheduling.RemainingWork",
                "Remaining hours",
                item,
                revision,
                true
            ));
            actions.Add(Edit("Add discussion", "System.History", "Discussion text", item, revision));
            actions.Add(Edit("Edit description", "System.Description", "Description (HTML supported)", item, revision));
            actions.Add(Edit("Edit tags", "System.Tags", "Tags separated by semicolons", item, revision));
        }

        return [.. actions];
    }

    private DeveloperOperation Edit(
        string label,
        string field,
        string input,
        DeveloperItem item,
        int revision,
        bool numeric = false
    ) =>
        new(label, $"Update #{item.Key} only if revision {revision} is unchanged. Azure DevOps process rules apply.", async (
            text,
            token
        ) => {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        object value = text;
        if (field == "Microsoft.VSTS.Common.Priority") {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority) || priority is < 1 or > 4) {
                throw new ArgumentException("Enter a priority from 1 (highest) to 4 (lowest).");
            }
            value = priority;
        }
        else if (numeric) {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)
                || !double.IsFinite(hours)
                || hours < 0) {
                throw new ArgumentException("Enter nonnegative hours using a decimal point.");
            }

            value = hours;
        }

        object[] patch = [new
        {
            op = "test",
            path = "/rev",
            value = (object)revision
        }, new
        {
            op = "add",
            path = "/fields/" + field,
            value
        }

        ];
        await _client.SendAsync(
            HttpMethod.Patch,
            Api("wit/workitems/" + E(item.Key) +
            "?api-version=7.1"),
            patch,
            token,
            true
        ).ConfigureAwait(false);
        return "Updated #" + item.Key;
    }, input, field == "System.History" ? "" : item.State is JsonElement original ? Identity(P(P(Details.TryGetValue(item.Key, out var latest) ? latest : original, "fields"), field)) : "");
    private DeveloperOperation Create(
        string label,
        string type,
        string? parent,
        JsonElement parentFields = default
    ) =>
        new(label, "Create " + type + (parent is null ? "" : " under #" + parent) +
        ". Custom required fields may need the web editor.", async (
            text,
            token
        ) => {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            var patch = new List<object>
            {
                new
            {
                op = "add",
                path = "/fields/System.Title",
                value = text
            }
            };
            if (parent is not null) {
                patch.Add(new {
                    op = "add",
                    path = "/relations/-",
                    value = new {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = _client.OrganizationUrl +
                    "/" + E(_project) +
                    "/_apis/wit/workItems/" + E(parent)
                    }
                });
            }

            string area = S(parentFields, "System.AreaPath");
            if (area.Length == 0) {
                area = _defaultArea;
            }

            if (area.Length != 0) {
                patch.Add(new { op = "add", path = "/fields/System.AreaPath", value = area });
            }

            string iteration = S(parentFields, "System.IterationPath");
            if (iteration.Length != 0) {
                patch.Add(new { op = "add", path = "/fields/System.IterationPath", value = iteration });
            }

            var result = await _client.SendAsync(
                HttpMethod.Post,
                Api("wit/workitems/$" + E(type) +
                "?api-version=7.1"),
                patch,
                token,
                true
            ).ConfigureAwait(false);
            return "Created #" + S(result, "id");
        }, "Title");
    /// <inheritdoc/>
    public void Dispose() {
        if (_client is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}
