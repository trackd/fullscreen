using System.Globalization;
using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Files;
/// <summary>Virtualized file panes with a retained rich-preview child. No file I/O in rendering.</summary>
public sealed partial class FileManagerSurface : Themes.ThemedSurface {
    private Style Body => Palette.Body;
    private Style Panel => Palette.Panel;
    private Style Accent => Palette.AccentStyle;
    private Style Green => Palette.Green;
    private Style Purple => Palette.PurpleStyle;
    private Style Muted => Palette.Muted;
    private Style SelectedStyle => Palette.Selected;

    [Bindable]
    private partial int Revision { get; set; }

    [Bindable]
    private partial int PreviewRevision { get; set; }
    /// <summary>Gets the backing table or directory model.</summary>
    public FileManagerModel Model { get; private set; } = new();
    /// <summary>Gets or sets the raw Git porcelain status text.</summary>
    public string Status { get; set; } = "Ready";
    /// <summary>Gets or sets whether a background operation is in progress.</summary>
    public bool Busy { get; set; }
    /// <summary>Gets or sets whether the current status represents an error.</summary>
    public bool HasError { get; set; }
    /// <summary>Gets whether the application has requested termination.</summary>
    public bool ExitRequested { get; private set; }
    /// <summary>Gets or sets whether file previews are enabled.</summary>
    public bool PreviewEnabled { get; set; } = true;
    /// <summary>Gets or sets whether this surface is embedded in a two-pane manager.</summary>
    public bool CommanderPane { get; set; }
    /// <summary>Gets the preview content, or null to clear the preview.</summary>
    public FilePreview? Preview { get; private set; }

    /// <summary>Raised when navigate requested is requested.</summary>
    public event Action<string, bool, string?>? NavigateRequested;
    /// <summary>Raised when preview requested is requested.</summary>
    public event Action<FileEntry?>? PreviewRequested;
    /// <summary>Raised when operation requested is requested.</summary>
    public event Action<FileOperation>? OperationRequested;
    private Visual? _previewVisual;
    private string[] _previewLines = [];
    private int _previewOffset;
    private bool _filtering;
    private string[] _clipboard = [];
    private bool _cut;
    private int ListHeight => Math.Max(1, Bounds.Height - (CommanderPane ? 2 : Minimal ? 1 : 8));
    private int EntryTop => CommanderPane || Minimal ? 1 : 4;
    private int HeaderTop => EntryTop - 1;
    /// <summary>Gets whether the preview panel is currently visible.</summary>
    public bool PreviewVisible => !CommanderPane && !Minimal && PreviewEnabled && Bounds.Width >= 65 && Bounds.Height >= 12;

    /// <inheritdoc/>
    protected override void OnMinimalChanged() =>
        Refresh();
    /// <summary>Gets whether the parent-directory pane is collapsed.</summary>
    public bool ParentPaneCollapsed { get; private set; }

    /// <summary>Changes whether the parent-directory pane is collapsed.</summary>
    /// <param name="collapsed">Whether the parent-directory pane is hidden.</param>
    public void SetParentPaneCollapsed(bool collapsed) {
        if (ParentPaneCollapsed == collapsed || CommanderPane) {
            return;
        }

        ParentPaneCollapsed = collapsed;
        PreviewRevision++;
        Refresh();
    }

    private int ExpandedParentWidth(int width) =>
        !CommanderPane && !Minimal && width >= 100 ? Math.Clamp(width / 6, 16, 26) : 0;
    private int ParentWidthAt(int width) {
        int expanded = ExpandedParentWidth(width);
        return expanded == 0 ? 0 : ParentPaneCollapsed ? 3 : expanded;
    }

    private int ParentWidth => ParentWidthAt(Bounds.Width);
    private int CurrentX => ParentWidth == 0 ? 1 : ParentWidth + 2;

    private int PreviewXAt(int width) =>
        !CommanderPane && !Minimal && PreviewEnabled && width >= 65 ? Math.Max(
            (ParentWidthAt(width) == 0 ? 1 : ParentWidthAt(width) + 2) + 22,
            width * 52 / 100 - (ParentPaneCollapsed ? ExpandedParentWidth(width) - ParentWidthAt(width) : 0)
        ) : width;
    private int PreviewX => PreviewVisible ? PreviewXAt(Bounds.Width) : Bounds.Width;
    private int CurrentWidth => Math.Max(1, PreviewX - CurrentX - 1);

