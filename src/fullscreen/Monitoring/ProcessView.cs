using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Selects the process field used for ordering rows.</summary>
public enum ProcessSort {
    /// <summary>Sorts processes by CPU utilization percentage.</summary>
    Cpu,
    /// <summary>Sorts processes by working-set memory in bytes.</summary>
    Memory,
    /// <summary>Sorts processes by process identifier.</summary>
    Id,
    /// <summary>Sorts processes by name.</summary>
    Name,
    /// <summary>Sorts processes by threads.</summary>
    Threads,
    /// <summary>Sorts processes by GPU utilization percentage.</summary>
    Gpu,
    /// <summary>Sorts processes by used graphics memory in bytes.</summary>
    GpuMemory,
    /// <summary>Sorts processes by account name.</summary>
    UserName,
    /// <summary>Sorts processes by parent process identifier.</summary>
    ParentId,
    /// <summary>Sorts processes by accumulated process CPU time.</summary>
    CpuTime,
    /// <summary>Sorts processes by status.</summary>
    Status,
    /// <summary>Sorts processes by disk throughput in bytes per second.</summary>
    Disk,
    /// <summary>Sorts processes by network throughput in bytes per second.</summary>
    Network
}

/// <summary>Selects the active performance monitor page.</summary>
public enum MonitorView {
    /// <summary>Shows the processes view.</summary>
    Processes,
    /// <summary>Shows the services view.</summary>
    Services,
    /// <summary>Shows the network view.</summary>
    Network,
    /// <summary>Shows the gpu view.</summary>
    Gpu,
    /// <summary>Shows the users view.</summary>
    Users,
    /// <summary>Shows the dashboard view.</summary>
    Dashboard
}

