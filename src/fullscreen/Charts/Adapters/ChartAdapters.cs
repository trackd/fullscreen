using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Collections;

namespace fullscreen.Charts;
/// <summary>Links existing XenoAtom chart data to the shared chart features without reparenting the source control.</summary>
public static class ChartAdapters {
    /// <summary>Wraps a native chart control with shared chart interaction and presentation
    /// features.</summary>
    /// <param name="source">The native XenoAtom chart to observe without reparenting it.</param>
    /// <returns>The adapter that remains connected to the native control.</returns>
    public static NativeChartAdapter Enhance(LineChart source) {
        ArgumentNullException.ThrowIfNull(source);
        var chart = new LinkedValuesChart(source, source.Values, () => (source.Minimum, source.Maximum), (min, max) => {
            source.Minimum = min;
            source.Maximum = max;
        }) {
            ShowAxes = true,
            ShowLegend = true
        };
        chart.Synchronize();
        return chart;
    }

    /// <summary>Wraps a native chart control with shared chart interaction and presentation
    /// features.</summary>
    /// <param name="source">The native XenoAtom chart to observe without reparenting it.</param>
    /// <returns>The adapter that remains connected to the native control.</returns>
    public static NativeChartAdapter Enhance(Sparkline source) {
        ArgumentNullException.ThrowIfNull(source);
        var chart = new LinkedValuesChart(source, source.Values, () => (source.Minimum, source.Maximum), (min, max) => {
            source.Minimum = min;
            source.Maximum = max;
        }) {
            ShowAxes = true
        };
        chart.Synchronize();
        return chart;
    }

    /// <summary>Wraps a native chart control with shared chart interaction and presentation
    /// features.</summary>
    /// <param name="source">The native XenoAtom chart to observe without reparenting it.</param>
    /// <returns>The adapter that remains connected to the native control.</returns>
    public static NativeChartAdapter Enhance(BarChart source) {
        ArgumentNullException.ThrowIfNull(source);
        var chart = new NativeItemsChart(source, () => source.Items.Count, index => {
            var item = source.Items[index];
            return new(item.Value, Label(item.Label), item.BarColor, item);
        }, () => (source.Minimum, source.Maximum), (min, max) => {
            source.Minimum = min;
            source.Maximum = max;
        }) {
            GraphKind = GraphKind.HorizontalBars,
            ShowAxes = true,
            YAxis = new() {
                Scale = ChartScale.Category
            }
        };
        chart.Synchronize();
        return chart;
    }

    /// <summary>Wraps a native chart control with shared chart interaction and presentation
    /// features.</summary>
    /// <param name="source">The native XenoAtom chart to observe without reparenting it.</param>
    /// <returns>The adapter that remains connected to the native control.</returns>
    public static NativeChartAdapter Enhance(BreakdownChart source) {
        ArgumentNullException.ThrowIfNull(source);
        var chart = new NativeItemsChart(source, () => source.Segments.Count, index => {
            var item = source.Segments[index];
            return new(item.Value, Label(item.Label), item.Color, item);
        }) {
            GraphKind = GraphKind.StackedBars,
            ShowAxes = true,
            ShowLegend = true
        };
        chart.Synchronize();
        return chart;
    }

    /// <summary>Call once per UI tick when hosting adapters outside FullscreenHost.</summary>
    /// <param name="root">The root visual whose descendants are inspected.</param>
    public static void Synchronize(Visual root) {
        foreach (var adapter in root.EnumerateVisualsDepthFirst().OfType<NativeChartAdapter>()) {
            adapter.Synchronize();
        }
    }

    private static string Label(Visual? visual) =>
        visual is TextBlock text ? text.Text ?? "" : "";
    private sealed class LinkedValuesChart(
        Visual source,
        BindableList<double> values,
        Func<(double? Minimum, double? Maximum)> read,
        Action<double?, double?> write
    ) : NativeChartAdapter(source, values, read, write) {
        private int _version = -1;
        public override void Synchronize() {
            bool changed = SynchronizeBounds();
            if (_version != Values.Version || changed) {
                _version = Values.Version;
                Refresh();
            }
        }
    }

