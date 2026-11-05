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
    private int _firstProcessColumn;
    private readonly List<ProcessColumn> _availableProcessColumns = [];
    private int _availableColumnsVersion = -1;
    private CollectorCapabilities? _availableCapabilities;
    private List<ProcessColumn> AvailableProcessColumns {
        get {
            if (_availableColumnsVersion != ColumnSettings.Version || _availableCapabilities != Frame.Capabilities) {
                _availableColumnsVersion = ColumnSettings.Version;
                _availableCapabilities = Frame.Capabilities;
                _availableProcessColumns.Clear();
                _availableProcessColumns.AddRange(ColumnSettings.Selected.Where(AvailableColumn));
                _columnLayoutWidth = -1;
            }
            return _availableProcessColumns;
        }
    }
    internal int FirstProcessColumn => _firstProcessColumn;
    private readonly record struct Column(int X, int Width, string Header, ProcessSort Sort, ProcessColumn Kind);
    private ReadOnlySpan<Column> Columns {
        get {
            LayoutColumns();
            return _columns.AsSpan(0, _columnCount);
        }
    }

    private static (string Header, int Width, int Priority, ProcessSort Sort) DescribeColumn(ProcessColumn column) =>
        column switch {
            ProcessColumn.Id => ("PID", 8, 9, ProcessSort.Id),
            ProcessColumn.ParentId => ("PPID", 7, 5, ProcessSort.ParentId),
            ProcessColumn.UserName => ("USER", 18, 6, ProcessSort.UserName),
            ProcessColumn.Cpu => ("CPU%", 6, 8, ProcessSort.Cpu),
            ProcessColumn.Memory => ("RSS", 10, 7, ProcessSort.Memory),
            ProcessColumn.MemoryPercent => ("MEM%", 7, 1, ProcessSort.Memory),
            ProcessColumn.Gpu => ("GPU%", 6, 4, ProcessSort.Gpu),
            ProcessColumn.GpuMemory => ("VRAM", 11, 2, ProcessSort.GpuMemory),
            ProcessColumn.CpuTime => ("TIME", 14, 3, ProcessSort.CpuTime),
            ProcessColumn.Status => ("STATUS", 12, 4, ProcessSort.Status),
            ProcessColumn.Disk => ("DISK/s", 12, 5, ProcessSort.Disk),
            ProcessColumn.Network => ("NET/s", 12, 5, ProcessSort.Network),
            ProcessColumn.Threads => ("THR", 6, 3, ProcessSort.Threads),
            _ => ("PROCESS", 24, 10, ProcessSort.Name)
        };
    private void LayoutColumns() {
        var selected = AvailableProcessColumns;
        if (_columnLayoutWidth == Bounds.Width && _columnLayoutVersion == ColumnSettings.Version) {
            return;
        }

        _columnLayoutWidth = Bounds.Width;
        _columnLayoutVersion = ColumnSettings.Version;
        _columnCount = 0;
        _firstProcessColumn = Math.Clamp(_firstProcessColumn, 0, Math.Max(0, selected.Count - 1));
        int x = 1;
        for (int i = _firstProcessColumn; i < selected.Count && x < Bounds.Width - 1; i++) {
            var (Header, Width, _, Sort) = DescribeColumn(selected[i]);
            _columns[_columnCount++] = new(x, Math.Min(Width, Math.Max(1, Bounds.Width - 1 - x)), Header, Sort, selected[i]);
            x += Width + 1;
        }
    }

    private void ScrollProcessColumns(int delta) {
        _firstProcessColumn = Math.Clamp(_firstProcessColumn + delta, 0, Math.Max(0, AvailableProcessColumns.Count - 1));
        _columnLayoutWidth = -1;
        Dirty = true;
        Refresh();
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
            "D defaults · ←/→ scroll overflow in table",
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
