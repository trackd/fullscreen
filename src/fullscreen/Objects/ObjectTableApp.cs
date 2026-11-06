using System.Collections;
using System.Management.Automation;
using fullscreen.Hosting;
using fullscreen.Tables;
using XenoAtom.Terminal.UI;

namespace fullscreen.Objects;
/// <summary>Builds table snapshots and object-inspection views from PowerShell objects.</summary>
public static class ObjectSnapshot {
    /// <summary>Stores the maximum number of columns inferred for an object snapshot.</summary>
    public const int MaximumColumns = 256;
    /// <summary>Creates a table snapshot of selected properties without changing the source
    /// objects.</summary>
    /// <param name="objects">The original PowerShell objects retained by each table row.</param>
    /// <param name="properties">The selected property names, or null to infer table columns. Defaults to
    /// null.</param>
    /// <returns>The table model retaining references to the original objects.</returns>
    public static TableModel Create(
        IReadOnlyList<PSObject> objects,
        string[]? properties = null
    ) =>
        CreateCancellable(objects, properties, default);
    internal static TableModel CreateCancellable(
        IReadOnlyList<PSObject> objects,
        string[]? properties,
        CancellationToken token
    ) {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (properties is { Length: > 0 }) {
            foreach (string name in properties) {
                if (seen.Add(name)) {
                    names.Add(name);
                }
            }

            if (names.Count > MaximumColumns) {
                throw new ArgumentException("Choose at most 256 properties.", nameof(properties));
            }
        }
        else {
            foreach (var item in objects) {
                token.ThrowIfCancellationRequested();
                if (names.Count >= MaximumColumns) {
                    break;
                }

                if (Scalar(item.BaseObject)) {
                    if (seen.Add("Value")) {
                        names.Add("Value");
                    }

                    continue;
                }

                foreach (var property in item.Properties) {
                    if (property.IsGettable && seen.Add(property.Name) && names.Count < MaximumColumns) {
                        names.Add(property.Name);
                    }
                }
            }
        }

        if (names.Count == 0) {
            names.Add("Value");
        }

        var model = new TableModel([.. names]) {
            ColorByType = true
        };
        for (int i = 0; i < objects.Count; i++) {
            token.ThrowIfCancellationRequested();
            var item = objects[i];
            var cells = new string[names.Count];
            var values = new object?[names.Count];
            for (int column = 0; column < names.Count; column++) {
                token.ThrowIfCancellationRequested();
                try {
                    values[column] = names[column].Equals("Value", StringComparison.OrdinalIgnoreCase) && Scalar(item.BaseObject) ? item.BaseObject : item.Properties[names[column]]?.Value;
                    cells[column] = model.DisplayCell(i, column, values[column]);
                }
                catch (Exception) {
                    cells[column] = "<property error>";
                }
            }

            model.Rows.Add(new(i, item, cells, values));
        }

        model.Rebuild();
        return model;
    }

    private static bool Scalar(object value) =>
        value is string or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Enum
        || value.GetType().IsPrimitive;

    internal static bool IsScalar(object value) =>
        Scalar(value is PSObject wrapped ? wrapped.BaseObject : value);

