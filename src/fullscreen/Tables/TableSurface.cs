using System.Text;
using System.Management.Automation;
using Wcwidth;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tables;
/// <summary>Shared virtualized grid for object snapshots and continuously arriving logs.</summary>
public sealed partial class TableSurface : Themes.ThemedSurface {
    private Style Body => Palette.TableBody;
    private Style Accent => Palette.AccentStyle;
    private Style Muted => Palette.TableMuted;
    private Style SelectedStyle => Palette.TableSelected;
    private Style MarkStyle => Palette.Mark;

    [Bindable]
    private partial int Revision { get; set; }
    /// <summary>Gets or sets the backing table or directory model.</summary>
    public TableModel Model { get; set; }
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; set; }
    /// <summary>Gets or sets the raw Git porcelain status text.</summary>
    public string Status { get; set; } = "";
    /// <summary>Gets or sets the application-specific keyboard help text.</summary>
    public string ExtraHelp { get; set; } = "";
    /// <summary>Gets or sets the primary footer key reference, or null for the standard table keys.</summary>
    public string? FooterHelp { get; set; }
    /// <summary>Gets or sets the object-navigation labels displayed by the table.</summary>
    public string Navigation { get; set; } = "";
    /// <summary>Gets or sets clickable commands displayed above the table.</summary>
    public (string Label, Action Execute)[] ToolbarCommands {
        get => _toolbarCommands;
        set {
            _toolbarCommands = value;
            _toolbar.Children.Clear();
            foreach (var command in value) {
                var (Label, Execute) = command;
                var button = new Button(Label) { MinHeight = 1, MaxHeight = 1 }.Click(() => {
                    App?.Focus(this);
                    Execute();
                });
                button.SetStyle(Palette.CompactActionButton);
                _toolbar.Add(button);
            }
            Refresh();
        }
    }
    private (string Label, Action Execute)[] _toolbarCommands = [];
    private readonly WrapHStack _toolbar = new() { Spacing = 1, RunSpacing = 1 };
    private Themes.ThemePalette? _toolbarPalette;
    private int _toolbarRows = 1;
    /// <summary>Gets or sets whether the user requested navigation to the previous object.</summary>
    public Func<bool>? BackRequested { get; set; }
    /// <summary>Gets or sets the preferred table column widths in terminal cells.</summary>
    public int[]? PreferredWidths { get; set; }
    /// <summary>Gets or sets the column that uses space remaining after other visible columns.</summary>
    public int StretchColumn { get; set; } = -1;
    /// <summary>Gets or sets the callback selecting an optional row accent.</summary>
    public Func<TableRow, Color?>? RowAccent { get; set; }
    /// <summary>Gets or sets the callback selecting an optional cell accent.</summary>
    public Func<TableRow, int, Color?>? CellAccent { get; set; }
    /// <summary>Gets or sets the callback selecting an optional detail or preview line accent.</summary>
    public Func<string, Color?>? DocumentAccent { get; set; }
    /// <summary>Gets or sets whether right-clicking a header opens its text filter.</summary>
    public bool ColumnFiltering { get; set; }
    /// <summary>Gets or sets the available tab headings.</summary>
    public string[]? Tabs { get; set; }
    /// <summary>Gets or sets the index of the active directory tab.</summary>
    public int ActiveTab { get; set; }

    /// <summary>Raised when tab requested is requested.</summary>
    public event Action<int>? TabRequested;
    /// <summary>Raised when context requested is requested.</summary>
    public event Action<int, int>? ContextRequested;
    /// <summary>Gets or sets the heading of the document panel.</summary>
    public string DocumentTitle { get; set; } = "JSON";
    /// <summary>Gets or sets the short context hint shown with the document.</summary>
    public string DocumentHint { get; set; } = "selected original object";
    /// <summary>Gets or sets the keyboard help shown for document navigation.</summary>
    public string? DocumentHelp { get; set; }

    private string[]? _preview;
    private string _previewTitle = "";
    private int _previewOffset;
    private int _splitPercent = 46;
    private bool _resizingSplit;
    internal int SplitPercent => _splitPercent;
    private int GridWidth => !Minimal && _preview is not null && Bounds.Width >= 100
        ? Math.Clamp(Bounds.Width * _splitPercent / 100, 1, Bounds.Width - 1)
        : Bounds.Width;

    /// <summary>Sets or clears an optional table preview panel.</summary>
    /// <param name="title">The displayed heading.</param>
    /// <param name="text">The source text to parse or display.</param>
    public void SetPreview(string title, string? text) {
        _previewTitle = title;
        _preview = text?.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        _previewOffset = 0;
        Refresh();
    }

    internal void UpdateLivePreview(string title, string text, bool follow) {
        _previewTitle = title;
        _preview = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        _previewOffset = follow ? Math.Max(0, _preview.Length - Height)
            : Math.Min(_previewOffset, Math.Max(0, _preview.Length - Height));
        Refresh();
    }

    private string[]? _document;
    private int _documentOffset;
    private int _documentX;
    private bool _documentSearching;
    private string _documentSearch = "";
    /// <summary>Sets or clears the document representation displayed by the surface.</summary>
    /// <param name="text">The source text to parse or display.</param>
    public void SetDocument(string? text) {
        ResetDocumentSelection();
        _document = text?.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        _filtering = false;
        _documentOffset = 0;
        _documentX = 0;
        _documentSearching = false;
        _documentSearch = "";
        Refresh();
    }

    internal void UpdateLiveDocument(string text, bool follow) {
        ResetDocumentSelection();
        _document = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        _documentOffset = follow ? Math.Max(0, _document.Length - DocumentRows)
            : Math.Min(_documentOffset, Math.Max(0, _document.Length - DocumentRows));
        Refresh();
    }

    /// <summary>Recalculates column layout from the current table data.</summary>
    public void ResetColumns() {
        _firstColumn = 0;
        _filtering = false;
        Refresh();
    }

    /// <summary>Raised when navigated is requested.</summary>
    public event Action? Navigated;
    /// <summary>Gets whether the application has requested termination.</summary>
    public bool ExitRequested { get; private set; }
    /// <summary>Gets whether the user accepted the current selection.</summary>
    public bool Accepted { get; private set; }
    /// <summary>Gets or sets whether Enter accepts the selected rows.</summary>
    public bool EnterAccepts { get; set; }
    /// <summary>Gets or sets whether the surface is hosted inside another visual.</summary>
    public bool Embedded { get; set; }

    /// <summary>Raised when inspect requested is requested.</summary>
    public event Action<TableRow>? InspectRequested;
    /// <summary>Raised when shortcut is requested.</summary>
    public event Action<string>? Shortcut;
    internal string[] PriorityShortcuts { get; set; } = [];

    private bool _filtering;
    private int _firstColumn;
    private int HeaderTop => Minimal ? (_filtering ? 1 : 0) : 2 + _toolbarRows;
    private int RowTop => HeaderTop + 1;
    private int DocumentTop => Minimal ? 0 : HeaderTop;
    private int DocumentRows => Math.Max(1, Bounds.Height - DocumentTop - (Minimal ? 0 : 2));
    private int Height => Math.Max(1, Bounds.Height - RowTop - (Minimal ? 0 : 2));
    internal int VisibleRowCount => Height;
    internal int FirstRowY => RowTop;

    /// <inheritdoc/>
    protected override void OnMinimalChanged() =>
        Refresh();
    /// <summary>Initializes a new TableSurface instance.</summary>
    /// <param name="model">The backing table or directory model.</param>
    /// <param name="title">The displayed heading.</param>
    public TableSurface(TableModel model, string title) {
        Model = model;
        Title = title;
        Focusable = true;
        AutoFocus = true;
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        AttachChild(_toolbar);
    }

    /// <summary>Requests a redraw of the table surface.</summary>
    public void Refresh() =>
        Revision++;
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = Revision;
        _toolbar.IsVisible = !Minimal && ToolbarCommands.Length > 0;
        if (!ReferenceEquals(_toolbarPalette, Palette)) {
            foreach (var button in _toolbar.Children.OfType<Button>()) {
                button.SetStyle(Palette.CompactActionButton);
            }
            _toolbarPalette = Palette;
        }
        var size = _toolbar.Measure(new LayoutConstraints(0, Math.Max(0, constraints.MaxWidth - 2), 0, Math.Max(0, constraints.MaxHeight - 6)));
        _toolbarRows = _toolbar.IsVisible ? Math.Max(1, size.Natural.Height) : 1;
        return SizeHints.Flex(new Size(0, 0), new Size(110, 30), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;
    /// <inheritdoc/>
    protected override Visual GetChild(int index) => _toolbar;
    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        _toolbar.Arrange(new(finalRect.X + 1, finalRect.Y + 2, Math.Max(0, finalRect.Width - 2), _toolbar.IsVisible ? _toolbarRows : 0));
    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        for (int y = 0; y < Bounds.Height; y++) {
            for (int x = 0; x < Bounds.Width; x++) {
                buffer.SetCell(Bounds.X + x, Bounds.Y + y, new Rune(' '), Body);
            }
        }

        if (!Minimal) {
            if (Tabs is null) {
                Write(buffer, 1, 0, Title, Accent, Bounds.Width - 2);
            }
            else {
                var (start, end) = TabRange();
                int x = start > 0 ? 3 : 1;
                if (start > 0) {
                    Write(buffer, 1, 0, "‹", Accent, 1);
                }

                for (int i = start; i < end; i++) {
                    string label = $" {i + 1} {Tabs[i]} ";
                    Write(
                        buffer,
                        x,
                        0,
                        label,
                        i == ActiveTab ? SelectedStyle.WithForeground(Palette.Accent) : Muted,
                        label.Length
                    );
                    x += label.Length + 1;
                }

                if (end < Tabs.Length) {
                    Write(buffer, Bounds.Width - 2, 0, "›", Accent, 1);
                }
                else {
                    Write(buffer, x + 2, 0, Title, Muted, Bounds.Width - x - 3);
                }
            }

            Write(
                buffer,
                1,
                1,
                Model.FilterError ?? (_documentSearching ? "FIND › " + _documentSearch +
                "▏ · Enter next · Esc finish" : _document is not null ? Status : _filtering ? "FILTER › " + Model.Filter +
                "▏" : Model.Filter.Length > 0 ? "FILTER · " + Model.Filter : Status),
                Model.FilterError is null ? Muted : Palette.Removed,
                Bounds.Width - 2
            );
            if (ToolbarCommands.Length == 0) {
                Write(buffer, 1, 2, Navigation, Accent, Bounds.Width - 2);
            }
        }
        else if (_filtering) {
            Write(buffer, 1, 0, "FILTER › " + Model.Filter + "▏", Muted, Bounds.Width - 2);
        }

        if (_document is not null) {
            for (int i = _documentOffset; i < Math.Min(_document.Length, _documentOffset + DocumentRows); i++) {
                var line = _document[i].AsSpan();
                Style style = DocumentStyle(_document[i]);
                Write(
                    buffer,
                    2,
                    DocumentTop + i - _documentOffset,
                    line[Math.Min(_documentX, line.Length)..],
                    style,
                    Bounds.Width - 3
                );
                if (DocumentTitle == "JSON") {
                    RenderJsonTokens(buffer, line, DocumentTop + i - _documentOffset);
                }
                RenderDocumentSelection(buffer, i, DocumentTop + i - _documentOffset);
            }

            if (!Minimal) {
                Write(
                    buffer,
                    1,
                    Bounds.Height - 2,
                    $"{DocumentTitle} · line {_documentOffset + 1}/{_document.Length} · {DocumentHint}",
                    Muted,
                    Bounds.Width - 2
                );
            }

            if (!Minimal) {
                Write(
                    buffer,
                    1,
                    Bounds.Height - 1,
                    DocumentHelp ?? "Tab next · ↑↓ scroll · ←→ pan · drag/Shift+arrows select · Ctrl+A all · Ctrl+C copy · " + (EnterAccepts ? "Enter accept · " : "") +
                    "Q close",
                    Accent,
                    Bounds.Width - 2
                );
            }

            RenderMinimalError(buffer);
            return;
        }

        for (int index = Model.Offset; index < Math.Min(Model.Visible.Count, Model.Offset + Height); index++) {
            if (index != Model.Selected) {
                continue;
            }

            for (int x = 0; x < GridWidth; x++) {
                buffer.SetCell(Bounds.X + x, Bounds.Y + RowTop + index - Model.Offset, new Rune(' '), SelectedStyle);
            }
        }

        for (int column = _firstColumn, x = 3; column < Model.Columns.Length && x < GridWidth - 1; column++) {
            if (!Model.Enabled[column]) {
                continue;
            }

            int columnWidth = Width(column, x);
            Write(
                buffer,
                x,
                HeaderTop,
                Model.Columns[column] + (Model.ColumnFilters.ContainsKey(column) ? " ◆" : "") + (column == Model.GroupColumn ? " ▦" : "") + (column == Model.SortColumn ? Model.Descending ? " ↓" : " ↑" : ""),
                Accent,
                columnWidth - 1
            );
            for (int index = Model.Offset; index < Math.Min(Model.Visible.Count, Model.Offset + Height); index++) {
                var row = Model.Visible[index];
                int y = RowTop + index - Model.Offset;
                Style style = index == Model.Selected ? SelectedStyle : Body;
                if (Model.ColorByType) {
                    style = style.WithForeground(ValueColor(row, column));
                }

                if (column == 1 && RowAccent?.Invoke(row) is { } color) {
                    style = style.WithForeground(color);
                }

                if (CellAccent?.Invoke(row, column) is { } cellColor) {
                    style = style.WithForeground(cellColor);
                }

                int padding = Model.RightAligned is { } aligned && column < aligned.Length && aligned[column] ? Math.Max(0, columnWidth - 2 - CellWidth(row.Cells[column])) : 0;
                Write(buffer, x + padding, y, row.Cells[column], style, columnWidth - 1 - padding);
                if (Model.CellStyles.TryGetValue((row.Id, column), out var runs)) {
                    foreach (var run in runs) {
                        int length = Math.Min(run.Length, row.Cells[column].Length - run.Start);
                        if (length > 0) {
                            int offset = CellWidth(row.Cells[column].AsSpan(0, run.Start));
                            var decorated = style.WithTextStyle(run.Style.TextStyle);
                            if (run.Style.TryGetForeground(out var foreground)) {
                                decorated = decorated.WithForeground(foreground);
                            }

                            if (index != Model.Selected && run.Style.TryGetBackground(out var background)) {
                                decorated = decorated.WithBackground(background);
                            }

                            Write(
                                buffer,
                                x + padding + offset,
                                y,
                                row.Cells[column].AsSpan(run.Start, length),
                                decorated,
                                columnWidth - 1 - padding - offset
                            );
                        }
                    }
                }
            }

            x += columnWidth;
        }

        for (int index = Model.Offset; index < Math.Min(Model.Visible.Count, Model.Offset + Height); index++) {
            Write(
                buffer,
                0,
                RowTop + index - Model.Offset,
                Model.Marked.Contains(Model.Visible[index].Id) ? " ●" : index == Model.Selected ? " ›" : "",
                MarkStyle,
                3
            );
        }

        if (Model.Visible.Count == 0) {
            Write(buffer, 3, RowTop, "No matching rows", Muted, Bounds.Width - 4);
        }

        if (_preview is not null && GridWidth < Bounds.Width) {
            for (int y = HeaderTop; y < Bounds.Height - 2; y++) {
                for (int x = GridWidth; x < Bounds.Width; x++) {
                    buffer.SetCell(Bounds.X + x, Bounds.Y + y, new Rune(' '), Body);
                }

                Write(buffer, GridWidth, y, "│", _resizingSplit ? Accent : Muted, 1);
            }

            Write(buffer, GridWidth + 2, HeaderTop, _previewTitle, Accent, Bounds.Width - GridWidth - 3);
            for (int i = _previewOffset; i < Math.Min(_preview.Length, _previewOffset + Height); i++) {
                string line = _preview[i];
                Style style = DocumentStyle(line, diff: true);
                Write(buffer, GridWidth + 2, HeaderTop + 1 + i - _previewOffset, line, style, Bounds.Width - GridWidth - 3);
            }
        }

        if (!Minimal) {
            Write(
                buffer,
                1,
                Bounds.Height - 2,
                $"{Model.Visible.Count:N0}/{Model.Rows.Count:N0} rows · {Model.Marked.Count:N0} marked  {ExtraHelp}" + (_preview is not null ? " · drag/Shift←→ resize panes" : ""),
                Muted,
                Bounds.Width - 2
            );
        }

        if (!Minimal) {
            Write(
                buffer,
                1,
                Bounds.Height - 1,
                FooterHelp ?? ("↑↓/jk move  ←→ columns  / filter  C columns  S sort  R reverse  Space mark  I inspect  Enter " + (EnterAccepts ? "accept" : "inspect") +
                "  Q close"),
                Accent,
                Bounds.Width - 2
            );
        }

        RenderMinimalError(buffer);
    }

    private void RenderMinimalError(CellBuffer buffer) {
        if (Minimal && Status.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) {
            Write(buffer, 1, Bounds.Height - 1, Status, Palette.Removed, Bounds.Width - 2);
        }
    }

    private Style DocumentStyle(string line, bool diff = false) {
        if (DocumentAccent?.Invoke(line) is { } color) {
            return Body.WithForeground(color);
        }

        return (diff || DocumentTitle != "JSON") && line.StartsWith('+') ? Palette.Added
            : (diff || DocumentTitle != "JSON") && line.StartsWith('-') ? Palette.Removed
            : line.AsSpan().TrimStart().StartsWith("\"") || line.StartsWith("@@", StringComparison.Ordinal) ? Accent : Body;
    }

    private int ColumnWidth() {
        int count = 0;
        for (int i = _firstColumn; i < Model.Enabled.Length; i++) {
            if (Model.Enabled[i]) {
                count++;
            }
        }

        return Math.Max(16, (GridWidth - 4) / Math.Max(1, Math.Min(5, count)));
    }

    private (int Start, int End) TabRange() {
        if (Tabs is null || Tabs.Length == 0) {
            return (0, 0);
        }

        int active = Math.Clamp(ActiveTab, 0, Tabs.Length - 1), start = 0, used = 0;
        for (int i = 0; i <= active; i++) {
            used += Tabs[i].Length + 5;
        }

        while (start < active && used > Math.Max(1, Bounds.Width - 5)) {
            used -= Tabs[start++].Length + 5;
        }

        int x = start > 0 ? 3 : 1, end = start;
        while (end < Tabs.Length && (x + Tabs[end].Length + 4 < Bounds.Width - 2 || end == start)) {
            x += Tabs[end].Length + 5;
            end++;
        }

        return (start, end);
    }

    private int Width(
        int column,
        int x
    ) {
        if (column == StretchColumn && PreferredWidths is { } preferred) {
            int reserved = 0;
            for (int next = column + 1; next < Model.Columns.Length; next++) {
                if (Model.Enabled[next]) {
                    reserved += next < preferred.Length && preferred[next] > 0 ? preferred[next] : ColumnWidth();
                }
            }
            return Math.Max(12, GridWidth - x - reserved - 1);
        }
        return PreferredWidths is { } widths && column < widths.Length ? widths[column] == 0 ? Math.Max(16, GridWidth - x - 1) : widths[column] : ColumnWidth();
    }
    private static int CellWidth(ReadOnlySpan<char> text) {
        int width = 0;
        foreach (var rune in text.EnumerateRunes()) {
            width += Math.Max(0, UnicodeCalculator.GetWidth(rune));
        }

        return width;
    }

    private Color ValueColor(TableRow row, int column) {
        if (Model.PropertyRows && column == 0) {
            return Palette.Property;
        }

        if (Model.PropertyRows && column == 2) {
            return Palette.Type;
        }

        if (row.Cells[column] is "<property error>" or "<display error>") {
            return Palette.Error;
        }

        object? value = row.Values[column];
        if (value is PSObject wrapped) {
            value = wrapped.BaseObject;
        }

        if (value is null or bool or Enum) {
            return Palette.Keyword;
        }

        if (value is string or char) {
            return Palette.StringLiteral;
        }

        if (Type.GetTypeCode(value.GetType()) is >= TypeCode.SByte and <= TypeCode.Decimal
            || value is Half or Int128 or UInt128) {
            return Palette.Number;
        }

        return Palette.Type;
    }

    private void Write(CellBuffer buffer, int x, int y, ReadOnlySpan<char> text, Style style, int width) {
        if (x < 0 || y < 0 || x >= Bounds.Width || y >= Bounds.Height || width <= 0) {
            return;
        }

        Span<char> safe = stackalloc char[Math.Min(1024, text.Length)];
        for (int i = 0; i < safe.Length; i++) {
            safe[i] = char.IsControl(text[i]) ? ' ' : text[i];
        }

        buffer.PushClip(new Rectangle(Bounds.X + x, Bounds.Y + y, Math.Min(width, Bounds.Width - x), 1));
        buffer.WriteText(Bounds.X + x, Bounds.Y + y, safe, style);
        buffer.PopClip();
    }

    private void Move(int delta) {
        Navigated?.Invoke();
        Model.Move(delta, Height);
        Refresh();
    }

    private void Inspect() {
        if (Model.Current is { } row) {
            if (InspectRequested is not null) {
                InspectRequested(row);
            }
            else {
                ShowDetail(row.Detail);
            }
        }
    }

    /// <summary>Opens a text-detail dialog.</summary>
    /// <param name="text">The source text to parse or display.</param>
    public void ShowDetail(string text) {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Details"),
            IsModal = true,
            Width = Math.Max(1, Math.Min(100, Bounds.Width - 4)),
            Height = Math.Max(1, Bounds.Height - 4),
            Content = new VStack(new ScrollViewer(new Paragraph(text) { Wrap = true }) {
                HorizontalScrollEnabled = false,
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch
            }, new Button("Close").Click(() => dialog!.Close()))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }

    /// <summary>Opens a source-code or diff detail dialog.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="fileName">The executable or source filename.</param>
    /// <param name="diff">Whether to use diff presentation. Defaults to false.</param>
    public void ShowCodeDetail(string text, string fileName, bool diff = false) {
        Dialog? dialog = null;
        var scroll = new ScrollViewer(Files.SyntaxPreview.Code(text, fileName, diff)) {
            HorizontalScrollEnabled = false,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        dialog = new Dialog {
            Title = new TextBlock(diff ? "Diff" : fileName),
            IsModal = true,
            Width = Math.Max(20, Bounds.Width - 4),
            Height = Math.Max(8, Bounds.Height - 4),
            Content = new VStack(scroll, new Button("Close · Esc").Click(() => {
                dialog!.Close();
                App?.Focus(this);
            }))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                App?.Focus(this);
                e.Handled = true;
            }
        });
        dialog.Show();
        App?.Focus(scroll);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview || !HasFocus) {
            return;
        }

        if (_documentSearching) {
            if (e.Key == TerminalKey.Escape) {
                _documentSearching = false;
            }
            else if (e.Key == TerminalKey.Enter) {
                FindDocument(next: true);
            }
            else if (e.Key == TerminalKey.Backspace && _documentSearch.Length > 0) {
                _documentSearch = _documentSearch[..^1];
                FindDocument(next: false);
            }

            e.Handled = true;
            Refresh();
            return;
        }

        if (_document is not null && HandleDocumentSelectionKey(e)) { return; }

        if (e.Key == TerminalKey.F5) {
            Shortcut?.Invoke("refresh");
            e.Handled = true;
            return;
        }

        if (!_filtering && e.Key == TerminalKey.Backspace && BackRequested?.Invoke() == true) {
            e.Handled = true;
            return;
        }

        if (!_filtering && e.Key == TerminalKey.Escape && (_document is not null || Model.Filter.Length == 0 && Model.ColumnFilters.Count == 0)
            && BackRequested?.Invoke() == true) {
            e.Handled = true;
            return;
        }

        if (e.Key == TerminalKey.F10) {
            Shortcut?.Invoke("menu");
            e.Handled = true;
            return;
        }

        if (_preview is not null && (e.Modifiers & TerminalModifiers.Shift) != 0
            && e.Key is TerminalKey.Up or TerminalKey.Down) {
            ScrollPreview(e.Key == TerminalKey.Up ? -3 : 3);
            e.Handled = true;
            return;
        }

        if (_preview is not null && _document is null && !_filtering && !Minimal
            && (e.Modifiers & TerminalModifiers.Shift) != 0 && e.Key is TerminalKey.Left or TerminalKey.Right) {
            ResizeSplit(e.Key == TerminalKey.Left ? -2 : 2);
            e.Handled = true;
            return;
        }

        if (Tabs is not null && e.Key == TerminalKey.Tab) {
            TabRequested?.Invoke((ActiveTab + ((e.Modifiers & TerminalModifiers.Shift) != 0 ? Tabs.Length - 1 : 1)) % Tabs.Length);
            e.Handled = true;
            return;
        }

        if (_document is not null) {
            if (e.Key == TerminalKey.Enter && !EnterAccepts) {
                e.Handled = true;
                return;
            }

            int delta = e.Key switch {
                TerminalKey.Up => -1,
                TerminalKey.Down => 1,
                TerminalKey.PageUp => -Height,
                TerminalKey.PageDown => Height,
                TerminalKey.Home => -int.MaxValue,
                TerminalKey.End => int.MaxValue,
                _ => 0
            };
            if (delta != 0) {
                ScrollDocument(delta);
                e.Handled = true;
                return;
            }

            if (e.Key is TerminalKey.Left or TerminalKey.Right) {
                _documentX = Math.Max(0, _documentX + (e.Key == TerminalKey.Left ? -8 : 8));
                Refresh();
                e.Handled = true;
                return;
            }
        }

        if (_filtering) {
            if (e.Key is TerminalKey.Enter or TerminalKey.Escape) {
                _filtering = false;
            }
            else if (e.Key == TerminalKey.Backspace && Model.Filter.Length > 0) {
                Model.Filter = Model.Filter[..^1];
                Model.Rebuild();
            }

            e.Handled = true;
            Refresh();
            return;
        }

        switch (e.Key) {
            case TerminalKey.Up:
                Move(-1);
                break;
            case TerminalKey.Down:
                Move(1);
                break;
            case TerminalKey.PageUp:
                Move(-Height);
                break;
            case TerminalKey.PageDown:
                Move(Height);
                break;
            case TerminalKey.Home:
                Move(-int.MaxValue);
                break;
            case TerminalKey.End:
                Move(int.MaxValue);
                break;
            case TerminalKey.Left:
                _firstColumn = Math.Max(0, _firstColumn - 1);
                break;
            case TerminalKey.Right:
                _firstColumn = Math.Min(Model.Columns.Length - 1, _firstColumn + 1);
                break;
            case TerminalKey.Enter:
                if (EnterAccepts) {
                    Accepted = true;
                    ExitRequested = true;
                }
                else {
                    Inspect();
                }

                break;
            case TerminalKey.Backspace:
                Shortcut?.Invoke("back");
                break;
            case TerminalKey.Escape:
                if (Model.Filter.Length > 0 || Model.ColumnFilters.Count > 0) {
                    Model.Filter = "";
                    Model.ColumnFilters.Clear();
                    Model.Rebuild();
                }
                else if (Embedded) {
                    return;
                }
                else {
                    ExitRequested = true;
                }

                break;
            default:
                return;
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e) {
        if (!HasFocus) {
            return;
        }

        if (_documentSearching) {
            _documentSearch += new string([.. e.Text.Where(c => !char.IsControl(c))]);
            _documentSearch = _documentSearch[..Math.Min(256, _documentSearch.Length)];
            FindDocument(next: false);
            e.Handled = true;
            Refresh();
            return;
        }

        if (!_filtering && PriorityShortcuts.Contains(e.Text, StringComparer.Ordinal)) {
            Shortcut?.Invoke(e.Text);
            e.Handled = true;
            Refresh();
            return;
        }

        if (!_filtering && Tabs is not null && e.Text.Length == 1 && e.Text[0] >= '1'
            && e.Text[0] < '1' + Tabs.Length) {
            TabRequested?.Invoke(e.Text[0] - '1');
            e.Handled = true;
            return;
        }

        if (_document is not null && e.Text is not ("q" or "Q")) {
            if (e.Text == "/") {
                _documentSearching = true;
                Refresh();
            }
            else if (e.Text is "n" or "N") {
                FindDocument(next: true);
            }
            else {
                Shortcut?.Invoke(e.Text);
            }

            e.Handled = true;
            return;
        }

        if (_filtering) {
            AppendFilter(e.Text);
        }
        else {
            switch (e.Text) {
                case "q":
                case "Q":
                    if (Embedded) {
                        return;
                    }

                    ExitRequested = true;
                    break;
                case "j":
                    Move(1);
                    break;
                case "k":
                    Move(-1);
                    break;
                case "/":
                    _filtering = true;
                    break;
                case "c":
                case "C":
                    Columns();
                    break;
                case "s":
                case "S":
                    Model.SortColumn = (Model.SortColumn + 1) % Model.Columns.Length;
                    Model.Rebuild();
                    break;
                case "r":
                case "R":
                    Model.Descending = !Model.Descending;
                    Model.Rebuild();
                    break;
                case "g":
                case "G":
                    Model.GroupColumn = (Model.GroupColumn + 2) % (Model.Columns.Length + 1) - 1;
                    Model.Rebuild();
                    break;
                case " ":
                    Model.ToggleMark();
                    if (Model.Include is not null) {
                        Model.Rebuild();
                    }

                    Move(1);
                    break;
                case "i":
                case "I":
                    Inspect();
                    break;
                default:
                    Shortcut?.Invoke(e.Text);
                    break;
            }
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPaste(PasteEventArgs e) {
        if (_filtering) {
            AppendFilter(e.Text);
            e.Handled = true;
        }
    }

    private void AppendFilter(string text) {
        foreach (char c in text) {
            if (!char.IsControl(c) && Model.Filter.Length < 256) {
                Model.Filter += c;
            }
        }

        Model.Narrow();
        Refresh();
    }

    /// <summary>Activates the table&apos;s filter editor.</summary>
    public void BeginFilter() {
        if (_document is not null) {
            _documentSearching = true;
        }
        else {
            _filtering = true;
        }

        Refresh();
    }

    private void FindDocument(bool next) {
        if (_document is null || _document.Length == 0 || _documentSearch.Length == 0) {
            return;
        }

        for (int step = next ? 1 : 0; step < _document.Length + (next ? 1 : 0); step++) {
            int index = (_documentOffset + step) % _document.Length;
            int column = _document[index].IndexOf(_documentSearch, StringComparison.OrdinalIgnoreCase);
            if (column < 0) {
                continue;
            }

            _documentOffset = index;
            _documentX = Math.Max(0, column - 8);
            Status = $"Found '{_documentSearch}' · line {index + 1} · N next · / search";
            Refresh();
            return;
        }

        Status = $"No matches for '{_documentSearch}' · / search";
        Refresh();
    }

    /// <summary>Opens the table&apos;s sort-column menu.</summary>
    public void ShowSortMenu() {
        var items = Model.Columns.Select((name, index) => new MenuItem(name, () => {
            if (Model.SortColumn == index) {
                Model.Descending = !Model.Descending;
            }
            else {
                Model.SortColumn = index;
                Model.Descending = false;
            }

            Model.Rebuild();
            Refresh();
        })).ToArray();
        ContextMenuService.Show(this, items, Bounds.X + 2, Bounds.Y + 3);
    }

    /// <summary>Opens a column picker followed by a text-only filter editor.</summary>
    public void ShowColumnFilterMenu() {
        var items = Model.Columns.Select((name, column) =>
            new MenuItem(name, () => ShowColumnFilter(column))).ToList();
        items.Add(new MenuItem("Clear column filters", () => {
            Model.ColumnFilters.Clear();
            Model.Rebuild();
            Refresh();
        }));
        ContextMenuService.Show(this, items, Bounds.X + 2, Bounds.Y + 3);
    }

    private void ShowColumnFilter(int column) {
        var input = new TextBox(Model.ColumnFilters.GetValueOrDefault(column, ""));
        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            App?.Focus(this);
        }
        void Apply() {
            string value = input.Text ?? "";
            if (value.Length == 0) {
                Model.ColumnFilters.Remove(column);
            }
            else {
                Model.ColumnFilters[column] = value;
            }
            Model.Rebuild();
            Refresh();
            Close();
        }
        dialog = new Dialog {
            Title = new TextBlock("Filter " + Model.Columns[column]),
            IsModal = true,
            Width = Math.Max(1, Math.Min(70, Bounds.Width - 4)),
            Content = new VStack(new TextBlock("Contains text (case-insensitive); empty clears"), input,
                new HStack(new Button("Apply").Click(Apply), new Button("Cancel").Click(Close)))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                Close();
                e.Handled = true;
            }
            else if (e.Key == TerminalKey.Enter) {
                Apply();
                e.Handled = true;
            }
        });
        dialog.Show();
        App?.Focus(input);
    }

    private void Columns() {
        var items = new MenuItem[Model.Columns.Length];
        for (int i = 0; i < items.Length; i++) {
            int column = i;
            items[i] = new MenuItem((Model.Enabled[i] ? "● " : "○ ") + Model.Columns[i], () => {
                if (!Model.Enabled[column] || Model.Enabled.Count(enabled => enabled) > 1) {
                    Model.Enabled[column] = !Model.Enabled[column];
                }

                Refresh();
            });
        }

        ContextMenuService.Show(this, items, Bounds.X + 2, Bounds.Y + 3);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (_preview is not null && _document is null && !Minimal && e.Button == TerminalMouseButton.Left
            && e.LocalY >= HeaderTop && e.LocalY < Bounds.Height - 2 && Math.Abs(e.LocalX - GridWidth) <= 1) {
            _resizingSplit = true;
            SetSplitFromPointer(e.LocalX);
            e.Handled = true;
            return;
        }

        if (!Minimal && Navigation.Length > 0 && e.LocalY == 2 && e.LocalX < 11) {
            BackRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (!Minimal && Tabs is not null && e.LocalY == 0) {
            var (start, end) = TabRange();
            int x = start > 0 ? 3 : 1;
            if (start > 0 && e.LocalX < 3) {
                TabRequested?.Invoke(start - 1);
            }
            else if (end < Tabs.Length && e.LocalX >= Bounds.Width - 3) {
                TabRequested?.Invoke(end);
            }
            else {
                for (int i = start; i < end; i++) {
                    int width = Tabs[i].Length + 4;
                    if (e.LocalX >= x && e.LocalX < x + width) {
                        TabRequested?.Invoke(i);
                        break;
                    }

                    x += width + 1;
                }
            }

            e.Handled = true;
            return;
        }

        if (_document is not null) {
            if (e.Button == TerminalMouseButton.Left && e.LocalY >= DocumentTop
                && e.LocalY < DocumentTop + DocumentRows) {
                App?.Focus(this);
                _documentCaret = DocumentPoint(e.LocalX, e.LocalY);
                if ((e.Modifiers & TerminalModifiers.Shift) == 0) { _documentAnchor = _documentCaret; }
                _selectingDocument = true;
                Refresh();
                e.Handled = true;
            }
            else if (e.Button == TerminalMouseButton.Right) {
                ContextMenuService.Show(this, [
                    new("Copy selection", CopyDocumentSelection)
                ], e.UiX, e.UiY);
                e.Handled = true;
            }
            return;
        }

        if (e.LocalX >= GridWidth) {
            return;
        }

        App?.Focus(this);
        if (e.LocalY == HeaderTop) {
            for (int i = _firstColumn, x = 3; i < Model.Columns.Length; i++) {
                if (!Model.Enabled[i]) {
                    continue;
                }

                int width = Width(i, x);
                if (e.LocalX >= x && e.LocalX < x + width) {
                    if (ColumnFiltering && e.Button == TerminalMouseButton.Right) {
                        ShowColumnFilter(i);
                    }
                    else if (Model.SortColumn == i) {
                        Model.Descending = !Model.Descending;
                    }
                    else {
                        Model.SortColumn = i;
                        Model.Descending = false;
                    }

                    Model.Rebuild();
                    break;
                }

                x += width;
            }
        }
        else if (e.LocalY >= RowTop && e.LocalY < RowTop + Height) {
            Navigated?.Invoke();
            Model.Select(Model.Offset + e.LocalY - RowTop, Height);
            if (e.Button == TerminalMouseButton.Right && ContextRequested is not null) {
                ContextRequested(e.UiX, e.UiY);
            }
            else if (e.Button == TerminalMouseButton.Right || e.ClickCount >= 2) {
                Inspect();
            }
        }

        Refresh();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e) {
        if (e.RoutingPhase != RoutingPhase.Preview && _selectingDocument) {
            ExtendDocumentSelection(e);
            return;
        }
        if (e.RoutingPhase == RoutingPhase.Preview || !_resizingSplit) {
            return;
        }

        SetSplitFromPointer(e.LocalX);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerEventArgs e) {
        if (e.RoutingPhase != RoutingPhase.Preview && _selectingDocument && e.Button == TerminalMouseButton.Left) {
            ExtendDocumentSelection(e);
            _selectingDocument = false;
            return;
        }
        if (e.RoutingPhase == RoutingPhase.Preview || !_resizingSplit) {
            return;
        }

        _resizingSplit = false;
        e.Handled = true;
        Refresh();
    }

    private void ResizeSplit(int delta) {
        _splitPercent = Math.Clamp(_splitPercent + delta, 30, 70);
        Refresh();
    }

    private void SetSplitFromPointer(int x) {
        if (Bounds.Width <= 0) {
            return;
        }

        _splitPercent = Math.Clamp((int)Math.Round((double)x * 100 / Bounds.Width), 30, 70);
        Refresh();
    }

    private void ScrollDocument(int delta) {
        _documentOffset = (int)Math.Clamp((long)_documentOffset + delta, 0, Math.Max(0, (_document?.Length ?? 0) - DocumentRows));
        Refresh();
    }

    private void ScrollPreview(int delta) {
        _previewOffset = Math.Clamp(_previewOffset + delta, 0, Math.Max(0, (_preview?.Length ?? 0) - Height));
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPointerWheel(PointerEventArgs e) {
        if (_document is not null) {
            ScrollDocument(e.WheelDelta > 0 ? -3 : 3);
        }
        else if (_preview is not null && e.LocalX >= GridWidth) {
            ScrollPreview(e.WheelDelta > 0 ? -3 : 3);
        }
        else {
            Move(e.WheelDelta > 0 ? -3 : 3);
        }

        e.Handled = true;
    }
}
