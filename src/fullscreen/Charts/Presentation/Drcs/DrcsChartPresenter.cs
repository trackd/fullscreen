using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Charts;
/// <summary>How the presenter chooses its one terminal soft font.</summary>
internal enum DrcsFontScope {
    /// <summary>The finest font covering the graph kinds on screen; replaced only when a new kind is not covered.</summary>
    Visible,
    /// <summary>One coarser font covering every graph kind, downloaded once.</summary>
    Universal
}

/// <summary>Opt-in DRCS presenter. Charts share one fixed font; frames only write characters.</summary>
internal sealed class DrcsChartPresenter(
    TerminalGraphicsCapabilities capabilities,
    DrcsFontScope scope = DrcsFontScope.Visible
) : IBufferedTerminalGraphicsPresenter {
    private sealed record Placement(
        ulong Id,
        Rectangle Bounds,
        Rectangle Clip,
        ChartPixelKey Key,
        char[] Cells,
        bool Text
    );
    private List<Placement> _previous = [];
    // The font currently loaded in the terminal, if any.
    private DrcsFont? _font;
    private double[] _scratch = [];
    private char[] _grid = [];
    public TerminalGraphicsCapabilities Capabilities => capabilities;

    public bool CanPresent(TerminalGraphicsCapabilities detected) =>
        detected.SupportState != TerminalGraphicsSupportState.Disabled && !detected.IsMultiplexer;
    private static Rectangle Clip(
        GraphicsCommand command,
        TerminalGraphicsPresentContext context
    ) =>
        ChartClipping.Intersect(
            ChartClipping.Intersect(command.CellBounds, command.ClipBounds),
            new(0, 0, context.ViewportBounds.Width, context.ViewportBounds.Height)
        );
    private static Rectangle[] Windows(TerminalGraphicsPresentContext context) =>
        [.. context.App.Root.EnumerateVisualsDepthFirst().Where(visual => visual.IsVisible && visual.Parent is WindowLayer layer && !ReferenceEquals(visual, layer.Content)).Select(TerminalOverlayBounds.Window)];
    // Charts under a popup are withheld; counting them would leave output pending forever.
    private static List<(ulong Id, Rectangle Bounds, Rectangle Clip, ChartPixelKey Key)> Visible(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        Rectangle[] windows
    ) {
        var charts = new List<(ulong Id, Rectangle Bounds, Rectangle Clip, ChartPixelKey Key)>();
        foreach (var command in current) {
            if (command.Content.Source is not Chart chart) {
                continue;
            }

            var bounds = command.CellBounds;
            var clip = Clip(command, context);
            if (clip.Width <= 0 || clip.Height <= 0 || windows.Any(window => window.Intersects(clip))
                || (long)bounds.Width * bounds.Height > 1_048_576) {
                continue;
            }

            charts.Add((command.VisualRenderId, bounds, clip, chart.PixelKey));
        }

        return charts;
    }

    public bool HasPendingOutput(GraphicsCommandBuffer current, TerminalGraphicsPresentContext context) {
        if (context.TextFrameKind != TerminalGraphicsTextFrameKind.None) {
            return true;
        }

        var charts = Visible(current, context, Windows(context));
        return charts.Count != _previous.Count
            || charts.Any(chart => !_previous.Any(old => old.Id == chart.Id && old.Bounds == chart.Bounds && old.Clip == chart.Clip && old.Key == chart.Key));
    }

    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException("DRCS requires the buffered frame writer.");
    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        AnsiWriter writer,
        CancellationToken cancellationToken
    ) {
        var windows = Windows(context);
        var charts = Visible(current, context, windows);
        int kinds = 0;
        foreach (var chart in charts) {
            kinds |= DrcsFont.Mask(chart.Key.Kind);
        }

        // Download a font only when a visible chart needs a kind the loaded font lacks.
        var font = kinds == 0 || _font is not null && (_font.Kinds & kinds) == kinds ? _font : DrcsFont.For(scope == DrcsFontScope.Universal ? DrcsFont.AllKinds : kinds);
        bool fontChanged = !ReferenceEquals(font, _font);
        var next = new List<Placement>(charts.Count);
        foreach (var (id, bounds, clip, key) in charts) {
            var old = _previous.Find(old => old.Id == id && old.Bounds == bounds && old.Clip == clip && old.Key == key);
            if (old is not null && !fontChanged) {
                next.Add(old);
                continue;
            }

            int size = bounds.Width * bounds.Height;
            if (_grid.Length < size) {
                _grid = new char[size];
            }

            bool text = DrcsFont.Layout(font, key, bounds.Width, bounds.Height, _grid.AsSpan(0, size), ref _scratch);
            var cells = new char[clip.Width * clip.Height];
            for (int y = 0; y < clip.Height; y++) {
                _grid.AsSpan((clip.Y - bounds.Y + y) * bounds.Width + clip.X - bounds.X, clip.Width).CopyTo(cells.AsSpan(y * clip.Width));
            }

            next.Add(new(id, bounds, clip, key, cells, text));
        }

        writer.SaveCursor();
        try {
            bool removed = false;
            foreach (var old in _previous) {
                if (!next.Any(placement => placement.Id == old.Id && placement.Clip == old.Clip)) {
                    removed = true;
                    ChartClipping.Erase(writer, old.Clip, windows, context.ViewportBounds);
                }
            }

            if (fontChanged && font is not null) {
                writer.Write(font.Download);
            }

            foreach (var placement in next) {
                cancellationToken.ThrowIfCancellationRequested();
                var foreground = placement.Key.Ink.ToRgb();
                var background = placement.Key.Background.ToRgb();
                writer.Write($"\x1b[0;38;2;{foreground.R};{foreground.G};{foreground.B};48;2;{background.R};{background.G};{background.B}m");
                if (!placement.Text) {
                    writer.Write("\x1b- @\x0e");
                }

                var previous = _previous.Find(old => old.Id == placement.Id);
                bool repaint = fontChanged || previous is null || previous.Clip != placement.Clip
                    || previous.Text != placement.Text
                    || previous.Key.InspectionColumn != placement.Key.InspectionColumn
                    || context.TextFrameKind != TerminalGraphicsTextFrameKind.None
                    && placement.Clip.Intersects(context.TextRepaintBounds);
                char[] cells = placement.Cells, before = previous?.Cells ?? cells;
                int width = placement.Clip.Width;
                int inspection = placement.Key.InspectionColumn < 0 ? -1 : placement.Key.InspectionColumn - (placement.Clip.X - placement.Bounds.X);
                for (int y = 0; y < placement.Clip.Height; y++) {
                    for (int x = 0; x < width;) {
                        int index = y * width + x;
                        if (!repaint && before[index] == cells[index]) {
                            x++;
                            continue;
                        }

                        int start = x++;
                        while (x < width && (repaint || before[y * width + x] != cells[y * width + x])) {
                            x++;
                        }

                        writer.CursorPosition(
                            placement.Clip.Y + y + context.ViewportBounds.Y + 1,
                            placement.Clip.X + start + context.ViewportBounds.X + 1
                        );
                        var run = cells.AsSpan(y * width + start, x - start);
                        // The inspected column is shown in reverse video; glyphs stay unchanged.
                        if (inspection >= start && inspection < x) {
                            writer.Write(run[..(inspection - start)]);
                            writer.Write("\x1b[7m");
                            writer.Write(run.Slice(inspection - start, 1));
                            writer.Write("\x1b[27m");
                            writer.Write(run[(inspection - start + 1)..]);
                        }
                        else {
                            writer.Write(run);
                        }
                    }
                }

                writer.Write("\x0f\x1b)B\x1b[0m");
            }

            _previous = next;
            _font = font;
            if (removed) {
                context.App.RequestFullRender();
            }
        }
        finally {
            writer.Write("\x0f\x1b)B\x1b[0m");
            writer.RestoreCursor();
        }

        return ValueTask.CompletedTask;
    }

    public void Reset() {
        _previous.Clear();
        _font = null;
    }

    public void Dispose() =>
        Reset();
}
