using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;
using static fullscreen.Charts.ChartMath;

namespace fullscreen.Charts;

public partial class Chart {
    private void DrawCells(CellBuffer buffer, ChartDataSeries[] data) {
        if (ChartRadial.IsRadial(GraphKind)) {
            DrawRadialCells(buffer, data);
            return;
        }
        if (ChartGeometry.IsAdditional(GraphKind)) {
            DrawGeometryCells(buffer, data);
            return;
        }

        var plotBounds = PlotBounds;
        int left = 0, top = 0, width = plotBounds.Width, height = plotBounds.Height;
        var range = RenderRange(data);
        double minX = range.MinX, maxX = range.MaxX, minY = range.MinY, maxY = range.MaxY;
        var distributions = GraphKind is GraphKind.Histogram or GraphKind.BoxPlot ? GetDistributions(data, range, width) : [];
        if (GraphKind == GraphKind.Histogram) {
            minY = 0;
            maxY = 1;
            foreach (var distribution in distributions) {
                foreach (int count in distribution.Bins) {
                    maxY = Math.Max(maxY, count);
                }
            }
        }

        int X(double value) =>
            left + (int)Math.Round(Fraction(value, minX, maxX) * (width - 1));
        int Y(double value) =>
            top + height - 1 - (int)Math.Round(Fraction(value, minY, maxY) * (height - 1));
        void Dot(int x, int y, string glyph, Style ink) {
            if (UseSixel) {
                return;
            }

            if (x >= left && x < left + width && y >= top && y < top + height) {
                buffer.WriteText(plotBounds.X + x, plotBounds.Y + y, glyph, ink);
            }
        }

        void Vertical(int x, int from, int to, string glyph, Style ink) {
            for (int y = Math.Max(top, Math.Min(from, to)); y <= Math.Min(top + height - 1, Math.Max(from, to)); y++) {
                Dot(x, y, glyph, ink);
            }
        }

        ChartLineStyle lineStyle;
        void Line(int x0, int y0, int x1, int y1, Style ink, bool area = false) {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
            int step = 0;
            while (true) {
                if (area) {
                    Vertical(x0, Y(0), y0, "░", ink);
                }

                if (lineStyle == ChartLineStyle.Solid || lineStyle == ChartLineStyle.Dashed && step % 6 < 4
                    || lineStyle == ChartLineStyle.Dotted && step % 3 == 0) {
                    Dot(x0, y0, "•", ink);
                }

                step++;
                if (x0 == x1 && y0 == y1) {
                    break;
                }

                int twice = 2 * error;
                if (twice >= dy) {
                    error += dy;
                    x0 += sx;
                }

                if (twice <= dx) {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        var columns = new ChartColumns(data, GraphKind);
        for (int draw = 0; draw < data.Length * (GraphKind == GraphKind.ColumnLine ? 2 : 1); draw++) {
            int seriesIndex = draw % data.Length;
            var series = data[seriesIndex];
            GraphKind kind = ChartColumns.Kind(series, GraphKind, seriesIndex);
            if (GraphKind == GraphKind.ColumnLine && (draw < data.Length) != (kind == GraphKind.Bars)) {
                continue;
            }

            lineStyle = series.LineStyle;
            Style ink = Palette.Body.WithForeground(series.Color ?? DefaultSeriesColor(series.SourceIndex < 0 ? seriesIndex : series.SourceIndex));
            if (GraphKind == GraphKind.Histogram) {
                int[] counts = distributions[seriesIndex].Bins;
                for (int bin = 0; bin < counts.Length; bin++) {
                    int start = left + bin * width / counts.Length, span = Math.Max(1, width / counts.Length - 1);
                    int x0 = start + seriesIndex * span / data.Length, x1 = start + (seriesIndex + 1) * span / data.Length;
                    if (counts[bin] > 0) {
                        for (int x = x0; x < Math.Max(x0 + 1, x1); x++) {
                            Vertical(x, Y(0), Y(counts[bin]), "█", ink);
                        }
                    }
                }

                continue;
            }

            if (GraphKind == GraphKind.BoxPlot) {
                double[] values = distributions[seriesIndex].Sorted;
                if (values.Length == 0) {
                    continue;
                }

                var box = ChartStatistics.SummarizeSorted(values);
                double q1 = box.FirstQuartile, median = box.Median, q3 = box.ThirdQuartile;
                double low = box.LowerWhisker, high = box.UpperWhisker;
                int row = top + Math.Min(
                    height - 1,
                    seriesIndex * Math.Max(1, height / data.Length) + height / (data.Length * 2)
                );
                for (int x = X(low); x <= X(high); x++) {
                    Dot(x, row, x >= X(q1) && x <= X(q3) ? "═" : "─", ink);
                }

                Dot(X(low), row, "│", ink);
                Dot(X(q1), row, "│", ink);
                Dot(X(median), row, "│", ink);
                Dot(X(q3), row, "│", ink);
                Dot(X(high), row, "│", ink);
                foreach (double value in values) {
                    if (value < low || value > high) {
                        Dot(X(value), row, "•", ink);
                    }
                }

                continue;
            }

            ChartPoint? previous = null;
            foreach (var point in series.Span) {
                if (!point.Y.HasValue) {
                    previous = null;
                    continue;
                }

                int x = GraphKind is GraphKind.Bars or GraphKind.ColumnLine ? columns.Center(point.X, range, width) : X(point.X), y = Y(point.Y.Value);
                if (kind == GraphKind.Bars) {
                    var bounds = columns.Bounds(seriesIndex, point.X, range, width);
                    for (int column = bounds.X; column < bounds.Right; column++) {
                        Vertical(column, Y(0), y, "█", ink);
                    }
                }
                else if (kind is GraphKind.Stem or GraphKind.Area) {
                    Vertical(x, Y(0), y, kind == GraphKind.Area ? "░" : "│", ink);
                }

                if (previous is { } before && kind is GraphKind.Line or GraphKind.Area or GraphKind.Step) {
                    int oldX = GraphKind == GraphKind.ColumnLine ? columns.Center(before.X, range, width) : X(before.X), oldY = Y(before.Y!.Value);
                    if (kind == GraphKind.Step) {
                        Line(oldX, oldY, x, oldY, ink);
                        Line(x, oldY, x, y, ink);
                    }
                    else {
                        Line(oldX, oldY, x, y, ink, kind == GraphKind.Area);
                    }
                }

                if (point.Index == SelectedIndex || series.Marker != ChartMarker.None) {
                    Dot(x, y, point.Index == SelectedIndex || series.Marker == ChartMarker.Diamond ? "◆" : "•", ink);
                }

                previous = point;
            }
        }

        if (Thresholds.Count > 0 || Annotations.Count > 0 || SelectedRange is not null) {
            DrawGeometryCells(buffer, data, overlaysOnly: true);
        }
    }

}
