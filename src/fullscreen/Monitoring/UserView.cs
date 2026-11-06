namespace fullscreen.Monitoring;
/// <summary>Represents a user group, login session, or process in the user tree.</summary>
/// <param name="Key">The stable lookup key.</param>
/// <param name="UserName">The account name associated with the session or process.</param>
/// <param name="Sessions">The login sessions represented by a user group.</param>
/// <param name="Status">The raw Git porcelain status text.</param>
/// <param name="ProcessCount">The number of valid process rows in the process buffer.</param>
/// <param name="CpuPercent">The CPU utilization percentage.</param>
/// <param name="WorkingSet">The resident process memory in bytes.</param>
/// <param name="Process">The source process row, when this tree node represents a process. Defaults to null.</param>
public sealed record UserViewRow(
    string Key,
    string UserName,
    string Sessions,
    string Status,
    int ProcessCount,
    double CpuPercent,
    long WorkingSet,
    ProcessRow? Process = null
) {
    /// <summary>Gets or sets the named login sessions for the selected user.</summary>
    public IReadOnlyList<UserSessionRow> Logins { get; init; } = [];
    /// <summary>Gets the process depth within its account's process tree.</summary>
    public int Depth { get; init; }
    /// <summary>Gets whether this process has child processes in the same account.</summary>
    public bool HasChildren { get; init; }
    /// <summary>Gets whether this process branch is collapsed.</summary>
    public bool Collapsed { get; init; }
}

