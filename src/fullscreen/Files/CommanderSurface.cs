using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Files;
/// <summary>Displays two file-manager panes and tracks which pane receives input.</summary>
public sealed partial class CommanderSurface : Themes.ThemedSurface {
    /// <summary>Gets whether the two-pane manager uses its classic color scheme.</summary>
    public static Themes.ThemePalette ClassicTheme { get; } = Themes.ThemeCatalog.FromDictionary(new System.Collections.Hashtable {
        ["Name"] = "Commander Classic",
        ["Background"] = "#000080",
        ["Panel"] = "#000080",
        ["Foreground"] = "#c0c0c0",
        ["Accent"] = "#ffff00",
        ["Muted"] = "#00aaaa",
        ["Selection"] = "#00aaaa",
        ["SelectedText"] = "#000000",
        ["Success"] = "#ffffff"
    });

    private readonly FileManagerSurface[] _panes;
    [Bindable]
    private partial int Revision { get; set; }
    /// <summary>Gets the index of the pane receiving keyboard input.</summary>
    public int ActivePane { get; private set; }
    /// <summary>Gets the pane receiving keyboard input.</summary>
    public FileManagerSurface Active => _panes[ActivePane];
    /// <summary>Gets the source frame to copy.</summary>
    public FileManagerSurface Other => _panes[1 - ActivePane];

