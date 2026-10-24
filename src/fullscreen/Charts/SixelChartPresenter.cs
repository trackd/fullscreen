using System.Diagnostics;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Charts;
/// <summary>Presents managed chart rasters within XenoAtom's synchronized frame writer.</summary>
internal sealed class SixelChartPresenter(
    TerminalGraphicsCapabilities capabilities,
    TerminalPixelMetrics metrics
) : IBufferedTerminalGraphicsPresenter, ITerminalGraphicsPresenterDiagnostics {
    private readonly long _started = Stopwatch.GetTimestamp();
    internal bool HasPresented => _diagnostics.PresentationCount > 0;
    private TerminalGraphicsPresenterDiagnostics _diagnostics = new() {
        Name = "Sixel charts",
        Protocol = TerminalGraphicsProtocol.Sixel
    };
    public TerminalGraphicsPresenterDiagnostics GetDiagnosticsSnapshot() =>
        _diagnostics with {
            Name = "Sixel charts" + (_previous.Count == 0 ? "" : " · " + string.Join(", ", _previous.Values.Select(placement => placement.Key.Kind).Distinct())),
            AverageEncodeDuration = _diagnostics.EncodedFrameCount == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(_diagnostics.TotalEncodeDuration.Ticks / _diagnostics.EncodedFrameCount),
            EffectiveFramesPerSecond = _diagnostics.EncodedFrameCount / Math.Max(Stopwatch.GetElapsedTime(_started).TotalSeconds, double.Epsilon)
        };
    private sealed record Placement(
        Rectangle Bounds,
        Rectangle Clip,
        ChartPixelKey Key,
        int CellWidth,
        int CellHeight,
        string Payload,
        Rectangle[] Windows,
        Color? Background
    );
    private Dictionary<ulong, Placement> _previous = [], _next = [];
    private readonly SixelPlot _plot = new();
    private readonly SixelEncoder _encoder = new();
    private byte[] _cropped = [];
    private readonly List<Rectangle> _windows = [];
    public TerminalGraphicsCapabilities Capabilities => capabilities;

    public bool CanPresent(TerminalGraphicsCapabilities detected) =>
        SupportsSixel(capabilities) && metrics.CellPixelWidth > 0 && metrics.CellPixelHeight > 0;
    internal static bool SupportsSixel(TerminalGraphicsCapabilities detected) =>
        detected.SupportState is not (TerminalGraphicsSupportState.Disabled or TerminalGraphicsSupportState.Unsupported)
        && !detected.IsMultiplexer
        && detected.SupportedProtocols.Contains(TerminalGraphicsProtocol.Sixel);
    private void RefreshWindows(TerminalGraphicsPresentContext context) {
        _windows.Clear();
        var debug = TerminalOverlayBounds.Debug(context.App);
        if (debug.Width > 0 && debug.Height > 0) {
            _windows.Add(debug);
        }

        foreach (var visual in context.App.Root.EnumerateVisualsDepthFirst()) {
            if (visual is Hosting.FullscreenChrome chrome) {
                _windows.Add(chrome.CloseBounds);
            }

            if (visual is Chart chart && visual.IsVisible) {
                _windows.AddRange(chart.AnnotationBounds);
            }

            if (visual.IsVisible && visual.Parent is WindowLayer layer && !ReferenceEquals(visual, layer.Content)) {
                // Popup is a viewport-sized input host; only its content covers pixels.
                _windows.Add(visual is Popup { Content: { } content } ? content.Bounds : visual.Bounds);
            }
        }
    }

    private bool Covered(Rectangle clip) {
        foreach (var window in _windows) {
            if (window.Intersects(clip)) {
                return true;
            }
        }

        return false;
    }

    private bool SameWindows(Placement placement) =>
        placement.Windows.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_windows));
    public bool HasPendingOutput(GraphicsCommandBuffer current, TerminalGraphicsPresentContext context) {
        // XenoAtom computes the F12 rectangle after this query. Enter presentation
        // so the final rectangle can mask graphics; unchanged payloads stay cached.
        if (TerminalOverlayBounds.DebugVisible(context.App)) {
            return true;
        }

        var pixelMetrics = metrics;
        RefreshWindows(context);
        int count = 0;
        foreach (var command in current) {
            var chart = command.Content.Source as Chart;
            string? image = (command.Content.Source as Files.ImagePreview)?.Payload;
            if (chart is null && image is null) {
                continue;
            }

            var clip = Intersect(
                Intersect(command.CellBounds, command.ClipBounds),
                new(0, 0, context.ViewportBounds.Width, context.ViewportBounds.Height)
            );
            if (clip.Width <= 0 || clip.Height <= 0
                || image is not null && (clip != command.CellBounds || Covered(clip))) {
                continue;
            }

            count++;
            var key = chart?.PixelKey ?? default;
            if (!_previous.TryGetValue(command.VisualRenderId, out var previous)
                || previous.Bounds != command.CellBounds
                || previous.Clip != clip
                || previous.Key != key
                || !SameWindows(previous)
                || image is not null && !ReferenceEquals(previous.Payload, image)
                || previous.CellWidth != pixelMetrics.CellPixelWidth
                || previous.CellHeight != pixelMetrics.CellPixelHeight
                || context.TextFrameKind != TerminalGraphicsTextFrameKind.None
                && clip.Intersects(context.TextRepaintBounds)) {
                return true;
            }
        }

        return count != _previous.Count;
    }

    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException("Sixel charts require the buffered frame writer.");
    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        AnsiWriter writer,
        CancellationToken cancellationToken
    ) {
        long started = Stopwatch.GetTimestamp();
        _diagnostics = _diagnostics with {
            PresentationCount = _diagnostics.PresentationCount + 1,
            LastCommandCount = current.Count,
            LastEncodedFrameCount = 0,
            LastEncodeDuration = TimeSpan.Zero,
            LastPayloadByteCount = 0,
            LastCacheHitCount = 0,
            LastCacheMissCount = 0,
            LastCacheStoreCount = 0
        };
        try {
            return PresentCore(current, context, writer, cancellationToken);
        }
        finally {
            _diagnostics = _diagnostics with {
                LastPresentationDuration = Stopwatch.GetElapsedTime(started)
            };
        }
    }

    private ValueTask PresentCore(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        AnsiWriter writer,
        CancellationToken cancellationToken
    ) {
        var pixelMetrics = metrics;
        int cellWidth = pixelMetrics.CellPixelWidth, cellHeight = pixelMetrics.CellPixelHeight;
        if (cellWidth <= 0 || cellHeight <= 0) {
            return ValueTask.CompletedTask;
        }

        var next = _next;
        next.Clear();
        RefreshWindows(context);
        // Resolve the visible rectangle before rasterizing. Crop pixels afterward so
        // scrolling/clipping does not stretch the time axis into the clipped region.
        foreach (var command in current) {
            var chart = command.Content.Source as Chart;
            // Images arrive as finished Sixel payloads from the Sixel module; they cannot be cropped.
            string? image = (command.Content.Source as Files.ImagePreview)?.Payload;
            if (chart is null && image is null) {
                continue;
            }

            var key = chart?.PixelKey ?? default;
            var bounds = command.CellBounds;
            var clip = Intersect(
                Intersect(bounds, command.ClipBounds),
                new(0, 0, context.ViewportBounds.Width, context.ViewportBounds.Height)
            );
            // Finished image payloads cannot be masked; managed chart rasters can.
            if (image is not null && Covered(clip)) {
                continue;
            }

            if (clip.Width <= 0 || clip.Height <= 0
                || (long)bounds.Width * cellWidth * bounds.Height * cellHeight > 16_777_216
                || image is not null && clip != bounds) {
                continue;
            }

            string payload;
            if (_previous.TryGetValue(command.VisualRenderId, out var previous) && previous.Bounds == bounds
                && previous.Clip == clip
                && previous.Key == key
                && SameWindows(previous)
                && (image is null || ReferenceEquals(previous.Payload, image))
                && previous.CellWidth == cellWidth
                && previous.CellHeight == cellHeight) {
                _diagnostics = _diagnostics with {
                    CacheHitCount = _diagnostics.CacheHitCount + 1,
                    LastCacheHitCount = _diagnostics.LastCacheHitCount + 1
                };
                next[command.VisualRenderId] = previous;
                continue;
            }
            else {
                _diagnostics = _diagnostics with {
                    CacheMissCount = _diagnostics.CacheMissCount + 1,
                    LastCacheMissCount = _diagnostics.LastCacheMissCount + 1
                };
                if (image is not null) {
                    next[command.VisualRenderId] = new(bounds, clip, key, cellWidth, cellHeight, image, [.. _windows], null);
                    continue;
                }

                chart!.DrawPixels(_plot, checked(bounds.Width * cellWidth), checked(bounds.Height * cellHeight));
                int width = clip.Width * cellWidth, height = clip.Height * cellHeight;
                long encodeStarted = Stopwatch.GetTimestamp();
                if (clip == bounds && !Covered(clip)) {
                    payload = _encoder.EncodePlot(_plot);
                }
                else {
                    int size = checked(width * height);
                    if (_cropped.Length < size) {
                        _cropped = new byte[size];
                    }

                    for (int y = 0; y < height; y++) {
                        _plot.Pixels.Slice(
                            ((clip.Y - bounds.Y) * cellHeight + y) * _plot.Width + (clip.X - bounds.X) * cellWidth,
                            width
                        ).CopyTo(_cropped.AsSpan(y * width));
                    }

                    // Leave popup cells transparent so the cell-based menu stays
                    // above the chart while all uncovered pixels remain visible.
                    foreach (var window in _windows) {
                        var covered = Intersect(clip, window);
                        if (covered.Width <= 0 || covered.Height <= 0) {
                            continue;
                        }

                        for (int y = (covered.Y - clip.Y) * cellHeight; y < (covered.Bottom - clip.Y) * cellHeight; y++) {
                            _cropped.AsSpan(y * width + (covered.X - clip.X) * cellWidth, covered.Width * cellWidth).Clear();
                        }
                    }

                    payload = _encoder.EncodeFrame(_cropped.AsSpan(0, size), width, height, _plot.Palette);
                }

                var elapsed = Stopwatch.GetElapsedTime(encodeStarted);
                // Sixel payloads contain only ASCII, so characters equal encoded bytes.
                _diagnostics = _diagnostics with {
                    EncodedFrameCount = _diagnostics.EncodedFrameCount + 1,
                    LastEncodedFrameCount = _diagnostics.LastEncodedFrameCount + 1,
                    TotalEncodeDuration = _diagnostics.TotalEncodeDuration + elapsed,
                    LastEncodeDuration = _diagnostics.LastEncodeDuration + elapsed,
                    PayloadByteCount = _diagnostics.PayloadByteCount + payload.Length,
                    LastPayloadByteCount = _diagnostics.LastPayloadByteCount + payload.Length,
                    CacheStoreCount = _diagnostics.CacheStoreCount + 1,
                    LastCacheStoreCount = _diagnostics.LastCacheStoreCount + 1
                };
            }

            next[command.VisualRenderId] = new(bounds, clip, key, cellWidth, cellHeight, payload, [.. _windows], key.Background);
        }

        writer.SaveCursor();
        try {
            bool cleared = false;
            foreach (var (id, previous) in _previous) {
                if (next.TryGetValue(id, out var placement) && placement.Clip == previous.Clip) {
                    continue;
                }

                SetBackground(writer, previous.Background);
                Erase(writer, previous.Clip, _windows, context.ViewportBounds);
                cleared = true;
            }

            foreach (var (id, placement) in next) {
                cancellationToken.ThrowIfCancellationRequested();
                bool changed = !_previous.TryGetValue(id, out var old) || old != placement;
                bool repainted = context.TextFrameKind != TerminalGraphicsTextFrameKind.None
                    && placement.Clip.Intersects(context.TextRepaintBounds);
                if (!changed && !repainted && !cleared) {
                    continue;
                }

                // Transparent Sixel is not a retained image replacement on every
                // terminal. Erase cells, rather than encoding background pixels.
                if (changed && old is not null && old.Clip == placement.Clip) {
                    SetBackground(writer, placement.Background);
                    Erase(writer, placement.Clip, _windows, context.ViewportBounds);
                }

                writer.CursorPosition(
                    placement.Clip.Y + context.ViewportBounds.Y + 1,
                    placement.Clip.X + context.ViewportBounds.X + 1
                );
                writer.Write(placement.Payload);
            }

            (_previous, _next) = (_next, _previous);
            _next.Clear();
            if (cleared) {
                context.App.RequestFullRender();
            }
        }
        finally {
            writer.RestoreCursor();
        }

        return ValueTask.CompletedTask;
    }

    internal static Rectangle Intersect(Rectangle a, Rectangle b) {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    private static void SetBackground(AnsiWriter writer, Color? color) {
        if (color is { } value) {
            var rgb = value.ToRgb();
            writer.Write($"\x1b[48;2;{rgb.R};{rgb.G};{rgb.B}m");
        }
    }

    /// <summary>Erases an old graphic's cells except those under popup windows, which are already painted.</summary>
    internal static void Erase(
        AnsiWriter writer,
        Rectangle area,
        IReadOnlyList<Rectangle> windows,
        Rectangle viewport
    ) {
        var visible = Intersect(area, new(0, 0, viewport.Width, viewport.Height));
        for (int y = visible.Y; y < visible.Bottom; y++) {
            for (int x = visible.X; x < visible.Right;) {
                if (Inside(windows, x, y)) {
                    x++;
                    continue;
                }

                int start = x++;
                while (x < visible.Right && !Inside(windows, x, y)) {
                    x++;
                }

                writer.CursorPosition(y + viewport.Y + 1, start + viewport.X + 1);
                // ECH erases cells without advancing/wrapping the cursor.
                writer.Write($"\x1b[{x - start}X");
            }
        }
    }

    private static bool Inside(IReadOnlyList<Rectangle> windows, int x, int y) {
        foreach (var window in windows) {
            if (x >= window.X && x < window.Right && y >= window.Y && y < window.Bottom) {
                return true;
            }
        }

        return false;
    }

    public void Reset() {
        _previous.Clear();
        _next.Clear();
        _windows.Clear();
    }

    public void Dispose() =>
        Reset();
}
