using System.Globalization;
using System.Text.Json;

namespace fullscreen.Development;

internal sealed record SprintReview(
    string Name, string Path, string Start, string Finish, string AsOf, int Total, int Completed, int Removed, int Unknown,
    (string State, int Count)[] States, string[] Measures, (string Name, string Path)[] Sprints,
    SprintMetric[]? Metrics = null);

internal sealed record SprintMetric(string Type, string Unit, double Completed, double Remaining);
internal sealed record CapacityReview(double? Hours, (string Type, double Hours, int Reported, int Total)[] Planned, string Notice);
internal sealed record BoardTrend(DateOnly Date, string Column, int Count);
internal sealed record SprintVelocity(string Name, int Planned, int Completed, int Incomplete);
internal sealed record BoardsAnalytics(string Board, string Range, BoardTrend[] Flow, SprintVelocity[] Velocity,
    string[] Boards, CapacityReview Capacity, string Notice);

public sealed partial class AzureDevOpsBackend {
    internal async Task<SprintReview> LoadSprintReviewAsync(string? selectedPath, CancellationToken token, bool currentOnly = false) {
        await EnsureTeam(token).ConfigureAwait(false);
        await LoadIterations(token).ConfigureAwait(false);
        string? path = currentOnly ? _currentIteration : selectedPath ?? (string.IsNullOrEmpty(_iterationSelection) ? _currentIteration : _iterationSelection);
        if (path is null || !_iterationDetails.TryGetValue(path, out var iteration)) {
            return new("Choose a sprint", "", "", "", "", 0, 0, 0, 0, [], [], _iterations);
        }
        var settings = await Get(TeamApi("teamsettings/teamfieldvalues"), token).ConfigureAwait(false);
        string[] areas = [.. Values(settings, "values").Select(area =>
            "[System.AreaPath] " + (P(area, "includeChildren").ValueKind == JsonValueKind.True ? "UNDER " : "= ") + Q(S(area, "value")))];
        string scope = areas.Length == 0 ? "[System.Id] = 0" : "(" + string.Join(" OR ", areas) + ")";
        var query = await _client.SendAsync(HttpMethod.Post, Api("wit/wiql?api-version=7.1"),
            new {
                query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.IterationPath] = " +
                Q(path) + " AND " + scope + " ORDER BY [System.Id]"
            }, token).ConfigureAwait(false);
        if (P(query, "workItems").ValueKind != JsonValueKind.Array || S(query, "asOf").Length == 0) {
            throw new InvalidOperationException("Azure DevOps did not return a complete sprint query snapshot.");
        }
        int[] ids = [.. Values(query, "workItems").Select(item => Number(item, "id")).Distinct()];
        var fields = new List<JsonElement>(ids.Length);
        foreach (var chunk in ids.Chunk(200)) {
            var batch = await _client.SendAsync(HttpMethod.Post, Api("wit/workitemsbatch?api-version=7.1"),
                new Dictionary<string, object> { ["ids"] = chunk, ["asOf"] = S(query, "asOf") }, token).ConfigureAwait(false);
            var items = Values(batch).ToArray();
            if (items.Length != chunk.Length || !items.Select(item => Number(item, "id")).ToHashSet().SetEquals(chunk)) {
                throw new InvalidOperationException("The sprint review response is incomplete. Refresh before reviewing sprint totals.");
            }
            fields.AddRange(items.Select(item => P(item, "fields")));
        }
        var measures = new List<string>();
        var metrics = new List<SprintMetric>();
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int completed = 0, removed = 0, unknown = 0;
        foreach (var type in fields.GroupBy(item => S(item, "System.WorkItemType"))) {
            var workflow = await Get(Api("wit/workitemtypes/" + E(type.Key) + "/states"), token).ConfigureAwait(false);
            var categories = Values(workflow).ToDictionary(state => S(state, "name"), state => S(state, "category"), StringComparer.OrdinalIgnoreCase);
            var active = new List<(JsonElement Fields, bool Completed)>();
            foreach (var item in type) {
                string state = S(item, "System.State");
                string category = categories.GetValueOrDefault(state, "Unknown");
                states[state] = states.GetValueOrDefault(state) + 1;
                bool done = category.Equals("Completed", StringComparison.OrdinalIgnoreCase);
                bool excluded = category.Equals("Removed", StringComparison.OrdinalIgnoreCase);
                completed += done ? 1 : 0;
                removed += excluded ? 1 : 0;
                unknown += category is "" or "Unknown" ? 1 : 0;
                if (!excluded) {
                    active.Add((item, done));
                }
            }
            measures.Add(type.Key + " · " + active.Count + " in scope · " + active.Count(item => item.Completed) + " completed");
            foreach (var (field, unit) in new[] {
                ("Microsoft.VSTS.Scheduling.StoryPoints", "story points"),
                ("Microsoft.VSTS.Scheduling.Effort", "effort"),
                ("Microsoft.VSTS.Scheduling.Size", "size")
            }) {
                var estimates = active.Where(item => Numeric(item.Fields, field).HasValue).ToArray();
                if (estimates.Length > 0) {
                    double done = estimates.Where(item => item.Completed).Sum(item => Numeric(item.Fields, field)!.Value);
                    double remaining = estimates.Where(item => !item.Completed).Sum(item => Numeric(item.Fields, field)!.Value);
                    measures.Add(FormattableString.Invariant($"  {unit} · {done:0.##} completed / {remaining:0.##} not completed · {estimates.Length}/{active.Count} estimated"));
                    metrics.Add(new(type.Key, unit, done, remaining));
                }
            }
            foreach (var (field, label) in new[] {
                ("Microsoft.VSTS.Scheduling.CompletedWork", "Recorded completed hours"),
                ("Microsoft.VSTS.Scheduling.RemainingWork", "Recorded remaining hours")
            }) {
                double[] hours = [.. active.Select(item => Numeric(item.Fields, field)).Where(value => value.HasValue).Select(value => value!.Value)];
                if (hours.Length > 0) {
                    measures.Add(FormattableString.Invariant($"  {label} · {hours.Sum():0.##} · {hours.Length}/{active.Count} reported"));
                }
            }
        }
        var attributes = P(iteration, "attributes");
        return new(S(iteration, "name"), path, S(attributes, "startDate"), S(attributes, "finishDate"), S(query, "asOf"),
            fields.Count - removed, completed, removed, unknown,
            [.. states.OrderByDescending(state => state.Value).Select(state => (state.Key, state.Value))],
            [.. measures], _iterations, [.. metrics]);
    }

    internal async Task<BoardsAnalytics> LoadBoardsAnalyticsAsync(bool last30Days, string? boardName, CancellationToken token) {
        await EnsureTeam(token).ConfigureAwait(false);
        await LoadIterations(token).ConfigureAwait(false);
        var boards = await Get(TeamApi("boards"), token).ConfigureAwait(false);
        string[] names = [.. Values(boards).Select(board => S(board, "name")).Where(name => name.Length > 0)];
        string name = boardName ?? _board ?? (BoardLevelLabel.Length > 0 ? BoardLevelLabel : names.FirstOrDefault())
            ?? throw new InvalidOperationException("No backlog-level boards configured for this team.");
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Selected board no longer exists. Choose another board.");
        }
        var board = Values(boards).First(item => S(item, "name").Equals(name, StringComparison.OrdinalIgnoreCase));
        var definition = await Get(TeamApi("boards/" + E(S(board, "id"))), token).ConfigureAwait(false);
        string[] columns = [.. Values(definition, "columns").Select(column => S(column, "name"))];
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly start = today.AddDays(-29);
        string iterationFilter = "";
        if (!last30Days) {
            if (_currentIteration is null || !_iterationDetails.TryGetValue(_currentIteration, out var current)
                || !TryDate(S(P(current, "attributes"), "startDate"), out start)) {
                return new(name, "Current sprint", [], [], names, new(null, [], "No current sprint capacity."),
                    "No dated current sprint configured. Choose Last 30 days.");
            }
            iterationFilter = " and Iteration/IterationPath eq " + Q(_currentIteration);
        }
        string team = _team.Length > 0 ? _team : _defaultTeam;
        string teamFilter = Guid.TryParse(team, out var id)
            ? "Team/TeamId eq " + id.ToString("D") : "Team/TeamName eq " + Q(team);
        string filter = teamFilter + " and BoardName eq " + Q(name);
        async Task<JsonElement> Query(string scope, string group) =>
            await _client.ReadAnalyticsAsync(_project, "WorkItemBoardSnapshot?$apply=" + E(
                "filter(" + filter + " and " + scope + ")/groupby((" + group + "),aggregate($count as Count))"), token).ConfigureAwait(false);
        string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "Z";
        var flow = await Query("DateValue ge " + Date(start) + " and DateValue le " + Date(today) + iterationFilter,
            "DateValue,ColumnName").ConfigureAwait(false);
        var trends = AnalyticsRows(flow).Select(row => {
            TryDate(S(row, "DateValue"), out var date);
            if (S(row, "ColumnName").Length == 0) {
                throw new InvalidOperationException("Analytics returned a snapshot without a board column.");
            }
            return new BoardTrend(date, S(row, "ColumnName"), Number(row, "Count"));
        }).OrderBy(row => Array.IndexOf(columns, row.Column) is var order && order >= 0 ? order : int.MaxValue)
            .ThenBy(row => row.Date).ToArray();
        var sprints = _iterationDetails.Values.Select(iteration => (
            Name: S(iteration, "name"), Path: S(iteration, "path"),
            Start: TryDate(S(P(iteration, "attributes"), "startDate"), out var began) ? began : (DateOnly?)null,
            Finish: TryDate(S(P(iteration, "attributes"), "finishDate"), out var finished) ? finished : (DateOnly?)null))
            .Where(sprint => sprint.Start.HasValue && sprint.Finish.HasValue && sprint.Start <= today
                && (last30Days ? sprint.Finish >= today.AddDays(-29) : sprint.Path == _currentIteration))
            .OrderByDescending(sprint => sprint.Start).Take(6).OrderBy(sprint => sprint.Start).ToArray();
        var velocity = new List<SprintVelocity>();
        if (sprints.Length > 0) {
            string paths = string.Join(" or ", sprints.Select(sprint => "Iteration/IterationPath eq " + Q(sprint.Path)));
            var history = await Query("DateValue ge " + Date(sprints[0].Start!.Value) + " and DateValue le " + Date(today) +
                " and (" + paths + ")", "DateValue,Iteration/IterationPath,StateCategory").ConfigureAwait(false);
            var rows = AnalyticsRows(history);
            if (rows.Any(row => S(P(row, "Iteration"), "IterationPath").Length == 0 || S(row, "StateCategory").Length == 0)) {
                throw new InvalidOperationException("Analytics returned incomplete sprint history.");
            }
            foreach (var sprint in sprints) {
                DateOnly end = sprint.Finish!.Value < today ? sprint.Finish.Value : today;
                var matching = rows.Where(row => S(P(row, "Iteration"), "IterationPath") == sprint.Path
                    && TryDate(S(row, "DateValue"), out var date) && date >= sprint.Start && date <= end).ToArray();
                if (matching.Length == 0) {
                    continue;
                }
                DateOnly first = matching.Min(row => DateOnly.Parse(S(row, "DateValue")[..10], CultureInfo.InvariantCulture));
                DateOnly last = matching.Max(row => DateOnly.Parse(S(row, "DateValue")[..10], CultureInfo.InvariantCulture));
                int CountAt(DateOnly date, bool? completed) => matching.Where(row =>
                    S(row, "DateValue").StartsWith(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    && S(row, "StateCategory") != "Removed" && (completed is null || (S(row, "StateCategory") == "Completed") == completed))
                    .Sum(row => Number(row, "Count"));
                // A missing sprint-start snapshot is not a valid historical planning baseline.
                if (first != sprint.Start!.Value) {
                    continue;
                }
                velocity.Add(new(sprint.Name, CountAt(first, null), CountAt(last, true), CountAt(last, false)));
            }
        }
        var capacity = await LoadCapacityAsync(_currentIteration, token).ConfigureAwait(false);
        return new(name, last30Days ? "Last 30 days" : "Current sprint · " + _currentIteration, trends, [.. velocity], names, capacity,
            "Daily Analytics snapshots; may lag live Boards. Dates use UTC range boundaries; the organization's daily cutoff may differ. Only observed dates are plotted; missing days are not zero observations. Velocity uses first-day scope vs latest snapshot at/before sprint end; not the web widget's exact baseline or completed-late calculation. Missing start snapshots are omitted. Capacity is the current sprint, not a 30-day projection.");
    }

    internal async Task<CapacityReview> LoadCapacityAsync(string? path, CancellationToken token) {
        if (path is null || !_iterationDetails.TryGetValue(path, out var iteration) || S(iteration, "id").Length == 0) {
            return new(null, [], "No selected sprint with an iteration ID; capacity unavailable.");
        }
        try {
            var attributes = P(iteration, "attributes");
            if (!TryDate(S(attributes, "startDate"), out var start) || !TryDate(S(attributes, "finishDate"), out var finish)
                || finish < start || finish.DayNumber - start.DayNumber > 366) {
                return new(null, [], "Sprint dates missing or invalid; capacity unavailable.");
            }
            var settings = await Get(TeamApi("teamsettings"), token).ConfigureAwait(false);
            var days = Values(settings, "workingDays").Select(day => day.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (days.Count == 0) {
                return new(null, [], "Team working days are not configured; capacity is not assumed.");
            }
            if (days.Any(day => !Enum.TryParse<DayOfWeek>(day, true, out _))) {
                throw new InvalidOperationException("Azure DevOps returned invalid team working days.");
            }
            string resource = "teamsettings/iterations/" + E(S(iteration, "id"));
            var capacities = await Get(TeamApi(resource + "/capacities"), token).ConfigureAwait(false);
            var daysOff = await Get(TeamApi(resource + "/teamdaysoff"), token).ConfigureAwait(false);
            var members = Values(capacities, "teamMembers").ToArray();
            if (members.Length == 0) {
                return new(null, [], "No team member capacities configured for this sprint.");
            }
            bool Off(JsonElement[] ranges, DateOnly day) => ranges.Any(range => {
                if (!TryDate(S(range, "start"), out var from) || !TryDate(S(range, "end"), out var to) || to < from) {
                    throw new InvalidOperationException("Azure DevOps returned invalid days-off dates.");
                }
                return day >= from && day <= to;
            });
            var teamOff = Values(daysOff, "daysOff").ToArray();
            double hours = 0;
            foreach (var member in members) {
                if (P(member, "activities").ValueKind != JsonValueKind.Array
                    || Values(member, "activities").Any(activity => Numeric(activity, "capacityPerDay") is null)) {
                    throw new InvalidOperationException("Azure DevOps returned incomplete member capacities.");
                }
                var individualOff = Values(member, "daysOff").ToArray();
                int working = 0;
                for (var day = start; day <= finish; day = day.AddDays(1)) {
                    if (days.Contains(day.DayOfWeek.ToString()) && !Off(teamOff, day) && !Off(individualOff, day)) {
                        working++;
                    }
                }
                hours += working * Values(member, "activities").Sum(activity => Numeric(activity, "capacityPerDay") ?? 0);
            }
            var query = await _client.SendAsync(HttpMethod.Post, Api("wit/wiql?api-version=7.1"), new {
                query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.IterationPath] = " + Q(path) +
                " AND (" + await TeamAreaClause(token).ConfigureAwait(false) + ") ORDER BY [System.Id]"
            }, token).ConfigureAwait(false);
            if (P(query, "workItems").ValueKind != JsonValueKind.Array || S(query, "asOf").Length == 0) {
                throw new InvalidOperationException("Azure DevOps did not return a complete capacity work-item snapshot.");
            }
            var fields = new List<JsonElement>();
            foreach (var chunk in Values(query, "workItems").Select(item => Number(item, "id")).Distinct().Chunk(200)) {
                var batch = await _client.SendAsync(HttpMethod.Post, Api("wit/workitemsbatch?api-version=7.1"),
                    new { ids = chunk, asOf = S(query, "asOf") }, token).ConfigureAwait(false);
                var items = Values(batch).ToArray();
                if (items.Length != chunk.Length || !items.Select(item => Number(item, "id")).ToHashSet().SetEquals(chunk)) {
                    throw new InvalidOperationException("Capacity work-item response is incomplete.");
                }
                fields.AddRange(items.Select(item => P(item, "fields")));
            }
            return new(hours, [.. fields.GroupBy(item => S(item, "System.WorkItemType")).Select(type =>
                (type.Key, type.Sum(item => Numeric(item, "Microsoft.VSTS.Scheduling.OriginalEstimate") ?? 0),
                    type.Count(item => Numeric(item, "Microsoft.VSTS.Scheduling.OriginalEstimate").HasValue), type.Count()))],
                "Capacity hours account for configured working days and team/member days off. Original estimates include all current iteration assignments (including removed items), not sprint-start commitment; types are separate to avoid parent/child double counting. Story points are not hours.");
        }
        catch (Exception error) when (error is HttpRequestException or NotSupportedException) {
            return new(null, [], "Capacity unavailable: " + error.Message);
        }
    }

    private async Task<string> TeamAreaClause(CancellationToken token) {
        var settings = await Get(TeamApi("teamsettings/teamfieldvalues"), token).ConfigureAwait(false);
        string[] areas = [.. Values(settings, "values").Select(area =>
            "[System.AreaPath] " + (P(area, "includeChildren").ValueKind == JsonValueKind.True ? "UNDER " : "= ") + Q(S(area, "value")))];
        return areas.Length == 0 ? "[System.Id] = 0" : string.Join(" OR ", areas);
    }

    private static bool TryDate(string text, out DateOnly date) {
        date = default;
        return text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static JsonElement[] AnalyticsRows(JsonElement response) {
        if (P(response, "value").ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("Analytics did not return a snapshot collection.");
        }
        var rows = Values(response).ToArray();
        if (rows.Any(row => !TryDate(S(row, "DateValue"), out _) || P(row, "Count").ValueKind != JsonValueKind.Number
            || !P(row, "Count").TryGetInt32(out int count) || count < 0)) {
            throw new InvalidOperationException("Analytics returned an invalid snapshot date or count.");
        }
        return rows;
    }

    private static double? Numeric(JsonElement fields, string name) =>
        double.TryParse(S(fields, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value) && value >= 0 ? value : null;
}
