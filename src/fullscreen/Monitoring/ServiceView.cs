using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Identifies a selectable service table column.</summary>
public enum ServiceColumn {
    /// <summary>Displays the name.</summary>
    Name,
    /// <summary>Displays the state.</summary>
    State,
    /// <summary>Displays the process identifier.</summary>
    ProcessId,
    /// <summary>Displays the description.</summary>
    Description,
    /// <summary>Displays the detail.</summary>
    Detail,
    /// <summary>Displays the account name.</summary>
    UserName
}

/// <summary>Filters, sorts, and navigates service rows.</summary>
public sealed class ServiceView : IComparer<ServiceRow> {
    private ServiceRow[] _rows = new ServiceRow[128];
    /// <summary>Gets the active entry count.</summary>
    public int Count { get; private set; }
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; set; } = "";
    /// <summary>Gets or sets the process sort field.</summary>
    public ServiceColumn Sort { get; set; }
    /// <summary>Gets or sets whether rows are ordered in ascending order.</summary>
    public bool Ascending { get; set; } = true;
    /// <summary>Gets or sets the selected service name.</summary>
    public string? SelectedName { get; set; }
    /// <summary>Gets or sets the zero-based scroll offset.</summary>
    public int Offset { get; set; }
    /// <summary>Gets the filters applied to individual columns.</summary>
    public Dictionary<ServiceColumn, string> ColumnFilters { get; } = [];
    /// <summary>Gets the source rows or requested terminal row count.</summary>
    public ReadOnlySpan<ServiceRow> Rows => _rows.AsSpan(0, Count);

    /// <summary>Rebuilds visible service rows from the latest snapshot.</summary>
    /// <param name="source">The active service rows in the latest snapshot.</param>
    public void Update(ReadOnlySpan<ServiceRow> source) {
        if (_rows.Length < source.Length) {
            Array.Resize(ref _rows, Math.Max(source.Length, _rows.Length * 2));
        }

        Count = 0;
        foreach (ref readonly var row in source) {
            if (Filter.Length > 0 && !row.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                && !row.State.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                && !row.Description.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                && !row.Detail.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                && !(row.UserName?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(row.ProcessId?.ToString(CultureInfo.InvariantCulture).Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)) {
                continue;
            }

            bool matches = true;
            foreach (var filter in ColumnFilters) {
                if (!Matches(row, filter.Key, filter.Value)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                _rows[Count++] = row;
            }
        }

        Array.Sort(_rows, 0, Count, this);
        bool selected = false;
        foreach (ref readonly var row in Rows) {
            if (row.Name == SelectedName) {
                selected = true;
                break;
            }
        }

        if (!selected) {
            SelectedName = Count > 0 ? _rows[0].Name : null;
        }

        Offset = Math.Clamp(Offset, 0, Math.Max(0, Count - 1));
    }

    private static bool Matches(ServiceRow row, ServiceColumn column, string filter) {
        if (column == ServiceColumn.ProcessId && filter.StartsWith('>')) {
            return int.TryParse(filter.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int minimum)
                && row.ProcessId > minimum;
        }

        return filter.StartsWith('~') ? Text(row, column).AsSpan().Contains(filter.AsSpan(1), StringComparison.OrdinalIgnoreCase) : string.Equals(Text(row, column), filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Formats a service column&apos;s display value.</summary>
    /// <param name="row">The visible row or source record to inspect.</param>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>The formatted column value.</returns>
    public static string Text(in ServiceRow row, ServiceColumn column) =>
        column switch {
            ServiceColumn.Name => row.Name,
            ServiceColumn.State => row.State,
            ServiceColumn.Description => row.Description,
            ServiceColumn.Detail => row.Detail,
            ServiceColumn.UserName => row.UserName ?? "—",
            _ => row.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "—"
        };
    /// <summary>Compares services using the current sort field and direction.</summary>
    /// <param name="left">The left value in the comparison.</param>
    /// <param name="right">The right value in the comparison.</param>
    /// <returns>A negative, zero, or positive value indicating the ordering.</returns>
    public int Compare(ServiceRow left, ServiceRow right) {
        int comparison;
        if (Sort == ServiceColumn.ProcessId) {
            if (!left.ProcessId.HasValue || !right.ProcessId.HasValue) {
                return left.ProcessId.HasValue ? -1 : right.ProcessId.HasValue ? 1 : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            }

            comparison = left.ProcessId.Value.CompareTo(right.ProcessId.Value);
        }
        else {
            comparison = StringComparer.OrdinalIgnoreCase.Compare(Text(left, Sort), Text(right, Sort));
        }

        if (!Ascending) {
            comparison = -comparison;
        }

        return comparison == 0 ? StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name) : comparison;
    }

    /// <summary>Moves service selection and adjusts the visible page.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <param name="pageSize">The number of entries that fit in the visible page.</param>
    public void Move(int delta, int pageSize) {
        int index = 0;
        for (int i = 0; i < Count; i++) {
            if (_rows[i].Name == SelectedName) {
                index = i;
                break;
            }
        }

        index = (int)Math.Clamp((long)index + delta, 0, Math.Max(0, Count - 1));
        SelectedName = Count > 0 ? _rows[index].Name : null;
        if (index < Offset) {
            Offset = index;
        }

        if (index >= Offset + pageSize) {
            Offset = index - pageSize + 1;
        }

        Offset = Math.Clamp(Offset, 0, Math.Max(0, Count - pageSize));
    }
}
