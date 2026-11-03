using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;
/// <summary>A single virtualized visual; numeric formatting and table rows stay on the stack.</summary>
public sealed partial class MonitorSurface : Themes.ThemedSurface {
    private Style Body => Palette.Body;
    private Style Panel => Palette.Panel;
    private Style Accent => Palette.AccentStyle;
    private Style Selected => Palette.Selected;
    private Style Muted => Palette.Muted;
    private Style Hot => Palette.Hot;
    private Style Green => Palette.Green;
    private Style Purple => Palette.PurpleStyle;

    [Bindable]
    private partial int Revision { get; set; }
    /// <summary>Gets the backing table or directory model.</summary>
    public ProcessView Model { get; } = new();

    private MonitorView _view = MonitorView.Processes;
    /// <summary>Gets or sets whether only the user-session view is available.</summary>
    public bool UsersOnly { get; init; }
    /// <summary>Gets or sets whether owned processes are shown in the user-session tree.</summary>
    public bool IncludeUserProcesses { get; set; } = true;
    /// <summary>Gets or sets the resource or monitor view index.</summary>
    public MonitorView View {
        get => UsersOnly ? MonitorView.Users : _view;
        set => _view = value;
    }
    /// <summary>Gets whether live updates are paused.</summary>
    public bool Paused { get; private set; }
    /// <summary>Gets whether the application has requested termination.</summary>
    public bool ExitRequested { get; private set; }
    /// <summary>Gets or sets whether the document has changes not yet saved.</summary>
    public bool Dirty { get; set; } = true;
    /// <summary>Gets or sets the CPU utilization percentage.</summary>
    public double? CpuPercent { get; set; }
    /// <summary>Gets or sets the total physical memory in bytes.</summary>
    public ulong? TotalMemory { get; set; }
    /// <summary>Gets or sets the available physical memory in bytes.</summary>
    public ulong? AvailableMemory { get; set; }
    /// <summary>Gets or sets the number of valid process rows in the process buffer.</summary>
    public int ProcessCount { get; set; }
    /// <summary>Gets or sets the elapsed metric-collection time in milliseconds.</summary>
    public double SampleMilliseconds { get; set; }
    /// <summary>Gets or sets the number of managed bytes allocated during collection.</summary>
    public long SampleAllocation { get; set; }

    private bool _editingFilter;
    private static int SummaryHeight(int height) =>
        Math.Clamp(height / 6, 6, 8);
    private int CardHeight => SummaryHeight(Bounds.Height);

    private int DetailChartHeight(int height) {
        int rows = View == MonitorView.Network ? Frame.NetworkCount : Frame.GpuCount;
        int tableRows = Math.Clamp(rows, 3, Math.Max(3, height / 3));
        return Math.Clamp(height - tableRows - 8, 7, Math.Max(7, height - 11));
    }

    private bool ProcessTable => View == MonitorView.Processes;
    private int TableTop => Minimal ? (_editingFilter ? 2 : 1) : Bounds.Width >= 76 && Bounds.Height >= 20 ? 5 + (View is MonitorView.Network or MonitorView.Gpu ? DetailChartHeight(Bounds.Height) : CardHeight) : 8;
    private int Footer => Minimal ? 0 : 1;
    private int PageSize => Math.Max(1, Bounds.Height - TableTop - Footer);

