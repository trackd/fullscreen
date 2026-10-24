using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Charts;
/// <summary>Signed, object-backed plots rendered in terminal cells on every supported terminal.</summary>
internal sealed partial class ObjectChartSurface : Themes.ThemedSurface {
    private ObjectChartData _data;
    internal Chart Plot { get; } = new() {
        ShowAxes = true
    };
    internal ChartDataSeries[] Series { get; private set; } = [];
    internal ChartPoint[] ValidPoints { get; private set; } = [];

    internal static readonly string[] MenuLabels = ["[X ▾]", "[Y ▾]", "[G Chart ▾]", "[F Filter ▾]", "[T Data]"];
    internal static int MenuColumn(int index) =>
        MenuLabels.Take(index).Sum(label => label.Length + 3);
    private readonly Button[] _menuButtons;
    private Themes.ThemePalette? _menuPalette;
    internal Rectangle? MenuAnchor { get; private set; }
    internal Rectangle PlotBounds => Plot.PlotBounds;
    internal string Title { get; set; } = "Chart";
    internal GraphKind GraphType { get; set; }
    internal string? XProperty { get; private set; }
    internal int SelectedIndex { get; private set; }
    internal int[] VisibleIndices { get; private set; } = [];
    internal string FilterLabel { get; private set; } = "All";

    private string _message = "";
    internal bool ExitRequested { get; private set; }
    internal bool PassThru { get; set; }
    internal bool Accepted { get; private set; }

    internal Action? ChooseX, ChooseY, ChooseType, ChooseFilter, Inspect, ShowData;
    [Bindable]
    private partial int Revision { get; set; }

    internal ObjectChartSurface(ObjectChartData data) {
        _data = data;
        Focusable = true;
        IsTabStop = true;
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        AttachChild(Plot);
        _menuButtons = [.. MenuLabels.Select((
            label,
            index
        ) => new Button(label) {
            MinHeight = 1,
            MaxHeight = 1
        }.Click(() => {
            if (index == 4) {
                ShowData?.Invoke();
            }
            else {
                OpenMenu(index);
            }
        }))];
        foreach (var button in _menuButtons) {
            AttachChild(button);
        }
    }

    internal void Update(ChartDataSeries[] series, string? xProperty, int[] indices, string filterLabel) {
        Series = series;
        Plot.GraphKind = GraphType;
        Plot.SetData(series);
        Plot.Selection = null;
        VisibleIndices = indices;
        FilterLabel = filterLabel;
        if (!indices.Contains(SelectedIndex)) {
            SelectedIndex = indices.FirstOrDefault(-1);
        }

        ValidPoints = [.. series.SelectMany(item => item.Points).Where(point => point.Y.HasValue)];
        Plot.IsVisible = ValidPoints.Length > 0;
        XProperty = xProperty;
        Revision++;
    }

    private void Move(int delta) {
        if (VisibleIndices.Length == 0) {
            return;
        }

        int position = Array.IndexOf(VisibleIndices, SelectedIndex);
        SelectedIndex = VisibleIndices[(int)Math.Clamp((long)position + delta, 0, VisibleIndices.Length - 1)];
        Plot.SelectedIndex = SelectedIndex;
        Plot.Selection = null;
    }

    internal void ReplaceSource(ObjectChartData data) {
        _data = data;
        Revision++;
    }

    internal void SelectOriginal(int index) {
        SelectedIndex = index;
        Revision++;
    }

    internal void ShowMessage(string message) {
        _message = message;
        Revision++;
    }

    internal void Cancel() {
        Accepted = false;
        ExitRequested = true;
        Revision++;
    }

    private void OpenMenu(int index, Rectangle? anchor = null) {
        MenuAnchor = anchor ?? new Rectangle(Bounds.X + MenuColumn(index), Bounds.Y, 1, 1);
        try {
            (index switch {
                0 => ChooseX,
                1 => ChooseY,
                2 => ChooseType,
                _ => ChooseFilter
            })?.Invoke();
        }
        finally {
            MenuAnchor = null;
        }
    }

