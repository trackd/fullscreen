using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
    private void DrawCells(CellBuffer buffer, ChartDataSeries[] data) {
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

        ChartLineStyle lineStyle = ChartLineStyle.Solid;
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

        for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
            var series = data[seriesIndex];
            lineStyle = series.LineStyle;
            Style ink = Ink(seriesIndex);
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

                double q1 = Quantile(values, .25), median = Quantile(values, .5), q3 = Quantile(values, .75), iqr = q3 - q1;
                double low = values.First(value => value >= q1 - 1.5 * iqr), high = values.Last(value => value <= q3 + 1.5 * iqr);
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

                int x = GraphKind == GraphKind.Bars ? BarCenter(point.X, minX, maxX, width, series.Count) : X(point.X), y = Y(point.Y.Value);
                if (GraphKind == GraphKind.Bars) {
                    int span = Math.Max(1, width / Math.Max(1, series.Count) - 1);
                    int start = Math.Clamp(x - span / 2, left, left + width - span);
                    int from = start + seriesIndex * span / data.Length, to = start + (seriesIndex + 1) * span / data.Length;
                    for (int column = from; column < Math.Max(from + 1, to); column++) {
                        Vertical(column, Y(0), y, "█", ink);
                    }
                }
                else if (GraphKind is GraphKind.Stem or GraphKind.Area) {
                    Vertical(x, Y(0), y, GraphKind == GraphKind.Area ? "░" : "│", ink);
                }

                if (previous is { } before && GraphKind is GraphKind.Line or GraphKind.Area or GraphKind.Step) {
                    int oldX = X(before.X), oldY = Y(before.Y!.Value);
                    if (GraphKind == GraphKind.Step) {
                        Line(oldX, oldY, x, oldY, ink);
                        Line(x, oldY, x, y, ink);
                    }
                    else {
                        Line(oldX, oldY, x, y, ink, GraphKind == GraphKind.Area);
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

    internal static double Fraction(double value, double minimum, double maximum) {
        if (minimum == maximum) {
            return .5;
        }

        double scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        return Math.Clamp((value / scale - minimum / scale) / (maximum / scale - minimum / scale), 0, 1);
    }

    internal static int BarCenter(double value, double minimum, double maximum, int width, int count) {
        int span = Math.Max(1, width / Math.Max(1, count) - 1);
        return span / 2 + (int)Math.Round(Fraction(value, minimum, maximum) * (width - span));
    }

    internal static int[] Histogram(ReadOnlySpan<ChartPoint> points, double minimum, double maximum, int count) {
        var bins = new int[count];
        foreach (var point in points) {
            if (point.Y.HasValue) {
                bins[Math.Min(count - 1, (int)(Fraction(point.Y.Value, minimum, maximum) * count))]++;
            }
        }

        return bins;
    }

    internal static double Quantile(double[] sorted, double fraction) {
        double index = (sorted.Length - 1) * fraction;
        int start = (int)index;
        return sorted[start] * (1 - (index - start)) + sorted[Math.Min(start + 1, sorted.Length - 1)] * (index - start);
    }
}
