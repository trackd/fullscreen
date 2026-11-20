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
        public bool Members { get; set; }
        public bool FormattedMemberRows { get; set; }
    }

    /// <summary>Gets the display path of the current nested object.</summary>
    public string NodePath => _node?.Path ?? "$";
    /// <summary>Gets the number of nested object-navigation levels.</summary>
    public int NavigationDepth => _parents.Count;

    /// <summary>Opens an inspection view of the selected row&apos;s original object.</summary>
    /// <param name="row">The table row whose original object is inspected.</param>
    public void InspectSelected(TableRow row) {
        if (View == ObjectView.Formatted && _node?.FormattedMemberRows == true) {
            if (row.Values[1] is { } member) {
                OpenNode(member, row.Cells[0]);
            }
            return;
        }
        var inspection = _node ?? _inspection;
        if (View == ObjectView.Inspect && inspection?.Members == true) {
            if (row.Original is PSMethodInfo or ObjectMembers.Signature) {
                Surface.ShowDetail(row.Detail);
            }
            else if (row.Values[3] is { } memberValue) {
                OpenNode(memberValue, row.Cells[2]);
            }
            return;
        }

        object? value = View == ObjectView.Inspect && inspection?.MemberRows == true ? row.Values[1] : row.Original;
        if (value is not null) {
            OpenNode(value, View == ObjectView.Inspect && inspection?.MemberRows == true ? row.Cells[0] : $"[{row.Id}]");
        }
    }

    /// <summary>Drills into a collection or object's properties without switching to Details.</summary>
    public void BrowseSelected(TableRow row) {
        var node = _node ?? (View == ObjectView.Inspect ? _inspection : null);
        object? value;
        string name;
        if (View == ObjectView.Inspect && node?.Members == true) {
            if (row.Original is PSMethodInfo or ObjectMembers.Signature) {
                Surface.ShowDetail(row.Detail);
                return;
            }
            value = row.Values[3];
            name = row.Cells[2];
        }
        else if (View == ObjectView.Formatted && node?.FormattedMemberRows == true
            || View == ObjectView.Inspect && node?.MemberRows == true) {
            value = row.Values[1];
            name = row.Cells[0];
        }
        else {
            value = row.Original;
            name = $"[{row.Id}]";
        }
        if (value is not null && !ObjectSnapshot.IsScalar(value)) {
            OpenNode(value, name, ObjectView.Formatted);
        }
        else {
            Surface.Status = "Scalar value · I opens details";
            Surface.Refresh();
        }
    }

    /// <summary>Navigates into a nested object under a display name.</summary>
    /// <param name="value">The original object to open as a navigation node.</param>
    /// <param name="name">The label identifying the nested object in navigation.</param>
    public void OpenNode(object value, string name) => OpenNode(value, name, ObjectView.Inspect);

    private void OpenNode(object value, string name, ObjectView view) {
        if (view is not (ObjectView.Formatted or ObjectView.Inspect)) {
            throw new ArgumentOutOfRangeException(nameof(view));
        }
        if ((View == ObjectView.Inspect ? _node ?? _inspection : _node) is { } current
            && ObjectSnapshot.IsScalar(current.Value) && ObjectSnapshot.IsScalar(value)) {
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
            string path = _node?.Path ?? (View == ObjectView.Inspect ? _inspection?.Path : null) ?? "$";
            var node = new NodeState(value, path + (name.StartsWith('[') ? name : "." + name));
            // Resolve getters before changing navigation, so a failure leaves the parent usable.
            node.Models[(int)view] = CreateNodeModel(node, view);
            _parents.Push((_node, Surface.Model, View));
            _node = node;
            SwitchNodeView(view);
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
        Surface.EnterAccepts = _node is null && _options.PassThru && View == ObjectView.Formatted;
        Surface.ActiveTab = (int)View;
        if (_node is not null) {
            SwitchNodeView(View);
        }
        else if (View == ObjectView.Json) {
            SwitchView(View);
        }
        else if (View == ObjectView.Inspect && _inspection is not null) {
            ShowInspection(_inspection);
        }

        UpdateNavigation();
        Surface.Refresh();
        return true;
    }

    private static TableModel CreateNodeModel(NodeState node, ObjectView view, string[]? properties = null) {
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

            return view == ObjectView.Formatted ? PowerShellObjectViews.Format(node.Items) : ObjectSnapshot.Create(node.Items, properties);
        }

        if (view == ObjectView.Formatted) {
            node.FormattedMemberRows = true;
            return ObjectSnapshot.Inspect(node.Value);
        }
        node.MemberRows = raw is IDictionary;
        node.Members = !node.MemberRows;
        return node.Members ? ObjectMembers.Create(node.Value) : ObjectSnapshot.Inspect(node.Value);
    }

    private void SwitchNodeView(ObjectView view) {
        var node = _node!;
        if (view == ObjectView.Inspect) {
            ShowInspection(node);
        }
        else if (view == ObjectView.Json) {
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
        Surface.Status = _node is null ? "Select a row · Enter/O browse · I details · / search · click column to sort" : "Selected node · Enter/O browse · I details · up to 1,000 rows · Backspace/Esc back";
        Surface.FooterHelp = Surface.EnterAccepts ? "Enter accept · O browse · I details · / search · F column filters · Q cancel"
            : "Enter/O browse · I details · Backspace back · / search · F column filters · Q close";
        Surface.DocumentHelp = "↑↓ navigate · ← collapse · → expand · Enter/Space fold · / search · N next · Ctrl+A/C copy · Backspace back · Tab views";
        if (View == ObjectView.Inspect && (_node ?? _inspection)?.Members == true) {
            object? value = _node?.Value ?? (_selected is { } id && id < _objects.Count ? _objects[(int)id] : null);
            string type = value is PSObject ps ? ps.TypeNames.FirstOrDefault() ?? ps.BaseObject.GetType().FullName ?? "" : value?.GetType().FullName ?? "";
            Surface.Status = TableModel.Text(type) + " · Enter/O browse value · I details · up to 1,000 members";
        }
    }

    private void ShowMenu(int x, int y) {
        var items = new List<MenuItem>();
        if (_parents.Count > 0) {
            items.Add(new("Back to parent · Backspace / Esc", () => GoBack()));
        }

        if (Surface.Model.Current is { } row && View != ObjectView.Json) {
            items.Add(new(Surface.EnterAccepts ? "Browse selected · O" : "Browse selected · Enter / O", () => BrowseSelected(row)));
            items.Add(new("Details · I", () => InspectSelected(row)));
            object? selected = View == ObjectView.Inspect ? null : row.Original;
            if (selected is not null) {
                foreach (var member in ObjectSnapshot.Inspect(selected).Rows) {
                    if (member.Values[1] is { } value) {
                        string name = member.Cells[0];
                        string prefix = _node?.MemberRows == true ? row.Cells[0] +
                            "." : _node is { Items: null } ? "" : $"[{row.Id}].";
                        items.Add(new("Browse " + name, () => OpenNode(value, prefix + name, ObjectView.Formatted)));
                    }
                }
            }
        }

        items.Add(new("Formatted view", () => SwitchView(ObjectView.Formatted)));
        items.Add(new("Inspect view", () => SwitchView(ObjectView.Inspect)));
        items.Add(new("JSON of this node", () => SwitchView(ObjectView.Json)));
        items.Add(new("Search / filter · /", Surface.BeginFilter));
        if (View != ObjectView.Json) {
            items.Add(new("Column filters · F", Surface.ShowColumnFilterMenu));
            items.Add(new("Sort by column", Surface.ShowSortMenu));
        }

        ContextMenuService.Show(Surface, items.ToArray(), x, y);
    }
}
