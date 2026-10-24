using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    /// <summary>Gets or sets the current process-column configuration.</summary>
    public ProcessColumns ColumnSettings { get; init; } = new();
    /// <summary>Gets whether the column-selection interface is open.</summary>
    public bool ColumnsOpen { get; private set; }

    private int _columnPickerIndex;
    private readonly Column[] _columns = new Column[14];
    private int _columnCount, _columnLayoutWidth = -1, _columnLayoutVersion = -1;
    private readonly record struct Column(int X, int Width, string Header, ProcessSort Sort, ProcessColumn Kind);
    private ReadOnlySpan<Column> Columns {
        get {
            LayoutColumns();
            return _columns.AsSpan(0, _columnCount);
        }
    }

    private static (string Header, int Width, int Priority, ProcessSort Sort) DescribeColumn(ProcessColumn column) =>
        column switch {
            ProcessColumn.Id => ("PID", 7, 9, ProcessSort.Id),
            ProcessColumn.ParentId => ("PPID", 7, 5, ProcessSort.ParentId),
            ProcessColumn.UserName => ("USER", 20, 6, ProcessSort.UserName),
            ProcessColumn.Cpu => ("CPU%", 7, 8, ProcessSort.Cpu),
            ProcessColumn.Memory => ("RSS", 11, 7, ProcessSort.Memory),
            ProcessColumn.MemoryPercent => ("MEM%", 7, 1, ProcessSort.Memory),
            ProcessColumn.Gpu => ("GPU%", 7, 4, ProcessSort.Gpu),
            ProcessColumn.GpuMemory => ("VRAM", 11, 2, ProcessSort.GpuMemory),
            ProcessColumn.CpuTime => ("TIME", 14, 3, ProcessSort.CpuTime),
            ProcessColumn.Status => ("STATUS", 12, 4, ProcessSort.Status),
            ProcessColumn.Disk => ("DISK/s", 14, 5, ProcessSort.Disk),
            ProcessColumn.Network => ("NET/s", 14, 5, ProcessSort.Network),
            ProcessColumn.Threads => ("THR", 6, 3, ProcessSort.Threads),
            _ => ("PROCESS", 24, 10, ProcessSort.Name)
        };
    private void LayoutColumns() {
        if (_columnLayoutWidth == Bounds.Width && _columnLayoutVersion == ColumnSettings.Version) {
            return;
        }

        _columnLayoutWidth = Bounds.Width;
        _columnLayoutVersion = ColumnSettings.Version;
        _columnCount = 0;
        var selected = ColumnSettings.Selected;
        Span<bool> visible = stackalloc bool[14];
        Span<int> widths = stackalloc int[14];
        visible.Clear();
        widths.Clear();
        int total = 0, active = selected.Count;
        for (int i = 0; i < selected.Count; i++) {
            if (!_probeColumns && !AvailableColumn(selected[i])) {
                active--;
                continue;
            }

            visible[i] = true;
            widths[i] = DescribeColumn(selected[i]).Width;
            if (selected[i] == ProcessColumn.UserName && Bounds.Width < 115) {
                widths[i] = 12;
            }

            total += widths[i] + 1;
        }

        int available = Math.Max(1, Bounds.Width - 2);
        while (total - 1 > available && active > 1) {
            int hide = -1, priority = int.MaxValue;
            for (int i = 0; i < selected.Count; i++) {
                int candidate = DescribeColumn(selected[i]).Priority;
                if (visible[i] && candidate < priority) {
                    hide = i;
                    priority = candidate;
                }
            }

            if (hide < 0) {
                break;
            }

            visible[hide] = false;
            total -= widths[hide] + 1;
            active--;
        }

        int x = 1, extra = Math.Max(0, available - (total - 1));
        for (int i = 0; i < selected.Count; i++) {
            if (!visible[i]) {
                continue;
            }

            var (Header, Width, Priority, Sort) = DescribeColumn(selected[i]);
            int width = selected[i] == ProcessColumn.Name ? widths[i] + extra : widths[i];
            _columns[_columnCount++] = new(x, Math.Min(width, Math.Max(1, Bounds.Width - 1 - x)), Header, Sort, selected[i]);
            x += width + 1;
        }
    }

    /// <summary>Toggles the process-tree view and adjusts required columns.</summary>
    public void ToggleTree() {
        Model.Tree = !Model.Tree;
        if (Model.Tree) {
            ColumnSettings.EnsureTreeName();
        }

        Dirty = true;
        Refresh();
    }

    private void TreeName(CellBuffer buffer, int x, int y, int width, int row, Style style) {
        Span<char> prefix = stackalloc char[64];
        int depth = Model.DepthAt(row), shown = Math.Min(depth, Math.Min(28, Math.Max(0, (width - 8) / 2)));
        int length = shown * 2;
        prefix[..length].Fill(' ');
        if (shown < depth && length > 0) {
            prefix[0] = '…';
        }

        if (depth > 0) {
            prefix[length++] = Model.IsLastAt(row) ? '└' : '├';
            prefix[length++] = '─';
        }

        prefix[length++] = Model.HasChildrenAt(row) ? Model.IsCollapsedAt(row) ? '▸' : '▾' : ' ';
        prefix[length++] = ' ';
        Write(buffer, x, y, prefix[..length], style, width);
        Write(buffer, x + length, y, Model.Rows[row].Name, style, width - length);
    }

    private Rectangle PickerBounds => new(
        Math.Max(1, (Bounds.Width - Math.Min(54, Bounds.Width - 4)) / 2),
        1,
        Math.Min(54, Bounds.Width - 4),
        Math.Min(20, Bounds.Height - 2)
    );
    private int PickerVisible => Math.Max(1, PickerBounds.Height - 5);
    private int PickerOffset => Math.Max(0, _columnPickerIndex - PickerVisible + 1);

    private void RenderColumnPicker(CellBuffer buffer) {
        var box = PickerBounds;
        Box(buffer, box.X, box.Y, box.Width, box.Height, PickerTitle, Accent);
        Write(buffer, box.X + 2, box.Y + 1, "Space / click toggle · Enter / Esc close", Muted, box.Width - 4);
        for (int i = PickerOffset; i < Math.Min(PickerCount, PickerOffset + PickerVisible); i++) {
            int y = box.Y + 3 + i - PickerOffset;
            Style style = i == _columnPickerIndex ? Selected : Panel;
            Fill(buffer, box.X + 1, y, box.Width - 2, 1, style);
            Write(buffer, box.X + 2, y, PickerContains(i) ? "[✓]" : "[ ]", style);
            Write(buffer, box.X + 7, y, PickerName(i), style, box.Width - 10);
        }

        Write(
            buffer,
            box.X + 2,
            box.Y + box.Height - 2,
            "D defaults · narrow screens hide overflow",
            Muted,
            box.Width - 4
        );
    }

    private void TogglePickerColumn() {
        ToggleActiveColumn(_columnPickerIndex);
        Dirty = true;
        Refresh();
    }

    private void PickerKey(KeyEventArgs e) {
        switch (e.Key) {
            case TerminalKey.Up:
                _columnPickerIndex = Math.Max(0, _columnPickerIndex - 1);
                break;
            case TerminalKey.Down:
                _columnPickerIndex = Math.Min(PickerCount - 1, _columnPickerIndex + 1);
                break;
            case TerminalKey.Escape:
            case TerminalKey.Enter:
                ColumnsOpen = false;
                break;
            default:
                return;
        }

        e.Handled = true;
        Refresh();
    }

    private void PickerPointer(PointerEventArgs e) {
        var box = PickerBounds;
        int index = PickerOffset + e.LocalY - box.Y - 3;
        if (e.LocalX >= box.X && e.LocalX < box.X + box.Width && e.LocalY >= box.Y + 3
            && e.LocalY < box.Y + box.Height - 2
            && index >= 0
            && index < PickerCount) {
            _columnPickerIndex = index;
            TogglePickerColumn();
        }
        else if (e.LocalX < box.X || e.LocalX >= box.X + box.Width || e.LocalY < box.Y
            || e.LocalY >= box.Y + box.Height) {
            ColumnsOpen = false;
        }

        e.Handled = true;
        Refresh();
    }
}
