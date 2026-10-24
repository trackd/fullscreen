using System.Globalization;
using System.Text.Json;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Custom;

public sealed partial class TuiDesignerApp {
    private readonly TextBox _title = new() { Placeholder = "Application title" };
    private readonly TextArea _text = new() {
        MinHeight = 1,
        MaxHeight = 1,
        AcceptTab = false
    };
    private readonly Dictionary<string, Visual> _editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Visual> _propertyRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Paragraph> _propertyErrors = new(StringComparer.Ordinal);
    private readonly Paragraph _validation = new("") {
        Wrap = true,
        IsVisible = false
    };
    private bool _paletteOpen = true, _inspectorOpen;
    private string _propertyGroup = "Content";
    private readonly TextBlock _selectionCaption = new();
    private readonly Dictionary<string, Button> _groupButtons = [];
    private readonly WrapHStack _propertyGroups = new() {
        Spacing = 1
    };
    private Visual _documentProperties = null!;
    private ScrollViewer _propertyViewport = null!;
    private string _inspectedType = "";
    private int _paneWidth = -1;
    private int _inspectorExtent = -1;
    private int _inspectorChrome = -1;
    private VStack CreateInspector() {
        var groups = _propertyGroups;
        foreach (string group in new[]
        {
            "Content",
            "Layout",
            "Appearance",
            "Behavior",
            "Document"
        }

        ) {
            var button = new Button(group).Click(() => SelectPropertyGroup(group));
            _groupButtons.Add(group, button);
            groups.Children.Add(button);
        }

        groups.Children.Add(new Button("−").Click(() => ResizeInspector(-1)));
        groups.Children.Add(new Button("+").Click(() => ResizeInspector(1)));
        groups.Children.Add(new Button("Hide").Click(HideInspector));
        groups.Children.Add(new Button("?").Click(ShowPropertyHelp));
        foreach (var button in groups.Children.OfType<Button>()) {
            button.SetStyle(ButtonStyle.Default with { Padding = new(1, 0, 1, 0) });
        }

        var fields = new WrapHStack {
            Spacing = 2,
            HorizontalAlignment = Align.Stretch
        };
        _documentProperties = new VStack(new TextBlock("App title"), _title) {
            MinWidth = 36,
            MaxWidth = 36
        };
        fields.Children.Add(_documentProperties);
        var properties = TuiControlCatalog.All.SelectMany(control => TuiControlCatalog.Properties(control.Name)).DistinctBy(property => property.Name).ToArray();
        foreach (var property in properties.OrderBy(property => property.Name == "Text" ? 0 : 1)) {
            Visual editor = property.Name == "Text" ? _text : property.Choices.Length > 0 ? new Select<string>(property.Choices) : property.Type == "Boolean" ? new CheckBox(property.Name) : property.Type is "String[]" or "Dictionary`2" ? new TextArea {
                MinHeight = 1,
                MaxHeight = 1,
                AcceptTab = false
            }

            : new TextBox();
            string hint = PropertyHint(property);
            if (editor is TextBox input) {
                input.Placeholder = hint;
            }
            else if (editor is TextArea area) {
                area.Placeholder = hint;
            }

            _editors.Add(property.Name, editor);
            var error = new Paragraph("") {
                Wrap = true,
                IsVisible = false
            };
            _propertyErrors.Add(property.Name, error);
            int width = editor is TextArea ? 36 : 23;
            var field = new VStack(new TextBlock(property.Name), editor, error) {
                MinWidth = width,
                MaxWidth = width
            };
            _propertyRows.Add(property.Name, field);
            fields.Children.Add(field);
        }

        _propertyViewport = new ScrollViewer(new VStack(_validation, fields) { HorizontalAlignment = Align.Stretch }) {
            HorizontalScrollEnabled = false,
            VerticalAlignment = Align.Stretch
        };
        ApplyInspectorStyles();
        return new VStack(_selectionCaption, groups, _propertyViewport) {
            VerticalAlignment = Align.Stretch
        };
    }

