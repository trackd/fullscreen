using System.Globalization;
using System.Text.Json;

namespace fullscreen.Development;

public sealed partial class AzureDevOpsBackend {
    private JsonElement[] _backlogs = [];
    private string? _backlogId;
    private string _backlogName = "";
    private string[] _backlogTypes = [];
    private int[]? _backlogIds;

    internal string BacklogLabel => _backlogName.Length == 0 ? "Choose level" : _backlogName;

    internal DeveloperOperation[] BacklogOperations() =>
        [.. _backlogs.Select(level => new DeveloperOperation(
            S(level, "name"),
            "Show this team's backlog level in its planning order, across all sprints.",
            (_, _) => {
                SelectBacklog(level);
                return Task.FromResult("Backlog level changed");
            }, ReadOnly: true, TargetView: 6))];

    private void SelectBacklog(JsonElement level) {
        _backlogId = S(level, "id");
        _backlogName = S(level, "name");
        _backlogTypes = [.. new[] { S(P(level, "defaultWorkItemType"), "name") }
            .Concat(Values(level, "workItemTypes").Select(type => S(type, "name"))).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        _backlogIds = null;
    }

    private async Task<DeveloperPage> Backlog(int page, CancellationToken token) {
        if (page == 0) {
            await EnsureTeam(token).ConfigureAwait(false);
            var levels = await Get(TeamApi("backlogs"), token).ConfigureAwait(false);
            if (P(levels, "value").ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Azure DevOps did not return the team's backlog level configuration.");
            }
            _backlogs = [.. Values(levels).Where(level => P(level, "isHidden").ValueKind != JsonValueKind.True && S(level, "id").Length > 0)
                .Select(level => level.Clone())];
            var selected = _backlogs.FirstOrDefault(level => S(level, "id") == _backlogId);
            if (selected.ValueKind == JsonValueKind.Undefined) {
                selected = _backlogs.FirstOrDefault(level => S(level, "type") == "requirement");
                if (selected.ValueKind == JsonValueKind.Undefined) {
                    selected = _backlogs.FirstOrDefault();
                }
            }
            if (selected.ValueKind == JsonValueKind.Undefined) {
                _backlogIds = null;
                _backlogId = null;
                _backlogName = "";
                _backlogTypes = [];
                return new(WorkColumns, [], "No visible backlog levels configured for this team.", WorkWidths);
            }
            SelectBacklog(selected);
            var backlog = await Get(TeamApi("backlogs/" + E(_backlogId!) + "/workItems"), token).ConfigureAwait(false);
            if (P(backlog, "workItems").ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Azure DevOps did not return the selected backlog's work items.");
            }
            int[] ids = [.. Values(backlog, "workItems").Select(link => Number(P(link, "target"), "id"))
                .Where(id => id > 0).Distinct()];
            if (_mine && ids.Length > 0) {
                var matching = new HashSet<int>();
                foreach (var chunk in ids.Chunk(200)) {
                    var query = await _client.SendAsync(HttpMethod.Post, Api("wit/wiql?api-version=7.1"),
                        new {
                            query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.AssignedTo] = @Me AND [System.Id] IN (" +
                            string.Join(",", chunk.Select(id => id.ToString(CultureInfo.InvariantCulture))) + ")"
                        }, token).ConfigureAwait(false);
                    matching.UnionWith(Values(query, "workItems").Select(item => Number(item, "id")));
                }
                ids = [.. ids.Where(matching.Contains)];
            }
            if (!_areasLoaded) {
                var settings = await Get(TeamApi("teamsettings/teamfieldvalues"), token).ConfigureAwait(false);
                _defaultArea = S(settings, "defaultValue");
            }
            _backlogIds = ids;
        }
        if (_backlogIds is null) {
            return new(WorkColumns, [], "Refresh the first backlog page before paging.", WorkWidths);
        }
        int[] pageIds = [.. _backlogIds.Skip(page * _pageSize).Take(_pageSize)];
        var rows = pageIds.Length == 0 ? [] : await LoadWorkItemRows(pageIds, "System.BoardColumn", token).ConfigureAwait(false);
        return new(WorkColumns, rows,
            "Backlog · " + _backlogName + " · " + _backlogIds.Length + " items · team planning order · all sprints · L choose level",
            WorkWidths);
    }
}
