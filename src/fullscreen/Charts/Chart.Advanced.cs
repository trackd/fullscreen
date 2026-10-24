using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
    private readonly ChartGeometry _cellGeometry = new();
    private IEnumerable<ChartThreshold>? RenderThresholds => SelectedRange is { } range ? [
        ..Thresholds,
        new(range.MinimumX, Vertical: true),
        new(range.MaximumX, Vertical: true)
    ] : Thresholds.Count == 0 ? null : Thresholds;

    private void DrawGeometryCells(CellBuffer buffer, ChartDataSeries[] data, bool overlaysOnly = false) {
        var plotBounds = PlotBounds;
        _cellGeometry.Build(
            data,
            RenderRange(data),
            overlaysOnly ? GraphKind.Line : GraphKind,
            plotBounds.Width,
            plotBounds.Height,
            RenderThresholds,
            Annotations.Count == 0 ? null : Annotations,
            XAxis,
            YAxis
        );
        foreach (var shape in _cellGeometry.Primitives) {
            if (shape.Kind == ChartPrimitiveKind.Text) {
                continue;
            }

            Color color = shape.Color ?? (shape.Shade > 0 ? Color.Mix(PixelBackground, PixelInk, (shape.Shade - 1) / 5f) : data.ElementAtOrDefault(shape.Series)?.Color ?? DefaultSeriesColor(shape.Series));
            var ink = Palette.Body.WithForeground(color);
            if (shape.Kind == ChartPrimitiveKind.Rectangle) {
                for (int y = shape.Y0; y <= shape.Y1; y++) {
                    for (int x = shape.X0; x <= shape.X1; x++) {
                        buffer.WriteText(plotBounds.X + x, plotBounds.Y + y, "█", ink);
                    }
                }
            }
            else {
                int steps = Math.Max(1, Math.Max(Math.Abs(shape.X1 - shape.X0), Math.Abs(shape.Y1 - shape.Y0)));
                for (int index = 0; index <= steps; index++) {
                    int x = (int)Math.Round(shape.X0 + (shape.X1 - shape.X0) * index / (double)steps), y = (int)Math.Round(shape.Y0 + (shape.Y1 - shape.Y0) * index / (double)steps);
                    buffer.WriteText(
                        plotBounds.X + x,
                        plotBounds.Y + y,
                        shape.X0 == shape.X1 ? "│" : shape.Y0 == shape.Y1 ? "─" : "•",
                        ink
                    );
                }
            }
        }
    }
}

internal sealed partial class SixelPlot {
    private readonly ChartGeometry _geometry = new();
    private void DrawGeometry(
        ChartDataSeries[] data,
        ChartRange range,
        GraphKind kind,
        int width,
        int height,
        IEnumerable<ChartThreshold>? thresholds,
        IEnumerable<ChartAnnotation>? annotations,
        ChartAxis? xAxis,
        ChartAxis? yAxis
    ) {
        _geometry.Build(data, range, kind, width, height, thresholds, annotations, xAxis, yAxis);
        foreach (var shape in _geometry.Primitives) {
            if (shape.Kind == ChartPrimitiveKind.Text) {
                continue;
            }

            byte color = shape.Shade > 0 ? shape.Shade : data.Length > 1 ? (byte)(2 + shape.Series % 5) : (byte)6;
            if (shape.Color is { } requested) {
                var rgb = requested.ToRgb();
                int nearest = int.MaxValue;
                for (byte index = 2; index <= 6; index++) {
                    var candidate = Palette[index].ToRgb();
                    int distance = (candidate.R - rgb.R) * (candidate.R - rgb.R) + (candidate.G - rgb.G) * (candidate.G - rgb.G) + (candidate.B - rgb.B) * (candidate.B - rgb.B);
                    if (distance < nearest) {
                        nearest = distance;
                        color = index;
                    }
                }
            }

            if (shape.Kind == ChartPrimitiveKind.Rectangle) {
                for (int y = shape.Y0; y <= shape.Y1; y++) {
                    for (int x = shape.X0; x <= shape.X1; x++) {
                        SetPixel(x, y, color);
                    }
                }
            }
            else {
                int steps = Math.Max(1, Math.Max(Math.Abs(shape.X1 - shape.X0), Math.Abs(shape.Y1 - shape.Y0)));
                for (int index = 0; index <= steps; index++) {
                    int x = (int)Math.Round(shape.X0 + (shape.X1 - shape.X0) * index / (double)steps), y = (int)Math.Round(shape.Y0 + (shape.Y1 - shape.Y0) * index / (double)steps);
                    if ((uint)x < (uint)width && (uint)y < (uint)height) {
                        SetPixel(x, y, color);
                    }
                }
            }
        }
    }
}
