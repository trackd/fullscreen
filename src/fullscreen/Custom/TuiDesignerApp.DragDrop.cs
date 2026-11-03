using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;
using IOPath = System.IO.Path;

namespace fullscreen.Custom;

public sealed partial class TuiDesignerApp {
    private sealed class PaletteButton : Button {
        public PaletteButton(string type) : base(new TextBlock(type) { IsSelectable = false }) {
            SetStyle(ButtonStyle.Default with { Padding = new(0) });
        }

        protected override void ArrangeCore(in Rectangle finalRect) =>
            Content?.Arrange(finalRect);
    }

    private readonly List<(string Type, Visual Visual)> _palette = [];
    private readonly List<(string Category, Visual Visual)> _paletteCategories = [];
    private readonly TextBox _paletteSearch = new() {
        Placeholder = "Search controls"
    };
    private readonly TextBlock _paletteHeading = new();
    private readonly TextBlock _paletteEmpty = new("No matching controls");
    private string _paletteQuery = "";
    private Visual _paletteViewport = null!;
    private string? _dragType, _dragId;
    private int _dragX, _dragY;
    private bool _dragging;
    private string _exportPath = "MyTui.ps1";
    private bool _linkedLayout = true;
    private Visual CreatePalette() {
        var stack = new VStack();
        foreach (var category in TuiControlCatalog.All.GroupBy(control => control.Category).OrderBy(group => group.Key)) {
            var heading = new TextBlock(category.Key.ToUpperInvariant()) {
                HorizontalAlignment = Align.Stretch,
                IsSelectable = false
            };
            _paletteCategories.Add((category.Key, heading));
            stack.Children.Add(heading);
            foreach (var control in category.OrderBy(control => control.Name)) {
                string type = control.Name;
                var button = new PaletteButton(type).Click(() => Add(type));
                button.MinHeight = button.MaxHeight = 1;
                button.HorizontalAlignment = Align.Stretch;
                var item = new TooltipHost(button) {
                    TooltipContent = new Paragraph(type + "\n" + control.Description) {
                        Wrap = true,
                        MaxWidth = 50
                    },
                    HorizontalAlignment = Align.Stretch
                };
                _palette.Add((type, item));
                stack.Children.Add(item);
            }
        }

        _paletteEmpty.IsVisible = false;
        _paletteHeading.Text = "CONTROLS · " + _palette.Count;
        ApplyPaletteColors();
        stack.Children.Add(_paletteEmpty);
        _paletteSearch.KeyDown((_, e) => {
            if (e.Key != TerminalKey.Enter) {
                return;
            }

            UpdatePalette();
            var match = _palette.FirstOrDefault(item => item.Visual.IsVisible && item.Type.Equals(_paletteQuery, StringComparison.OrdinalIgnoreCase));
            if (match.Visual is null) {
                match = _palette.FirstOrDefault(item => item.Visual.IsVisible);
            }

            if (match.Visual is not null) {
                Add(match.Type);
            }

            e.Handled = true;
        });
        return _paletteViewport = new ScrollViewer(stack) {
            MinHeight = 3,
            VerticalAlignment = Align.Stretch,
            HorizontalScrollEnabled = false
        };
    }

    private void ApplyPaletteColors() {
        foreach (var (category, heading) in _paletteCategories) {
            Color color = CategoryColor(category);
            heading.SetStyle(new TextBlockStyle {
                Background = color,
                Foreground = _theme.Background,
                FillBackground = true,
                TextStyle = TextStyle.Bold
            });
        }

        foreach (var (type, visual) in _palette) {
            Color color = CategoryColor(TuiControlCatalog.Describe(type).Category);
            var button = (Button)((TooltipHost)visual).Content!;
            var normal = _theme.Body.WithForeground(color).WithBackground(Color.Mix(_theme.Background, color, 0.08f));
            var active = normal.WithBackground(Color.Mix(_theme.Background, color, 0.22f));
            button.SetStyle(ButtonStyle.Default with {
                Padding = new(0),
                Normal = normal,
                Hovered = active,
                Focused = active.WithTextStyle(TextStyle.Bold | TextStyle.Underline),
                Pressed = active.WithTextStyle(TextStyle.Bold)
            });
            button.Content!.SetStyle(new TextBlockStyle { Foreground = color });
        }
    }

    private Color CategoryColor(string category) {
        string family = category.StartsWith("Native ", StringComparison.Ordinal) ? category[7..] : category;
        return family.ToLowerInvariant() switch {
            "application" or "commands" => _theme.Accent,
            "data" => _theme.Success,
            "display" => _theme.Foreground,
            "input" => _theme.Warning,
            "layout" => _theme.Purple,
            "overlay" => _theme.Error,
            _ => _theme.Accent
        };
    }