    private readonly record struct NativeItem(double Value, string Label, Color? Color, object Source);
    private sealed class NativeItemsChart(
        Visual source,
        Func<int> count,
        Func<int, NativeItem> read,
        Func<(double? Minimum, double? Maximum)>? bounds = null,
        Action<double?, double?>? write = null
    ) : NativeChartAdapter(source, null, bounds, write) {
        private NativeItem[] _previous = [];
        private ChartDataSeries[] _data = [];
        private long _version;
        private GraphKind _kind;
        private bool _initializedItems;
        public override void Synchronize() {
            long before = _version;
            bool changed = SynchronizeBounds();
            _ = GetData(1);
            if (changed || before != _version) {
                Refresh();
            }
        }

        internal override long DataVersion {
            get {
                _ = GetData(1);
                return _version;
            }
        }

        internal override ChartDataSeries[] GetData(int width) {
            int length = count();
            bool changed = !_initializedItems || _previous.Length != length || _kind != GraphKind;
            for (int index = 0; !changed && index < length; index++) {
                changed = read(index) != _previous[index];
            }

            if (!changed) {
                return _data;
            }

            InvalidateDerivedData();
            var values = new NativeItem[length];
            for (int index = 0; index < length; index++) {
                values[index] = read(index);
            }

            _previous = values;
            _kind = GraphKind;
            _initializedItems = true;
            _version++;
            var previous = _data;
            _data = GraphKind == GraphKind.StackedBars ? [.. values.Select((
                item,
                index
            ) => new ChartDataSeries(
                item.Label,
                [new(index, 0, double.IsFinite(item.Value) ? item.Value : null, item.Label) { Source = item.Source }]
            ) { Color = item.Color })] : [new(
                "",
                [.. values.Select((
                    item,
                    index
                ) => new ChartPoint(index, index, double.IsFinite(item.Value) ? item.Value : null, item.Label) { Source = item.Source })]
            )];
            foreach (var series in _data) {
                var old = previous.FirstOrDefault(item => item.Property == series.Property);
                if (old is not null) {
                    series.Color = old.Color;
                    series.Visible = old.Visible;
                    series.LineStyle = old.LineStyle;
                    series.Marker = old.Marker;
                }
            }

            return _data;
        }
    }
}

/// <summary>Linked native-chart adapter. Synchronize on the UI thread after changing its native source.</summary>
public abstract class NativeChartAdapter : Chart {
    /// <summary>Gets the original XenoAtom control synchronized by this adapter.</summary>
    public Visual NativeControl { get; }

    private readonly Func<(double? Minimum, double? Maximum)>? _readBounds;
    private readonly Action<double?, double?>? _writeBounds;
    private (double? Minimum, double? Maximum) _lastSource, _lastView;
    private bool _initialized;
    internal NativeChartAdapter(
        Visual source,
        BindableList<double>? values = null,
        Func<(double? Minimum, double? Maximum)>? read = null,
        Action<double?, double?>? write = null
    ) : base(values ?? new BindableList<double>("NativeChartValues")) {
        NativeControl = source;
        _readBounds = read;
        _writeBounds = write;
    }

    /// <summary>Copies native control range settings when they have changed.</summary>
    /// <returns>True when the chart&apos;s bounds changed.</returns>
    protected bool SynchronizeBounds() {
        if (_readBounds is null) {
            return false;
        }

        var source = _readBounds();
        var current = (Minimum, Maximum);
        bool changed = !_initialized || source != _lastSource || current != _lastView;
        if (!_initialized || source != _lastSource) {
            Minimum = source.Minimum;
            Maximum = source.Maximum;
        }
        else if (current != _lastView) {
            _writeBounds?.Invoke(Minimum, Maximum);
            source = current;
        }

        _lastSource = source;
        _lastView = (Minimum, Maximum);
        _initialized = true;
        return changed;
    }

    /// <summary>Copies the current native control data and presentation options into this
    /// adapter.</summary>
    public abstract void Synchronize();
}