    protected override void OnMinimalChanged() =>
        Revision++;
    protected override int ChildrenCount => 1 + _menuButtons.Length;

    protected override Visual GetChild(int index) =>
        index == 0 ? Plot : index > 0 && index <= _menuButtons.Length ? _menuButtons[index - 1] : throw new ArgumentOutOfRangeException(nameof(index));
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = Revision;
        if (!ReferenceEquals(_menuPalette, Palette)) {
            _menuPalette = Palette;
            var normal = Palette.AccentStyle.WithBackground(Color.Mix(Palette.Background, Palette.Accent, .18f)) | TextStyle.Bold;
            foreach (var button in _menuButtons) {
                button.SetStyle(ButtonStyle.Default with {
                    Padding = new(1, 0, 1, 0),
                    ShowBorder = false,
                    Normal = normal,
                    Hovered = Palette.Body.WithBackground(Palette.Accent).WithForeground(Palette.Background) | TextStyle.Bold,
                    Pressed = Palette.Selected | TextStyle.Bold,
                    Focused = normal | TextStyle.Underline
                });
            }
        }

        for (int index = 0; index < _menuButtons.Length; index++) {
            _menuButtons[index].Measure(new Size(MenuLabels[index].Length + 2, 1));
        }

        Plot.Measure(new Size(constraints.MaxWidth, Math.Max(0, constraints.MaxHeight - (Minimal ? 2 : 3))));
        return SizeHints.Flex(new(0, 0), new(100, 30), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    protected override void ArrangeCore(in Rectangle finalRect) {
        Plot.Arrange(new(
            finalRect.X,
            finalRect.Y + 1,
            finalRect.Width,
            Math.Max(0, finalRect.Height - (Minimal ? 2 : 3))
        ));
        for (int index = 0; index < _menuButtons.Length; index++) {
            int column = MenuColumn(index);
            int width = Math.Clamp(finalRect.Width - column, 0, MenuLabels[index].Length + 2);
            _menuButtons[index].Arrange(new(finalRect.X + column, finalRect.Y, width, width > 0 ? 1 : 0));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        switch (e.Key) {
            case TerminalKey.Escape:
                Accepted = false;
                ExitRequested = true;
                break;
            case TerminalKey.F10:
                Accepted = true;
                ExitRequested = true;
                break;
            case TerminalKey.F2:
                OpenMenu(0);
                break;
            case TerminalKey.F3:
                OpenMenu(1);
                break;
            case TerminalKey.F5:
                OpenMenu(2);
                break;
            case TerminalKey.F6:
                OpenMenu(3);
                break;
            case TerminalKey.Enter:
                Inspect?.Invoke();
                break;
            case TerminalKey.Left:
                Move(-1);
                break;
            case TerminalKey.Right:
                Move(1);
                break;
            case TerminalKey.Home:
                Move(-int.MaxValue);
                break;
            case TerminalKey.End:
                Move(int.MaxValue);
                break;
            default:
                return;
        }

        e.Handled = true;
        Revision++;
    }

    protected override void OnTextInput(TextInputEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        switch (e.Text.ToLowerInvariant()) {
            case "x":
                OpenMenu(0);
                break;
            case "y":
                OpenMenu(1);
                break;
            case "g":
                OpenMenu(2);
                break;
            case "f":
                OpenMenu(3);
                break;
            case "d":
                Inspect?.Invoke();
                break;
            case "t":
                ShowData?.Invoke();
                break;
            case "q":
                Accepted = true;
                ExitRequested = true;
                break;
            default:
                return;
        }

        e.Handled = true;
        Revision++;
    }

    protected override void OnPointerPressed(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        int x = e.UiX - Bounds.X, y = e.UiY - Bounds.Y;
        if (y == 0) {
            return;
        }

        if (e.Button == TerminalMouseButton.Right) {
            OpenMenu(2, new(e.UiX, e.UiY, 1, 1));
        }
        else if (e.Button == TerminalMouseButton.Left && e.UiX >= PlotBounds.X && e.UiX < PlotBounds.Right
            && e.UiY >= PlotBounds.Y
            && e.UiY < PlotBounds.Bottom
            && ValidPoints is { Length: > 0 } points) {
            SelectedIndex = Plot.Pick(e.UiX, e.UiY);
            Plot.SelectedIndex = SelectedIndex;
            if (e.ClickCount >= 2) {
                Inspect?.Invoke();
            }
        }

        Revision++;
        e.Handled = true;
    }

    private void Write(CellBuffer buffer, int x, int y, string text, Style style, int width = int.MaxValue) {
        if (x < 0 || y < 0 || x >= Bounds.Width || y >= Bounds.Height || width <= 0) {
            return;
        }

        buffer.PushClip(new(Bounds.X + x, Bounds.Y + y, Math.Min(width, Bounds.Width - x), 1));
        buffer.WriteText(Bounds.X + x, Bounds.Y + y, Tables.TableModel.Text(text), style);
        buffer.PopClip();
    }

    private Style Ink(int index) =>
        Palette.Body.WithForeground((index % 5) switch {
            0 => Palette.Accent,
            1 => Palette.Success,
            2 => Palette.Warning,
            3 => Palette.Purple,
            _ => Palette.Error
        });
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        int titleColumn = MenuColumn(MenuLabels.Length);
        Write(buffer, titleColumn, 0, Title + " · " + GraphType, Palette.AccentStyle);
        int legend = titleColumn + Title.Length + GraphType.ToString().Length + 5;
        foreach (var series in Series) {
            Write(buffer, legend, 0, series.Property, Ink(Array.IndexOf(Series, series)));
            legend += series.Property.Length + 3;
        }

        string axis = GraphType is GraphKind.Histogram or GraphKind.BoxPlot ? "Y distribution" : XProperty ?? "input order";
        string count = VisibleIndices.Length == _data.Objects.Count ? $"{_data.Objects.Count} objects" : $"{VisibleIndices.Length}/{_data.Objects.Count} objects";
        string details = $"{count} · {FilterLabel} · X: {axis}";
        if (_message.Length > 0) {
            details += " · " + _message;
        }

        string selected = "";
        if (Plot.Selection is { } selection) {
            selected = $" · {selection.Label} · {selection.SeriesName}: {selection.Y.ToString("G6", CultureInfo.InvariantCulture)}";
        }
        else if (SelectedIndex >= 0) {
            int position = Array.IndexOf(VisibleIndices, SelectedIndex);
            selected = $" · Object #{SelectedIndex + 1}" + (Series.FirstOrDefault()?.Points.ElementAtOrDefault(position).Label is { Length: > 0 } label ? $" ({label})" : "") +
                " · " + string.Join(
                    " · ",
                    Series.Select(series => $"{series.Property}: {(position >= 0 && position < series.Count ? series.Points[position].Y?.ToString("G6", CultureInfo.InvariantCulture) : null) ?? "missing"}")
                );
        }

        if (Plot.SelectedRange is { } rangeSelection) {
            details += $" · range {rangeSelection.MinimumX:G4}–{rangeSelection.MaximumX:G4}";
        }

        Write(
            buffer,
            0,
            Bounds.Height - (Minimal ? 1 : 2),
            selected.Length > 0 ? selected[3..] +
            " · " + details : details,
            Palette.Body
        );
        if (!Minimal) {
            Write(
                buffer,
                0,
                Bounds.Height - 1,
                PassThru ? "Q/F10 return rows · Esc cancel · Double-click/D details · T data · Wheel zoom · Shift-drag range" : "Double-click/D details · T data · Wheel zoom · Shift-drag range · Q quit",
                Palette.Muted
            );
        }

        if (Bounds.Width < 18 || Bounds.Height < 9) {
            return;
        }

        var valid = ValidPoints;
        if (valid.Length == 0) {
            Write(
                buffer,
                2,
                4,
                VisibleIndices.Length == 0 ? "No rows match this filter. F: change filter or show all." : "Choose numeric Y properties (Y/F3). No plottable values in this selection.",
                Palette.Muted
            );
            return;
        }
    }
}
