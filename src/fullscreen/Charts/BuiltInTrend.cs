namespace fullscreen.Charts;
/// <summary>Time-history adapter for the reusable numeric Chart control.</summary>
public sealed class BuiltInTrend : Chart {
    private readonly TimeSeriesChartAdapter _adapter = new();
    private TimeSeries? _source;
    private long _version = -1, _revision;
    private DateTimeOffset _end;
    private double _maximum;
    internal override long DataVersion => _revision;
    internal override bool UsesWidthForData => _source is not null && GraphKind is GraphKind.Line or GraphKind.Area or GraphKind.Bars;

    internal override ChartDataSeries[] GetData(int width) =>
        _source is null ? base.GetData(width) : _adapter.Project(_source, _end, GraphKind, width);
    internal override ChartRange Range(ChartDataSeries[] data) =>
        new(0, GraphKind is GraphKind.Histogram or GraphKind.BoxPlot ? _maximum : 120, 0, _maximum);
    /// <summary>Refreshes the trend from timestamped history and updates its range.</summary>
    /// <param name="source">The timestamped history to resample.</param>
    /// <param name="end">The timestamp at the right edge of the history window.</param>
    /// <param name="maximum">The upper chart scale limit.</param>
    public void Update(TimeSeries source, DateTimeOffset end, double maximum) {
        SetColors(PixelInk, PixelBackground);
        if (_source == source && _version == source.Version && _end == end && _maximum == maximum) {
            return;
        }

        _source = source;
        InvalidateDerivedData();
        _version = source.Version;
        _end = end;
        _maximum = maximum;
        Minimum = 0;
        Maximum = maximum;
        _revision++;
    }

    /// <summary>Resamples timestamped history into a caller-provided numeric buffer.</summary>
    /// <param name="source">The timestamped history to resample.</param>
    /// <param name="end">The timestamp at the right edge of the history window.</param>
    /// <param name="target">The destination buffer; each element receives a resampled Y value.</param>
    public static void Sample(
        TimeSeries source,
        DateTimeOffset end,
        Span<double> target
    ) =>
        TimeSeriesChartAdapter.Sample(source, end, target);
}