    /// <summary>Initializes a new FileManagerSurface instance.</summary>
    public FileManagerSurface() {
        Focusable = true;
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    /// <summary>Invalidates the file-manager surface for redraw.</summary>
    public void Refresh() =>
        Revision++;
    /// <summary>Updates previews and rendering after the selected entry changes.</summary>
    public void SelectionChanged() {
        Model.KeepVisible(ListHeight);
        PreviewRequested?.Invoke(Model.Selected);
        Refresh();
    }

    /// <summary>Refreshes directory state after a file operation completes.</summary>
    public void OperationFinished() {
        if (_cut) {
            _clipboard = [];
            _cut = false;
        }
    }

    /// <summary>Shows <paramref name="preview"/>; <paramref name="language"/> forces a TextMate language id or extension.</summary>
    /// <param name="preview">The preview content, or null to clear the preview.</param>
    /// <param name="language">The explicit syntax language, or null for automatic selection.</param>
    public void SetPreview(FilePreview? preview, string? language = null) {
        if (_previewVisual is not null) {
            DetachChild(_previewVisual);
            _previewVisual = null;
        }

        Preview = preview;
        _previewOffset = 0;
        _previewLines = [];
        if (preview is { Kind: PreviewKind.Markdown or PreviewKind.Text }) {
            _previewVisual = new ScrollViewer(SyntaxPreview.File(preview.Text, preview.Path, language)) {
                HorizontalScrollEnabled = false
            };
        }
        else if (preview is { Kind: PreviewKind.Image }) {
            _previewVisual = new ImagePreview(preview.Path, preview.Text);
        }
        else if (preview is not null) {
            _previewLines = preview.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        }

        if (_previewVisual is not null) {
            _previewVisual.AddCommand(new Command {
                Id = "fullscreen.files.expand-preview",
                LabelMarkup = "Expand preview",
                Gesture = new KeyGesture(TerminalKey.Right),
                Execute = _ => {
                    if (ParentWidth > 3) {
                        SetParentPaneCollapsed(true);
                    }
                }
            });
            _previewVisual.AddCommand(new Command {
                Id = "fullscreen.files.restore-parent",
                LabelMarkup = "Restore parent pane",
                Gesture = new KeyGesture(TerminalKey.Left),
                Execute = _ => {
                    if (ParentPaneCollapsed) {
                        SetParentPaneCollapsed(false);
                    }
                    else {
                        App?.Focus(this);
                    }
                }
            });
            AttachChild(_previewVisual);
        }

        PreviewRevision++;
        Refresh();
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => _previewVisual is null || !PreviewVisible ? 0 : 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 && _previewVisual is not null ? _previewVisual : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = PreviewRevision;
        if (!Minimal && PreviewEnabled) {
            _previewVisual?.Measure(new LayoutConstraints(
                0,
                Math.Max(1, constraints.MaxWidth - PreviewXAt(constraints.MaxWidth) - 4),
                0,
                Math.Max(1, constraints.MaxHeight - 8)
            ));
        }

        return SizeHints.Flex(new Size(0, 0), new Size(100, 28), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        _previewVisual?.Arrange(!Minimal && PreviewEnabled && finalRect.Width >= 65 && finalRect.Height >= 12 ? new Rectangle(
            finalRect.X + PreviewX + 2,
            finalRect.Y + 4,
            Math.Max(1, finalRect.Width - PreviewX - 4),
            ListHeight
        ) : default);
    }

    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        Fill(buffer, 0, 0, Bounds.Width, Bounds.Height, Body);
        if (CommanderPane) {
            if (Bounds.Width < 8 || Bounds.Height < 3) {
                return;
            }

            Write(buffer, 1, 0, _filtering ? "FILTER › " + Model.Filter : "Name", Accent, CurrentWidth);
            if (CurrentWidth >= 35) {
                Write(buffer, Bounds.Width - 11, 0, "Size", Accent, 10);
            }

            RenderEntries(buffer);
            Write(
                buffer,
                1,
                Bounds.Height - 1,
                Busy ? "Working…" : HasError ? Status : $"{Model.Entries.Count} files · {Model.Marked.Count} marked",
                HasError ? Purple : Muted,
                Bounds.Width - 2
            );
            return;
        }

        if (Bounds.Width < 35 || Bounds.Height < 12) {
            Write(buffer, 1, 0, "FULLSCREEN FILES · resize to 35 × 12", Accent);
            return;
        }

        if (!Minimal) {
            RenderTabs(buffer);
            Write(
                buffer,
                Math.Max(1, Bounds.Width - 16),
                0,
                Model.ShowHidden ? "● hidden shown" : "○ hidden files",
                Muted,
                15
            );
            Write(buffer, 1, 1, Model.CurrentPath.Length == 0 ? Status : Model.CurrentPath, Body, Bounds.Width - 2);
            Rule(buffer, 2);
        }

        if (ParentWidth > 0) {
            if (ParentPaneCollapsed) {
                Write(buffer, 1, 3, "›", Accent, ParentWidth);
                Write(buffer, 1, 4, "│", Muted, ParentWidth);
            }
            else {
                Write(buffer, 1, 3, "PARENT", Muted);
                RenderParent(buffer);
            }

            Vertical(buffer, ParentWidth + 1, 3, ListHeight + 1);
        }

        Write(
            buffer,
            CurrentX,
            HeaderTop,
            _filtering ? "FILTER › " + Model.Filter : Model.Filter.Length > 0 ? "MATCHES · " + Model.Filter : "NAME",
            Accent,
            CurrentWidth
        );
        if (CurrentWidth >= 35) {
            Write(
                buffer,
                PreviewX - 12,
                HeaderTop,
                Model.Sort.ToString() + (Model.Descending ? " ↓" : " ↑"),
                Muted,
                11
            );
        }

        RenderEntries(buffer);
        if (PreviewX < Bounds.Width) {
            Vertical(buffer, PreviewX, 3, ListHeight + 1);
            Write(
                buffer,
                PreviewX + 2,
                3,
                Preview?.Kind.ToString().ToUpperInvariant() ?? "PREVIEW",
                Purple,
                Bounds.Width - PreviewX - 4
            );
            if (_previewVisual is null) {
                RenderPlainPreview(buffer);
            }
        }

        if (Minimal) {
            if (HasError) {
                Write(buffer, 1, Bounds.Height - 1, "ERROR · " + Status, Purple, Bounds.Width - 2);
            }

            return;
        }

        int footer = Bounds.Height - 3;
        Rule(buffer, footer);
        if (Model.Selected is { } selected) {
            Span<char> label = stackalloc char[160];
            if (label.TryWrite(
                CultureInfo.InvariantCulture,
                $"{(selected.IsDirectory ? "DIR" : Path.GetExtension(selected.Name))} · {(selected.IsLink ? "link · " : "")}{selected.Length:N0} B · {selected.Modified.ToLocalTime():yyyy-MM-dd HH:mm}",
                out int written
            )) {
                Write(buffer, 1, footer, label[..written], Muted, Bounds.Width - 3);
            }
        }

        Write(buffer, 1, footer + 1, Status, Busy ? Purple : Muted, Bounds.Width - 28);
        Span<char> count = stackalloc char[48];
        if (count.TryWrite(
            CultureInfo.InvariantCulture,
            $"{Model.Entries.Count} items · {Model.Marked.Count} marked",
            out int n
        )) {
            Write(buffer, Math.Max(1, Bounds.Width - 27), footer + 1, count[..n], Green, 26);
        }

        Write(
            buffer,
            1,
            footer + 2,
            Bounds.Width >= 100 ? "hjkl · / filter · Space mark · y/x/p · F2 rename · t tab · {/} switch · w close tab · b bookmarks · B pin · ? help" : "hjkl · / filter · t tab · {/} switch · b bookmarks · ? help · Q quit",
            Accent,
            Bounds.Width - 2
        );
    }

    private void RenderEntries(CellBuffer buffer) {
        Model.KeepVisible(ListHeight);
        for (int i = Model.Offset; i < Math.Min(Model.Entries.Count, Model.Offset + ListHeight); i++) {
            var entry = Model.Entries[i];
            int y = EntryTop + i - Model.Offset;
            bool selected = i == Model.SelectedIndex, marked = Model.Marked.Contains(entry.FullPath);
            var style = selected ? SelectedStyle : (i & 1) == 0 ? Body : Panel;
            Fill(buffer, CurrentX, y, CurrentWidth, 1, style);
            Write(
                buffer,
                CurrentX,
                y,
                marked ? "●" : entry.IsDirectory ? "▸" : entry.IsLink ? "↗" : "·",
                selected ? style : marked ? Green : entry.IsDirectory ? Accent : Muted,
                1
            );
            int reserve = CurrentWidth >= 38 ? 11 : 0;
            Write(
                buffer,
                CurrentX + 2,
                y,
                entry.Name,
                selected ? style : entry.IsDirectory ? Accent : Body,
                CurrentWidth - 2 - reserve
            );
            if (reserve > 0) {
                Size(buffer, PreviewX - 11, y, entry.IsDirectory ? null : entry.Length, selected ? style : Muted, 10);
            }
        }

        if (Model.Entries.Count == 0) {
            Write(
                buffer,
                CurrentX + 1,
                EntryTop,
                Busy ? "Loading…" : Model.Filter.Length > 0 ? "No matches" : "Empty directory",
                Muted,
                CurrentWidth - 2
            );
        }
    }

    private void RenderParent(CellBuffer buffer) {
        var entries = Model.Snapshot.ParentEntries;
        int selected = -1;
        for (int i = 0; i < entries.Length; i++) {
            if (FileManagerModel.PathComparer.Equals(entries[i].FullPath, Model.CurrentPath)) {
                selected = i;
                break;
            }
        }

        int offset = Math.Clamp(selected - ListHeight / 2, 0, Math.Max(0, entries.Length - ListHeight));
        int y = 4;
        for (int i = offset; i < entries.Length && y < 4 + ListHeight; i++) {
            var entry = entries[i];
            if (!Model.ShowHidden && entry.Hidden) {
                continue;
            }

            var style = i == selected ? SelectedStyle : entry.IsDirectory ? Accent : Muted;
            if (i == selected) {
                Fill(buffer, 1, y, ParentWidth, 1, style);
            }

            Write(buffer, 1, y, entry.IsDirectory ? "▸" : " ", style, 1);
            Write(buffer, 3, y++, entry.Name, style, ParentWidth - 2);
        }
    }

    private FileEntry? ParentEntryAt(int row) {
        var entries = Model.Snapshot.ParentEntries;
        int selected = -1;
        for (int i = 0; i < entries.Length; i++) {
            if (FileManagerModel.PathComparer.Equals(entries[i].FullPath, Model.CurrentPath)) {
                selected = i;
                break;
            }
        }

        int offset = Math.Clamp(selected - ListHeight / 2, 0, Math.Max(0, entries.Length - ListHeight));
        for (int i = offset, visible = 0; i < entries.Length; i++) {
            if (!Model.ShowHidden && entries[i].Hidden) {
                continue;
            }

            if (visible++ == row) {
                return entries[i];
            }
        }

        return null;
    }

    private void RenderPlainPreview(CellBuffer buffer) {
        int x = PreviewX + 2, width = Bounds.Width - x - 2;
        if (Preview is null) {
            Write(buffer, x, 5, Model.Selected is null ? "Nothing selected" : "Loading preview…", Muted, width);
            return;
        }

        for (int i = _previewOffset; i < Math.Min(_previewLines.Length, _previewOffset + ListHeight); i++) {
            Write(buffer, x, 4 + i - _previewOffset, _previewLines[i], Body, width);
        }
    }

    private void Write(
        CellBuffer buffer,
        int x,
        int y,
        ReadOnlySpan<char> text,
        Style style,
        int width = int.MaxValue
    ) {
        if (x < 0 || x >= Bounds.Width || y < 0 || y >= Bounds.Height || width <= 0) {
            return;
        }

        Span<char> safe = stackalloc char[Math.Min(text.Length, 1024)];
        for (int i = 0; i < safe.Length; i++) {
            safe[i] = char.IsControl(text[i]) ? ' ' : text[i];
        }

        buffer.PushClip(new Rectangle(Bounds.X + x, Bounds.Y + y, Math.Min(width, Bounds.Width - x), 1));
        buffer.WriteText(Bounds.X + x, Bounds.Y + y, safe, style);
        buffer.PopClip();
    }

    private void Fill(CellBuffer buffer, int x, int y, int width, int height, Style style) {
        for (int row = Math.Max(0, y); row < Math.Min(Bounds.Height, y + height); row++) {
            for (int col = Math.Max(0, x); col < Math.Min(Bounds.Width, x + width); col++) {
                buffer.SetCell(Bounds.X + col, Bounds.Y + row, new Rune(' '), style);
            }
        }
    }

    private void Rule(CellBuffer buffer, int y) {
        for (int x = 1; x < Bounds.Width - 1; x++) {
            Write(buffer, x, y, "─", Muted, 1);
        }
    }

    private void Vertical(CellBuffer buffer, int x, int y, int height) {
        for (int row = y; row < y + height; row++) {
            Write(buffer, x, row, "│", Muted, 1);
        }
    }

    private void Size(CellBuffer buffer, int x, int y, long? bytes, Style style, int width) {
        if (!bytes.HasValue) {
            Write(buffer, x, y, "<dir>", style, width);
            return;
        }

        double value = bytes.Value;
        int unit = 0;
        ReadOnlySpan<string> units = ["B", "KiB", "MiB", "GiB", "TiB"];
        while (value >= 1024 && unit < 4) {
            value /= 1024;
            unit++;
        }

        Span<char> label = stackalloc char[32];
        if (label.TryWrite(CultureInfo.InvariantCulture, $"{value:F1} {units[unit]}", out int n)) {
            Write(buffer, x, y, label[..n], style, width);
        }
    }
}
