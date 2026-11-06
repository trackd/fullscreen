namespace fullscreen.Charts;
/// <summary>Bounded UI-owned history. Missing values break lines; timestamps determine horizontal placement.</summary>
/// <param name="capacity">The maximum number of retained samples. Defaults to 2048.</param>
public sealed class TimeSeries(int capacity = 2048) {
    private readonly double[] _values = new double[capacity > 1 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private readonly DateTimeOffset[] _times = new DateTimeOffset[capacity];
    private int _next;
    /// <summary>Gets the revision incremented when history changes.</summary>
    public long Version { get; private set; }
    /// <summary>Gets the active entry count.</summary>
    public int Count { get; private set; }
    /// <summary>Gets the largest available retained sample value.</summary>
    public double Maximum { get; private set; }
    /// <summary>Gets the mean of available numeric history samples.</summary>
    public double Average { get; private set; }
    /// <summary>Gets the rounded display scale, with hysteresis to avoid frequent shrinking.</summary>
    public double Scale { get; private set; } = 1;
    /// <summary>Gets the timestamp of the newest sample, or the default timestamp when empty.</summary>
    public DateTimeOffset Latest => Count == 0 ? default : TimeAt(Count - 1);

    /// <summary>Returns the timestamp at a retained sample index.</summary>
    /// <param name="index">The zero-based sample index, ordered from oldest to newest.</param>
    /// <returns>The timestamp of the indexed sample.</returns>
    public DateTimeOffset TimeAt(int index) =>
        _times[Slot(index)];
    /// <summary>Returns the numeric value at a retained sample index.</summary>
    /// <param name="index">The zero-based sample index, ordered from oldest to newest.</param>
    /// <returns>The sample value, or null for a gap.</returns>
    public double? ValueAt(int index) {
        double value = _values[Slot(index)];
        return double.IsFinite(value) ? value : null;
    }

    private int Slot(int index) =>
        (uint)index < (uint)Count ? (_next - Count + index + _values.Length) % _values.Length : throw new ArgumentOutOfRangeException(nameof(index));
    /// <summary>Removes all retained history samples and resets accumulated statistics.</summary>
    public void Clear() {
        Count = _next = 0;
        Maximum = Average = 0;
        Scale = 1;
        Version++;
    }

    /// <summary>Appends a timestamped value to the bounded 120-second history.</summary>
    /// <remarks>Older timestamps are ignored. Repeating the newest timestamp replaces its sample.
    /// Capacity and time limits both discard the oldest retained samples.</remarks>
    /// <param name="value">The nonnegative sample value; null, negative, and nonfinite values leave
    /// gaps.</param>
    /// <param name="timestamp">The sample timestamp.</param>
    public void Add(double? value, DateTimeOffset timestamp) {
        if (Count > 0 && timestamp < Latest) {
            return;
        }

        Version++;
        if (Count > 0 && timestamp == Latest) {
            _next = (_next - 1 + _values.Length) % _values.Length;
        }
        else {
            Count = Math.Min(Count + 1, _values.Length);
        }

        _times[_next] = timestamp;
        _values[_next] = value is >= 0 && double.IsFinite(value.Value) ? value.Value : double.NaN;
        _next = (_next + 1) % _values.Length;
        while (Count > 1 && timestamp - TimeAt(0) > TimeSpan.FromSeconds(120)) {
            Count--;
        }

        double sum = 0;
        int valid = 0;
        Maximum = 0;
        for (int i = 0; i < Count; i++) {
            if (ValueAt(i) is double sample) {
                sum += sample;
                valid++;
                Maximum = Math.Max(Maximum, sample);
            }
        }

        Average = valid == 0 ? 0 : sum / valid;
        // Grow immediately; shrink only after a full range drops below one quarter of the scale.
        if (Maximum > Scale || Maximum < Scale / 4) {
            Scale = NiceScale(Maximum);
        }
    }

    /// <summary>Finds the retained sample closest to a timestamp.</summary>
    /// <param name="time">The timestamp used to locate the nearest retained sample.</param>
    /// <returns>The closest sample index, or -1 when the history is empty.</returns>
    public int Nearest(DateTimeOffset time) {
        int best = -1;
        long distance = long.MaxValue;
        for (int i = 0; i < Count; i++) {
            long delta = Math.Abs((TimeAt(i) - time).Ticks);
            if (delta < distance) {
                distance = delta;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Rounds a positive chart scale to a readable axis limit.</summary>
    /// <param name="value">The largest value that the rounded axis limit must include.</param>
    /// <returns>A rounded scale suitable for chart bounds.</returns>
    public static double NiceScale(double value) {
        if (!double.IsFinite(value) || value <= 1) {
            return 1;
        }

        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double fraction = value / power;
        return Math.Min(double.MaxValue, (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power);
    }
}
