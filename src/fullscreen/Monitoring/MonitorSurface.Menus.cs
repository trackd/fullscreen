using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private bool Supports(CollectorCapabilities capability) =>
        !Frame.Capabilities.HasValue || (Frame.Capabilities.Value & capability) == capability;

    private bool AvailableColumn(ProcessColumn column) => column switch {
        ProcessColumn.ParentId => Supports(CollectorCapabilities.ProcessParent),
        ProcessColumn.UserName => Supports(CollectorCapabilities.ProcessOwner),
        ProcessColumn.Status => Supports(CollectorCapabilities.ProcessStatus),
        ProcessColumn.Disk => Supports(CollectorCapabilities.ProcessDisk),
        ProcessColumn.Network => Supports(CollectorCapabilities.ProcessNetwork),
        ProcessColumn.Gpu => Supports(CollectorCapabilities.ProcessGpu),
        ProcessColumn.GpuMemory => Supports(CollectorCapabilities.ProcessGpuMemory),
        _ => true
    };

    private bool AvailableColumn(GpuColumn column) => column switch {
        GpuColumn.Utilization => Supports(CollectorCapabilities.GpuUtilization),
        GpuColumn.UsedMemory => Supports(CollectorCapabilities.GpuUsedMemory),
        GpuColumn.TotalMemory => Supports(CollectorCapabilities.GpuTotalMemory),
        GpuColumn.Temperature => Supports(CollectorCapabilities.GpuTemperature),
        _ => true
    };

    /// <summary>Sets or clears the filter for a process column.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <param name="value">The input value to format, display, or append.</param>
    public void SetColumnFilter(ProcessColumn column, string? value) {
        if (value is null) {
            Model.ColumnFilters.Remove(column);
        }
        else {
            Model.ColumnFilters[column] = value;
        }

        Dirty = true;
        Refresh();
    }

    private void OpenHeaderMenu(ProcessColumn column, PointerEventArgs e) {
        var sort = DescribeColumn(column).Sort;
        var items = new List<MenuItem>
        {
            new("Sort ascending", () =>
            {
                Model.Sort = sort;
                Model.Ascending = true;
                Dirty = true;
                Refresh();
            }),
            new("Sort descending", () =>
            {
                Model.Sort = sort;
                Model.Ascending = false;
                Dirty = true;
                Refresh();
            }),
            MenuItem.Separator(),
            new("Clear this filter", () => SetColumnFilter(column, null)),
            new("Clear all column filters", () =>
            {
                Model.ColumnFilters.Clear();
                Dirty = true;
                Refresh();
            })
        };
        if (column is ProcessColumn.Name or ProcessColumn.UserName or ProcessColumn.Status) {
            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ref readonly var row in Frame.Processes.AsSpan(0, Frame.ProcessCount)) {
                values.Add(ProcessView.TextValue(row, column));
            }

            var valuesMenu = new MenuItem("Filter value");
            foreach (string value in values.Take(Math.Max(1, Math.Min(12, Bounds.Height - 8)))) {
                string captured = value;
                valuesMenu.Items.Add(new MenuItem(value, () => SetColumnFilter(column, captured)));
            }

            items.Add(valuesMenu);
        }
        else {
            items.Add(new("Greater than zero", () => SetColumnFilter(column, ">0")));
            if (column is ProcessColumn.Cpu or ProcessColumn.Gpu or ProcessColumn.MemoryPercent) {
                items.Add(new("Greater than 10%", () => SetColumnFilter(column, ">10")));
            }

            if (column == ProcessColumn.Memory) {
                items.Add(new("Greater than 100 MiB", () => SetColumnFilter(column, ">104857600")));
            }
        }

        ContextMenuService.Show(this, items, e.UiX, e.UiY);
        e.Handled = true;
    }

    private void OpenProcessMenu(PointerEventArgs e) {
        if (!ProcessTable || e.LocalY < TableTop || e.LocalY >= Bounds.Height - Footer) {
            return;
        }

        int visible = Math.Max(0, Bounds.Height - TableTop - Footer);
        int index = Math.Clamp(Model.Offset, 0, Math.Max(0, Model.Count - visible)) + e.LocalY - TableTop;
        if ((uint)index >= (uint)Model.Count) {
            return;
        }

        var row = Model.Rows[index];
        Model.SelectedId = row.Id;
        ShowProcessMenu(row, e.UiX, e.UiY);
        e.Handled = true;
        Refresh();
    }

    private void ShowProcessMenu(ProcessRow row, int x, int y) {
        var items = new List<MenuItem>
        {
            new($"{row.Name} · PID {row.Id}")
            {
                IsEnabled = false
            },
            MenuItem.Separator(),
            new("Filter this name", () => SetColumnFilter(ProcessColumn.Name, row.Name)),
            new("Exclude this process name (E)", () => ExcludeProcess(row.Name)),
            new("Restore excluded processes (U)", () =>
            {
                Model.ExcludedNames.Clear();
                Dirty = true;
                Refresh();
            })
            {
                IsEnabled = Model.ExcludedNames.Count > 0
            },
            new("Filter this user", () => SetColumnFilter(ProcessColumn.UserName, row.UserName ?? "—")),
            new("Toggle process tree", ToggleTree),
            new("Expand / collapse children", () =>
            {
                Dirty |= Model.ToggleSelected();
                Refresh();
            }),
            new("Expand all", () =>
            {
                Dirty |= Model.CollapseAll(false);
                Refresh();
            })
            {
                IsEnabled = Model.Tree
            },
            new("Collapse all", () =>
            {
                Dirty |= Model.CollapseAll(true);
                Refresh();
            })
            {
                IsEnabled = Model.Tree
            },
            new("Choose columns…", OpenColumns)
        };
        if (row.Name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)) {
            items.Insert(
                1,
                new MenuItem("Windows compressed-memory store · shared system accounting") { IsEnabled = false }
            );
        }

        items.Add(MenuItem.Separator());
        items.AddRange(ProcessActions(row));
        ContextMenuService.Show(this, items, x, y);
        Refresh();
    }
}
