namespace fullscreen.Monitoring;
/// <summary>Identifies a selectable process table column.</summary>
public enum ProcessColumn {
    /// <summary>Displays the process identifier.</summary>
    Id,
    /// <summary>Displays the parent process identifier.</summary>
    ParentId,
    /// <summary>Displays the account name.</summary>
    UserName,
    /// <summary>Displays the CPU utilization percentage.</summary>
    Cpu,
    /// <summary>Displays the working-set memory in bytes.</summary>
    Memory,
    /// <summary>Displays the working-set memory as a percentage of physical memory.</summary>
    MemoryPercent,
    /// <summary>Displays the GPU utilization percentage.</summary>
    Gpu,
    /// <summary>Displays the used graphics memory in bytes.</summary>
    GpuMemory,
    /// <summary>Displays the threads.</summary>
    Threads,
    /// <summary>Displays the name.</summary>
    Name,
    /// <summary>Displays the accumulated process CPU time.</summary>
    CpuTime,
    /// <summary>Displays the status.</summary>
    Status,
    /// <summary>Displays the disk throughput in bytes per second.</summary>
    Disk,
    /// <summary>Displays the network throughput in bytes per second.</summary>
    Network
}

/// <summary>Ordered per-monitor selection; narrow layouts may temporarily hide lower-priority columns.</summary>
public sealed class ProcessColumns {
    /// <summary>Gets the default visible process columns.</summary>
    public static ReadOnlySpan<ProcessColumn> Defaults => [
        ProcessColumn.Name,
        ProcessColumn.Id,
        ProcessColumn.UserName,
        ProcessColumn.Status,
        ProcessColumn.Cpu,
        ProcessColumn.Memory,
        ProcessColumn.Disk,
        ProcessColumn.Network,
        ProcessColumn.Gpu,
        ProcessColumn.GpuMemory,
        ProcessColumn.Threads
    ];

    private readonly List<ProcessColumn> _selected = [];
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<ProcessColumn> _readOnly;
    /// <summary>Gets the revision used to detect data changes.</summary>
    public int Version { get; private set; }
    /// <summary>Gets the currently selected row or entry.</summary>
    public IReadOnlyList<ProcessColumn> Selected => _readOnly;

    /// <summary>Initializes a new ProcessColumns instance.</summary>
    public ProcessColumns() {
        foreach (var column in Defaults) {
            _selected.Add(column);
        }

        _readOnly = _selected.AsReadOnly();
    }

    /// <summary>Checks whether a process column is enabled.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>True when the column is enabled.</returns>
    public bool Contains(ProcessColumn column) =>
        _selected.Contains(column);
    /// <summary>Applies explicit columns and exclusions while retaining the tree-name column when
    /// required.</summary>
    /// <param name="columns">The table column names or column definitions.</param>
    /// <param name="excluded">The columns excluded from display.</param>
    /// <param name="tree">Whether tree display requires the process-name column.</param>
    public void Configure(ProcessColumn[]? columns, ProcessColumn[]? excluded, bool tree) {
        var requested = new List<ProcessColumn>();
        if (columns is not null) {
            if (columns.Length == 0) {
                throw new ArgumentException("Column must contain at least one column.");
            }

            foreach (var column in columns) {
                if (!Enum.IsDefined(column) || requested.Contains(column)) {
                    throw new ArgumentException("Column names must be valid and unique.");
                }

                requested.Add(column);
            }
        }
        else {
            foreach (var column in Defaults) {
                requested.Add(column);
            }
        }

        if (excluded is not null) {
            foreach (var column in excluded) {
                if (!Enum.IsDefined(column)) {
                    throw new ArgumentException("Unknown excluded column.");
                }

                requested.Remove(column);
            }
        }

        if (tree && !requested.Contains(ProcessColumn.Name)) {
            requested.Add(ProcessColumn.Name);
        }

        if (requested.Count == 0) {
            throw new ArgumentException("At least one process column must remain enabled.");
        }

        _selected.Clear();
        _selected.AddRange(requested);
        Version++;
    }

    /// <summary>Toggles a process column while retaining required tree columns.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <param name="tree">Whether tree display requires the process-name column.</param>
    /// <returns>True when the column configuration changed.</returns>
    public bool Toggle(ProcessColumn column, bool tree) {
        if (!Enum.IsDefined(column)) {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if (_selected.Contains(column)) {
            if (_selected.Count == 1 || tree && column == ProcessColumn.Name) {
                return false;
            }

            _selected.Remove(column);
        }
        else {
            int name = _selected.IndexOf(ProcessColumn.Name);
            if (name > 0) {
                _selected.Insert(name, column);
            }
            else {
                _selected.Add(column);
            }
        }

        Version++;
        return true;
    }

    /// <summary>Ensures the process-name column is available for tree presentation.</summary>
    public void EnsureTreeName() {
        if (!_selected.Contains(ProcessColumn.Name)) {
            _selected.Add(ProcessColumn.Name);
            Version++;
        }
    }
}
