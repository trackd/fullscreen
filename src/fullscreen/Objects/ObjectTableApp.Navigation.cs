using System.Collections;
using System.Management.Automation;
using fullscreen.Tables;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Objects;

public sealed partial class ObjectTableApp {
    private sealed class NodeState(object value, string path) {
        public object Value { get; } = value;
        public string Path { get; } = path;
        public TableModel?[] Models { get; } = new TableModel?[2];
        public string? Json { get; set; }
        public List<PSObject>? Items { get; set; }
        public bool MemberRows { get; set; }
    }

    /// <summary>Gets the display path of the current nested object.</summary>
    public string NodePath => _node?.Path ?? "$";
    /// <summary>Gets the number of nested object-navigation levels.</summary>
    public int NavigationDepth => _parents.Count;

    /// <summary>Opens an inspection view of the selected row&apos;s original object.</summary>
    /// <param name="row">The table row whose original object is inspected.</param>
    public void InspectSelected(TableRow row) {
        if (_node is { MemberRows: false, Items: null } && View == ObjectView.Formatted) {
            SwitchNodeView(ObjectView.Expanded);
            return;
        }

        object? value = _node?.MemberRows == true ? row.Values[1] : row.Original;
        if (value is not null) {
            OpenNode(value, _node?.MemberRows == true ? row.Cells[0] : $"[{row.Id}]");
        }
    }

    /// <summary>Navigates into a nested object under a display name.</summary>
    /// <param name="value">The original object to open as a navigation node.</param>
    /// <param name="name">The label identifying the nested object in navigation.</param>
    public void OpenNode(object value, string name) {
        if (_node is not null && ObjectSnapshot.IsScalar(_node.Value) && ObjectSnapshot.IsScalar(value)) {
            Surface.Status = "Scalar values have no nested members";
            Surface.Refresh();
            return;
        }

        if (_parents.Count >= 16) {
            Surface.Status = "Maximum inspection depth reached (16) · Backspace returns";
            Surface.Refresh();
            return;
        }

        try {
            var node = new NodeState(value, NodePath + (name.StartsWith('[') ? name : "." + name));
            // Resolve getters before changing navigation, so a failure leaves the parent usable.
            node.Models[1] = CreateNodeModel(node, ObjectView.Expanded);
            _parents.Push((_node, Surface.Model, View));
            _node = node;
            SwitchNodeView(ObjectView.Expanded);
        }
        catch (Exception error) {
            Surface.ShowDetail(TableModel.Text(error.Message));
        }
    }

    /// <summary>Returns to the previous object-navigation level.</summary>
    /// <returns>True when a previous navigation level was available.</returns>
    public bool GoBack() {
        if (!_parents.TryPop(out var parent)) {
            return false;
        }

        _node = parent.Node;
        View = parent.View;
        Surface.Model = parent.Model;
        Surface.PreferredWidths = parent.Model.PreferredWidths;
        Surface.SetDocument(null);
        Surface.ResetColumns();
        Surface.EnterAccepts = _node is null && _options.PassThru;
        Surface.ActiveTab = (int)View;
        if (_node is not null) {
            SwitchNodeView(View);
        }
        else if (View == ObjectView.Json) {
            SwitchView(View);
        }

        UpdateNavigation();
        Surface.Refresh();
        return true;
    }

    private static TableModel CreateNodeModel(NodeState node, ObjectView view) {
        object raw = node.Value is PSObject ps ? ps.BaseObject : node.Value;
        if (raw is IEnumerable sequence and not string and not IDictionary) {
            if (node.Items is null) {
                node.Items = [];
                foreach (object? item in sequence) {
                    node.Items.Add(PSObject.AsPSObject(item ?? ""));
                    if (node.Items.Count == 1000) {
                        break;
                    }
                }
            }

            return view == ObjectView.Formatted ? PowerShellObjectViews.Format(node.Items) : ObjectSnapshot.Create(node.Items);
        }

        return view == ObjectView.Formatted ? PowerShellObjectViews.Format([PSObject.AsPSObject(node.Value)]) : ObjectSnapshot.Inspect(node.Value);
    }

    private void SwitchNodeView(ObjectView view) {
        var node = _node!;
        if (view == ObjectView.Json) {
            if (node.Json is null) {
                string json = PowerShellObjectViews.Json(node.Value, _options.JsonDepth);
                node.Json = json.Length <= 1024 * 1024 ? json : json[..(1024 * 1024)] +
                    "\n… [JSON display limited to 1 MiB]";
            }

            Surface.SetDocument(node.Json);
        }
        else {
            var model = node.Models[(int)view] ??= CreateNodeModel(node, view);
            if (node.Items is not null && node.Models.Contains(Surface.Model)
                && Surface.Model.Current is { } selected) {
                int index = model.Visible.FindIndex(row => row.Id == selected.Id);
                if (index >= 0) {
                    model.Select(index, Math.Max(1, Surface.Bounds.Height - 6));
                }
            }

            object raw = node.Value is PSObject ps ? ps.BaseObject : node.Value;
            node.MemberRows = view == ObjectView.Expanded && (raw is not IEnumerable || raw is string or IDictionary);
            Surface.Model = model;
            Surface.PreferredWidths = model.PreferredWidths;
            Surface.SetDocument(null);
            Surface.ResetColumns();
        }

        View = view;
        Surface.ActiveTab = (int)view;
        Surface.EnterAccepts = false;
        UpdateNavigation();
        Surface.Refresh();
    }

    private void UpdateNavigation() {
        Surface.Navigation = _node is null ? "$ · M/F10 menu" : "‹ Back · " + NodePath + " · M/F10 menu";
        Surface.DocumentHint = NodePath;
        Surface.Status = _node is null ? "Select a row · Enter/I inspect · / search · click column to sort" : "Selected node · Enter/I dive · / search · S sort · up to 1,000 members · Backspace/Esc back";
        Surface.DocumentHelp = "1 Formatted  2 Expanded  3 JSON · / search · N next · M/F10 menu · Backspace/Esc back · Q close";
    }

    private void ShowMenu(int x, int y) {
        var items = new List<MenuItem>();
        if (_parents.Count > 0) {
            items.Add(new("Back to parent · Backspace / Esc", () => GoBack()));
        }

        if (Surface.Model.Current is { } row && View != ObjectView.Json) {
            items.Add(new("Inspect selected · Enter / I", () => InspectSelected(row)));
            object? selected = _node?.MemberRows == true ? row.Values[1] : row.Original;
            if (selected is not null) {
                foreach (var member in ObjectSnapshot.Inspect(selected).Rows) {
                    if (member.Values[1] is { } value) {
                        string name = member.Cells[0];
                        string prefix = _node?.MemberRows == true ? row.Cells[0] +
                            "." : _node is { Items: null } ? "" : $"[{row.Id}].";
                        items.Add(new("Open " + name, () => OpenNode(value, prefix + name)));
                    }
                }
            }
        }

        items.Add(new("Formatted view", () => SwitchView(ObjectView.Formatted)));
        items.Add(new("Expanded view", () => SwitchView(ObjectView.Expanded)));
        items.Add(new("JSON of this node", () => SwitchView(ObjectView.Json)));
        items.Add(new("Search / filter · /", Surface.BeginFilter));
        if (View != ObjectView.Json) {
            items.Add(new("Sort by column", Surface.ShowSortMenu));
        }

        ContextMenuService.Show(Surface, items.ToArray(), x, y);
    }
}