    /// <summary>Creates a property-inspection table for an object.</summary>
    /// <param name="value">The original object whose readable properties are inspected.</param>
    /// <returns>The object&apos;s inspection table.</returns>
    public static TableModel Inspect(object value) {
        var model = new TableModel(["Property", "Value", "Type"]) {
            ColorByType = true,
            PropertyRows = true
        };
        void Add(string name, object? propertyValue, string? error = null) {
            object? raw = propertyValue is PSObject ps ? ps.BaseObject : propertyValue;
            string display = error ?? model.DisplayCell(model.Rows.Count, 1, raw);
            object? inspectable = propertyValue is PSObject { BaseObject: PSCustomObject } ? propertyValue : raw;
            model.Rows.Add(new(
                model.Rows.Count,
                propertyValue ?? "",
                [
                    TableModel.Text(name),
                    display,
                    raw?.GetType().FullName ?? "null"
                ],
                [
                    name,
                    inspectable,
                    raw?.GetType().FullName
                ]
            ));
        }

        object target = value is PSObject wrapped ? wrapped.BaseObject : value;
        if (Scalar(target)) {
            Add("Value", target);
        }
        else if (target is IDictionary dictionary) {
            foreach (DictionaryEntry entry in dictionary) {
                Add(TableModel.Text(entry.Key), entry.Value);
                if (model.Rows.Count >= 1000) {
                    break;
                }
            }
        }
        else if (target is IEnumerable enumerable and not string) {
            foreach (object? item in enumerable) {
                Add($"[{model.Rows.Count}]", item);
                if (model.Rows.Count >= 1000) {
                    break;
                }
            }
        }
        else {
            var ps = PSObject.AsPSObject(value);
            foreach (var property in ps.Properties) {
                if (!property.IsGettable) {
                    continue;
                }

                try {
                    Add(property.Name, property.Value);
                }
                catch (Exception) {
                    Add(property.Name, null, "<property error>");
                }

                if (model.Rows.Count >= 1000) {
                    break;
                }
            }

            if (model.Rows.Count == 0) {
                Add("Value", target);
            }
        }

        model.Rebuild();
        return model;
    }
}

/// <summary>Configures object-table views, refresh behavior, and object selection.</summary>
public sealed record ObjectTableOptions {
    /// <summary>Gets or sets the property names to display, or null to use PowerShell formatting
    /// defaults.</summary>
    public string[]? Property { get; init; }
    /// <summary>Gets or sets the property names excluded from table display.</summary>
    public string[]? ExcludeProperty { get; init; }
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; init; } = "◈ OBJECTS";
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; init; } = "";
    /// <summary>Gets or sets the field used to order displayed entries.</summary>
    public string? SortBy { get; init; }
    /// <summary>Gets or sets the property name used to group table rows.</summary>
    public string? GroupBy { get; init; }
    /// <summary>Gets or sets whether sorting uses descending order.</summary>
    public bool Descending { get; init; }
    /// <summary>Gets or sets whether accepted original objects are written to the pipeline.</summary>
    public bool PassThru { get; init; }
    /// <summary>Gets or sets the resource or monitor view index.</summary>
    public ObjectView View { get; init; }
    /// <summary>Gets or sets the maximum nesting depth used for JSON conversion.</summary>
    public int JsonDepth { get; init; } = 4;
}

/// <summary>Displays original PowerShell objects using table, formatted, JSON, and inspection views.</summary>
public sealed partial class ObjectTableApp : IFullscreenApp {
    private readonly IReadOnlyList<PSObject> _objects;
    private readonly ObjectTableOptions _options;
    private TableModel? _formatted;
    private NodeState? _inspection;
    private long? _inspectionId;
    private Themes.ThemePalette? _memberPalette;
    private int _memberWidth;
    private readonly HashSet<long> _marked = [];
    private readonly Dictionary<long, string> _json = [];
    private readonly Queue<long> _jsonOrder = new();
    private TableModel? _root;
    private long? _selected;
    private readonly Stack<(NodeState? Node, TableModel Model, ObjectView View)> _parents = new();
    private NodeState? _node;
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public TableSurface Surface { get; }
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => Surface;
    /// <summary>Gets the resource or monitor view index.</summary>
    public ObjectView View { get; private set; }

    /// <summary>Checks whether an object representation has been loaded.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    /// <returns>True when the requested view is available.</returns>
    public bool IsLoaded(ObjectView view) =>
        view == ObjectView.Json ? _json.Count > 0 : view == ObjectView.Inspect ? _inspection is not null : _formatted is not null;
    /// <summary>Gets the number of cached JSON object representations.</summary>
    public int CachedJsonCount => _json.Count;

