using fullscreen.Tables;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Custom;
/// <summary>Existing Fullscreen table surface, composed as a custom-TUI widget.</summary>
public sealed class ReusableTable : Visual {
    private readonly TuiNode _node;
    private object? _pending;
    private bool _dirty;
    private readonly VScrollBar _scrollBar = new() { IsVisible = false, IsTabStop = false };
    private bool _syncingScroll;
    private int ViewportRows => Surface.VisibleRowCount;
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public TableSurface Surface { get; }
    /// <summary>Gets the selected chart point and its original source reference.</summary>
    public object? Selection => Surface.Model.Current?.Original;

    /// <summary>Raised when submitted is requested.</summary>
    public event Action<object?>? Submitted;
    /// <summary>Initializes a new ReusableTable instance.</summary>
    /// <param name="node">The control definition being validated.</param>
    /// <param name="rows">The replacement objects used to populate the embedded table.</param>
    public ReusableTable(TuiNode node, object? rows) {
        _node = node;
        Surface = new(new TableModel(node.Columns.Length > 0 ? node.Columns : ["Value"]), node.Text) {
            Embedded = true,
            Minimal = node.Minimal
        };
        Surface.InspectRequested += row => Submitted?.Invoke(row.Original);
        if (rows is not null) {
            Replace(rows);
        }
        else if (node.Type == "FLogView" && node.Items.Length > 0) {
            Replace(node.Items);
        }

        AttachChild(Surface);
        AttachChild(_scrollBar);
        _scrollBar.ValueChangedRouted += (_, _) => {
            if (!_syncingScroll) {
                Surface.Model.ScrollTo(_scrollBar.Value, ViewportRows);
                Surface.Refresh();
            }
        };
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    /// <summary>Replaces the embedded table&apos;s source rows.</summary>
    /// <param name="rows">The replacement objects used to populate the embedded table.</param>
    public void Replace(object? rows) {
        _pending = rows;
        _dirty = true;
    }

    /// <summary>Processes embedded table updates without opening another fullscreen session.</summary>
    /// <param name="root">The root visual whose descendants are inspected.</param>
    /// <param name="token">The token used to cancel the operation. Defaults to default.</param>
    public void Tick(Visual root, CancellationToken token = default) {
        if (!TuiComponentHost.IsVisibleWithin(this, root)) {
            return;
        }

        if (_dirty) {
            LoadSnapshotCore(token);
        }
        _syncingScroll = true;
        try {
            _scrollBar.Maximum = Math.Max(0, Surface.Model.Visible.Count - ViewportRows);
            _scrollBar.ViewportSize = ViewportRows;
            _scrollBar.LargeChange = ViewportRows;
            _scrollBar.Value = Surface.Model.Offset;
            _scrollBar.IsVisible = Surface.Model.Visible.Count > ViewportRows;
        }
        finally {
            _syncingScroll = false;
        }
    }

    internal void LoadSnapshot() {
        LoadSnapshotCore(default);
    }

    private void LoadSnapshotCore(CancellationToken token) {
        if (!_dirty) {
            return;
        }

        object? rows = _pending;
        var previous = Surface.Model;
        var model = rows is null ? new TableModel(_node.Type == "FLogView" ? [
            "Timestamp",
            "Level",
            "Source",
            "Message"
        ] : _node.Columns.Length > 0 ? _node.Columns : ["Value"]) : TuiTableData.Create(rows, _node, token);
        model.Filter = previous.Filter;
        int MatchColumn(int index) =>
            index >= 0 && index < previous.Columns.Length ? Array.FindIndex(
                model.Columns,
                name => name.Equals(previous.Columns[index], StringComparison.OrdinalIgnoreCase)
            ) : -1;
        model.SortColumn = MatchColumn(previous.SortColumn);
        model.GroupColumn = MatchColumn(previous.GroupColumn);
        for (int column = 0; column < previous.Columns.Length; column++) {
            int target = MatchColumn(column);
            if (target >= 0) {
                model.Enabled[target] = previous.Enabled[column];
            }
        }

        model.Descending = previous.Descending;
        model.Rebuild();
        model.Select(previous.Selected, Math.Max(1, Surface.Bounds.Height - 6));
        Surface.Model = model;
        Surface.Status = $"{model.Rows.Count} rows · / filter · Enter submit";
        if (_node.Type == "FLogView") {
            Surface.RowAccent = row => row.Values[1] is Logs.LogLevel level ? level switch {
                Logs.LogLevel.Error or Logs.LogLevel.Critical => Surface.Palette.Error,
                Logs.LogLevel.Warning => Surface.Palette.Warning,
                _ => Surface.Palette.Success
            } : null;
        }

        if (!previous.Columns.SequenceEqual(model.Columns, StringComparer.OrdinalIgnoreCase)) {
            Surface.ResetColumns();
        }

        Surface.Refresh();
        _dirty = false;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 2;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index switch { 0 => Surface, 1 => _scrollBar, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        Surface.Measure(constraints);
        _scrollBar.Measure(constraints);
        return SizeHints.Flex(new(12, 5), new(70, 14), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        int width = _scrollBar.IsVisible ? 1 : 0;
        Surface.Arrange(new Rectangle(finalRect.X, finalRect.Y, Math.Max(0, finalRect.Width - width), finalRect.Height));
        _scrollBar.Arrange(new Rectangle(finalRect.Right - width, finalRect.Y + Surface.FirstRowY, width, ViewportRows));
    }
}