    /// <summary>Initializes a new MonitorSurface instance.</summary>
    public MonitorSurface() {
        this.AddCommand(new Command {
            Id = "Fullscreen.Monitor.Users",
            LabelMarkup = "User sessions",
            Gesture = new KeyGesture(TerminalKey.F8),
            Execute = _ => ShowUserSessions()
        });
        foreach (var chart in _plots) {
            AttachChild(chart);
        }
        InitializeUserButtons();

        Focusable = true;
        AutoFocus = true;
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    /// <summary>Requests a redraw of the monitor surface.</summary>
    public void Refresh() =>
        Revision++;
    /// <inheritdoc/>
    protected override int ChildrenCount => _plots.Length + 2;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index < _plots.Length ? _plots[index] : index == _plots.Length ? _markSessionsButton : _logoffSessionsButton;
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = Revision;
        PrepareCharts(Math.Min(constraints.MaxWidth, 2000), Math.Min(constraints.MaxHeight, 1000));
        MeasureUserButtons(constraints.MaxWidth, constraints.MaxHeight);
        for (int i = 0; i < _plots.Length; i++) {
            _plots[i].Measure(new Size(_chartBounds[i].Width, _chartBounds[i].Height));
        }

        return SizeHints.Flex(new Size(0, 0), new Size(80, 24), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        PrepareCharts(finalRect.Width, finalRect.Height);
        for (int i = 0; i < _plots.Length; i++) {
            var bounds = _chartBounds[i];
            _plots[i].Measure(new Size(bounds.Width, bounds.Height));
            _plots[i].Arrange(new(finalRect.X + bounds.X, finalRect.Y + bounds.Y, bounds.Width, bounds.Height));
        }
        ArrangeUserButtons(finalRect);
    }

    private void Write(
        CellBuffer buffer,
        int x,
        int y,
        ReadOnlySpan<char> text,
        Style style,
        int maxWidth = int.MaxValue
    ) {
        if (x < 0 || x >= Bounds.Width || y < 0 || y >= Bounds.Height || maxWidth <= 0) {
            return;
        }

        buffer.PushClip(new Rectangle(Bounds.X + x, Bounds.Y + y, Math.Min(Bounds.Width - x, maxWidth), 1));
        Span<char> safe = stackalloc char[Math.Min(text.Length, 512)];
        for (int i = 0; i < safe.Length; i++) {
            safe[i] = char.IsControl(text[i]) ? ' ' : text[i];
        }

        buffer.WriteText(Bounds.X + x, Bounds.Y + y, safe, style);
        buffer.PopClip();
    }

    private void Number(CellBuffer buffer, int x, int y, double value, ReadOnlySpan<char> format, Style style) {
        Span<char> chars = stackalloc char[32];
        if (value.TryFormat(chars, out int length, format, CultureInfo.InvariantCulture)) {
            Write(buffer, x, y, chars[..length], style);
        }
    }

    private void Meter(CellBuffer buffer, int x, int y, double percent, int width) {
        Span<char> chars = stackalloc char[64];
        width = Math.Clamp(width, 1, chars.Length);
        int filled = (int)Math.Round(width * Math.Clamp(percent, 0, 100) / 100);
        chars[..filled].Fill('━');
        chars[filled..width].Fill('─');
        Write(buffer, x, y, chars[..width], percent > 85 ? Hot : Accent);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.Handled) {
            return;
        }
        if (_monitorDialog?.App is not null) {
            if (e.Key == TerminalKey.Escape || e.Char == TerminalChar.CtrlQ || (!_monitorDialogEditable && e.Char is 'q' or 'Q')) {
                _monitorDialog.Close();
            }
            e.Handled = true;
            return;
        }
        if (e.RoutingPhase != RoutingPhase.Preview && e.Key == TerminalKey.F1) {
            ShowHelp();
            e.Handled = true;
            return;
        }

        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (ColumnsOpen) {
            PickerKey(e);
            return;
        }

        if ((e.Modifiers & TerminalModifiers.Ctrl) != 0 && e.Char is 'u' or 'U' or TerminalChar.CtrlU) {
            ClearFilter(false);
            e.Handled = true;
            return;
        }

        if (_editingFilter) {
            if (e.Key == TerminalKey.Escape) {
                ClearFilter();
                _editingFilter = false;
            }
            else if (e.Key == TerminalKey.Enter) {
                _editingFilter = false;
            }
            else if (e.Key == TerminalKey.Backspace && Model.Filter.Length > 0) {
                int last = StringInfo.ParseCombiningCharacters(Model.Filter)[^1];
                Model.Filter = Model.Filter[..last];
                Dirty = true;
            }

            e.Handled = true;
            Refresh();
            return;
        }

        if (View == MonitorView.Users && UserKey(e)) {
            return;
        }

        if (View == MonitorView.Services && ServiceKey(e)) {
            return;
        }

        if (View is MonitorView.Network or MonitorView.Gpu) {
            int? movement = e.Key switch {
                TerminalKey.Up => -1,
                TerminalKey.Down => 1,
                TerminalKey.PageUp => -PageSize,
                TerminalKey.PageDown => PageSize,
                TerminalKey.Home => -int.MaxValue,
                TerminalKey.End => int.MaxValue,
                _ => null
            };
            if (movement.HasValue) {
                MoveDetail(movement.Value);
                e.Handled = true;
                Refresh();
                return;
            }
        }

        switch (e.Key) {
            case TerminalKey.Escape:
                ClearFilter();
                break;
            case TerminalKey.Left when Model.Tree && ProcessTable && (e.Modifiers & TerminalModifiers.Shift) == 0:
                Dirty |= Model.CollapseSelected(true);
                break;
            case TerminalKey.Right when Model.Tree && ProcessTable && (e.Modifiers & TerminalModifiers.Shift) == 0:
                Dirty |= Model.CollapseSelected(false);
                break;
            case TerminalKey.Left when ProcessTable:
                ScrollProcessColumns(-1);
                break;
            case TerminalKey.Right when ProcessTable:
                ScrollProcessColumns(1);
                break;
            case TerminalKey.Enter when Model.Tree && ProcessTable:
                Dirty |= Model.ToggleSelected();
                break;
            case TerminalKey.Up:
                Model.Move(-1, PageSize);
                break;
            case TerminalKey.Down:
                Model.Move(1, PageSize);
                break;
            case TerminalKey.PageUp:
                Model.Move(-PageSize, PageSize);
                break;
            case TerminalKey.PageDown:
                Model.Move(PageSize, PageSize);
                break;
            case TerminalKey.Home:
                Model.Move(-Model.Count, PageSize);
                break;
            case TerminalKey.End:
                Model.Move(Model.Count, PageSize);
                break;
            case TerminalKey.F6:
                Model.Sort = (ProcessSort)(((int)Model.Sort + 1) % Enum.GetValues<ProcessSort>().Length);
                Dirty = true;
                break;
            default:
                return; // Printable shortcuts are handled only by TextInput, avoiding duplicate dispatch.
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.Handled || e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }
        if (_monitorDialog?.App is not null) {
            if (!_monitorDialogEditable && e.Text is "q" or "Q") {
                _monitorDialog.Close();
            }
            e.Handled = true;
            return;
        }
        if (ColumnsOpen) {
            if (e.Text == " ") {
                TogglePickerColumn();
            }
            else if (e.Text is "c" or "C" or "q" or "Q") {
                ColumnsOpen = false;
            }
            else if (e.Text is "d" or "D") {
                ResetActiveColumns();
                Dirty = true;
            }

            e.Handled = true;
            Refresh();
            return;
        }

        if (_editingFilter) {
            AppendFilter(e.Text);
        }
        else {
            if (View == MonitorView.Users && e.Text is "+" or "-" or "−") {
                ProjectUsers();
                Users.Expand(e.Text == "+");
                Refresh();
                e.Handled = true;
                return;
            }

            switch (e.Text) {
                case "?":
                    ShowHelp();
                    break;
                case "d":
                case "D":
                    if (ProcessTable) {
                        ShowSelectedProcessDetails();
                        break;
                    }
                    if (View != MonitorView.Users) {
                        return;
                    }

                    ShowUserDetails();
                    break;
                case " " when View == MonitorView.Users:
                    ToggleSelectedSessions();
                    break;
                case "m" or "M" when View == MonitorView.Users:
                    MarkAllSessions();
                    break;
                case "x" or "X" when View == MonitorView.Users:
                    ShowSessionSelection();
                    break;
                case "l":
                case "L":
                    if (View != MonitorView.Users) {
                        return;
                    }

                    IncludeUserProcesses = !IncludeUserProcesses;
                    ProjectUsers();
                    break;
                case "/":
                    _editingFilter = true;
                    break;
                case "q":
                case "Q":
                    ExitRequested = true;
                    break;
                case "p":
                case "P":
                    Paused = !Paused;
                    break;
                case "g":
                case "G":
                    GraphType = TimeGraphTypes[(Array.IndexOf(TimeGraphTypes, GraphType) + 1) % TimeGraphTypes.Length];
                    break;
                case "r":
                case "R":
                    if (View == MonitorView.Services) {
                        Services.Ascending = !Services.Ascending;
                    }
                    else {
                        Model.Ascending = !Model.Ascending;
                    }

                    Dirty = true;
                    break;
                case "t":
                case "T":
                    if (View == MonitorView.Users) {
                        Users.Tree = !Users.Tree;
                        ProjectUsers();
                    }
                    else if (ProcessTable) {
                        ToggleTree();
                    }

                    break;
                case "c":
                case "C":
                    OpenColumns();
                    break;
                case "a":
                case "A":
                    OpenSelectedActions();
                    break;
                case "e":
                case "E":
                    if (ProcessTable) {
                        ExcludeSelectedProcess();
                    }

                    break;
                case "u":
                case "U":
                    if (ProcessTable) {
                        Model.ExcludedNames.Clear();
                        Dirty = true;
                    }

                    break;
                case "1":
                    View = MonitorView.Processes;
                    break;
                case "2":
                    View = MonitorView.Services;
                    break;
                case "3":
                    View = MonitorView.Network;
                    break;
                case "4":
                    View = MonitorView.Gpu;
                    break;
                case "5":
                    ShowUserSessions();
                    break;
                case "6":
                    View = MonitorView.Dashboard;
                    break;
                default:
                    return;
            }
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPaste(PasteEventArgs e) {
        if (!_editingFilter) {
            return;
        }

        AppendFilter(e.Text);
        e.Handled = true;
    }

    private void AppendFilter(string text) {
        foreach (char c in text) {
            if (!char.IsControl(c) && Model.Filter.Length < 256) {
                Model.Filter += c;
            }
        }

        Dirty = true;
        _detailOffset = 0;
        Refresh();
    }

    private void ClearFilter(bool clearColumns = true) {
        Model.Filter = "";
        if (clearColumns) {
            if (View == MonitorView.Network) {
                _networkFilters.Clear();
            }
            else if (View == MonitorView.Gpu) {
                _gpuFilters.Clear();
            }
            else if (View == MonitorView.Services) {
                Services.ColumnFilters.Clear();
            }
            else {
                Model.ColumnFilters.Clear();
            }
        }

        Dirty = true;
        _detailOffset = 0;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPointerWheel(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (ColumnsOpen) {
            _columnPickerIndex = Math.Clamp(_columnPickerIndex + (e.WheelDelta > 0 ? -1 : 1), 0, PickerCount - 1);
            e.Handled = true;
            Refresh();
            return;
        }

        if (View == MonitorView.Dashboard) {
            return;
        }

        if (View == MonitorView.Users) {
            ProjectUsers();
            Users.Move(e.WheelDelta > 0 ? -3 : 3, UserPage);
            e.Handled = true;
            Refresh();
            return;
        }

        if (View == MonitorView.Services) {
            ScrollServices(e.WheelDelta > 0 ? -3 : 3);
            e.Handled = true;
            Refresh();
            return;
        }

        if (View is MonitorView.Network or MonitorView.Gpu) {
            MoveDetail(e.WheelDelta > 0 ? -3 : 3);
        }
        else {
            Model.Move(e.WheelDelta > 0 ? -3 : 3, PageSize);
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (!Minimal && e.Button == TerminalMouseButton.Left && e.LocalY == Bounds.Height - 1) {
            if (e.LocalX >= 1 && e.LocalX < FooterFilterX) {
                ShowHelp();
            }
            else {
                _editingFilter = true;
                Refresh();
            }

            e.Handled = true;
            return;
        }

        if (View == MonitorView.Users && UserPointer(e)) {
            return;
        }

        if (View == MonitorView.Services && ServicePointer(e)) {
            return;
        }

        if (e.Button == TerminalMouseButton.Right) {
            OpenProcessMenu(e);
            return;
        }

        if (e.Button != TerminalMouseButton.Left) {
            return;
        }

        if (ColumnsOpen) {
            PickerPointer(e);
            return;
        }

        int y = e.UiY - Bounds.Y;
        int x = e.UiX - Bounds.X;
        for (int i = 0; i < _plotHitCount; i++) {
            var hit = _plotHits[i];
            if (x >= hit.X && x < hit.X + hit.Width && y >= hit.Y && y < hit.Y + hit.Height) {
                _inspectX = x;
                _inspectY = y;
                e.Handled = true;
                Refresh();
                return;
            }
        }

        _inspectX = _inspectY = -1;
        if (!Minimal && y == 0) {
            int tab = (x - 1) / TabWidth;
            if (x >= 1 && x < 1 + TabWidth * NavigationViews.Length && tab >= 0 && tab < NavigationViews.Length) {
                View = NavigationViews[tab];
            }
        }
        else if (y == TableTop - 1 && View is MonitorView.Network or MonitorView.Gpu) {
            OpenDetailHeaderMenu(e);
            return;
        }
        else if (y == TableTop - 1 && ProcessTable) {
            if (x == 0 || x == Bounds.Width - 1) {
                ScrollProcessColumns(x == 0 ? -1 : 1);
                e.Handled = true;
                return;
            }
            foreach (ref readonly var header in Columns) {
                if (x >= header.X + header.Width - 2 && x < header.X + header.Width) {
                    OpenHeaderMenu(header.Kind, e);
                    return;
                }
            }

            var sort = ProcessSort.Name;
            foreach (ref readonly var column in Columns) {
                if (x >= column.X && x < column.X + column.Width) {
                    sort = column.Sort;
                    break;
                }
            }

            if (Model.Sort == sort) {
                Model.Ascending = !Model.Ascending;
            }
            else {
                Model.Sort = sort;
                Model.Ascending = false;
            }

            Dirty = true;
        }
        else if (y >= TableTop && y < Bounds.Height - Footer && ProcessTable) {
            int visible = Math.Max(0, Bounds.Height - TableTop - Footer);
            int offset = Math.Clamp(Model.Offset, 0, Math.Max(0, Model.Count - visible));
            int index = offset + y - TableTop;
            if (index < Model.Count) {
                Model.SelectedId = Model.Rows[index].Id;
                if (Model.Tree) {
                    foreach (ref readonly var column in Columns) {
                        if (column.Kind == ProcessColumn.Name && x >= column.X
                            && (e.ClickCount >= 2 || x < column.X + Math.Min(28, Model.DepthAt(index)) * 2 + 4)) {
                            Dirty |= Model.ToggleSelected();
                            break;
                        }
                    }
                }
            }
        }

        e.Handled = true;
        Refresh();
    }
}
