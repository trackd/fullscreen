using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

internal sealed partial class SixelPlot {
    // Every caller supplies numeric coordinates. Object and history semantics live in adapters.
    internal void Draw(
        ChartDataSeries[] data,
        ChartRange range,
        int width,
        int height,
        Color ink,
        Color background,
        Themes.ThemePalette? theme,
        GraphKind kind,
        int selected = -1,
        int inspection = -1,
        IEnumerable<ChartThreshold>? thresholds = null,
        IEnumerable<ChartAnnotation>? annotations = null,
        ChartAxis? xAxis = null,
        ChartAxis? yAxis = null,
        double reversalAmount = 1,
        bool flameReverse = false,
        int flameFocus = -1,
        int flameFrameHeight = 1,
        int taskOffset = 0
    ) {
        if (data.Length == 1 && data[0].Color is { } singleColor) {
            ink = singleColor;
        }

        Initialize(width, height, ink, background, kind);
        if (kind is GraphKind.Waterfall or GraphKind.Kagi) {
            Palette[2] = ink;
            Palette[3] = theme?.Success ?? ink;
            Palette[6] = theme?.Error ?? ink;
            _hasPalette = false;
        }
        if (kind == GraphKind.Gantt) {
            Palette[2] = ink;
            Palette[3] = theme?.Success ?? ink;
            _hasPalette = false;
        }
        if (ChartRadial.IsRadial(kind)) {
            DrawRadial(data, range, kind, theme, selected);
            return;
        }
        bool multiple = data.Length > 1;
        if (kind == GraphKind.Flamegraph && theme is not null) {
            for (byte shade = 2; shade <= 6; shade++) {
                Palette[shade] = Chart.FlameColor(theme, shade);
            }
            _hasPalette = false;
        }
        if (multiple && (theme is not null || Chart.UsesStackedPalette(kind))
            && kind != GraphKind.Heatmap && !ChartTopology.Supports(kind)) {
            var palette = theme ?? Themes.ThemePalette.Default;
            for (int index = 0; index < 5; index++) {
                Palette[2 + index] = Chart.AutomaticSeriesColor(palette, ink, kind, index);
            }
            for (int index = 0; index < Math.Min(5, data.Length); index++) {
                if (data[index].Color is { } color) {
                    Palette[2 + index] = color;
                }
            }

            _hasPalette = false;
        }

        if (ChartGeometry.IsAdditional(kind)) {
            DrawGeometry(data, range, kind, width, height, thresholds, annotations, xAxis, yAxis, selected, reversalAmount, flameReverse, flameFocus, flameFrameHeight, taskOffset);
            return;
        }

        double minY = range.MinY, maxY = range.MaxY;
        int X(double value) =>
            (int)Math.Round(ChartMath.Fraction(value, range.MinX, range.MaxX) * (width - 1));
        double Y(double value) =>
            (1 - ChartMath.Fraction(value, minY, maxY)) * (height - 1);
        int baseline = (int)Math.Round(Y(0));
        int binCount = Math.Clamp(width / 16, 1, _bins.Length), peak = 1;
        if (kind == GraphKind.Histogram) {
            BinCount = binCount;
            foreach (var series in data) {
                Array.Clear(_bins);
                foreach (var point in series.Span) {
                    if (point.Y is { } value && value >= range.MinX && value <= range.MaxX) {
                        peak = Math.Max(
                            peak,
                            ++_bins[Math.Min(binCount - 1, (int)(ChartMath.Fraction(value, range.MinX, range.MaxX) * binCount))]
                        );
                    }
                }
            }
        }

        var columns = new ChartColumns(data, kind);
        for (int draw = 0; draw < data.Length * (kind == GraphKind.ColumnLine ? 2 : 1); draw++) {
            int index = draw % data.Length;
            var series = data[index];
            GraphKind seriesKind = ChartColumns.Kind(series, kind, index);
            if (kind == GraphKind.ColumnLine && (draw < data.Length) != (seriesKind == GraphKind.Bars)) {
                continue;
            }

            byte color = multiple ? (byte)(2 + index % 5) : (byte)6;
            void SeriesPaint(int x, int y, byte shade) {
                if (kind == GraphKind.ColumnLine && (uint)x < (uint)Width && (uint)y < (uint)Height) {
                    SetPixel(x, y, shade);
                }
                else {
                    Paint(x, y, shade);
                }
            }

            void Segment(double x0, double y0, double x1, double y1) {
                if (!multiple && series.LineStyle == ChartLineStyle.Solid && kind != GraphKind.ColumnLine) {
                    Stroke(x0, y0, x1, y1);
                    return;
                }

                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))));
                for (int step = 0; step <= steps; step++) {
                    if (series.LineStyle == ChartLineStyle.Dashed && step % 12 >= 8
                        || series.LineStyle == ChartLineStyle.Dotted && step % 5 != 0) {
                        continue;
                    }

                    double t = (double)step / steps;
                    SeriesPaint((int)Math.Round(x0 + (x1 - x0) * t), (int)Math.Round(y0 + (y1 - y0) * t), color);
                }
            }

            if (kind == GraphKind.Histogram) {
                Array.Clear(_bins);
                foreach (var point in series.Span) {
                    if (point.Y is { } value && value >= range.MinX && value <= range.MaxX) {
                        _bins[Math.Min(binCount - 1, (int)(ChartMath.Fraction(value, range.MinX, range.MaxX) * binCount))]++;
                    }
                }

                for (int bin = 0; bin < binCount; bin++) {
                    if (_bins[bin] == 0) {
                        continue;
                    }

                    int left = bin * width / binCount, span = Math.Max(1, width / binCount - 1);
                    int from = left + index * span / Math.Max(1, data.Length), to = left + (index + 1) * span / Math.Max(1, data.Length);
                    int top = height - 1 - (int)Math.Round((double)_bins[bin] / peak * (height - 1));
                    for (int x = from; x < Math.Max(from + 1, to); x++) {
                        for (int y = top; y < height; y++) {
                            Paint(x, y, color);
                        }
                    }
                }

                continue;
            }

            if (kind == GraphKind.BoxPlot) {
                if (_observations.Length < series.Count) {
                    _observations = new double[series.Count];
                }

                int count = 0;
                foreach (var point in series.Span) {
                    if (point.Y is { } value) {
                        _observations[count++] = value;
                    }
                }

                if (count == 0) {
                    continue;
                }

                var box = ChartStatistics.Summarize(_observations.AsSpan(0, count));
                int center = (2 * index + 1) * height / (2 * Math.Max(1, data.Length));
                int half = Math.Max(0, height / Math.Max(4, data.Length * 4));
                int top = Math.Max(0, center - half), bottom = Math.Min(height - 1, center + half);
                Segment(X(box.LowerWhisker), center, X(box.UpperWhisker), center);
                Segment(X(box.FirstQuartile), top, X(box.ThirdQuartile), top);
                Segment(X(box.FirstQuartile), bottom, X(box.ThirdQuartile), bottom);
                foreach (double value in _observations.AsSpan(0, count)) {
                    if (value < box.LowerWhisker || value > box.UpperWhisker) {
                        Paint(X(value), center, color);
                    }
                }

                Segment(X(box.LowerWhisker), top, X(box.LowerWhisker), bottom);
                Segment(X(box.FirstQuartile), top, X(box.FirstQuartile), bottom);
                Segment(X(box.Median), top, X(box.Median), bottom);
                Segment(X(box.ThirdQuartile), top, X(box.ThirdQuartile), bottom);
                Segment(X(box.UpperWhisker), top, X(box.UpperWhisker), bottom);
                continue;
            }

            ChartPoint? previous = null;
            foreach (var point in series.Span) {
                if (point.Y is not { } value) {
                    previous = null;
                    continue;
                }

                int x = kind is GraphKind.Bars or GraphKind.ColumnLine ? columns.Center(point.X, range, width) : X(point.X);
                double y = Y(value);
                if (seriesKind is GraphKind.Area or GraphKind.Stem or GraphKind.Bars) {
                    var bounds = seriesKind == GraphKind.Bars ? columns.Bounds(index, point.X, range, width) : new XenoAtom.Terminal.UI.Geometry.Rectangle(x, 0, 1, 1);
                    for (int column = bounds.X; column < bounds.Right; column++) {
                        if (kind != GraphKind.ColumnLine) {
                            FillColumn(column, Math.Min(baseline, (int)Math.Ceiling(y)), Math.Max(baseline, (int)Math.Ceiling(y)),
                                multiple ? color : seriesKind == GraphKind.Area ? (byte)2 : seriesKind == GraphKind.Bars ? (byte)5 : color);
                            continue;
                        }

                        for (int row = Math.Min(baseline, (int)Math.Ceiling(y)); row <= Math.Max(baseline, (int)Math.Ceiling(y)); row++) {
                            SeriesPaint(
                                column,
                                row,
                                multiple ? color : seriesKind == GraphKind.Area ? (byte)2 : seriesKind == GraphKind.Bars ? (byte)5 : color
                            );
                        }
                    }
                }

                if (previous is { } before && seriesKind is GraphKind.Line or GraphKind.Area or GraphKind.Step) {
                    int oldX = kind == GraphKind.ColumnLine ? columns.Center(before.X, range, width) : X(before.X);
                    if (seriesKind == GraphKind.Area) {
                        double oldY = Y(before.Y!.Value);
                        for (int column = Math.Min(oldX, x); column <= Math.Max(oldX, x); column++) {
                            double edge = oldX == x ? y : oldY + (y - oldY) * (column - oldX) / (x - oldX);
                            FillColumn(column, Math.Min(baseline, (int)Math.Ceiling(edge)), Math.Max(baseline, (int)Math.Ceiling(edge)),
                                multiple ? color : (byte)2);
                        }
                    }

                    if (seriesKind == GraphKind.Step) {
                        Segment(oldX, Y(before.Y!.Value), x, Y(before.Y.Value));
                        Segment(x, Y(before.Y.Value), x, y);
                    }
                    else {
                        Segment(oldX, Y(before.Y!.Value), x, y);
                    }
                }

                if ((seriesKind != GraphKind.Bars && series.Marker != ChartMarker.None) || point.Index == selected) {
                    if (!multiple) {
                        Dot(x, y);
                    }
                    else {
                        SeriesPaint(x, (int)Math.Round(y), color);
                    }

                    if (kind == GraphKind.Scatter || point.Index == selected) {
                        for (int dx = -2; dx <= 2; dx++) {
                            for (int dy = -2; dy <= 2; dy++) {
                                if (dx * dx + dy * dy <= 4) {
                                    SeriesPaint(x + dx, (int)Math.Round(y) + dy, color);
                                }
                            }
                        }
                    }
                }

                previous = point;
            }
        }

        if (thresholds is not null || annotations is not null) {
            DrawGeometry(data, range, GraphKind.Line, width, height, thresholds, annotations, xAxis, yAxis);
        }

        if (kind is not (GraphKind.Histogram or GraphKind.BoxPlot) && (uint)inspection < (uint)width) {
            for (int y = 0; y < height; y++) {
                SetPixel(inspection, y, 6);
            }
        }
    }
}
