namespace fullscreen.Charts;
/// <summary>Projects a 120-second history into numeric samples without putting timestamps in the chart API.</summary>
internal sealed class TimeSeriesChartAdapter {
    private double[] _samples = [];
    private readonly ChartDataSeries[] _data = [new("", [])];
    internal ChartDataSeries[] Project(TimeSeries history, DateTimeOffset end, GraphKind kind, int width) {
        int capacity = kind is GraphKind.Line or GraphKind.Area or GraphKind.Bars ? Math.Max(1, width) : Math.Max(1, history.Count * 2);
        if (_data[0].Points.Length < capacity) {
            _data[0] = new("", new ChartPoint[capacity]);
        }

        var series = _data[0];
        series.Count = 0;
        if (kind is GraphKind.Line or GraphKind.Area or GraphKind.Bars) {
            if (_samples.Length < capacity) {
                _samples = new double[capacity];
            }

            Sample(history, end, _samples.AsSpan(0, capacity));
            for (int index = 0; index < capacity; index++) {
                series.Points[series.Count++] = new(
                    index,
                    capacity == 1 ? 0 : index * 120d / (capacity - 1),
                    double.IsFinite(_samples[index]) ? _samples[index] : null,
                    ""
                );
            }
        }
        else {
            DateTimeOffset previous = default;
            for (int index = 0; index < history.Count; index++) {
                double x = 120 - (end - history.TimeAt(index)).TotalSeconds;
                if (x is < 0 or > 120) {
                    continue;
                }

                if (kind == GraphKind.Step && previous != default
                    && history.TimeAt(index) - previous > TimeSpan.FromSeconds(30)) {
                    series.Points[series.Count++] = new(-1, x, null, "");
                }

                series.Points[series.Count++] = new(index, x, history.ValueAt(index), "");
                previous = history.TimeAt(index);
            }
        }

        return _data;
    }

    internal static void Sample(TimeSeries source, DateTimeOffset end, Span<double> target) {
        target.Fill(double.NaN);
        int previous = -1;
        double previousValue = 0;
        DateTimeOffset previousTime = default;
        for (int index = 0; index < source.Count; index++) {
            if (source.ValueAt(index) is not double value) {
                previous = -1;
                continue;
            }

            var time = source.TimeAt(index);
            double position = 1 - (end - time).TotalSeconds / 120;
            if (position < 0 || position > 1 || target.IsEmpty) {
                previous = -1;
                continue;
            }

            int bucket = (int)Math.Round(position * (target.Length - 1));
            if (previous >= 0 && time - previousTime <= TimeSpan.FromSeconds(30)) {
                for (int x = previous + 1; x < bucket; x++) {
                    double sample = previousValue + (value - previousValue) * (x - previous) / (bucket - previous);
                    target[x] = double.IsNaN(target[x]) ? sample : Math.Max(target[x], sample);
                }
            }

            target[bucket] = double.IsNaN(target[bucket]) ? value : Math.Max(target[bucket], value);
            previous = bucket;
            previousValue = value;
            previousTime = time;
        }
    }
}