    /// <summary>Gets the original objects accepted by the user.</summary>
    public IEnumerable<object> Chosen {
        get {
            if (!Surface.Accepted) {
                yield break;
            }

            SaveSelection();
            if (_marked.Count > 0) {
                for (int i = 0; i < _objects.Count; i++) {
                    if (_marked.Contains(i)) {
                        yield return _objects[i];
                    }
                }
            }
            else if (_selected is { } selected && selected >= 0 && selected < _objects.Count) {
                yield return _objects[(int)selected];
            }
        }
    }

    /// <summary>Initializes a new ObjectTableApp instance.</summary>
    /// <param name="objects">The original PowerShell objects retained by the view.</param>
    /// <param name="options">The object-view configuration.</param>
    public ObjectTableApp(IReadOnlyList<PSObject> objects, ObjectTableOptions options) {
        _objects = objects;
        _options = options;
        Surface = new(new TableModel(["Value"]), options.Title) {
            Tabs = ["Formatted", "Inspect", "JSON"],
            ExtraHelp = "Tab / 1–3 views · M/F10 menu · Backspace/Esc back · G group"
        };
        SwitchView(options.View);
        Surface.TabRequested += index => {
            try {
                SwitchView((ObjectView)index);
            }
            catch (Exception error) {
                Surface.Status = TableModel.Text(error.Message);
                Surface.Refresh();
            }
        };
        Surface.InspectRequested += InspectSelected;
        Surface.BackRequested = GoBack;
        Surface.ContextRequested += ShowMenu;
        Surface.PriorityShortcuts = ["m", "M"];
        Surface.Shortcut += key => {
            if (key is "menu" or "m" or "M") {
                ShowMenu(Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
            }
        };
        UpdateNavigation();
    }

    private void SaveSelection() {
        if (_root is null) {
            return;
        }

        _selected = _root.Current?.Id;
        _marked.Clear();
        _marked.UnionWith(_root.Marked);
    }

    /// <summary>Activates an object representation, loading it when necessary.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    public void SwitchView(ObjectView view) {
        if (!Enum.IsDefined(view)) {
            throw new ArgumentOutOfRangeException(nameof(view));
        }

        if (_node is not null) {
            SwitchNodeView(view);
            return;
        }

        SaveSelection();
        if (view == ObjectView.Inspect) {
            _selected ??= _objects.Count > 0 ? 0 : null;
            if (_selected is { } id && id < _objects.Count) {
                if (_inspection is null || _inspectionId != id) {
                    _inspection = new NodeState(_objects[(int)id], $"$[{id}]");
                    _inspectionId = id;
                }
                ShowInspection(_inspection);
            }
            else {
                Surface.Model = new TableModel(["Kind", "Type", "Name", "Value / signature"]);
                Surface.SetDocument(null);
                Surface.ResetColumns();
            }
        }
        else if (view == ObjectView.Json) {
            // Opening JSON directly does not materialize either table.
            if (_root is null) {
                _selected ??= _objects.Count > 0 ? 0 : null;
            }

            string text = "No object selected.";
            if (_selected is { } id && id < _objects.Count) {
                if (!_json.TryGetValue(id, out text!)) {
                    text = PowerShellObjectViews.Json(_objects[(int)id], _options.JsonDepth);
                    const int displayLimit = 1024 * 1024;
                    if (text.Length > displayLimit) {
                        text = text[..displayLimit] + "\n… [JSON display limited to 1 MiB]";
                    }

                    // Bound caching when users inspect many different objects.
                    if (_json.Count == 16) {
                        _json.Remove(_jsonOrder.Dequeue());
                    }

                    _json.Add(id, text);
                    _jsonOrder.Enqueue(id);
                }
            }

            Surface.SetDocument(text);
            Surface.Status = $"Selected object {(_selected is { } index ? index + 1 : 0)}/{_objects.Count} · bounded JSON snapshot · depth {_options.JsonDepth} · cycles marked";
        }
        else {
            var model = _formatted ?? CreateModel(view);
            model.Marked.Clear();
            model.Marked.UnionWith(_marked);
            if (_selected is { } id) {
                int index = model.Visible.FindIndex(row => row.Id == id);
                if (index >= 0) {
                    model.Select(index, Math.Max(1, Surface.Bounds.Height - 6));
                }
            }

            _formatted = model;
            _root = model;
            Surface.Model = model;
            Surface.PreferredWidths = model.PreferredWidths;
            Surface.SetDocument(null);
            Surface.ResetColumns();
            Surface.Status = "PowerShell format view · loaded format files, display labels and calculated values";
        }

        _parents.Clear();
        Surface.EnterAccepts = _options.PassThru && view == ObjectView.Formatted;
        View = view;
        Surface.ActiveTab = (int)view;
        UpdateNavigation();
        Surface.Refresh();
    }