    private void UpdatePalette() {
        string query = _paletteSearch.Text?.Trim() ?? "";
        if (query == _paletteQuery) {
            return;
        }

        _paletteQuery = query;
        foreach (var item in _palette) {
            var info = TuiControlCatalog.Describe(item.Type);
            item.Visual.IsVisible = item.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
                || info.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || info.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var category in _paletteCategories) {
            category.Visual.IsVisible = _palette.Any(item => item.Visual.IsVisible && TuiControlCatalog.Describe(item.Type).Category == category.Category);
        }

        _paletteEmpty.IsVisible = !_palette.Any(item => item.Visual.IsVisible);
        int matches = _palette.Count(item => item.Visual.IsVisible);
        _paletteHeading.Text = query.Length == 0 ? "CONTROLS · " + _palette.Count : $"FOUND · {matches}/{_palette.Count}";
        ((ScrollViewer)_paletteViewport).VerticalOffset = 0;
    }

    private static bool Inside(Visual visual, int x, int y) {
        for (Visual? current = visual; current is not null; current = current.Parent) {
            if (!current.IsVisible) {
                return false;
            }
        }

        return x >= visual.Bounds.X && y >= visual.Bounds.Y && x < visual.Bounds.Right && y < visual.Bounds.Bottom;
    }

    private static bool BelongsTo(object? source, Visual ancestor) {
        for (var visual = source as Visual; visual is not null; visual = visual.Parent) {
            if (ReferenceEquals(visual, ancestor)) {
                return true;
            }
        }

        return false;
    }

    private Row? HitPreview(
        int x,
        int y
    ) =>
        !Inside(_previewHost, x, y) ? null : _rows.Where(row => _preview.Controls.TryGetValue(row.Node.Id, out var control) && Inside(control, x, y)).OrderByDescending(row => row.Depth).FirstOrDefault() ?? _rows[0];
    private bool IsCanvasSource(object? source) =>
        BelongsTo(source, _previewHost) || ReferenceEquals(source, _panes.Cells[0]);
    private void InstallDragAndDrop() {
        Root.PointerPressed((_, e) => {
            if (e.RoutingPhase == RoutingPhase.Preview && e.Button == TerminalMouseButton.Right
                && IsCanvasSource(e.OriginalSource)
                && HitPreview(e.UiX, e.UiY) is { } context) {
                int selection = _rows.IndexOf(context);
                if (ApplyProperties()) {
                    _selection = selection;
                    _tree.SelectedIndex = _selection;
                    SelectionChanged();
                    LoadFields();
                    _markers?.Refresh();
                    ShowActions(e.UiX, e.UiY);
                }

                e.Handled = true;
                return;
            }

            if (e.RoutingPhase != RoutingPhase.Preview || e.Button != TerminalMouseButton.Left) {
                return;
            }

            _dragX = e.UiX;
            _dragY = e.UiY;
            _dragging = false;
            _dragType = _dragId = null;
            if (Inside(_paletteViewport, e.UiX, e.UiY)) {
                var item = _palette.FirstOrDefault(item => Inside(item.Visual, e.UiX, e.UiY));
                if (item.Visual is not null) {
                    _dragType = item.Type;
                    _status.Text = "Drag " + item.Type + " onto a container or beside a control";
                    e.Handled = true;
                }
            }
            else if (IsCanvasSource(e.OriginalSource) && HitPreview(e.UiX, e.UiY) is { } hit) {
                int selection = _rows.IndexOf(hit);
                if (!ApplyProperties()) {
                    e.Handled = true;
                    return;
                }

                _selection = selection;
                _tree.SelectedIndex = _selection;
                SelectionChanged();
                LoadFields();
                _markers?.Refresh();
                _dragId = _rows[_selection].Node.Id;
                _status.Text = "Selected " + _dragId + " · drag to move · edit properties at left";
                e.Handled = true;
            }
        });
        Root.PointerMoved((_, e) => {
            if (e.RoutingPhase != RoutingPhase.Preview || _dragType is null && _dragId is null) {
                return;
            }

            if (Math.Abs(e.UiX - _dragX) + Math.Abs(e.UiY - _dragY) >= 2) {
                _dragging = true;
            }

            if (_dragging) {
                var target = HitPreview(e.UiX, e.UiY);
                _dropId = target?.Node.Id;
                if (target is not null) {
                    var visual = _preview.Controls[target.Node.Id];
                    _dropAfter = target.Parent?.Type == "HStack" ? e.UiX >= visual.Bounds.X + visual.Bounds.Width / 2 : e.UiY >= visual.Bounds.Y + visual.Bounds.Height / 2;
                }

                _status.Text = target is not null ? "Drop " + (_dragType ?? _dragId) +
                    " at " + target.Node.Id + (target.Node.IsContainer ? " · inside" : _dropAfter ? " · after" : " · before") : "Move over the preview · release elsewhere to cancel";
                _markers?.Refresh();
            }

            e.Handled = true;
        });
        Root.PointerReleased((_, e) => {
            if (e.RoutingPhase != RoutingPhase.Preview || e.Button != TerminalMouseButton.Left) {
                return;
            }

            bool moved = _dragging || Math.Abs(e.UiX - _dragX) + Math.Abs(e.UiY - _dragY) >= 2;
            if (moved && HitPreview(e.UiX, e.UiY) is { } target) {
                var visual = _preview.Controls[target.Node.Id];
                bool after = target.Parent?.Type == "HStack" ? e.UiX >= visual.Bounds.X + visual.Bounds.Width / 2 : e.UiY >= visual.Bounds.Y + visual.Bounds.Height / 2;
                Place(_dragType, _dragId, target.Node.Id, after);
            }
            else if (!moved && _dragType is not null) {
                Add(_dragType);
            }

            if (_dragType is not null || _dragId is not null) {
                e.Handled = true;
            }

            _dragType = _dragId = _dropId = null;
            _dragging = false;
            _markers?.Refresh();
        });
    }