    /// <summary>Raised when function requested is requested.</summary>
    public event Action<int>? FunctionRequested;
    /// <summary>Initializes a new CommanderSurface instance.</summary>
    /// <param name="left">The left value in the comparison.</param>
    /// <param name="right">The right value in the comparison.</param>
    public CommanderSurface(FileManagerSurface left, FileManagerSurface right) {
        _panes = [left, right];
        foreach (var pane in _panes) {
            pane.CommanderPane = true;
            pane.PreviewEnabled = false;
            pane.CommanderKey += HandlePaneKey;
            AttachChild(pane);
        }

        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    private void HandlePaneKey(KeyEventArgs e) {
        if (_panes[0].HasFocus) {
            ActivePane = 0;
        }
        else if (_panes[1].HasFocus) {
            ActivePane = 1;
        }

        if (e.Key == TerminalKey.Tab) {
            SwitchPane();
            e.Handled = true;
            return;
        }

        if (e.Key == TerminalKey.Delete) {
            FunctionRequested?.Invoke(11);
            e.Handled = true;
            return;
        }

        int key = e.Key switch {
            TerminalKey.F1 => 1,
            TerminalKey.F2 => 2,
            TerminalKey.F3 => 3,
            TerminalKey.F5 => 5,
            TerminalKey.F6 => 6,
            TerminalKey.F7 => 7,
            TerminalKey.F8 => 8,
            TerminalKey.F9 => 9,
            TerminalKey.F10 => 10,
            _ => 0
        };
        if (key != 0) {
            FunctionRequested?.Invoke(key);
            e.Handled = true;
        }
    }

    /// <summary>Moves keyboard focus to the other file-manager pane.</summary>
    public void SwitchPane() {
        ActivePane = 1 - ActivePane;
        App?.Focus(Active);
        Refresh();
    }

    /// <summary>Requests a redraw of both file-manager panes.</summary>
    public void Refresh() {
        if (_panes[0].HasFocus) {
            ActivePane = 0;
        }
        else if (_panes[1].HasFocus) {
            ActivePane = 1;
        }

        Revision++;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 2;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        _panes[index];
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        foreach (var pane in _panes) {
            pane.Measure(new LayoutConstraints(0, Math.Max(0, constraints.MaxWidth / 2 - 2), 0, Math.Max(0, constraints.MaxHeight - 5)));
        }

        return SizeHints.Flex(new(0, 0), new(120, 32), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        if (finalRect.Width < 40 || finalRect.Height < 10) {
            foreach (var pane in _panes) {
                pane.Arrange(default);
            }

            return;
        }

        int top = Minimal ? 0 : 1, height = Math.Max(0, finalRect.Height - top - (Minimal ? 0 : 3)), half = finalRect.Width / 2;
        _panes[0].Arrange(new(finalRect.X + 1, finalRect.Y + top + 1, Math.Max(0, half - 2), Math.Max(0, height - 2)));
        _panes[1].Arrange(new(
            finalRect.X + half + 1,
            finalRect.Y + top + 1,
            Math.Max(0, finalRect.Width - half - 2),
            Math.Max(0, height - 2)
        ));
    }

    /// <inheritdoc/>
    protected override void OnMinimalChanged() =>
        Refresh();
    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        for (int y = 0; y < Bounds.Height; y++) {
            for (int x = 0; x < Bounds.Width; x++) {
                buffer.SetCell(Bounds.X + x, Bounds.Y + y, new Rune(' '), Palette.Body);
            }
        }

        if (Bounds.Width < 40 || Bounds.Height < 10) {
            Write(buffer, 0, 0, "COMMANDER · resize to 40 × 10", Palette.AccentStyle, Bounds.Width);
            return;
        }

        int top = Minimal ? 0 : 1, height = Bounds.Height - top - (Minimal ? 0 : 3), half = Bounds.Width / 2;
        if (!Minimal) {
            Write(
                buffer,
                1,
                0,
                " Left     File     Command     Options     Right                    FULLSCREEN COMMANDER",
                Palette.Selected,
                Bounds.Width - 2
            );
        }

        for (int pane = 0; pane < 2; pane++) {
            int x = pane == 0 ? 0 : half, width = pane == 0 ? half : Bounds.Width - half;
            var style = pane == ActivePane ? Palette.AccentStyle : Palette.Muted;
            for (int col = x; col < x + width; col++) {
                Write(buffer, col, top, "═", style, 1);
                Write(buffer, col, top + height - 1, "═", style, 1);
            }

            for (int row = top; row < top + height; row++) {
                Write(buffer, x, row, "║", style, 1);
                Write(buffer, x + width - 1, row, "║", style, 1);
            }

            Write(buffer, x, top, "╔", style, 1);
            Write(buffer, x + width - 1, top, "╗", style, 1);
            Write(buffer, x, top + height - 1, "╚", style, 1);
            Write(buffer, x + width - 1, top + height - 1, "╝", style, 1);
            Write(buffer, x + 2, top, " " + _panes[pane].Model.CurrentPath + " ", style, width - 4);
        }

        if (Minimal) {
            return;
        }

        Write(
            buffer,
            1,
            Bounds.Height - 3,
            Active.Status,
            Active.HasError ? Palette.Hot : Palette.Body,
            Bounds.Width - 2
        );
        Write(
            buffer,
            1,
            Bounds.Height - 2,
            "Tab switch pane · Enter open · Space/Insert mark · / filter · Ctrl+R refresh · Q close",
            Palette.Muted,
            Bounds.Width - 2
        );
        ReadOnlySpan<string> labels = [
            "Help",
            "Rename",
            "View",
            "Minimal",
            "Copy",
            "Move",
            "Mkdir",
            "Trash",
            "Menu",
            "Quit"
        ];
        for (int i = 0; i < labels.Length; i++) {
            int x = i * Bounds.Width / 10, width = (i + 1) * Bounds.Width / 10 - x;
            Write(buffer, x, Bounds.Height - 1, $"{i + 1}{labels[i]}", Palette.Selected, width - 1);
        }
    }

    private void Write(CellBuffer buffer, int x, int y, ReadOnlySpan<char> text, Style style, int width) {
        if (width <= 0 || y < 0 || y >= Bounds.Height || x < 0 || x >= Bounds.Width) {
            return;
        }

        Span<char> safe = stackalloc char[Math.Min(text.Length, 1024)];
        for (int i = 0; i < safe.Length; i++) {
            safe[i] = char.IsControl(text[i]) ? '�' : text[i];
        }

        buffer.PushClip(new(Bounds.X + x, Bounds.Y + y, Math.Min(width, Bounds.Width - x), 1));
        buffer.WriteText(Bounds.X + x, Bounds.Y + y, safe, style);
        buffer.PopClip();
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview || e.Button != TerminalMouseButton.Left) {
            return;
        }

        if (!Minimal && e.LocalY == Bounds.Height - 1) {
            int key = Math.Clamp(e.LocalX * 10 / Math.Max(1, Bounds.Width) + 1, 1, 10);
            if (key == 4) {
                return; // F4 is handled centrally by FullscreenHost.
            }

            FunctionRequested?.Invoke(key);
            e.Handled = true;
        }
        else if (!Minimal && e.LocalY == 0) {
            FunctionRequested?.Invoke(9);
            e.Handled = true;
        }
    }
}
