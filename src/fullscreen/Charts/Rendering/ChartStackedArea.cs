namespace fullscreen.Charts;

/// <summary>Aligned nonnegative area composition shared by cells, raster and exact source picking.</summary>
internal static class ChartStackedArea {
    internal static bool Supports(GraphKind kind) => kind is GraphKind.StackedArea or GraphKind.PercentStackedArea;

    internal static void ValidateOptions(GraphKind kind, double? minimum, double? maximum, ChartScale xScale, ChartScale yScale) {
        if (!Supports(kind)) {
            return;
        }
        if (xScale == ChartScale.Logarithmic || yScale != ChartScale.Linear) {
            throw new ArgumentException("Stacked areas require non-logarithmic X coordinates and a linear Y axis.");
        }
        if (kind == GraphKind.PercentStackedArea && (minimum is not null || maximum is not null)) {
            throw new ArgumentException("PercentStackedArea uses a fixed 0 to 100 percent Y range.");
        }
    }

    internal static void Validate(ChartDataSeries[] data) {
        int count = data.Length == 0 ? 0 : data[0].Count;
        foreach (var series in data) {
            if (series.Count != count) {
                throw new ArgumentException("Stacked areas require equal-length series with matching ordered X coordinates; use null for missing components.");
            }
            for (int index = 0; index < count; index++) {
                var point = series.Points[index];
                if (!double.IsFinite(point.X) || point.X != data[0].Points[index].X
                    || index > 0 && point.X <= series.Points[index - 1].X) {
                    throw new ArgumentException("Stacked areas require matching finite, strictly increasing X coordinates.");
                }
                if (point.Y is { } value && (!double.IsFinite(value) || value < 0)) {
                    throw new ArgumentException("Stacked areas require finite nonnegative component values.");
                }
            }
        }
    }

    private static bool Total(ChartDataSeries[] data, int index, bool percent, out double scale, out double total) {
        scale = percent ? 0 : 1;
        total = 0;
        foreach (var series in data) {
            if (!series.Visible) {
                continue;
            }
            if (series.Points[index].Y is not { } value) {
                return false;
            }
            if (percent) {
                scale = Math.Max(scale, value);
            }
        }
        if (scale == 0) {
            return false;
        }
        foreach (var series in data) {
            if (!series.Visible) {
                continue;
            }
            total += series.Points[index].Y!.Value / scale;
            if (!double.IsFinite(total)) {
                throw new ArgumentException("Stacked area totals must fit in finite double values.");
            }
        }
        return true;
    }

    internal static ChartRange Range(ChartDataSeries[] data, GraphKind kind, double? minimum, double? maximum) {
        Validate(data);
        double high = 0;
        int count = data.Length == 0 ? 0 : data[0].Count;
        bool percent = kind == GraphKind.PercentStackedArea;
        for (int index = 0; index < count; index++) {
            if (Total(data, index, percent, out _, out var total)) {
                high = Math.Max(high, total);
            }
        }
        if (percent && (minimum is not null || maximum is not null)) {
            throw new ArgumentException("PercentStackedArea uses a fixed 0 to 100 percent Y range.");
        }
        double low = minimum ?? 0;
        high = maximum ?? (percent ? 100 : Math.Max(1, high));
        if (!double.IsFinite(low) || !double.IsFinite(high) || low > high) {
            throw new ArgumentException("Stacked area Y bounds must be finite and ordered.");
        }
        return new(count == 0 ? 0 : data[0].Points[0].X, count == 0 ? 1 : data[0].Points[count - 1].X, low, high);
    }

    internal static void Build(List<ChartPrimitive> shapes, ChartDataSeries[] data, ChartRange range,
        GraphKind kind, int width, int height) {
        Validate(data);
        int count = data.Length == 0 ? 0 : data[0].Count;
        if (count == 0) {
            return;
        }
        bool percent = kind == GraphKind.PercentStackedArea;
        int segment = 0;
        double singleX = data[0].Points[0].X;
        int singleColumn = (int)Math.Round(ChartMath.Fraction(singleX, range.MinX, range.MaxX) * (width - 1));
        for (int column = 0; column < width; column++) {
            if (count == 1 && column != singleColumn) {
                continue;
            }
            double fraction = width == 1 ? .5 : column / (double)(width - 1);
            double x = count == 1 ? singleX : range.MinX * (1 - fraction) + range.MaxX * fraction;
            if (x < range.MinX || x > range.MaxX) {
                continue;
            }
            if (x < data[0].Points[0].X || x > data[0].Points[count - 1].X) {
                continue;
            }
            while (segment + 1 < count && data[0].Points[segment + 1].X <= x) {
                segment++;
            }
            int left = segment;
            int next = data[0].Points[left].X == x ? left : Math.Min(count - 1, left + 1);
            bool leftComplete = Total(data, left, percent, out var leftScale, out var leftTotal);
            bool rightComplete = Total(data, next, percent, out var rightScale, out var rightTotal);
            if (!leftComplete || !rightComplete) {
                if (!leftComplete && !rightComplete) {
                    continue;
                }
                int sample = leftComplete ? left : next;
                double sampleX = data[0].Points[sample].X;
                if (sampleX < range.MinX || sampleX > range.MaxX
                    || column != (int)Math.Round(ChartMath.Fraction(sampleX, range.MinX, range.MaxX) * (width - 1))) {
                    continue;
                }
                if (leftComplete) {
                    rightScale = leftScale;
                    rightTotal = leftTotal;
                }
                else {
                    leftScale = rightScale;
                    leftTotal = rightTotal;
                }
                left = next = sample;
            }
            double t = left == next ? 0 : ChartMath.Fraction(x, data[0].Points[left].X, data[0].Points[next].X);
            double baseline = 0;
            double leftCumulative = 0, rightCumulative = 0;
            for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
                var series = data[seriesIndex];
                if (!series.Visible) {
                    continue;
                }
                double a = series.Points[left].Y!.Value, b = series.Points[next].Y!.Value;
                double end;
                if (percent) {
                    leftCumulative += a / leftScale;
                    rightCumulative += b / rightScale;
                    double leftEnd = leftCumulative / leftTotal * 100, rightEnd = rightCumulative / rightTotal * 100;
                    end = leftEnd + (rightEnd - leftEnd) * t;
                }
                else {
                    end = baseline + a * (1 - t) + b * t;
                }
                if (!double.IsFinite(end)) {
                    throw new ArgumentException("Stacked area totals must fit in finite double values.");
                }
                if (end > baseline && end > range.MinY && baseline < range.MaxY) {
                    int top = Math.Clamp((int)Math.Ceiling((1 - ChartMath.Fraction(end, range.MinY, range.MaxY)) * height), 0, height);
                    int bottom = Math.Clamp((int)Math.Ceiling((1 - ChartMath.Fraction(baseline, range.MinY, range.MaxY)) * height) - 1, -1, height - 1);
                    if (top <= bottom) {
                        shapes.Add(new(ChartPrimitiveKind.Rectangle, column, top, column, bottom,
                            seriesIndex, Point: t < .5 ? left : next));
                    }
                }
                baseline = end;
            }
        }
    }
}
