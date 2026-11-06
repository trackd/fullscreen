using System.Text.Json;

namespace fullscreen.Development;

public sealed partial class AzureDevOpsBackend {
    private JsonElement[] _boards = [];
    private string? _landingBoardId;
    private string[]? _workItemTypes;
    private string? _workItemType;
    internal string BoardLevelLabel => S(_boards.FirstOrDefault(board => S(board, "id") == _landingBoardId), "name");
    internal string WorkItemTypeLabel => _workItemType ?? "All types";
    internal bool AllWorkItemTypes => _board is null && _workItemType is null;

    internal DeveloperOperation[] BoardLevelOperations() =>
        [.. _boards.Select(board => new DeveloperOperation(S(board, "name"),
            "Select one backlog-level board. Its own columns and type mappings are used when opened.", (_, _) => {
                _landingBoardId = S(board, "id");
                return Task.FromResult("Board level changed");
            }, ReadOnly: true, TargetView: 0))];

    internal DeveloperOperation[] WorkItemTypeOperations() {
        DeveloperOperation Select(string label, string? type) => new(label,
            "Change the work-item type filter without changing the sprint or assignee filter.", (_, _) => {
                ClearBoardFilter();
                _workItemType = type;
                _cursors.Clear();
                return Task.FromResult("Work-item type changed");
            }, ReadOnly: true, TargetView: 1);
        return [Select("All types", null), .. (_workItemTypes ?? []).Select(type => Select(type, type))];
    }

    private async Task LoadWorkItemTypes(CancellationToken token) {
        if (_workItemTypes is not null) {
            return;
        }
        var response = await Get(Api("wit/workitemtypes"), token).ConfigureAwait(false);
        if (P(response, "value").ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("Azure DevOps did not return the project's work-item types.");
        }
        string[] types = [.. Values(response).Where(type => P(type, "isDisabled").ValueKind != JsonValueKind.True)
            .Select(type => S(type, "name")).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (types.Length == 0) {
            throw new InvalidOperationException("No enabled work-item types are configured for this project.");
        }
        if (_workItemType == "User Story" && !types.Contains(_workItemType, StringComparer.OrdinalIgnoreCase)) {
            var process = await Get(Api("work/processconfiguration"), token).ConfigureAwait(false);
            string requirement = Values(P(process, "requirementBacklog"), "workItemTypes")
                .Select(type => S(type, "name")).FirstOrDefault(type => types.Contains(type, StringComparer.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The default story type is unavailable and the process has no supported requirement type. Specify -StoryType.");
            _workItemType = requirement;
        }
        _workItemTypes = types;
    }

    private void ClearBoardFilter(bool clearTeamScope = false) {
        _board = null;
        _boardTypes = [];
        _boardColumns = [];
        _boardColumn = "System.BoardColumn";
        if (clearTeamScope && _team.Length == 0) {
            _areas = [];
            _areasLoaded = false;
            _defaultArea = "";
        }
    }
}