    private static string PropertyHint(TuiPropertyInfo property) =>
        property.Name switch {
            "Width" or "Height" => "Automatic",
            "Id" => "Unique control name",
            "Text" => "Text or label",
            "Bind" => "State variable name",
            "SelectionBind" => "Selected value variable",
            "Foreground" or "Background" => "Color name or #RRGGBB",
            "Options" => "JSON properties: {}",
            "Spacing" => "Gap in cells",
            _ when property.Type == "String[]" => "One item per line",
            _ => "Enter " + property.Name
        };

    private void ApplyInspectorStyles() {
        var fill = Color.Mix(_theme.Background, _theme.Accent, 0.16f);
        var textBox = TextBoxStyle.Default with {
            Background = fill,
            Border = _theme.Accent,
            FocusBorder = _theme.Warning,
            Placeholder = _theme.Foreground
        };
        var textArea = TextAreaStyle.Default with {
            Background = fill,
            Border = _theme.Accent,
            FocusBorder = _theme.Warning,
            Placeholder = _theme.Foreground
        };
        _title.SetStyle(textBox);
        _paletteSearch.SetStyle(textBox);
        foreach (var editor in _editors.Values) {
            if (editor is TextBox input) {
                input.SetStyle(textBox);
            }
            else if (editor is TextArea area) {
                area.SetStyle(textArea);
            }
            else if (editor is Select<string> select) {
                select.SetStyle(SelectStyle.Default with {
                    NormalStyle = _theme.Body.WithBackground(fill),
                    HoverStyle = _theme.Selected,
                    FocusedStyle = _theme.Selected
                });
            }
        }
    }

