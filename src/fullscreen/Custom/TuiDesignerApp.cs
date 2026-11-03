using System.Diagnostics;
using IOPath = System.IO.Path;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Custom;
/// <summary>Tree/property editor with a retained, non-executing live preview.</summary>
public sealed partial class TuiDesignerApp : IFullscreenApp {
    private sealed class DesignerCanvas : Padder {
        public DesignerCanvas() {
            Focusable = true;
            IsTabStop = true;
        }
    }

    private sealed record Row(TuiNode Node, TuiNode? Parent, int Depth);
    private readonly ListBox<string> _tree = new() {
        MinHeight = 2,
        VerticalAlignment = Align.Stretch
    };
    private readonly TextBlock _status = new("Select a container in the tree, then click a palette control to add it. Drag in the preview to move.");
    private readonly TextBlock _heading = new();
    private readonly Padder _previewHost = new DesignerCanvas {
        Padding = new(0),
        HorizontalAlignment = Align.Stretch,
        VerticalAlignment = Align.Stretch
    };
    private string _lastStatus = "";
    private long _statusChanged;
    private readonly Action<TuiDocument, string> _save;
    private readonly Action<TuiDocument, string, bool>? _exportScript;
    private readonly Func<string, bool> _exists;
    private readonly Stack<string> _undo = new(), _redo = new();
    private readonly Dictionary<Visual, bool> _tabStops = [];
    private List<Row> _rows = [];
    private int _selection;
    private string _fields = "";
    private string _pending = "";
    private long _changed;
    private CustomTuiApp _preview = null!;
    private bool _exit;
    private bool _dirty;
    private bool _savedToDisk;
    private string _saved;
    private int _paneHeight = -1;
    private readonly Grid _panes;
    private readonly Border _sidebar;
    private readonly Padder _inspectorHost;
    private readonly string? _preferencesPath;
    private readonly TuiDesignerPreferences _preferences;
    private bool _minimal;
    private Themes.ThemePalette _theme = Themes.ThemePalette.Default;
    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.ApplyTheme"/>
    public void ApplyTheme(Themes.ThemePalette theme) {
        _theme = theme;
        theme.Apply(Root);
        ApplyPaletteColors();
        ApplyInspectorStyles();
        _preview.ApplyTheme(theme);
    }

    /// <summary>Gets the layout currently being edited.</summary>
    public TuiDocument Document { get; private set; }
    /// <summary>Gets the destination layout or scaffold path.</summary>
    public string? Path { get; private set; }
    /// <summary>Gets whether the document has changes not yet saved.</summary>
    public bool Dirty => _dirty || HasPendingFields;
    private bool HasPendingFields => _rows.Count > 0 && _fields != Fields();
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root { get; }
    /// <summary>Gets whether tree display requires the process-name column.</summary>
    public ListBox<string> Tree => _tree;
    /// <summary>Gets the preview content, or null to clear the preview.</summary>
    public Visual Preview => _preview.Root;

