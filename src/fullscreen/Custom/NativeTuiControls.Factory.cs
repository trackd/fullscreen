using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.CompilerServices;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.DataGrid;
using XenoAtom.Terminal.UI.Collections;
using System.Text.Json;
using XenoAtom.Terminal;

namespace fullscreen.Custom;
/// <summary>Describes a cell edit emitted by a custom TUI data grid.</summary>
/// <param name="Original">The original source object retained for selection output.</param>
/// <param name="Values">The source values used to initialize the object.</param>
public sealed record TuiGridEdit(object? Original, IReadOnlyDictionary<string, object?> Values);
internal static partial class NativeTuiControls {
    private sealed class NativeRows {
        public object[] Originals { get; set; } = [];
        public TextBlock? ComputedText { get; set; }
        public DataGridDataTableDocument? Document { get; set; }
        public Action<TuiGridEdit>? Edited { get; set; }
        public System.Data.DataRowChangeEventHandler? RowChanged { get; set; }
        public DataGridDocumentView? View { get; set; }
    }

    private static readonly ConditionalWeakTable<Visual, NativeRows> RowSnapshots = [];
    internal static Visual Create(
        TuiNode node,
        object? value,
        Func<TuiNode, Visual> build,
        Action<TuiEvent> enqueue,
        Func<string, Visual?> find
    ) {
        var children = node.Children.Select(build).ToArray();
        Visual? content = null;
        Visual Content() =>
            content ??= children.Length == 0 ? new TextBlock(node.Text) : children.Length == 1 ? children[0] : new VStack(children);
        Visual visual = node.Type switch {
            "ComputedVisual" => new ComputedVisual(Content),
            "HScrollBar" => new HScrollBar(),
            "VScrollBar" => new VScrollBar(),
            "TabControl" => new TabControl([..children.Select((
                child,
                index
            ) => new TabPage(
                node.Children[index].TabTitle.Length == 0 ? node.Children[index].Id : node.Children[index].TabTitle,
                child
            ))]),
            "HSplitter" => new HSplitter(
                children.ElementAtOrDefault(0) ?? new TextBlock(""),
                children.ElementAtOrDefault(1) ?? new TextBlock("")
            ),
            "VSplitter" => new VSplitter(
                children.ElementAtOrDefault(0) ?? new TextBlock(""),
                children.ElementAtOrDefault(1) ?? new TextBlock("")
            ),
            "Collapsible" => new Collapsible(new TextBlock(node.Text), Content()),
            "Table" => new Table(),
            "SearchReplacePopup" => new SearchReplacePopup(new NativeLogSearchTarget(() => find(node.Target) as LogControl ?? throw new ArgumentException("SearchReplacePopup.Target must identify a LogControl."))),
            _ => (Visual)Activator.CreateInstance(TypeOf(node))!
        };
        if (visual is DataGridControl) {
            RowSnapshots.GetOrCreateValue(visual).Edited = edit => enqueue(new(node.Id, "Edit", edit));
        }

        if (node.Type is not ("ComputedVisual" or "TabControl" or "HSplitter" or "VSplitter" or "Collapsible"
            or "DockLayout")) {
            if (visual.GetType().GetProperty("Children")?.GetValue(visual) is ICollection<Visual> collection) {
                foreach (var child in children) {
                    collection.Add(child);
                }
            }
            else if (visual.GetType().GetProperty("Content") is { CanWrite: true } property
                && property.PropertyType == typeof(Visual)) {
                property.SetValue(visual, Content());
            }
        }

        switch (visual) {
            case ComputedVisual when children.Length == 0:
                RowSnapshots.GetOrCreateValue(visual).ComputedText = (TextBlock)Content();
                break;
            case Grid grid:
                for (int i = 0; i < children.Length; i++) {
                    grid.Cells.Add(children[i] as GridCell ?? new GridCell(children[i]) { Row = i });
                }

                break;
            case Header header:
                header.Left = children.ElementAtOrDefault(0) ?? new TextBlock(node.Text);
                header.Center = children.ElementAtOrDefault(1);
                header.Right = children.ElementAtOrDefault(2);
                break;
            case Footer footer:
                footer.Left = children.ElementAtOrDefault(0) ?? new TextBlock(node.Text);
                footer.Center = children.ElementAtOrDefault(1);
                footer.Right = children.ElementAtOrDefault(2);
                break;
            case StatusBar status:
                status.LeftText = children.ElementAtOrDefault(0) ?? new TextBlock(node.Text);
                status.RightText = children.ElementAtOrDefault(1);
                break;
            case DockLayout dock:
                dock.Top = children.ElementAtOrDefault(0);
                dock.Content = children.ElementAtOrDefault(1);
                dock.Bottom = children.ElementAtOrDefault(2);
                break;
            case Group group:
                group.TopLeftText = new TextBlock(node.Text);
                break;
            case TooltipHost tooltip:
                tooltip.TooltipContent = new TextBlock(node.Text);
                break;
            case Spinner spinner:
                spinner.Label = Content();
                break;
            case Dialog dialog:
                dialog.Title = new TextBlock(node.Text);
                dialog.IsModal = true;
                if (dialog.Content is { } body && !body.EnumerateVisualsDepthFirst().Any(child => child.Focusable)) {
                    dialog.Content = null;
                    dialog.Content = new VStack(body, new Button("Close").Click(() => dialog.Close()));
                }

                dialog.KeyDown((_, e) => {
                    if (e.Key == TerminalKey.Escape) {
                        dialog.Close();
                        e.Handled = true;
                    }
                });
                break;
            case Popup popup:
                if (popup.Content is { } popupBody
                    && !popupBody.EnumerateVisualsDepthFirst().Any(child => child.Focusable)) {
                    popup.Content = null;
                    popup.Content = new VStack(popupBody, new Button("Close").Click(() => popup.Close()));
                }

                popup.KeyDown((_, e) => {
                    if (e.Key == TerminalKey.Escape) {
                        popup.Close();
                        e.Handled = true;
                    }
                });
                break;
            case RadioButton radio:
                radio.Text = new TextBlock(node.Text);
                break;
            case Link link:
                link.Text = node.Text;
                break;
            case OptionList<string> list:
                list.ItemActivated((_, _) => enqueue(new(node.Id, "Submit", Selection(list))));
                break;
            case PromptEditor prompt:
                prompt.Accepted((_, e) => enqueue(new(node.Id, "Submit", e.Text)));
                break;
            case MenuBar menu:
                foreach (string item in node.Items) {
                    menu.Items.Add(new MenuItem(item, () => enqueue(new(node.Id, "Click", item))));
                }

                break;
            case CommandBar or CommandPalette:
                foreach (string item in node.Items) {
                    visual.Commands.Add(new Command {
                        Id = node.Id +
                        "." + visual.Commands.Count,
                        LabelMarkup = item,
                        Presentation = CommandPresentation.CommandBar | CommandPresentation.CommandPalette,
                        Execute = _ => enqueue(new(node.Id, "Click", item))
                    });
                }

                break;
        }

        if (visual is not SearchReplacePopup) {
            Configure(visual, node);
        }

        Update(visual, node, value ?? (node.Items.Length > 0 ? node.Items : null));
        return visual;
    }