/// <summary>Filters, sorts, and navigates process rows while retaining stable process identities.</summary>
public sealed partial class ProcessView : IComparer<ProcessRow> {
    private readonly Comparison<ProcessRow> _comparison;
    private ProcessRow[] _rows = new ProcessRow[1024];
    /// <summary>Gets the active entry count.</summary>
    public int Count { get; private set; }
    /// <summary>Gets or sets the process sort field.</summary>
    public ProcessSort Sort { get; set; }
    /// <summary>Gets or sets whether rows are ordered in ascending order.</summary>
    public bool Ascending { get; set; }
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; set; } = "";
    /// <summary>Gets or sets the total physical memory in bytes.</summary>
    public ulong? TotalMemory { get; set; }
    /// <summary>Gets or sets the selected process identifier.</summary>
    public int SelectedId { get; set; } = -1;
    /// <summary>Gets or sets the zero-based scroll offset.</summary>
    public int Offset { get; set; }
    /// <summary>Gets the filters applied to individual columns.</summary>
    public Dictionary<ProcessColumn, string> ColumnFilters { get; } = [];
    /// <summary>Gets the process names excluded from display.</summary>
    public HashSet<string> ExcludedNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets the source rows or requested terminal row count.</summary>
    public ReadOnlySpan<ProcessRow> Rows => _rows.AsSpan(0, Count);

    /// <summary>Initializes a new ProcessView instance.</summary>
    public ProcessView() =>
        _comparison = Compare;
    /// <summary>Rebuilds visible process rows from the latest snapshot.</summary>
    /// <param name="source">The active process rows in the latest snapshot.</param>
    public void Update(ReadOnlySpan<ProcessRow> source) {
        if (_rows.Length < source.Length) {
            Array.Resize(ref _rows, Math.Max(source.Length, _rows.Length * 2));
        }

        Count = 0;
        if (Tree) {
            BuildTree(source);
        }
        else {
            foreach (ref readonly var row in source) {
                if (MatchesFilters(row)) {
                    _rows[Count++] = row;
                }
            }

            _rows.AsSpan(0, Count).Sort(_comparison);
        }

        bool selected = false;
        foreach (ref readonly var row in Rows) {
            if (row.Id == SelectedId) {
                selected = true;
                break;
            }
        }

        if (!selected) {
            SelectedId = Count > 0 ? _rows[0].Id : -1;
        }

        Offset = Math.Clamp(Offset, 0, Math.Max(0, Count - 1));
    }

    /// <summary>Checks process identity and display fields against a text filter.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <param name="filter">The text or expression used to restrict visible rows.</param>
    /// <returns>True when the process matches the filter.</returns>
    public static bool Matches(in ProcessRow row, ReadOnlySpan<char> filter) {
        if (filter.IsEmpty || row.Name.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.UserName.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        Span<char> pid = stackalloc char[16];
        row.Id.TryFormat(pid, out int length, provider: CultureInfo.InvariantCulture);
        return pid[..length].Contains(filter, StringComparison.Ordinal);
    }

    /// <summary>Compares two processes using the current sort field and direction.</summary>
    /// <param name="left">The left value in the comparison.</param>
    /// <param name="right">The right value in the comparison.</param>
    /// <returns>A negative, zero, or positive value indicating the ordering.</returns>
    public int Compare(ProcessRow left, ProcessRow right) {
        // Unsupported counters stay last in either sort direction.
        if (Sort is ProcessSort.Gpu or ProcessSort.GpuMemory or ProcessSort.UserName or ProcessSort.ParentId
            or ProcessSort.Disk
            or ProcessSort.Network
            or ProcessSort.Status) {
            bool leftKnown = Sort switch {
                ProcessSort.Gpu => left.GpuPercent.HasValue,
                ProcessSort.GpuMemory => left.GpuMemory.HasValue,
                ProcessSort.UserName => left.UserName is not null,
                ProcessSort.Disk => left.DiskBytesPerSecond.HasValue,
                ProcessSort.Network => left.NetworkBytesPerSecond.HasValue,
                ProcessSort.Status => left.Status is not null,
                _ => left.ParentId.HasValue
            };
            bool rightKnown = Sort switch {
                ProcessSort.Gpu => right.GpuPercent.HasValue,
                ProcessSort.GpuMemory => right.GpuMemory.HasValue,
                ProcessSort.UserName => right.UserName is not null,
                ProcessSort.Disk => right.DiskBytesPerSecond.HasValue,
                ProcessSort.Network => right.NetworkBytesPerSecond.HasValue,
                ProcessSort.Status => right.Status is not null,
                _ => right.ParentId.HasValue
            };
            if (leftKnown != rightKnown) {
                return leftKnown ? -1 : 1;
            }
        }

        int result = Sort switch {
            ProcessSort.Memory => right.WorkingSet.CompareTo(left.WorkingSet),
            ProcessSort.Id => right.Id.CompareTo(left.Id),
            ProcessSort.Name => StringComparer.OrdinalIgnoreCase.Compare(right.Name, left.Name),
            ProcessSort.Threads => right.Threads.CompareTo(left.Threads),
            ProcessSort.Gpu => CompareNullable(left.GpuPercent, right.GpuPercent),
            ProcessSort.GpuMemory => CompareNullable(left.GpuMemory, right.GpuMemory),
            ProcessSort.UserName => StringComparer.OrdinalIgnoreCase.Compare(right.UserName, left.UserName),
            ProcessSort.ParentId => Nullable.Compare(right.ParentId, left.ParentId),
            ProcessSort.CpuTime => right.CpuTimeTicks.CompareTo(left.CpuTimeTicks),
            ProcessSort.Status => StringComparer.OrdinalIgnoreCase.Compare(right.Status, left.Status),
            ProcessSort.Disk => CompareNullable(left.DiskBytesPerSecond, right.DiskBytesPerSecond),
            ProcessSort.Network => CompareNullable(left.NetworkBytesPerSecond, right.NetworkBytesPerSecond),
            _ => right.CpuPercent.CompareTo(left.CpuPercent)
        };
        if (Ascending) {
            result = -result;
        }

        return result == 0 ? left.Id.CompareTo(right.Id) : result;
    }

    /// <summary>Moves process selection and adjusts the visible page.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <param name="pageSize">The number of visible process rows.</param>
    public void Move(int delta, int pageSize) {
        int index = 0;
        for (int i = 0; i < Count; i++) {
            if (_rows[i].Id == SelectedId) {
                index = i;
                break;
            }
        }

        index = Math.Clamp(index + delta, 0, Math.Max(0, Count - 1));
        SelectedId = Count > 0 ? _rows[index].Id : -1;
        if (index < Offset) {
            Offset = index;
        }

        if (index >= Offset + pageSize) {
            Offset = Math.Max(0, index - pageSize + 1);
        }
    }

    private static int CompareNullable<T>(T? left, T? right)
        where T : struct, IComparable<T> =>
        !left.HasValue ? (right.HasValue ? 1 : 0) : !right.HasValue ? -1 : right.Value.CompareTo(left.Value);
}