/// <summary>Account groups own process rows; login sessions describe the account, not inferred process logins.</summary>
public sealed class UserView {
    private readonly ProcessView _groupSort = new();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcessView> _processViews = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the field used to sort account totals and their processes.</summary>
    public ProcessSort Sort { get; set; }
    /// <summary>Gets or sets whether account and process sorting is ascending.</summary>
    public bool Ascending { get; set; }
    /// <summary>Gets or sets whether processes form parent-child trees within each account.</summary>
    public bool Tree { get; set; }
    /// <summary>Gets the source rows or requested terminal row count.</summary>
    public IReadOnlyList<UserViewRow> Rows { get; private set; } = [];
    /// <summary>Gets or sets the stable key of the selected tree row.</summary>
    public string? SelectedKey { get; set; }
    /// <summary>Gets or sets the zero-based scroll offset.</summary>
    public int Offset { get; set; }
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; set; } = "";
    /// <summary>Gets or sets whether owned processes are included beneath user sessions.</summary>
    public bool IncludeProcesses { get; set; } = true;
    /// <summary>Gets the currently selected row or entry.</summary>
    public UserViewRow? Selected => Rows.FirstOrDefault(row => row.Key == SelectedKey);

    /// <summary>Checks whether a user-tree node is expanded.</summary>
    /// <param name="key">The stable user-tree node key.</param>
    /// <returns>True when the node is expanded.</returns>
    public bool IsExpanded(string key) =>
        _expanded.Contains(key);
    /// <summary>Expands, collapses, or toggles the selected user-tree node.</summary>
    /// <param name="expand">True to expand, false to collapse, or null to toggle the selected node.
    /// Defaults to null.</param>
    public void Expand(bool? expand = null) {
        var row = Selected;
        if (row is null || !IncludeProcesses) {
            return;
        }

        string key = "user:" + row.UserName;
        if (Tree && row.Process is { } process && _processViews.TryGetValue(row.UserName, out var view)) {
            view.SelectedId = process.Id;
            if (expand.HasValue) {
                view.CollapseSelected(!expand.Value);
            }
            else {
                view.ToggleSelected();
            }
            foreach (var selected in view.Rows) {
                if (selected.Id == view.SelectedId) {
                    SelectedKey = key + ":" + selected.Id + ":" + selected.StartTimeTicks;
                    break;
                }
            }
            return;
        }
        bool next = expand ?? !_expanded.Contains(key);
        if (next) {
            _expanded.Add(key);
        }
        else {
            _expanded.Remove(key);
        }

        if (!next && row.Process.HasValue) {
            SelectedKey = key;
        }
    }

    /// <summary>Moves selection through the visible user-tree rows.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <param name="page">The number of visible user-tree rows.</param>
    public void Move(int delta, int page) {
        if (Rows.Count == 0) {
            return;
        }

        int index = 0;
        for (int i = 0; i < Rows.Count; i++) {
            if (Rows[i].Key == SelectedKey) {
                index = i;
                break;
            }
        }

        index = (int)Math.Clamp((long)index + delta, 0, Rows.Count - 1);
        SelectedKey = Rows[index].Key;
        Offset = Math.Clamp(Offset, Math.Max(0, index - page + 1), index);
    }

    private static ProcessRow Aggregate(UserViewRow row, ProcessRow[] owned) =>
        new(row.ProcessCount, row.UserName, row.CpuPercent, row.WorkingSet, owned.Sum(process => process.Threads),
            GpuPercent: owned.Any(process => process.GpuPercent.HasValue) ? owned.Sum(process => process.GpuPercent ?? 0) : null,
            GpuMemory: owned.Any(process => process.GpuMemory.HasValue) ? owned.Aggregate(0UL, (sum, process) => sum + (process.GpuMemory ?? 0)) : null,
            ParentId: owned.Min(process => process.ParentId),
            UserName: row.UserName,
            CpuTimeTicks: owned.Sum(process => process.CpuTimeTicks),
            Status: row.Status,
            DiskBytesPerSecond: owned.Any(process => process.DiskBytesPerSecond.HasValue) ? owned.Sum(process => process.DiskBytesPerSecond ?? 0) : null,
            NetworkBytesPerSecond: owned.Any(process => process.NetworkBytesPerSecond.HasValue) ? owned.Sum(process => process.NetworkBytesPerSecond ?? 0) : null);

    /// <summary>Groups named sessions and owned processes from the supplied frame.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    public void Update(CollectorFrame frame) {
        bool windows = frame.Platform.Contains("Windows", StringComparison.OrdinalIgnoreCase);
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var sessions = frame.Users.Take(frame.UserCount).Where(row => !string.IsNullOrWhiteSpace(row.UserName) && (!windows || row.IsNamedWindowsSession)).GroupBy(row => row.UserName, comparer).ToDictionary(group => group.Key, group => group.ToArray(), comparer);
        var processes = frame.Processes.Take(IncludeProcesses ? frame.ProcessCount : 0).Where(row => !string.IsNullOrWhiteSpace(row.UserName) && (!windows || sessions.ContainsKey(row.UserName))).GroupBy(row => row.UserName!, comparer).ToDictionary(
            group => group.Key,
            group => group.ToArray(),
            comparer
        );
        var groups = new List<(UserViewRow Row, ProcessRow[] Owned, bool Matches, ProcessRow Total)>();
        IEnumerable<string> accounts = windows ? sessions.Keys : sessions.Keys.Concat(processes.Keys).Distinct(comparer);
        foreach (string account in accounts) {
            var logins = sessions.GetValueOrDefault(account) ?? [];
            var owned = processes.GetValueOrDefault(account) ?? [];
            bool matches = account.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || logins.Any(row => row.Session.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || row.Host.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || row.ClientName.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || row.State.Contains(Filter, StringComparison.OrdinalIgnoreCase));
            ProcessRow[] visible = matches ? owned : [.. owned.Where(row => row.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(Filter, StringComparison.Ordinal))];
            if (!matches && visible.Length == 0) {
                continue;
            }

            string key = "user:" + account;
            var header = new UserViewRow(
                key,
                account,
                string.Join(
                    ", ",
                    logins.Select(row => row.SessionId.HasValue ? row.Session +
                " #" + row.SessionId : row.Session)
                ),
                logins.Length == 0 ? "No login recorded" : string.Join(", ", logins.Select(row => row.State).Distinct()),
                visible.Length,
                visible.Sum(row => row.CpuPercent),
                visible.Sum(row => row.WorkingSet)
            ) { Logins = logins };
            groups.Add((header, owned, matches, Aggregate(header, visible)));
        }

        _groupSort.Sort = Sort;
        _groupSort.Ascending = Ascending;
        groups.Sort((left, right) => {
            int comparison = _groupSort.Compare(left.Total, right.Total);
            return comparison == 0 ? comparer.Compare(left.Row.UserName, right.Row.UserName) : comparison;
        });
        var rows = new List<UserViewRow>();
        foreach (var (Row, Owned, Matches, Total) in groups) {
            var header = Row;
            string key = header.Key, account = header.UserName;
            var logins = header.Logins;
            var owned = Owned;
            bool matches = Matches;
            rows.Add(header);
            if (_expanded.Contains(key) || !matches) {
                if (!_processViews.TryGetValue(account, out var view)) {
                    view = new ProcessView();
                    _processViews.Add(account, view);
                }
                view.Sort = Sort;
                view.Ascending = Ascending;
                view.Tree = Tree;
                view.Filter = matches ? "" : Filter;
                view.Update(owned);
                for (int index = 0; index < view.Count; index++) {
                    var row = view.Rows[index];
                    rows.Add(new UserViewRow(
                    key +
                    ":" + row.Id +
                    ":" + row.StartTimeTicks,
                    account,
                    "",
                    row.Status ?? "—",
                    1,
                    row.CpuPercent,
                    row.WorkingSet,
                    row
                    ) {
                        Logins = logins,
                        Depth = view.DepthAt(index),
                        HasChildren = view.HasChildrenAt(index),
                        Collapsed = view.IsCollapsedAt(index)
                    });
                }
            }
        }

        Rows = rows;
        if (!rows.Any(row => row.Key == SelectedKey)) {
            SelectedKey = rows.FirstOrDefault()?.Key;
        }

        _expanded.RemoveWhere(key => !accounts.Any(account => key == "user:" + account));
        foreach (string account in _processViews.Keys.Where(account => !processes.ContainsKey(account)).ToArray()) {
            _processViews.Remove(account);
        }
        Offset = Math.Clamp(Offset, 0, Math.Max(0, rows.Count - 1));
    }
}