    private void ShowPropertyHelp() {
        var lines = TuiControlCatalog.Properties(_inspectedType).Where(property => PropertyGroup(property.Name) == _propertyGroup).Select(property => property.Name +
            " · " + property.Description);
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock(_propertyGroup + " properties"),
            IsModal = true,
            Width = Math.Min(72, Root.Bounds.Width - 4),
            Content = new VStack(
                new Paragraph(_propertyGroup == "Document" ? "App title · The title displayed by the application." : string.Join('\n', lines)) { Wrap = true },
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        dialog.Show();
    }

    private static string PropertyGroup(string name) =>
        name switch {
            "Width" or "Height" or "Spacing" or "HorizontalAlignment" or "VerticalAlignment" => "Layout",
            "Foreground" or "Background" or "Bold" => "Appearance",
            "Bind" or "SelectionBind" or "Enabled" or "Close" or "Target" or "RefreshInterval"
                or "IncludeProcesses"
                or "Options" => "Behavior",
            _ => "Content"
        };
    private void SelectPropertyGroup(string group) {
        _propertyGroup = group;
        UpdateInspector(_inspectedType);
        _propertyViewport.VerticalOffset = 0;
    }

    private static string EditorText(Visual editor) =>
        editor switch {
            TextBox input => input.Text ?? "",
            TextArea area => area.Text ?? "",
            CheckBox check => check.IsChecked.ToString(),
            Select<string> select => select.SelectedIndex >= 0 && select.SelectedIndex < select.Items.Count ? select.Items[select.SelectedIndex] : "",
            _ => ""
        };
    private string Fields() =>
        _title.Text +
        "\0" + string.Join('\0', _editors.Select(pair => pair.Key +
        "=" + EditorText(pair.Value)));
    private void UpdateInspector(string type) {
        var allowed = TuiControlCatalog.Properties(type);
        foreach (var row in _propertyRows) {
            var metadata = allowed.FirstOrDefault(property => property.Name == row.Key);
            row.Value.IsVisible = metadata is not null && PropertyGroup(row.Key) == _propertyGroup
                && (row.Key != "TabTitle" || _rows[_selection].Parent?.Type is "Tabs" or "TabControl");
            if (metadata is not null && _editors[row.Key] is Select<string> select
                && !select.Items.SequenceEqual(metadata.Choices)) {
                string value = EditorText(select);
                select.Items.Clear();
                foreach (string choice in metadata.Choices) {
                    select.Items.Add(choice);
                }

                select.SelectedIndex = Array.IndexOf(metadata.Choices, value);
                if (select.SelectedIndex < 0) {
                    select.SelectedIndex = Array.IndexOf(metadata.Choices, Convert.ToString(metadata.DefaultValue, CultureInfo.InvariantCulture));
                }
            }
        }

        _documentProperties.IsVisible = _propertyGroup == "Document";
        foreach (var (group, button) in _groupButtons) {
            button.IsEnabled = group != _propertyGroup;
        }

        _inspectedType = type;
        UpdateTabStops();
    }

    private void LoadFields() {
        ClearValidation();
        var node = _rows[_selection].Node;
        _selectionCaption.Text = "PROPERTIES · " + string.Join(" / ", Ancestors(node)) + " · " + node.Type;
        _title.Text = Document.Title;
        UpdateInspector(node.Type);
        foreach (var (name, editor) in _editors) {
            object? value = typeof(TuiNode).GetProperty(name)!.GetValue(node);
            string text = value is string[] items ? string.Join('\n', items) : value is Dictionary<string, JsonElement> options ? JsonSerializer.Serialize(options, TuiDocument.JsonOptions) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            switch (editor) {
                case TextBox input:
                    input.Text = text;
                    break;
                case TextArea area:
                    area.Text = text;
                    break;
                case CheckBox check:
                    check.IsChecked = value is true;
                    break;
                case Select<string> select:
                    select.SelectedIndex = select.Items.IndexOf(text);
                    break;
            }
        }

        _fields = _pending = Fields();
    }

    private Stack<string> Ancestors(TuiNode node) {
        var path = new Stack<string>();
        for (TuiNode? current = node; current is not null; current = _rows.First(row => row.Node == current).Parent) {
            path.Push(current.Id);
        }

        return path;
    }

    private void ClearValidation() {
        _validation.IsVisible = false;
        foreach (var error in _propertyErrors.Values) {
            error.IsVisible = false;
        }
    }

    /// <summary>Validates editor values and applies them to the selected control.</summary>
    /// <param name="revealError">Whether an invalid property value is shown to the user. Defaults to
    /// true.</param>
    /// <returns>True when the edited properties are valid.</returns>
    public bool ApplyProperties(bool revealError = true) {
        if (_rows.Count == 0 || _fields == Fields()) {
            return true;
        }

        string? invalidProperty = null;
        try {
            var candidate = TuiDocument.FromJson(Document.ToJson());
            var node = Flatten(candidate.Root)[_selection].Node;
            candidate.Title = _title.Text ?? "";
            node.Type = EditorText(_editors["Type"]);
            TuiControlCatalog.ResetUnsupported(node);
            foreach (var metadata in TuiControlCatalog.Properties(node.Type)) {
                var property = typeof(TuiNode).GetProperty(metadata.Name)!;
                string text = EditorText(_editors[metadata.Name]);
                try {
                    object? value = property.PropertyType == typeof(string[]) ? text.Length == 0 ? Array.Empty<string>() : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split(
                        '\n',
                        metadata.Name == "Columns" ? StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries : StringSplitOptions.None
                    ) : property.PropertyType == typeof(bool) ? bool.Parse(text) : property.PropertyType == typeof(int?) ? string.IsNullOrWhiteSpace(text) ? null : int.Parse(text, CultureInfo.InvariantCulture) : property.PropertyType == typeof(double?) ? string.IsNullOrWhiteSpace(text) ? null : double.Parse(text, CultureInfo.InvariantCulture) : property.PropertyType == typeof(int) ? int.Parse(text, CultureInfo.InvariantCulture) : property.PropertyType == typeof(double) ? double.Parse(text, CultureInfo.InvariantCulture) : property.PropertyType == typeof(Dictionary<string, JsonElement>) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text, TuiDocument.JsonOptions) ?? [] : text;
                    property.SetValue(node, value);
                }
                catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or JsonException) {
                    invalidProperty = metadata.Name;
                    throw new ArgumentException(
                        $"Control '{node.Id}', property '{metadata.Name}': expected {TuiControlCatalog.TypeDescription(property.PropertyType)}; received '{text}'.",
                        error
                    );
                }
            }

            try {
                candidate.Validate();
            }
            catch (ArgumentException error) {
                invalidProperty = TuiControlCatalog.Properties(node.Type).FirstOrDefault(property => error.Message.Contains(property.Name, StringComparison.OrdinalIgnoreCase))?.Name;
                throw new ArgumentException($"Control '{node.Id}' ({node.Type}): {error.Message}", error);
            }

            Remember();
            Document = candidate;
            _dirty = true;
            _rows = Flatten(Document.Root);
            _tree.Items[_selection] = new string(' ', _rows[_selection].Depth * 2) + node.Type + " · " + node.Id;
            LoadFields();
            RefreshPreview();
            UpdateHeading();
            _status.Text = "Unsaved · preview updated";
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) {
            ClearValidation();
            _status.Text = "Property error · " + error.Message;
            var message = invalidProperty is not null ? _propertyErrors[invalidProperty] : _validation;
            message.Text = error.Message;
            message.IsVisible = true;
            if (revealError) {
                if (invalidProperty is not null) {
                    SelectPropertyGroup(PropertyGroup(invalidProperty));
                }

                _inspectorOpen = true;
                UpdatePanes();
                if (invalidProperty is not null) {
                    Root.App?.Focus(_editors[invalidProperty]);
                }
            }

            return false;
        }
    }

    private void TogglePane(bool inspector) {
        if (inspector) {
            _inspectorOpen = true;
        }
        else {
            _paletteOpen = true;
        }

        UpdatePanes();
        if (_minimal) {
            return;
        }

        Visual target = inspector ? _propertyGroup == "Document" ? _title : _propertyGroup == "Content" ? _editors["Id"] : _editors.First(pair => _propertyRows[pair.Key].IsVisible).Value : _paletteSearch;
        Root.App?.Focus(target);
    }

    private void SelectionChanged() {
        if (_preferences.Properties == DesignerPropertiesMode.Always) {
            _inspectorOpen = true;
        }
        else if (_preferences.Properties == DesignerPropertiesMode.Hidden) {
            _inspectorOpen = false;
        }

        _propertyViewport.VerticalOffset = 0;
        UpdatePanes();
    }

    private void HideInspector() {
        _inspectorOpen = false;
        UpdatePanes();
        if (!_minimal) {
            _paletteOpen = true;
            UpdatePanes();
            Root.App?.Focus(_previewHost);
        }
    }

    private void ResizeInspector(int delta) {
        _preferences.Height = Math.Clamp(_preferences.Height + delta, 4, 12);
        UpdatePanes();
        SavePreferences();
    }

    private void UpdatePanes() {
        int width = Root.Bounds.Width;
        if (width == 0 || Root.Bounds.Height == 0) {
            return;
        }

        var layout = (Grid)Root;
        _inspectorExtent = _propertyViewport.Content!.DesiredSize.Height;
        _inspectorChrome = 1 + Math.Max(1, _propertyGroups.DesiredSize.Height);
        int minimumHeight = _inspectorChrome + 2;
        int inspectorHeight = Math.Clamp(
            _inspectorChrome + _inspectorExtent,
            minimumHeight,
            Math.Max(minimumHeight, _preferences.Height)
        );
        // Keep complete label/value pairs in the compact viewport.
        if (_inspectorExtent % 2 == 0 && (inspectorHeight - _inspectorChrome) % 2 != 0) {
            inspectorHeight--;
        }

        for (int i = 0; i < 3; i++) {
            bool visible = i == 1 || !_minimal && (i != 2 || _inspectorOpen);
            layout.Cells[i].Content!.IsVisible = visible;
            layout.RowDefinitions[i].Height = !visible ? GridLength.Fixed(0) : i == 1 ? GridLength.Star(1) : GridLength.Fixed(i == 2 ? inspectorHeight : 1);
        }

        bool palette = !_minimal && _paletteOpen;
        _sidebar.IsVisible = palette;
        _panes.ColumnDefinitions[1].Width = GridLength.Fixed(palette ? 20 : 0);
        _panes.ColumnGap = palette ? 1 : 0;
        _paneWidth = width;
        _paneHeight = Root.Bounds.Height;
        UpdateTabStops();
    }
}
