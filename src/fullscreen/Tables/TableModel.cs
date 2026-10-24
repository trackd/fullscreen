using System.Globalization;
using System.Text.RegularExpressions;
using XenoAtom.Terminal.UI.Text;

namespace fullscreen.Tables;
/// <summary>Associates displayed cells and sortable values with an original source object.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Original">The original source object retained for selection output.</param>
/// <param name="Cells">The formatted cell texts.</param>
/// <param name="Values">The source values used to initialize the object.</param>
/// <param name="Detail">The additional display text for the row. Defaults to &quot;&quot;.</param>
public sealed record TableRow(long Id, object Original, string[] Cells, object?[] Values, string Detail = "");
/// <summary>Cached display data; rebuilding happens on changes, never during painting.</summary>
/// <param name="columns">The table column names or column definitions.</param>
public sealed class TableModel(string[] columns) {
    /// <summary>Gets the display headings in their original order.</summary>
    public string[] Columns { get; } = columns;
    /// <summary>Gets whether the control accepts user interaction.</summary>
    public bool[] Enabled { get; } = [.. Enumerable.Repeat(true, columns.Length)];
    /// <summary>Gets or sets the columns rendered with right-aligned values.</summary>
    public bool[]? RightAligned { get; set; }
    /// <summary>Gets or sets the preferred table column widths in terminal cells.</summary>
    public int[]? PreferredWidths { get; set; }
    /// <summary>Gets or sets whether cell styling reflects the value&apos;s type.</summary>
    public bool ColorByType { get; init; }
    /// <summary>Gets or sets whether rows represent object properties rather than source records.</summary>
    public bool PropertyRows { get; init; }
    /// <summary>Gets the original table rows.</summary>
    public List<TableRow> Rows { get; } = [];
    /// <summary>Gets whether the series participates in rendering.</summary>
    public List<TableRow> Visible { get; } = [];
    internal Dictionary<(long Row, int Column), StyledRun[]> CellStyles { get; } = [];
    /// <summary>Gets the identifiers or paths marked for an operation.</summary>
    public HashSet<long> Marked { get; } = [];
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; set; } = "";
    /// <summary>Gets the latest filter parsing error, or null when the filter is valid.</summary>
    public string? FilterError { get; private set; }
    /// <summary>Gets or sets the column index used for sorting.</summary>
    public int SortColumn { get; set; } = -1;
    /// <summary>Gets or sets the column index used for grouping.</summary>
    public int GroupColumn { get; set; } = -1;
    /// <summary>Gets or sets whether sorting uses descending order.</summary>
    public bool Descending { get; set; }
    /// <summary>Gets the currently selected row or entry.</summary>
    public int Selected { get; private set; }
    /// <summary>Gets the zero-based scroll offset.</summary>
    public int Offset { get; private set; }
    /// <summary>Gets or sets the row predicate used to restrict table contents.</summary>
    public Predicate<TableRow>? Include { get; set; }
    /// <summary>Gets the current monotonic counter sample.</summary>
    public TableRow? Current => Selected < Visible.Count ? Visible[Selected] : null;

    private string? _applied;
    /// <summary>Applies a longer plain filter to the already sorted visible rows; anything else rebuilds.</summary>
    public void Narrow() {
        string filter = Filter;
        if (_applied is null || !filter.Contains(_applied, StringComparison.OrdinalIgnoreCase)
            || Pattern(filter)
            || Pattern(_applied)) {
            Rebuild();
            return;
        }

        long? selected = Current?.Id;
        Visible.RemoveAll(row => !row.Cells.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _applied = filter;
        Reselect(selected);
    }

    private static bool Pattern(string filter) =>
        filter.StartsWith("regex:", StringComparison.OrdinalIgnoreCase)
        || filter.StartsWith("wildcard:", StringComparison.OrdinalIgnoreCase);
    private void Reselect(long? selected) {
        int index = selected.HasValue ? Visible.FindIndex(row => row.Id == selected.Value) : -1;
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, Math.Max(0, Visible.Count - 1));
        Offset = Math.Min(Offset, Selected);
    }

    /// <summary>Reapplies table filtering, sorting, and grouping.</summary>
    public void Rebuild() {
        Regex? pattern = null;
        string filter = Filter;
        _applied = null;
        FilterError = null;
        try {
            if (filter.StartsWith("regex:", StringComparison.OrdinalIgnoreCase)) {
                pattern = new Regex(
                    filter[6..],
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50)
                );
            }
            else if (filter.StartsWith("wildcard:", StringComparison.OrdinalIgnoreCase)) {
                pattern = new Regex(
                    "^" + Regex.Escape(filter[9..]).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) +
                    "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50)
                );
            }
        }
        catch (ArgumentException error) {
            FilterError = error.Message;
            Visible.Clear();
            Selected = Offset = 0;
            return;
        }

        long? selected = Current?.Id;
        Visible.Clear();
        foreach (var row in Rows) {
            if (Include is not null && !Include(row)) {
                continue;
            }

            try {
                if (filter.Length > 0
                    && !row.Cells.Any(cell => pattern is null ? cell.Contains(filter, StringComparison.OrdinalIgnoreCase) : pattern.IsMatch(cell))) {
                    continue;
                }
            }
            catch (RegexMatchTimeoutException) {
                FilterError = "Regular expression exceeded 50 ms; simplify the pattern.";
                Visible.Clear();
                Selected = Offset = 0;
                return;
            }

            Visible.Add(row);
        }

        if (SortColumn >= 0 || GroupColumn >= 0) {
            Visible.Sort((a, b) => {
                if (GroupColumn >= 0 && GroupColumn < Columns.Length) {
                    int group = Compare(a.Values[GroupColumn], b.Values[GroupColumn]);
                    if (group != 0) {
                        return group;
                    }
                }

                int result = SortColumn >= 0 && SortColumn < Columns.Length ? Compare(a.Values[SortColumn], b.Values[SortColumn]) : 0;
                if (Descending) {
                    result = -Math.Sign(result);
                }

                return result == 0 ? a.Id.CompareTo(b.Id) : result;
            });
        }

        _applied = filter;
        Reselect(selected);
    }

    /// <summary>Selects a visible table row and adjusts the scroll offset.</summary>
    /// <param name="index">The zero-based entry index.</param>
    /// <param name="height">The number of visible body rows.</param>
    public void Select(int index, int height) {
        Selected = Math.Clamp(index, 0, Math.Max(0, Visible.Count - 1));
        height = Math.Max(1, height);
        Offset = Math.Clamp(Offset, Math.Max(0, Selected - height + 1), Selected);
    }

    /// <summary>Moves the selected table row within the current page.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <param name="height">The number of visible body rows.</param>
    public void Move(int delta, int height) =>
        Select((int)Math.Clamp((long)Selected + delta, 0, int.MaxValue), height);
    /// <summary>Toggles the mark on the current table row.</summary>
    public void ToggleMark() {
        if (Current is { } row && !Marked.Add(row.Id)) {
            Marked.Remove(row.Id);
        }
    }

    /// <summary>Gets the original objects accepted by the user.</summary>
    public IEnumerable<TableRow> Chosen => Marked.Count > 0 ? Rows.Where(row => Marked.Contains(row.Id)) : Current is { } row ? [row] : [];

    /// <summary>Compares sortable cell values while handling nulls and differing types.</summary>
    /// <param name="left">The left value in the comparison.</param>
    /// <param name="right">The right value in the comparison.</param>
    /// <returns>A negative, zero, or positive value indicating the ordering.</returns>
    public static int Compare(object? left, object? right) {
        if (left is null) {
            return right is null ? 0 : -1;
        }

        if (right is null) {
            return 1;
        }

        if (Numeric(left) && Numeric(right)) {
            try {
                return Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
            }
            catch (OverflowException) {
                return Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
            }
        }

        if (left.GetType() == right.GetType() && left is IComparable comparable) {
            try {
                return comparable.CompareTo(right);
            }
            catch (ArgumentException) {
            }
        }

        return StringComparer.OrdinalIgnoreCase.Compare(Text(left), Text(right));
    }

    private static bool Numeric(object value) =>
        Type.GetTypeCode(value.GetType()) is >= TypeCode.SByte and <= TypeCode.Decimal;
    internal string DisplayCell(long row, int column, object? value) {
        string display;
        try {
            display = value?.ToString() ?? "null";
        }
        catch (Exception) {
            return "<display error>";
        }

        if (display.Contains('\u001b')) {
            var styled = Custom.SpectreView.ParseAnsi(display);
            CellStyles[(row, column)] = styled.Runs;
            return Text(styled.Text);
        }

        return Text(display);
    }

    /// <summary>Converts a cell value to bounded terminal display text.</summary>
    /// <param name="value">The cell value to format for display.</param>
    /// <param name="maximumLength">The maximum display-text length. Defaults to 4096.</param>
    /// <returns>The formatted and length-limited cell text.</returns>
    public static string Text(object? value, int maximumLength = 4096) {
        string text;
        try {
            text = value?.ToString() ?? "";
        }
        catch (Exception) {
            text = "<display error>";
        }

        int length = Math.Min(text.Length, maximumLength);
        return string.Create(length, text, static (span, source) => {
            for (int i = 0; i < span.Length; i++) {
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });
    }
}