    private static object? Unwrap(object? value) =>
        value is PSObject wrapper && wrapper.BaseObject is not PSCustomObject ? wrapper.BaseObject : value;
    private static object?[] Items(object? value) {
        value = Unwrap(value);
        if (value is null) {
            return [];
        }

        if (value is string || value is IDictionary) {
            return [value];
        }

        object?[] result = value is IEnumerable enumerable ? [.. enumerable.Cast<object?>().Take(10001)] : [value];
        if (result.Length > 10000) {
            throw new ArgumentException("Native widget data allows at most 10,000 items per snapshot.");
        }

        return result;
    }

    private static string Text(object? value) =>
        Convert.ToString(Unwrap(value), CultureInfo.InvariantCulture) ?? "";
    private static double Number(object? value) =>
        Convert.ToDouble(Unwrap(value), CultureInfo.InvariantCulture);
    internal static void Update(Visual visual, TuiNode node, object? value) {
        value = Unwrap(value);
        switch (visual) {
            case Markup markup:
                markup.Text = value is null ? node.Text : Text(value);
                break;
            case Paragraph paragraph:
                paragraph.Text = value is null ? node.Text : Text(value);
                break;
            case TextBlock text:
                text.Text = value is null ? node.Text : Text(value);
                break;
            case TextFiglet figlet:
                figlet.Text = value is null ? node.Text : Text(value);
                break;
            case MaskedInput input:
                input.Value = value is null ? node.Text : Text(value);
                break;
            case PromptEditor prompt:
                prompt.Text = value is null ? node.Text : Text(value);
                break;
            case NumberBox<double> number:
                if (value is not null) {
                    number.Value = Number(value);
                }

                break;
            case Slider<double> slider:
                if (value is not null) {
                    slider.Value = Number(value);
                }

                break;
            case Switch toggle:
                if (value is not null) {
                    toggle.IsOn = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                }

                break;
            case RadioButton radio:
                if (value is not null) {
                    radio.IsChecked = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                }

                break;
            case ColorPicker picker:
                if (value is not null) {
                    picker.Value = value is Color color ? color : (Color)ConvertValue(JsonSerializer.SerializeToElement(Text(value)), typeof(Color))!;
                }

                break;
            case Link link:
                if (value is not null) {
                    link.Uri = Text(value);
                }

                break;
            case ProgressBar progress:
                if (value is not null) {
                    progress.Value = Math.Clamp(Number(value), 0, 1);
                }

                break;
            case ContentSwitcher content:
                if (value is not null) {
                    content.SelectedIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                break;
            case TabControl tabs:
                if (value is not null) {
                    tabs.SelectedIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                break;
            case Collapsible section:
                if (value is not null) {
                    section.IsExpanded = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                }

                break;
            case HScrollBar scroll:
                if (value is not null) {
                    scroll.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                break;
            case VScrollBar scroll:
                if (value is not null) {
                    scroll.Value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }

                break;
            case ComputedVisual computed:
                if (RowSnapshots.TryGetValue(computed, out var computedRows) && computedRows.ComputedText is { } label) {
                    label.Text = value is null ? node.Text : Text(value);
                }

                break;
            case EnumSelect<DayOfWeek> select:
                if (value is not null) {
                    select.Value = Enum.Parse<DayOfWeek>(Text(value), true);
                }

                break;
            case DataPresenter<object> presenter:
                presenter.Value = value ?? node.Text;
                break;
            case LogControl logs:
                logs.Clear();
                foreach (var line in Items(value)) {
                    logs.AppendLine(Text(line));
                }

                break;
            case DocumentFlow flow:
                flow.Items.Clear();
                foreach (var item in Items(value)) {
                    flow.Items.Add(new() { Content = new FlowDocument().AddParagraph(Text(item)) });
                }

                break;
            case BarChart bars:
                bars.Title = new TextBlock(node.Text);
                bars.Items.Clear();
                foreach (var item in Items(value)) {
                    bars.Items.Add(new(
                        new TextBlock(Text(Member(item, "Label") ?? Member(item, "Name"))),
                        Number(Member(item, "Value") ?? item)
                    ));
                }

                break;
            case BreakdownChart breakdown:
                breakdown.Title = new TextBlock(node.Text);
                breakdown.Segments.Clear();
                foreach (var item in Items(value)) {
                    breakdown.Segments.Add(new(
                        Number(Member(item, "Value") ?? item),
                        new TextBlock(Text(Member(item, "Label") ?? Member(item, "Name")))
                    ));
                }

                break;
            case ProgressTaskGroup group:
                group.Tasks.Clear();
                foreach (var item in Items(value)) {
                    group.Tasks.Add(new(new TextBlock(Text(Member(item, "Label") ?? Member(item, "Name")))) {
                        Value = Number(Member(item, "Value") ?? 0),
                        Maximum = Number(Member(item, "Maximum") ?? 100)
                    });
                }

                break;
            case LineChart line:
                line.Values.Clear();
                foreach (var item in Items(value)) {
                    line.Values.Add(Number(item));
                }

                break;
            case Sparkline sparkline:
                sparkline.Values.Clear();
                foreach (var item in Items(value)) {
                    sparkline.Values.Add(Number(item));
                }

                break;
            case ListBox<string> list:
                SetItems(list.Items, value);
                break;
            case OptionList<string> list:
                SetItems(list.Items, value);
                break;
            case RadioButtonList<string> list:
                SetItems(list.Items, value);
                break;
            case SelectionList<string> list:
                list.Items.Clear();
                list.Checked.Clear();
                foreach (var item in Items(value)) {
                    list.AddItem(Text(item));
                }

                break;
            case Table table:
                table.HeaderCells.Clear();
                table.RowCells.Clear();
                if (value is null) {
                    break;
                }

                var model = TuiTableData.Create(value, node);
                foreach (string column in model.Columns) {
                    table.HeaderCells.Add(new TextBlock(column));
                }

                foreach (var row in model.Rows) {
                    var cells = new VisualList<Visual>(table, "RowCells");
                    foreach (string cell in row.Cells) {
                        cells.Add(new TextBlock(cell));
                    }

                    table.RowCells.Add(cells);
                }

                break;
            case DataGridControl grid:
                grid.ReadOnly = !node.Options.ContainsKey("ReadOnly") || node.Options["ReadOnly"].GetBoolean();
                var data = RowSnapshots.GetOrCreateValue(grid);
                DisposeDocument(data);
                data.Document = DataDocument(value ?? Array.Empty<object>(), node, out var originals);
                grid.Document = data.Document;
                data.View = new DataGridDocumentView(data.Document);
                grid.View = data.View;
                grid.Columns.Clear();
                for (int column = 0; column < data.Document.CurrentSnapshot.ColumnCount; column++) {
                    grid.Columns.Add(GridColumn(data.Document.CurrentSnapshot.GetColumn(column)));
                }

                data.Originals = originals;
                data.RowChanged = (_, e) => {
                    int index = e.Row.Table.Rows.IndexOf(e.Row);
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Data.DataColumn column in e.Row.Table.Columns) {
                        values.Add(column.ColumnName, e.Row[column] is DBNull ? null : e.Row[column]);
                    }

                    data.Edited?.Invoke(new((uint)index < (uint)data.Originals.Length ? data.Originals[index] : null, values));
                };
                data.Document.Table.RowChanged += data.RowChanged;
                break;
            case TreeView tree:
                tree.Roots.Clear();
                int count = 0;
                var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
                foreach (var item in Items(value)) {
                    tree.Roots.Add(Tree(item, 0, ref count, ancestors));
                }

                break;
            case Canvas canvas:
                string[] lines = (value is null ? node.Text : Text(value)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                canvas.Painter(context => {
                    for (int y = 0; y < Math.Min(lines.Length, canvas.Bounds.Height); y++) {
                        context.WriteText(0, y, lines[y]);
                    }
                });
                break;
            default:
                if (value is not null && visual.GetType().IsGenericType
                    && visual.GetType().GetGenericTypeDefinition() == typeof(EnumSelect<>)) {
                    var enumType = visual.GetType().GetGenericArguments()[0];
                    visual.GetType().GetProperty("Value")!.SetValue(visual, Enum.Parse(enumType, Text(value), true));
                }
                else if (visual.GetType().GetProperty("Text") is { CanWrite: true, PropertyType: var textType } property
                    && textType == typeof(string)) {
                    property.SetValue(visual, value is null ? node.Text : Text(value));
                }

                break;
        }
    }

    private static object? Member(object? value, string name) {
        value = Unwrap(value);
        return value is IDictionary dictionary ? dictionary[name] : value is null ? null : PSObject.AsPSObject(value).Properties[name]?.Value;
    }

    private static void SetItems(BindableList<string> target, object? value) {
        target.Clear();
        foreach (var item in Items(value)) {
            target.Add(Text(item));
        }
    }

    private static DataGridDataTableDocument DataDocument(object value, TuiNode node, out object[] originals) {
        var snapshot = TuiTableData.Create(value, node);
        originals = [.. snapshot.Rows.Select(row => row.Original)];
        var table = new System.Data.DataTable {
            Locale = CultureInfo.InvariantCulture
        };
        for (int column = 0; column < snapshot.Columns.Length; column++) {
            var types = snapshot.Rows.Select(row => Unwrap(row.Values[column])?.GetType()).Where(type => type is not null).Distinct().Take(2).ToArray();
            Type type = types.Length == 1 && snapshot.Rows.All(row => Unwrap(row.Values[column]) is not null)
                && (types[0]!.IsPrimitive || types[0] == typeof(decimal) || types[0] == typeof(string)
                || types[0] == typeof(DateTime)
                || types[0] == typeof(DateTimeOffset)) ? types[0]! : typeof(object);
            table.Columns.Add(snapshot.Columns[column], type);
        }

        foreach (var row in snapshot.Rows) {
            table.Rows.Add([.. row.Values.Select(value => Unwrap(value) ?? DBNull.Value)]);
        }

        return new DataGridDataTableDocument(table);
    }

    private static DataGridColumn GridColumn(DataGridColumnInfo info) =>
        Type.GetTypeCode(info.ValueType) switch {
            TypeCode.Boolean => GridColumn<bool>(info),
            TypeCode.Byte => GridColumn<byte>(info),
            TypeCode.Int16 => GridColumn<short>(info),
            TypeCode.Int32 => GridColumn<int>(info),
            TypeCode.Int64 => GridColumn<long>(info),
            TypeCode.Single => GridColumn<float>(info),
            TypeCode.Double => GridColumn<double>(info),
            TypeCode.Decimal => GridColumn<decimal>(info),
            TypeCode.DateTime => GridColumn<DateTime>(info),
            TypeCode.String => GridColumn<string>(info),
            _ => GridColumn<object>(info)
        };
    private static DataGridColumn<T> GridColumn<T>(DataGridColumnInfo info) =>
        new() {
            Key = info.Key,
            Header = new TextBlock(info.HeaderText),
            ReadOnly = info.ReadOnly,
            Sortable = true,
            TypedValueAccessor = new BindingAccessor<T>(
                info.Key,
                row => info.Accessor.GetValueAsObject(row) is T value ? value : default!,
                (
                    row,
                    value
                ) => info.Accessor.SetValueAsObject(row, value)
            )
        };
    private static TreeNode Tree(object? value, int depth, ref int count, HashSet<object> ancestors) {
        if (++count > 10000 || depth > 24) {
            throw new ArgumentException("TreeView allows at most 10,000 nodes and 24 levels.");
        }

        object? identity = Unwrap(value);
        if (identity is not null && !ancestors.Add(identity)) {
            throw new ArgumentException("TreeView data contains a cycle.");
        }

        object? label = Member(value, "Text") ?? Member(value, "Name") ?? value;
        var node = new TreeNode(new TextBlock(Text(label))) {
            Data = value
        };
        foreach (var child in Items(Member(value, "Children"))) {
            node.Children.Add(Tree(child, depth + 1, ref count, ancestors));
        }

        if (identity is not null) {
            ancestors.Remove(identity);
        }

        return node;
    }

    internal static bool IsInput(Visual visual) =>
        visual is MaskedInput or PromptEditor or NumberBox<double> or Slider<double> or Switch
        or RadioButton
        or ColorPicker
        or HScrollBar
        or VScrollBar
        or ContentSwitcher
        or TabControl
        or Collapsible
        || visual.GetType().IsGenericType
        && visual.GetType().GetGenericTypeDefinition() == typeof(EnumSelect<>);
    internal static object? Selection(Visual visual) =>
        visual switch {
            TreeView tree => tree.SelectedNode?.Data,
            DataGridControl grid => SelectedGridRow(grid),
            SelectionList<string> list => list.Items.Where((
                _,
                index
            ) => index < list.Checked.Count && list.Checked[index]).ToArray(),
            _ => visual.GetType().GetProperty("SelectedIndex")?.GetValue(visual) is int index
                && visual.GetType().GetProperty("Items")?.GetValue(visual) is IEnumerable items ? items.Cast<object?>().ElementAtOrDefault(index) : null
        };
    private static object? SelectedGridRow(DataGridControl grid) {
        var view = grid.View?.CurrentSnapshot;
        int index = view is not null && (uint)grid.SelectedRow < (uint)view.RowCount ? view.MapRowToDocument(grid.SelectedRow) : grid.SelectedRow;
        return RowSnapshots.TryGetValue(grid, out var rows) && (uint)index < (uint)rows.Originals.Length ? rows.Originals[index] : null;
    }

    internal static void Dispose(Visual visual) {
        if (RowSnapshots.TryGetValue(visual, out var data)) {
            DisposeDocument(data);
            RowSnapshots.Remove(visual);
        }
    }

    private static void DisposeDocument(NativeRows data) {
        if (data.Document is not { } document) {
            return;
        }

        if (data.RowChanged is not null) {
            document.Table.RowChanged -= data.RowChanged;
        }

        data.View?.Dispose();
        document.Dispose();
        document.Table.Dispose();
        data.Document = null;
        data.RowChanged = null;
        data.View = null;
    }

    internal static object? Read(Visual visual) =>
        visual switch {
            MaskedInput input => input.Value,
            PromptEditor prompt => prompt.Text,
            NumberBox<double> number => number.Value,
            Slider<double> slider => slider.Value,
            Switch toggle => toggle.IsOn,
            RadioButton radio => radio.IsChecked,
            ColorPicker picker => (picker.Value.IsRgbLike ? picker.Value : picker.Value.ToRgb()).ToHexString(picker.Value.A != byte.MaxValue),
            EnumSelect<DayOfWeek> select => select.Value.ToString(),
            HScrollBar scroll => scroll.Value,
            VScrollBar scroll => scroll.Value,
            TabControl tabs => tabs.SelectedIndex,
            ContentSwitcher content => content.SelectedIndex,
            Collapsible section => section.IsExpanded,
            _ => visual.GetType().IsGenericType
                && visual.GetType().GetGenericTypeDefinition() == typeof(EnumSelect<>) ? visual.GetType().GetProperty("Value")!.GetValue(visual)?.ToString() : null
        };
}