    /// <summary>Drop inserts into containers or before/after a sibling. Invalid drops leave the document intact.</summary>
    /// <param name="type">The control catalog name to insert.</param>
    /// <param name="movingId">The existing control identifier to move, or null when adding a new control.</param>
    /// <param name="targetId">The identifier of the control used as the placement target.</param>
    /// <param name="after">Whether to place the control after the target rather than before it. Defaults to
    /// true.</param>
    /// <returns>True when the requested placement succeeds.</returns>
    public bool Place(string? type, string? movingId, string targetId, bool after = true) {
        if (!ApplyProperties()) {
            return false;
        }

        try {
            var candidate = TuiDocument.FromJson(Document.ToJson());
            var rows = Flatten(candidate.Root);
            var target = rows.First(row => row.Node.Id == targetId);
            var parent = target.Node.IsContainer ? target.Node : target.Parent ?? throw new ArgumentException("Drop onto a container.");
            int index = target.Node.IsContainer ? parent.Children.Count : parent.Children.IndexOf(target.Node) + (after ? 1 : 0);
            TuiNode node;
            if (movingId is not null) {
                var source = rows.First(row => row.Node.Id == movingId);
                if (source.Parent is null || Flatten(source.Node).Any(row => row.Node == parent)) {
                    throw new ArgumentException("Cannot move the root or place a container inside itself.");
                }

                node = source.Node;
                int old = source.Parent.Children.IndexOf(node);
                source.Parent.Children.Remove(node);
                if (source.Parent == parent && old < index) {
                    index--;
                }
            }
            else {
                if (type is null || !TuiDocument.ControlTypes.Contains(type)) {
                    throw new ArgumentException("Choose a control from the palette.");
                }

                node = NewControl(type, rows);
            }

            parent.Children.Insert(index, node);
            candidate.Validate();
            Remember();
            Document = candidate;
            _dirty = true;
            Rebuild(Flatten(Document.Root).FindIndex(row => row.Node.Id == node.Id));
            _status.Text = "Placed " + node.Id + " · edit Bind and styling, then export .ps1 to wire behavior";
            return true;
        }
        catch (ArgumentException error) {
            _status.Text = "Drop cancelled · " + error.Message;
            return false;
        }
    }

    private void ExportScript() {
        if (!ApplyProperties() || _exportScript is null) {
            return;
        }

        var input = new TextBox(_exportPath);
        var error = new Paragraph("") {
            Wrap = true,
            IsVisible = false
        };
        Dialog? dialog = null;
        var linked = new CheckBox("For .ps1: separate PSD1 layout (data paths are always linked)", _linkedLayout);
        dialog = new Dialog {
            Title = new TextBlock("Export runnable PowerShell"),
            IsModal = true,
            Width = 70,
            Content = new VStack(new Paragraph("Export .ps1, .psd1 or .json. Data files get a companion .ps1 if missing. Existing scripts are never overwritten.") { Wrap = true }, input, linked, error, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Export").Click(() => {
                string path = input.Text ?? "";
                _exportPath = path;
                _linkedLayout = linked.IsChecked;
                void Write() {
                    try {
                        _exportScript(Document, path, linked.IsChecked);
                        _status.Text = "Exported " + path + " · edit Handler / OnTick in .ps1; existing scripts preserved";
                        dialog!.Close();
                    }
                    catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException) {
                        _status.Text = "Export failed · " + failure.Message;
                        error.Text = _status.Text;
                        error.IsVisible = true;
                        Root.App?.Focus(input);
                    }
                }

                try {
                    var (scriptPath, layoutPath) = TuiScriptExporter.Paths(path, linked.IsChecked);
                    if (layoutPath is not null && _exists(layoutPath)) {
                        Confirm(
                            "Replace layout '" + layoutPath +
                            "'? Script '" + scriptPath +
                            "' keeps its existing handlers.",
                            Write
                        );
                    }
                    else {
                        Write();
                    }
                }
                catch (ArgumentException failure) {
                    error.Text = failure.Message;
                    error.IsVisible = true;
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
}
