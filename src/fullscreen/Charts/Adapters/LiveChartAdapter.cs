namespace fullscreen.Charts;
/// <summary>A thread-safe bounded producer buffer; call Tick on the TUI thread to update its chart.</summary>
public sealed class LiveChartAdapter : IDisposable {
    private readonly Lock _gate = new();
    private readonly Queue<(double X, double? Value, object? Source)> _samples = new();
    private long _version, _applied = -1;
    private double _lastX;
    private bool _following = true, _applying, _disposed;
    /// <summary>Gets the numeric chart receiving samples.</summary>
    public Chart Chart { get; }
    /// <summary>Gets the maximum number of retained samples.</summary>
    public int Capacity { get; }
    /// <summary>Gets the retained sample duration, or null to use only the capacity limit.</summary>
    public TimeSpan? Window { get; }
    /// <summary>Gets or sets whether live updates are paused.</summary>
    public bool Paused { get; set; }

    /// <summary>Gets or sets whether the viewport follows newly appended samples.</summary>
    public bool FollowLatest {
        get => _following;
        set {
            _following = value;
            _applied = -1;
        }
    }

    /// <summary>Gets the number of samples currently retained in the producer buffer.</summary>
    public int Count {
        get {
            lock (_gate) {
                return _samples.Count;
            }
        }
    }

    /// <summary>Initializes a new LiveChartAdapter instance.</summary>
    /// <param name="chart">The numeric chart receiving samples.</param>
    /// <param name="capacity">The maximum number of retained samples. Defaults to 1000.</param>
    /// <param name="window">The retained sample duration, or null to use only the capacity limit. Defaults
    /// to null.</param>
    public LiveChartAdapter(Chart chart, int capacity = 1000, TimeSpan? window = null) {
        ArgumentNullException.ThrowIfNull(chart);
        if (capacity is < 1 or > 1000000 || window <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Chart = chart;
        Capacity = capacity;
        Window = window;
        chart.ViewChanged += OnViewChanged;
        chart.PauseRequested += TogglePause;
        chart.RangeSelected += OnRangeSelected;
    }

    private void OnViewChanged() {
        if (!_applying) {
            _following = false;
        }
    }

    private void TogglePause() =>
        Paused = !Paused;
    private void OnRangeSelected(ChartSelectedRange? range) {
        if (range is not null) {
            Paused = true;
            _following = false;
        }
    }

    /// <summary>Append a time sample. Null or nonfinite values leave gaps.</summary>
    /// <param name="value">The sample&apos;s Y value; null and nonfinite values leave gaps.</param>
    /// <param name="timestamp">The sample timestamp, or null to use the current UTC time. Defaults to null.</param>
    /// <param name="source">The original object associated with this sample; it is retained without mutation. Defaults
    /// to null.</param>
    public void Append(
        double? value,
        DateTimeOffset? timestamp = null,
        object? source = null
    ) =>
        Append(
            (timestamp ?? DateTimeOffset.UtcNow).UtcTicks / (double)TimeSpan.TicksPerSecond,
            value,
            source
        );
    /// <summary>Append an explicit numeric X coordinate. A configured window interprets X as seconds.</summary>
    /// <param name="x">The numeric X coordinate in nondecreasing order.</param>
    /// <param name="value">The sample&apos;s Y value; null and nonfinite values leave gaps.</param>
    /// <param name="source">The original object associated with this sample; it is retained without mutation. Defaults
    /// to null.</param>
    public void Append(double x, double? value, object? source = null) {
        if (!double.IsFinite(x)) {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_samples.Count > 0 && x < _lastX) {
                throw new ArgumentException("Live X values must be nondecreasing.", nameof(x));
            }

            _lastX = x;
            _samples.Enqueue((x, value.HasValue && double.IsFinite(value.Value) ? value : null, source));
            while (_samples.Count > Capacity
                || Window is { } window && _samples.Count > 1 && x - _samples.Peek().X > window.TotalSeconds) {
                _samples.Dequeue();
            }

            _version++;
        }
    }

    /// <summary>Copies pending producer samples to the chart on the TUI thread.</summary>
    /// <returns>True when new samples were applied; false while paused or unchanged.</returns>
    public bool Tick() {
        if (Paused) {
            return false;
        }

        (double X, double? Value, object? Source)[] samples;
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_applied == _version) {
                return false;
            }

            samples = [.. _samples];
            _applied = _version;
        }

        Chart.SetData([new(
            "",
            [.. samples.Select((
                sample,
                index
            ) => new ChartPoint(index, sample.X, sample.Value, Chart.XAxis.FormatValue(sample.X)) { Source = sample.Source })]
        )]);
        if (_following && samples.Length > 0) {
            _applying = true;
            try {
                Chart.SetView(Window is { } window ? samples[^1].X - window.TotalSeconds : samples[0].X, samples[^1].X);
            }
            finally {
                _applying = false;
            }
        }

        return true;
    }

    /// <summary>Resumes chart updates and restores following of the newest samples.</summary>
    public void Resume() {
        Paused = false;
        FollowLatest = true;
    }

    /// <inheritdoc/>
    public void Dispose() {
        lock (_gate) {
            _disposed = true;
            _samples.Clear();
        }

        Chart.ViewChanged -= OnViewChanged;
        Chart.PauseRequested -= TogglePause;
        Chart.RangeSelected -= OnRangeSelected;
    }
}
