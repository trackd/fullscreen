using System.Globalization;
using System.Text.Json;

namespace fullscreen.Development;

internal sealed record SprintReview(
    string Name, string Path, string Start, string Finish, string AsOf, int Total, int Completed, int Removed, int Unknown,
    (string State, int Count)[] States, string[] Measures, (string Name, string Path)[] Sprints);

public sealed partial class AzureDevOpsBackend {
    internal async Task<SprintReview> LoadSprintReviewAsync(string? selectedPath, CancellationToken token) {
        await EnsureTeam(token).ConfigureAwait(false);
        await LoadIterations(token).ConfigureAwait(false);
        string? path = selectedPath ?? (string.IsNullOrEmpty(_iterationSelection) ? _currentIteration : _iterationSelection);
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
            [.. measures], _iterations);
    }

    private static double? Numeric(JsonElement fields, string name) =>
        double.TryParse(S(fields, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value) && value >= 0 ? value : null;
}
