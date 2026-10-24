using System.Text.Json;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Custom;

public sealed partial class TuiDesignerApp {
    private void ShowViewMenu() =>
        ContextMenuService.Show(Root, [new MenuItem(
            "Properties · always visible" + (_preferences.Properties == DesignerPropertiesMode.Always ? " ✓" : ""),
            () => SetPropertiesMode(DesignerPropertiesMode.Always)
        ), new MenuItem(
            "Properties · on demand" + (_preferences.Properties == DesignerPropertiesMode.OnDemand ? " ✓" : ""),
            () => SetPropertiesMode(DesignerPropertiesMode.OnDemand)
        ), new MenuItem(
            "Properties · hidden" + (_preferences.Properties == DesignerPropertiesMode.Hidden ? " ✓" : ""),
            () => SetPropertiesMode(DesignerPropertiesMode.Hidden)
        ), MenuItem.Separator(), new MenuItem("Shorter properties panel", () => ResizeInspector(-1)), new MenuItem("Taller properties panel", () => ResizeInspector(1)), new MenuItem(_paletteOpen ? "Hide control list" : "Show control list", () =>
    {
        _paletteOpen = !_paletteOpen;
        UpdatePanes();
    })], Root.Bounds.X + 2, Root.Bounds.Y + 1);
    internal void SetPropertiesMode(DesignerPropertiesMode mode) {
        _preferences.Properties = mode;
        _inspectorOpen = mode == DesignerPropertiesMode.Always;
        UpdatePanes();
        SavePreferences();
    }

    private void SavePreferences() {
        if (_preferencesPath is null) {
            return;
        }

        try {
            _preferences.Save(_preferencesPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            _status.Text = "View changed · could not remember preferences: " + error.Message;
        }
    }

    private void ShowActions(int? x = null, int? y = null) {
        if (_rows.Count == 0) {
            return;
        }

        var row = _rows[_selection];
        bool child = row.Parent is not null;
        var wrap = new MenuItem("Wrap in…");
        foreach (string type in new[]
        {
            "HStack",
            "VStack",
            "Border"
        }

        ) {
            wrap.Items.Add(new MenuItem(
                type == "HStack" ? "Row" : type == "VStack" ? "Column" : "Border",
                () => WrapSelected(type)
            ) { IsEnabled = child });
        }

        ContextMenuService.Show(
            Root,
            [
                new MenuItem(row.Node.Type +
            " · " + row.Node.Id) { IsEnabled = false },
                new MenuItem("Edit properties · F3", () => TogglePane(true)),
                new MenuItem("Select parent", SelectParent) { IsEnabled = child },
                new MenuItem("Duplicate", DuplicateSelected) { IsEnabled = child },
                wrap,
                MenuItem.Separator(),
                new MenuItem("Move earlier", () => Move(-1)) { IsEnabled = child },
                new MenuItem("Move later", () => Move(1)) { IsEnabled = child },
                new MenuItem("Move into previous container", Indent) { IsEnabled = child },
                new MenuItem("Move out of parent", Outdent) { IsEnabled = child },
                MenuItem.Separator(),
                new MenuItem("Delete…", () => Confirm("Delete selected control and its children?", DeleteSelected)) { IsEnabled = child }
            ],
            x ?? _previewHost.Bounds.X + 2,
            y ?? _previewHost.Bounds.Y + 2
        );
    }

    private void SelectParent() {
        if (!ApplyProperties() || _rows[_selection].Parent is not { } parent) {
            return;
        }

        _selection = _rows.FindIndex(row => row.Node == parent);
        _tree.SelectedIndex = _selection;
        SelectionChanged();
        LoadFields();
        _markers?.Refresh();
    }

    private void DuplicateSelected() {
        if (!ApplyProperties() || _rows[_selection].Parent is null) {
            return;
        }

        var candidate = TuiDocument.FromJson(Document.ToJson());
        var rows = Flatten(candidate.Root);
        var selected = rows[_selection];
        var clone = JsonSerializer.Deserialize<TuiNode>(
            JsonSerializer.Serialize(selected.Node, TuiDocument.JsonOptions),
            TuiDocument.JsonOptions
        )!;
        var ids = rows.Select(row => row.Node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Flatten(clone)) {
            string old = row.Node.Id, id = old + "_copy";
            for (int suffix = 2; !ids.Add(id); suffix++) {
                id = old + "_copy" + suffix;
            }

            renamed.Add(old, id);
            row.Node.Id = id;
        }

        foreach (var row in Flatten(clone)) {
            if (renamed.TryGetValue(row.Node.Target, out string? target)) {
                row.Node.Target = target;
            }
        }

        selected.Parent!.Children.Insert(selected.Parent.Children.IndexOf(selected.Node) + 1, clone);
        CommitStructure(candidate, clone.Id);
    }

    private void WrapSelected(string type) {
        if (!ApplyProperties() || _rows[_selection].Parent is null) {
            return;
        }

        var candidate = TuiDocument.FromJson(Document.ToJson());
        var rows = Flatten(candidate.Root);
        var selected = rows[_selection];
        var wrapper = NewControl(type, rows);
        wrapper.TabTitle = selected.Node.TabTitle;
        selected.Node.TabTitle = "";
        wrapper.Children.Add(selected.Node);
        selected.Parent!.Children[selected.Parent.Children.IndexOf(selected.Node)] = wrapper;
        CommitStructure(candidate, wrapper.Id);
    }

    private void CommitStructure(TuiDocument candidate, string id) {
        try {
            candidate.Validate();
            Remember();
            Document = candidate;
            Rebuild(Flatten(Document.Root).FindIndex(row => row.Node.Id == id));
        }
        catch (ArgumentException error) {
            _status.Text = "Layout unchanged · " + error.Message;
        }
    }
}