    /// <summary>Initializes a new TuiDesignerApp instance.</summary>
    /// <param name="document">The layout currently being edited.</param>
    /// <param name="save">The callback used to persist the edited document.</param>
    /// <param name="path">The destination layout or scaffold path. Defaults to null.</param>
    /// <param name="exists">The callback used to check resource existence. Defaults to null.</param>
    /// <param name="exportScript">Whether the designer writes a PowerShell scaffold instead of only a
    /// layout. Defaults to null.</param>
    /// <param name="preferencesPath">The file used to persist file-manager preferences. Defaults to
    /// null.</param>
    public TuiDesignerApp(
        TuiDocument document,
        Action<TuiDocument, string> save,
        string? path = null,
        Func<string, bool>? exists = null,
        Action<TuiDocument, string, bool>? exportScript = null,
        string? preferencesPath = null
    ) {
        _preferencesPath = preferencesPath;
        _preferences = TuiDesignerPreferences.Load(preferencesPath);
        _inspectorOpen = _preferences.Properties == DesignerPropertiesMode.Always;
        _exportScript = exportScript;
        Document = TuiDocument.FromJson(document.ToJson());
        _saved = Document.ToJson();
        _save = save;
        _exists = exists ?? File.Exists;
        Path = path;
        _savedToDisk = path is not null && _exists(path);
        var toolbar = new HStack(
            IconButton("☰", "Controls · F2", () => TogglePane(false)),
            IconButton("≡", "Properties · F3", () => TogglePane(true)),
            IconButton("⋮", "Item actions · F7", () => ShowActions()),
            IconButton("⚙", "View preferences", ShowViewMenu),
            IconButton("↶", "Undo", Undo),
            IconButton("↷", "Redo", Redo),
            IconButton("▣", "Save layout", Save),
            IconButton("↗", "Export app", ExportScript)
        );
        var header = new Grid {
            MaxHeight = 1,
            HorizontalAlignment = Align.Stretch
        };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Star(1) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Fixed(1) });
        header.Cells.Add(new(_heading) { Column = 0 });
        header.Cells.Add(new(toolbar) { Column = 1 });
        var properties = CreateInspector();
        _inspectorHost = new Padder(properties) {
            Padding = new(0),
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        var panes = _panes = new Grid {
            ColumnGap = 1,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        panes.ColumnDefinitions.Add(new() { Width = GridLength.Star(1) });
        panes.ColumnDefinitions.Add(new() { Width = GridLength.Fixed(20) });
        panes.RowDefinitions.Add(new() { Height = GridLength.Star(1) });
        panes.Cells.Add(new(_previewHost) { Row = 0, Column = 0 });
        _sidebar = new Border(new VStack(_paletteHeading, _paletteSearch, CreatePalette()) { VerticalAlignment = Align.Stretch }) {
            VerticalAlignment = Align.Stretch
        };
        panes.Cells.Add(new(_sidebar) { Row = 0, Column = 1 });
        var layout = new Grid {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Star(1) });
        Visual[] regions = [header, panes, _inspectorHost];
        for (int row = 0; row < regions.Length; row++) {
            layout.RowDefinitions.Add(new() { Height = row == 1 ? GridLength.Star(1) : GridLength.Fixed(row == 2 ? _preferences.Height : 1) });
            layout.Cells.Add(new(regions[row]) { Row = row, Column = 0 });
        }

        _status.IsVisible = false;
        layout.Cells.Add(new(_status) { Row = 0, Column = 0, IsVisible = false });
        Root = layout;
        Root.AddCommand(new Command {
            Id = "Fullscreen.Designer.Close",
            LabelMarkup = "Close designer",
            Gesture = new KeyGesture(TerminalKey.F10),
            Execute = _ => RequestClose()
        });
        InitializeMarkers();
        Root.KeyDown((_, e) => {
            if (e.Handled || e.RoutingPhase == RoutingPhase.Preview) {
                return;
            }

            if (e.Key == TerminalKey.Escape) {
                if (_inspectorOpen && BelongsTo(Root.App?.FocusedElement, _inspectorHost)) {
                    HideInspector();
                }
                else {
                    RequestClose();
                }

                e.Handled = true;
            }
            else if (e.Key == TerminalKey.F2) {
                TogglePane(false);
                e.Handled = true;
            }
            else if (e.Key == TerminalKey.F3) {
                TogglePane(true);
                e.Handled = true;
            }
            else if (e.Key == TerminalKey.F5) {
                RefreshPreview();
                e.Handled = true;
            }
            else if (e.Key == TerminalKey.F7) {
                ShowActions();
                e.Handled = true;
            }
        });
        InstallDragAndDrop();
        Rebuild(0);
    }

    private static TooltipHost IconButton(string icon, string description, Action action) {
        var button = new Button(icon) {
            MinWidth = 5,
            MaxWidth = 5,
            MinHeight = 1,
            MaxHeight = 1
        }.Click(action);
        // Leave room for symbols that occupy two terminal cells.
        button.SetStyle(ButtonStyle.Default with { Padding = new(1, 0, 1, 0) });
        return new(button) {
            TooltipContent = new TextBlock(description),
            MaxHeight = 1
        };
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.SetMinimal"/>
    public void SetMinimal(bool minimal) {
        _minimal = minimal;
        UpdatePanes();
        _preview.SetMinimal(true);
    }

    private static List<Row> Flatten(TuiNode root) {
        var rows = new List<Row>();
        Visit(root, null, 0);
        return rows;
        void Visit(TuiNode node, TuiNode? parent, int depth) {
            rows.Add(new(node, parent, depth));
            foreach (var child in node.Children) {
                Visit(child, node, depth + 1);
            }
        }
    }

    private void Rebuild(int selection) {
        _rows = Flatten(Document.Root);
        _tree.Items.Clear();
        foreach (var row in _rows) {
            _tree.Items.Add(new string(' ', row.Depth * 2) + row.Node.Type + " · " + row.Node.Id);
        }

        _selection = Math.Clamp(selection, 0, _rows.Count - 1);
        _tree.SelectedIndex = _selection;
        SelectionChanged();
        LoadFields();
        RefreshPreview();
        _dirty = Document.ToJson() != _saved;
        UpdateHeading();
        _status.Text = _dirty ? "Unsaved · layout updated" : !_savedToDisk ? "New layout · save or export to keep it" : "Saved · layout matches the saved version";
    }

    private void UpdateHeading() {
        string status = _status.Text ?? "";
        if (status != _lastStatus) {
            _lastStatus = status;
            _statusChanged = Stopwatch.GetTimestamp();
        }

        bool routine = status is "New layout · save or export to keep it" or "Unsaved · layout updated"
            or "Unsaved · preview updated"
            or "Saved · layout matches the saved version";
        _heading.Text = !routine && status.Length > 0 && Stopwatch.GetElapsedTime(_statusChanged).TotalSeconds < 4 ? status : "TUI · " + (Path is null ? "Untitled" : IOPath.GetFileName(Path)) + (Dirty ? " · Unsaved" : !_savedToDisk ? " · Not saved" : " · Saved");
    }

    private void Remember() {
        if (_undo.Count >= 50) {
            var keep = _undo.Take(49).Reverse().ToArray();
            _undo.Clear();
            foreach (string item in keep) {
                _undo.Push(item);
            }
        }

        _undo.Push(Document.ToJson());
        _redo.Clear();
    }

    private void RefreshPreview() {
        var next = new CustomTuiApp(Document, preview: true);
        next.SetMinimal(true);
        next.ApplyTheme(_theme);
        var old = _preview;
        _preview = next;
        _previewContent.Content = next.Root;
        old?.Dispose();
        _markers?.Refresh();
        UpdateTabStops();
    }

    private void UpdateTabStops() {
        // XenoAtom's focus walk visits descendants of hidden containers. Exclude
        // hidden panes and property rows without losing their original tab policy.
        var current = Root.EnumerateVisualsDepthFirst().ToHashSet();
        foreach (var stale in _tabStops.Keys.Where(visual => !current.Contains(visual)).ToArray()) {
            _tabStops.Remove(stale);
        }

        static bool Visible(Visual visual) {
            for (Visual? parent = visual; parent is not null; parent = parent.Parent) {
                if (!parent.IsVisible) {
                    return false;
                }
            }

            return true;
        }

        foreach (var visual in current) {
            if (!_tabStops.TryGetValue(visual, out bool tabStop)) {
                _tabStops.Add(visual, tabStop = visual.IsTabStop);
            }

            visual.IsTabStop = tabStop && Visible(visual);
        }

        if (Root.App?.FocusedElement is { } focus && BelongsTo(focus, Root) && !Visible(focus)) {
            Root.App.Focus(Root.EnumerateVisualsDepthFirst().FirstOrDefault(visual => visual.Focusable && visual.IsTabStop && Visible(visual)));
        }
    }

    /// <summary>Adds a control of the requested type to the current document.</summary>
    /// <param name="type">The control catalog name to insert.</param>
    public void Add(string type) {
        if (!ApplyProperties()) {
            return;
        }

        try {
            var row = _rows[_selection];
            var parent = row.Node.IsContainer ? row.Node : row.Parent ?? Document.Root;
            if (_rows.Count >= 512 || _rows.First(x => x.Node == parent).Depth >= 24) {
                throw new ArgumentException("The control count or depth limit has been reached.");
            }

            if (parent.Type is "Border" or "Button" && parent.Children.Count != 0) {
                throw new ArgumentException(parent.Type + " already has content. Select a stack or tab container.");
            }

            if (!parent.IsContainer) {
                throw new ArgumentException("Select a container first.");
            }

            var node = NewControl(type, _rows);
            Remember();
            parent.Children.Add(node);
            _dirty = true;
            Rebuild(Flatten(Document.Root).FindIndex(x => x.Node == node));
        }
        catch (ArgumentException error) {
            _status.Text = error.Message;
        }
    }

    private static TuiNode NewControl(string type, List<Row> rows) {
        var info = TuiControlCatalog.Describe(type);
        string id;
        int number = 1;
        do {
            id = type.ToLowerInvariant() + number++;
        }
        while (rows.Any(row => row.Node.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        return new() {
            Id = id,
            Type = type,
            Text = info.Category == "Layout" ? "" : type,
            Bind = type != "Text" && info.Properties.Contains("Bind") ? id : "",
            Items = type == "Select" ? ["First", "Second"] : [],
            Height = type == "Chart" ? 10 : null,
            Target = type == "SearchReplacePopup" ? rows.FirstOrDefault(row => row.Node.Type == "LogControl")?.Node.Id ?? throw new ArgumentException("Add a LogControl before adding its search popup.") : ""
        };
    }

    /// <summary>Removes the selected control when the document permits it.</summary>
    public void DeleteSelected() {
        if (!ApplyProperties()) {
            return;
        }

        if (_rows[_selection].Parent is not { } parent) {
            _status.Text = "The root cannot be deleted.";
            return;
        }

        Remember();
        parent.Children.Remove(_rows[_selection].Node);
        _dirty = true;
        Rebuild(Math.Max(0, _selection - 1));
    }

    /// <summary>Moves the selected control relative to its siblings.</summary>
    /// <param name="direction">The signed direction in which to move the control.</param>
    public void Move(int direction) {
        if (!ApplyProperties()) {
            return;
        }

        if (_rows[_selection].Parent is not { } parent) {
            _status.Text = "The root cannot be moved.";
            return;
        }

        var node = _rows[_selection].Node;
        int index = parent.Children.IndexOf(node), target = index + direction;
        if (target < 0 || target >= parent.Children.Count) {
            _status.Text = direction < 0 ? "Already the first control in this container." : "Already the last control in this container.";
            return;
        }

        Remember();
        parent.Children.RemoveAt(index);
        parent.Children.Insert(target, node);
        _dirty = true;
        Rebuild(Flatten(Document.Root).FindIndex(x => x.Node == node));
    }

    private void Indent() {
        if (!ApplyProperties()) {
            return;
        }

        if (_rows[_selection].Parent is not { } parent) {
            _status.Text = "The root cannot be indented.";
            return;
        }

        var node = _rows[_selection].Node;
        int index = parent.Children.IndexOf(node);
        if (index == 0 || !parent.Children[index - 1].IsContainer) {
            _status.Text = "A previous sibling container is required.";
            return;
        }

        var destination = parent.Children[index - 1];
        if (destination.Type == "Border" && destination.Children.Count != 0) {
            _status.Text = "Border already has content. Choose a stack or tab container.";
            return;
        }

        if (_rows.First(x => x.Node == destination).Depth + 1 + Flatten(node).Max(x => x.Depth) > 24) {
            _status.Text = "Maximum layout depth reached.";
            return;
        }

        Remember();
        parent.Children.Remove(node);
        destination.Children.Add(node);
        _dirty = true;
        Rebuild(Flatten(Document.Root).FindIndex(x => x.Node == node));
    }

    private void Outdent() {
        if (!ApplyProperties()) {
            return;
        }

        if (_rows[_selection].Parent is not { } parent) {
            _status.Text = "The root cannot be outdented.";
            return;
        }

        var grandparent = _rows.First(x => x.Node == parent).Parent;
        if (grandparent is null || grandparent.Type == "Border") {
            _status.Text = "This control cannot be moved outside its parent container.";
            return;
        }

        var node = _rows[_selection].Node;
        Remember();
        parent.Children.Remove(node);
        grandparent.Children.Insert(grandparent.Children.IndexOf(parent) + 1, node);
        _dirty = true;
        Rebuild(Flatten(Document.Root).FindIndex(x => x.Node == node));
    }

    /// <summary>Restores the previous document state from undo history.</summary>
    public void Undo() {
        if (!ApplyProperties()) {
            return;
        }

        if (_undo.TryPop(out string? json)) {
            _redo.Push(Document.ToJson());
            Document = TuiDocument.FromJson(json);
            Rebuild(_selection);
        }
        else {
            _status.Text = "Nothing to undo.";
        }
    }

    /// <summary>Reapplies the next document state from redo history.</summary>
    public void Redo() {
        if (HasPendingFields) {
            if (ApplyProperties()) {
                _status.Text = "New edits applied · redo history cleared.";
            }

            return;
        }

        if (_redo.TryPop(out string? json)) {
            _undo.Push(Document.ToJson());
            Document = TuiDocument.FromJson(json);
            Rebuild(_selection);
        }
        else {
            _status.Text = "Nothing to redo.";
        }
    }

    private void Save() {
        if (!ApplyProperties()) {
            return;
        }

        if (Path is not null) {
            if (_exists(Path)) {
                Confirm("Replace layout '" + Path + "'? Existing companion scripts are preserved.", () => SaveTo(Path));
            }
            else {
                SaveTo(Path);
            }
            return;
        }

        var input = new TextBox("layout.tui.psd1");
        var error = new Paragraph("") {
            IsVisible = false,
            Wrap = true
        };
        Dialog? dialog = null;
        void Write() {
            if (SaveTo(input.Text ?? "")) {
                dialog!.Close();
            }
            else {
                error.Text = _status.Text;
                error.IsVisible = true;
                Root.App?.Focus(input);
            }
        }

        dialog = new Dialog {
            Title = new TextBlock("Save layout"),
            IsModal = true,
            Width = 70,
            Content = new VStack(new Paragraph("Save .psd1 or .json layout data and create a companion .ps1 scaffold if missing. Existing scripts are preserved.") { Wrap = true }, input, error, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Save").Click(() => {
                if (_exists(input.Text ?? "")) {
                    Confirm("Replace layout '" + input.Text + "'?", Write);
                }
                else {
                    Write();
                }
            })))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
        Root.App?.Focus(input);
    }

    /// <summary>Validates and saves the edited document to a filesystem path.</summary>
    /// <param name="path">The destination layout or scaffold path.</param>
    /// <returns>True when saving succeeds.</returns>
    public bool SaveTo(string path) {
        if (!ApplyProperties()) {
            return false;
        }

        try {
            _save(Document, path);
            Path = path;
            _saved = Document.ToJson();
            _dirty = false;
            _savedToDisk = true;
            UpdateHeading();
            _status.Text = "Saved · " + path + " · app code: " + IOPath.ChangeExtension(path, ".ps1") + " · existing scripts preserved";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
            _status.Text = "Save failed · " + error.Message;
        }

        return false;
    }

    void IFullscreenApp.RequestClose() =>
        RequestClose();
    private void RequestClose() {
        if (HasPendingFields) {
            ApplyProperties();
        }

        if (Dirty) {
            Confirm("Discard unsaved changes and close?", () => _exit = true);
        }
        else {
            _exit = true;
        }
    }

    private static void Confirm(string text, Action action) {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Confirm"),
            IsModal = true,
            Width = 65,
            Content = new VStack(new Paragraph(text) { Wrap = true }, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Confirm").Click(() => {
                dialog!.Close();
                action();
            })))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        if (_paneWidth != Root.Bounds.Width || _paneHeight != Root.Bounds.Height
            || _inspectorExtent != _propertyViewport.Content!.DesiredSize.Height
            || _inspectorChrome != 1 + Math.Max(1, _propertyGroups.DesiredSize.Height)) {
            UpdatePanes();
        }

        UpdatePalette();
        if (_tree.SelectedIndex >= 0 && _tree.SelectedIndex != _selection) {
            if (ApplyProperties()) {
                _selection = _tree.SelectedIndex;
                SelectionChanged();
                LoadFields();
                _markers?.Refresh();
            }
            else {
                _tree.SelectedIndex = _selection;
            }
        }

        string requestedType = EditorText(_editors["Type"]);
        if (requestedType != _inspectedType && TuiDocument.ControlTypes.Contains(requestedType)) {
            UpdateInspector(requestedType);
        }

        string fields = Fields();
        if (fields != _pending) {
            _pending = fields;
            _changed = Stopwatch.GetTimestamp();
        }
        else if (fields != _fields && Stopwatch.GetElapsedTime(_changed).TotalMilliseconds >= 150) {
            ApplyProperties(revealError: false);
        }

        UpdateHeading();
        _preview.Tick();
        return !_exit;
    }

    /// <inheritdoc/>
    public void Dispose() =>
        _preview.Dispose();
}
