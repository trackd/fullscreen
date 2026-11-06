namespace fullscreen.Charts;

internal readonly record struct NumericChartSamples(ChartDataSeries Series, ChartRange Range) {
    internal int Count => Series.Count;

    internal double? ValueAt(int index) =>
        Series.Points[index].Y;
    internal double PositionAt(int index) =>
        ChartMath.Fraction(Series.Points[index].X, Range.MinX, Range.MaxX);
}

internal static class NumericChartSampling {
    internal static void Sample(NumericChartSamples source, Span<double> target, bool step = false) {
        target.Fill(double.NaN);
        int previous = -1;
        double previousValue = 0;
        for (int index = 0; index < source.Count; index++) {
            if (source.ValueAt(index) is not double value || target.IsEmpty) {
                previous = -1;
                continue;
            }

            int bucket = (int)Math.Round(source.PositionAt(index) * (target.Length - 1));
            if (previous >= 0) {
                int direction = bucket >= previous ? 1 : -1;
                for (int x = previous; x != bucket; x += direction) {
                    double sample = step ? previousValue : previousValue + (value - previousValue) * (x - previous) / (bucket - previous);
                    target[x] = double.IsNaN(target[x]) ? sample : Math.Max(target[x], sample);
                }
            }

            target[bucket] = double.IsNaN(target[bucket]) ? value : Math.Max(target[bucket], value);
            previous = bucket;
            previousValue = value;
        }
    }
}
