using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using fullscreen.Hosting;

namespace fullscreen.Charts;
/// <summary>Experimental Contour ReGIS backend. Owns the terminal-wide 800x480 canvas.</summary>
/// <remarks>Explicit opt-in; DA1 must advertise ReGIS support.</remarks>
internal sealed class RegisChartPresenterPrototype(TerminalInstance terminal, TerminalProbeResult evidence) : IBufferedTerminalGraphicsPresenter {
    private string _previous = "";
    private readonly ChartGeometry _geometry = new();
    public TerminalGraphicsCapabilities Capabilities => evidence.Capabilities;

    public bool CanPresent(TerminalGraphicsCapabilities detected) =>
        evidence.Completed && evidence.Regis;
    internal static bool Supports(GraphKind kind) =>
        kind is GraphKind.Line or GraphKind.Step or GraphKind.Scatter or GraphKind.Stem or GraphKind.Bars
        or GraphKind.HorizontalBars
        or GraphKind.StackedBars
        or GraphKind.Heatmap
        or GraphKind.ErrorBars;
    public bool HasPendingOutput(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context
    ) =>
        TerminalOverlayBounds.DebugVisible(context.App) || Build(current, context) != _previous;
    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        CancellationToken cancellationToken
    ) =>
        throw new NotSupportedException("ReGIS requires the buffered frame writer.");
    public ValueTask PresentAsync(
        GraphicsCommandBuffer current,
        TerminalGraphicsPresentContext context,
        AnsiWriter writer,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        string frame = Build(current, context);
        if (frame != _previous) {
            writer.Write(frame);
            _previous = frame;
        }

        return ValueTask.CompletedTask;
    }

    private string Build(GraphicsCommandBuffer current, TerminalGraphicsPresentContext context) {
        // ReGIS is an overlay independent of text repainting. Clear and rebuild the
        // whole graphics layer, including all charts, so removed plots leave no ink.
        var output = new StringBuilder("\x1bP1pS(A[0,0][799,479])W(VP1L1);");
        var viewport = context.ViewportBounds;
        int columns = Math.Max(1, context.Terminal.Size.Columns), rows = Math.Max(1, context.Terminal.Size.Rows);
        int ScreenX(int x) =>
            Math.Clamp((int)Math.Round(x * 799d / columns), 0, 799);
        int ScreenY(int y) =>
            Math.Clamp((int)Math.Round(y * 479d / rows), 0, 479);
        var windows = context.App.Root.EnumerateVisualsDepthFirst().Where(visual => visual.IsVisible && visual.Parent is WindowLayer layer && !ReferenceEquals(visual, layer.Content)).Select(TerminalOverlayBounds.Window).ToList();
        var debug = TerminalOverlayBounds.Debug(context.App);
        if (debug.Width > 0 && debug.Height > 0) {
            windows.Add(debug);
        }

        windows.AddRange(context.App.Root.EnumerateVisualsDepthFirst().OfType<Hosting.FullscreenChrome>().Select(chrome => chrome.CloseBounds));
        foreach (var command in current) {
            if (command.Content.Source is not Chart chart || !Supports(chart.GraphKind)) {
                continue;
            }

            var bounds = command.CellBounds;
            var clip = ChartClipping.Intersect(
                ChartClipping.Intersect(bounds, command.ClipBounds),
                new(0, 0, viewport.Width, viewport.Height)
            );
            if (clip.Width <= 0 || clip.Height <= 0) {
                continue;
            }

            var exclusions = new List<Rectangle>(windows);
            exclusions.AddRange(chart.AnnotationBounds);
            // The prototype clips by visible cell rectangles. Split these before
            // emitting vectors; opaque ReGIS pixels otherwise cover popup text.
            var visible = new List<Rectangle>
            {
                clip
            };
            foreach (var hole in exclusions) {
                visible = Subtract(visible, hole);
            }

            int left = ScreenX(bounds.X + viewport.X), top = ScreenY(bounds.Y + viewport.Y);
            int width = Math.Max(1, ScreenX(bounds.Right + viewport.X) - left), height = Math.Max(1, ScreenY(bounds.Bottom + viewport.Y) - top);
            var data = chart.GetRenderData(width);
            var range = chart.RenderRange(data);
            BuildGeometry(chart, data, range, width, height);
            foreach (var shape in _geometry.Primitives) {
                if (shape.Kind == ChartPrimitiveKind.Text) {
                    continue;
                }

                var color = shape.Color ?? (chart.GraphKind == GraphKind.Heatmap ? chart.PixelKey.Palette.SequentialColor((Math.Clamp(shape.Shade, (byte)2, (byte)6) - 2) / 4f)
                    : shape.Shade > 0 ? Color.Mix(chart.PixelKey.Background, chart.PixelKey.Ink, (shape.Shade - 1) / 5f)
                    : data.ElementAtOrDefault(shape.Series)?.Color ?? Chart.AutomaticSeriesColor(chart.PixelKey.Palette, chart.PixelKey.Ink, chart.GraphKind, shape.Series));
                var rgb = color.ToRgb();
                int pattern = data.ElementAtOrDefault(shape.Series)?.LineStyle switch {
                    ChartLineStyle.Dashed => 2,
                    ChartLineStyle.Dotted => 4,
                    _ => 1
                };
                output.Append(FormattableString.Invariant($"W(P{pattern}I(R{rgb.R * 100 / 255}G{rgb.G * 100 / 255}B{rgb.B * 100 / 255}));"));
                foreach (var cell in visible) {
                    int x0 = ScreenX(cell.X + viewport.X), y0 = ScreenY(cell.Y + viewport.Y);
                    int x1 = ScreenX(cell.Right + viewport.X) - 1, y1 = ScreenY(cell.Bottom + viewport.Y) - 1;
                    if (shape.Kind == ChartPrimitiveKind.Rectangle) {
                        int a = Math.Max(x0, left + shape.X0), b = Math.Max(y0, top + shape.Y0);
                        int c = Math.Min(x1, left + shape.X1), d = Math.Min(y1, top + shape.Y1);
                        if (a > c || b > d) {
                            continue;
                        }

                        if (a == c || b == d) {
                            Line(output, a, b, c, d, x0, y0, x1, y1);
                        }
                        else if (a < c && b < d) {
                            output.Append(FormattableString.Invariant($"P[{a},{b}]F(V[{c},{b}][{c},{d}][{a},{d}][{a},{b}]);"));
                        }
                    }
                    else {
                        Line(output, left + shape.X0, top + shape.Y0, left + shape.X1, top + shape.Y1, x0, y0, x1, y1);
                    }
                }
            }
        }

        return output.Append("\x1b\\").ToString();
    }

    private void BuildGeometry(Chart chart, ChartDataSeries[] data, ChartRange range, int width, int height) {
        _geometry.Build(data, range, chart.GraphKind, width, height, chart.Thresholds, null, chart.XAxis, chart.YAxis);
        if (ChartGeometry.IsAdditional(chart.GraphKind)) {
            return;
        }

        int X(double value) =>
            (int)Math.Round(ChartMath.Fraction(value, range.MinX, range.MaxX) * (width - 1));
        int Y(double value) =>
            height - 1 - (int)Math.Round(ChartMath.Fraction(value, range.MinY, range.MaxY) * (height - 1));
        var columns = new ChartColumns(data, chart.GraphKind);
        for (int index = 0; index < data.Length; index++) {
            var series = data[index];
            ChartPoint? previous = null;
            foreach (var point in series.Span) {
                if (point.Y is not { } value || !double.IsFinite(point.X) || !double.IsFinite(value)) {
                    previous = null;
                    continue;
                }

                int x = X(point.X), y = Y(value), baseline = Y(Math.Clamp(0, range.MinY, range.MaxY));
                void Segment(
                    int a,
                    int b,
                    int c,
                    int d
                ) =>
                    _geometry.Primitives.Add(new(ChartPrimitiveKind.Line, a, b, c, d, index));
                if (chart.GraphKind == GraphKind.Bars) {
                    var bounds = columns.Bounds(index, point.X, range, width);
                    int a = bounds.X, c = bounds.Right - 1;
                    _geometry.Primitives.Add(new(
                        ChartPrimitiveKind.Rectangle,
                        a,
                        Math.Min(y, baseline),
                        Math.Max(a, c),
                        Math.Max(y, baseline),
                        index
                    ));
                }
                else if (chart.GraphKind == GraphKind.Stem) {
                    Segment(x, baseline, x, y);
                }
                else if (chart.GraphKind == GraphKind.Scatter) {
                    Segment(x - 1, y, x + 1, y);
                    Segment(x, y - 1, x, y + 1);
                }
                else if (previous is { Y: { } before } old) {
                    if (chart.GraphKind == GraphKind.Step) {
                        Segment(X(old.X), Y(before), x, Y(before));
                        Segment(x, Y(before), x, y);
                    }
                    else {
                        Segment(X(old.X), Y(before), x, y);
                    }
                }
                else {
                    Segment(x, y, x, y);
                }

                previous = point;
            }
        }
    }

    private static List<Rectangle> Subtract(List<Rectangle> source, Rectangle hole) {
        var result = new List<Rectangle>();
        foreach (var cell in source) {
            var overlap = ChartClipping.Intersect(cell, hole);
            if (overlap.Width <= 0 || overlap.Height <= 0) {
                result.Add(cell);
                continue;
            }

            if (overlap.Y > cell.Y) {
                result.Add(new(cell.X, cell.Y, cell.Width, overlap.Y - cell.Y));
            }

            if (overlap.Bottom < cell.Bottom) {
                result.Add(new(cell.X, overlap.Bottom, cell.Width, cell.Bottom - overlap.Bottom));
            }

            if (overlap.X > cell.X) {
                result.Add(new(cell.X, overlap.Y, overlap.X - cell.X, overlap.Height));
            }

            if (overlap.Right < cell.Right) {
                result.Add(new(overlap.Right, overlap.Y, cell.Right - overlap.Right, overlap.Height));
            }
        }

        return result;
    }

    private static void Line(
        StringBuilder output,
        double ax,
        double ay,
        double bx,
        double by,
        int left,
        int top,
        int right,
        int bottom
    ) {
        if (left > right || top > bottom) {
            return;
        }

        double dx = bx - ax, dy = by - ay, start = 0, end = 1;
        bool Clip(double p, double q) {
            if (p == 0) {
                return q >= 0;
            }

            double t = q / p;
            if (p < 0) {
                start = Math.Max(start, t);
            }
            else {
                end = Math.Min(end, t);
            }

            return start <= end;
        }

        if (Clip(-dx, ax - left) && Clip(dx, right - ax) && Clip(-dy, ay - top) && Clip(dy, bottom - ay)) {
            output.Append(FormattableString.Invariant($"P[{(int)Math.Round(ax + start * dx)},{(int)Math.Round(ay + start * dy)}]V[{(int)Math.Round(ax + end * dx)},{(int)Math.Round(ay + end * dy)}];"));
        }
    }

    public void Reset() {
        if (_previous.Length > 0) {
            terminal.Write("\x1bP1p\x1b\\");
        }

        _previous = "";
    }

    public void Dispose() =>
        Reset();
}
