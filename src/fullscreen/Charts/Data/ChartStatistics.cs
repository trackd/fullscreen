namespace fullscreen.Charts;

internal static class ChartStatistics {
    internal static int[] Histogram(ReadOnlySpan<ChartPoint> points, double minimum, double maximum, int count) {
        var bins = new int[count];
        foreach (var point in points) {
            if (point.Y.HasValue) {
                bins[Math.Min(count - 1, (int)(ChartMath.Fraction(point.Y.Value, minimum, maximum) * count))]++;
            }
        }

        return bins;
    }

    // Sort only caller-owned scratch storage; cached cell distributions are already sorted.
    internal static BoxSummary Summarize(Span<double> values) {
        if (values.IsEmpty) {
            throw new ArgumentException("Expected observations.", nameof(values));
        }

        values.Sort();
        return SummarizeSorted(values);
    }

    // Linear-interpolated quantiles (R type 7) and observed Tukey whiskers within 1.5 IQR.
    internal static BoxSummary SummarizeSorted(ReadOnlySpan<double> values) {
        if (values.IsEmpty) {
            throw new ArgumentException("Expected observations.", nameof(values));
        }

        double q1 = Quantile(values, .25), median = Quantile(values, .5), q3 = Quantile(values, .75), iqr = q3 - q1;
        int lower = 0, upper = values.Length - 1;
        while (lower < upper && values[lower] < q1 - 1.5 * iqr) {
            lower++;
        }

        while (upper > lower && values[upper] > q3 + 1.5 * iqr) {
            upper--;
        }

        return new(q1, median, q3, values[lower], values[upper]);
    }

    private static double Quantile(ReadOnlySpan<double> sorted, double fraction) {
        double position = (sorted.Length - 1) * fraction;
        int index = (int)position;
        double weight = position - index;
        return sorted[index] * (1 - weight) + sorted[Math.Min(index + 1, sorted.Length - 1)] * weight;
    }
}

internal readonly record struct BoxSummary(
    double FirstQuartile,
    double Median,
    double ThirdQuartile,
    double LowerWhisker,
    double UpperWhisker
);