    private TableModel CreateModel(ObjectView view) {
        var model = PowerShellObjectViews.Format(_objects, _options.Property);
        ConfigureModel(model, view);
        return model;
    }

    private void ConfigureModel(TableModel model, ObjectView view) {
        if (_options.ExcludeProperty is { } excluded) {
            for (int i = 0; i < model.Columns.Length; i++) {
                model.Enabled[i] = !excluded.Contains(model.Columns[i], StringComparer.OrdinalIgnoreCase);
            }
        }

        if (!model.Enabled.Any(enabled => enabled)) {
            throw new ArgumentException("At least one column must remain visible.");
        }

        int Find(string name) {
            int index = Array.FindIndex(model.Columns, column => column.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && view == _options.View) {
                throw new ArgumentException($"Column '{name}' is not available in the {view} view. Use a displayed column label or select it with -Property.");
            }

            return index;
        }

        if (_options.SortBy is { } sort) {
            model.SortColumn = Find(sort);
        }

        if (_options.GroupBy is { } group) {
            model.GroupColumn = Find(group);
        }

        model.Filter = _options.Filter;
        model.Descending = _options.Descending;
        model.Rebuild();
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        if (View == ObjectView.Inspect && (_node ?? _inspection)?.Members == true) {
            if (!ReferenceEquals(_memberPalette, Surface.Palette)) {
                ObjectMembers.Style(Surface.Model, Surface.Palette);
                _memberPalette = Surface.Palette;
                Surface.Refresh();
            }
            if (_memberWidth != Surface.Bounds.Width) {
                UpdateMemberWidths();
                Surface.Refresh();
            }
        }
        return !Surface.ExitRequested;
    }

    private void ShowInspection(NodeState node) {
        var model = node.Models[1];
        if (model is null) {
            model = CreateNodeModel(node, ObjectView.Inspect, ReferenceEquals(node, _inspection) ? _options.Property : null);
            if (ReferenceEquals(node, _inspection) && _options.View == ObjectView.Inspect) {
                ConfigureModel(model, ObjectView.Inspect);
            }
            node.Models[1] = model;
        }
        Surface.Model = model;
        Surface.PreferredWidths = model.PreferredWidths;
        Surface.SetDocument(null);
        Surface.ResetColumns();
        if (node.Members) {
            ObjectMembers.Style(model, Surface.Palette);
            _memberPalette = Surface.Palette;
            UpdateMemberWidths();
        }
    }

    private void UpdateMemberWidths() {
        _memberWidth = Surface.Bounds.Width;
        int width = _memberWidth > 0 ? _memberWidth : 120;
        int Column(int column, int maximum) =>
            Math.Clamp(Surface.Model.Rows.Select(row => row.Cells[column].Length + 2).DefaultIfEmpty(8).Max(), 8, Math.Max(8, maximum));
        Surface.PreferredWidths = [
            Column(0, width / 8),
            Column(1, width / 6),
            Column(2, width / 5),
            0
        ];
    }
    /// <inheritdoc/>
    public void Dispose() {
    }
}
