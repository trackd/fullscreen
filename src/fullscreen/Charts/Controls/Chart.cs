using System.Globalization;
using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;
/// <summary>A numeric series, optionally with explicit X coordinates and point labels.</summary>
public sealed class ChartSeries {
    private Color? _color;
    private bool _visible = true;
    private ChartLineStyle _lineStyle;
    private ChartMarker _marker;
    internal event Action? Changed;
    /// <summary>Gets the display name.</summary>
    public string Name { get; }
    /// <summary>Gets the initial Y values; NaN leaves a gap.</summary>
    public BindableList<double> Values { get; }
    /// <summary>Gets the explicit X coordinates corresponding to numeric values.</summary>
    public BindableList<double> XValues { get; }
    /// <summary>Gets the display labels associated with data points.</summary>
    public BindableList<string> Labels { get; }
    /// <summary>Gets the original source objects associated with data points.</summary>
    public BindableList<object?> Sources { get; }
    /// <summary>Gets the lower bounds corresponding to data points.</summary>
    public BindableList<double> LowerValues { get; }
    /// <summary>Gets the upper bounds corresponding to data points.</summary>
    public BindableList<double> UpperValues { get; }

    /// <summary>Gets or sets the display color, or null to use the palette default.</summary>
    public Color? Color {
        get => _color;
        set {
            if (_color != value) {
                _color = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets whether the series participates in rendering.</summary>
    public bool Visible {
        get => _visible;
        set {
            if (_visible != value) {
                _visible = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the series stroke pattern.</summary>
    public ChartLineStyle LineStyle {
        get => _lineStyle;
        set {
            if (_lineStyle != value) {
                _lineStyle = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the point marker style.</summary>
    public ChartMarker Marker {
        get => _marker;
        set {
            if (_marker != value) {
                _marker = value;
                Changed?.Invoke();
            }
        }
    }

    internal ChartSeriesVersion Version => new(
        Values.Version,
        XValues.Version,
        Labels.Version,
        Sources.Version,
        LowerValues.Version,
        UpperValues.Version,
        Color,
        Visible,
        LineStyle,
        Marker
    );

    /// <summary>Initializes a new ChartSeries instance.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="values">The initial Y values; NaN leaves a gap.</param>
    public ChartSeries(string name, IEnumerable<double> values) {
        Name = name;
        Values = new(this, nameof(Values));
        XValues = new(this, nameof(XValues));
        Labels = new(this, nameof(Labels));
        Sources = new(this, nameof(Sources));
        LowerValues = new(this, nameof(LowerValues));
        UpperValues = new(this, nameof(UpperValues));
        foreach (double value in values) {
            Values.Add(value);
        }
    }
}

/// <summary>Reusable numeric chart with automatic Sixel presentation and a terminal-cell fallback.</summary>
/// <remarks>Values, Minimum and Maximum follow XenoAtom's LineChart contract. NaN represents a gap.</remarks>
public partial class Chart : Themes.ThemedSurface, IGraphicsRenderableVisual {
    /// <summary>Gets the source values used to initialize the object.</summary>
    [Bindable]
    public BindableList<double> Values { get; }

    /// <summary>Gets the explicit X coordinates corresponding to numeric values.</summary>
    [Bindable]
    public BindableList<double> XValues { get; }

    /// <summary>Gets the chart&apos;s additional numeric series.</summary>
    [Bindable]
    public BindableList<ChartSeries> Series { get; }

    /// <summary>Gets or sets the lower Y-axis limit, or null for automatic scaling.</summary>
    [Bindable]
    public partial double? Minimum { get; set; }

    /// <summary>Gets or sets the upper chart scale limit.</summary>
    [Bindable]
    public partial double? Maximum { get; set; }

    /// <summary>Gets or sets the visual representation used to draw the data.</summary>
    [Bindable]
    public partial GraphKind GraphKind { get; set; }

    /// <summary>Gets or sets the displayed heading.</summary>
    [Bindable]
    public partial string Title { get; set; }

    /// <summary>Gets or sets the zero-based selected row or point index.</summary>
    [Bindable]
    public partial int SelectedIndex { get; set; }

    /// <summary>Gets or sets the plot column currently being inspected.</summary>
    [Bindable]
    public partial int InspectionColumn { get; set; }

    [Bindable]
    private partial int Revision { get; set; }

    /// <summary>Raised when the selected data-point index changes.</summary>
    public event Action<int>? PointSelected;
    /// <summary>Raised when a data point is activated by keyboard or pointer input.</summary>
    public event Action<int>? PointActivated;
    private bool _interactive = true;
    /// <summary>Gets or sets whether the chart accepts keyboard and pointer interaction.</summary>
    public bool Interactive {
        get => _interactive;
        set {
            _interactive = value;
            Focusable = value;
            IsTabStop = value;
        }
    }

    internal Color PixelInk { get; set; }
    internal Color PixelBackground { get; set; }

    private bool _explicitColors;
    private TerminalGraphicContent? _pixelContent;
    private ChartDataSeries[]? _adapted;
    private ChartDataSeries[] _snapshot = [];
    private int _valuesVersion = -1, _xVersion = -1, _seriesVersion = -1;
    private long _dataVersion;
    private ChartSeriesStyle _primaryStyle = new();
    private ChartSeriesVersion[] _versions = [];
    internal bool UseSixel => _exportingPixels || ChartPresenters.Supports(App, GraphKind);
    internal Rectangle PlotBounds => ContentPlotBounds;
    internal virtual bool UsesWidthForData => false;
    internal ChartPixelKey PixelKey => new(
        this,
        DataVersion,
        Minimum,
        Maximum,
        PixelInk,
        PixelBackground,
        Palette,
        GraphKind,
        SelectedIndex,
        InspectionColumn,
        ViewVersion
    );

    internal virtual long DataVersion {
        get {
            _ = GetData(Math.Max(1, Bounds.Width));
            return _dataVersion;
        }
    }

    /// <summary>Initializes a new Chart instance.</summary>
    public Chart() {
        Title = "";
        XAxis = new();
        YAxis = new();
        Interactive = true;
        SelectedIndex = InspectionColumn = -1;
        Values = new(this, nameof(Values));
        XValues = new(this, nameof(XValues));
        Series = new(this, nameof(Series), series => series.Changed += Refresh, series => series.Changed -= Refresh);
        Labels = new(this, nameof(Labels));
        Sources = new(this, nameof(Sources));
        Thresholds = new(this, nameof(Thresholds));
        Annotations = new(this, nameof(Annotations));
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        PixelInk = Palette.Accent;
        PixelBackground = Palette.Background;
    }

    /// <summary>Initializes a new Chart instance.</summary>
    /// <param name="values">The source values used to initialize the object.</param>
    public Chart(IEnumerable<double> values) : this() {
        foreach (double value in values) {
            Values.Add(value);
        }
    }

    internal Chart(BindableList<double> values) : this() {
        Values = values;
    }

    /// <summary>Invalidates cached chart data and requests a redraw.</summary>
    public void Refresh() {
        InvalidateDerivedData();
        _valuesVersion = -1;
        Revision = unchecked((int)++_dataVersion);
    }

    internal void SetColors(Color ink, Color background) {
        _explicitColors = true;
        PixelInk = ink;
        PixelBackground = background;
    }

    internal void SetData(ChartDataSeries[] series) {
        InvalidateDerivedData();
        _adapted = series;
        Revision = unchecked((int)++_dataVersion);
    }

    internal virtual ChartDataSeries[] GetData(int width) {
        if (_adapted is not null) {
            return _adapted;
        }

        bool changed = _valuesVersion != Values.Version || _xVersion != XValues.Version || _seriesVersion != Series.Version;
        for (int index = 0; !changed && index < Series.Count; index++) {
            var series = Series[index];
            changed = _versions[index] != series.Version;
        }

        changed |= _labelsVersion != Labels.Version || _sourcesVersion != Sources.Version;
        if (!changed) {
            return _snapshot;
        }

        InvalidateDerivedData();
        var data = new List<ChartDataSeries>();
        if (Values.Count > 0) {
            var primary = Project("", Values, XValues, Labels, Sources);
            primary.Color = _primaryStyle.Color;
            primary.Visible = _primaryStyle.Visible;
            primary.LineStyle = _primaryStyle.LineStyle;
            primary.Marker = _primaryStyle.Marker;
            data.Add(primary);
        }

        var versions = new ChartSeriesVersion[Series.Count];
        for (int index = 0; index < Series.Count; index++) {
            var series = Series[index];
            var projected = Project(
                series.Name,
                series.Values,
                series.XValues,
                series.Labels,
                series.Sources,
                series.LowerValues,
                series.UpperValues
            );
            projected.Color = series.Color;
            projected.Visible = series.Visible;
            projected.LineStyle = series.LineStyle;
            projected.Marker = series.Marker;
            data.Add(projected);
            versions[index] = series.Version;
        }

        _valuesVersion = Values.Version;
        _xVersion = XValues.Version;
        _seriesVersion = Series.Version;
        _labelsVersion = Labels.Version;
        _sourcesVersion = Sources.Version;
        _versions = versions;
        _snapshot = [.. data];
        _dataVersion++;
        return _snapshot;
    }

    private static ChartDataSeries Project(
        string name,
        BindableList<double> values,
        BindableList<double> xs,
        BindableList<string>? labels,
        BindableList<object?>? sources = null,
        BindableList<double>? lower = null,
        BindableList<double>? upper = null
    ) {
        if (xs.Count != 0 && xs.Count != values.Count) {
            throw new ArgumentException("XValues must be empty or contain one coordinate per value.");
        }

        if (lower is not null && lower.Count != 0 && lower.Count != values.Count
            || upper is not null && upper.Count != 0 && upper.Count != values.Count) {
            throw new ArgumentException("Error bounds must be empty or contain one value per point.");
        }

        var points = new ChartPoint[values.Count];
        for (int index = 0; index < values.Count; index++) {
            double x = xs.Count == 0 ? index : xs[index];
            points[index] = new(
                index,
                x,
                double.IsFinite(values[index]) && double.IsFinite(x) ? values[index] : null,
                labels is not null && index < labels.Count ? labels[index] : x.ToString("G6", CultureInfo.InvariantCulture)
            ) {
                Source = sources is not null && index < sources.Count ? sources[index] : null,
                Lower = lower is not null && index < lower.Count && double.IsFinite(lower[index]) ? lower[index] : null,
                Upper = upper is not null && index < upper.Count && double.IsFinite(upper[index]) ? upper[index] : null
            };
            if (points[index].Lower > points[index].Upper) {
                throw new ArgumentException("Lower error bounds cannot exceed upper error bounds.");
            }
        }

        return new(name, points);
    }

    internal virtual ChartRange Range(ChartDataSeries[] data) =>
        ChartRange.From(data, GraphKind, Minimum, Maximum);
    internal void DrawPixels(SixelPlot plot, int width, int height) {
        var data = GetRenderData(width);
        plot.Draw(
            data,
            RenderRange(data),
            width,
            height,
            PixelInk,
            PixelBackground,
            Palette,
            GraphKind,
            SelectedIndex,
            InspectionColumn < 0 || Bounds.Width == 0 ? -1 : InspectionColumn * width / Bounds.Width,
            RenderThresholds,
            Annotations.Count == 0 ? null : Annotations,
            XAxis,
            YAxis
        );
    }

    /// <inheritdoc/>
    public void RenderGraphics(GraphicsRenderContext context) {
        if (UseSixel && PlotBounds.Width > 0 && PlotBounds.Height > 0
            && GetData(Bounds.Width).Any(series => series.Count > 0)) {
            context.Add(
                PlotBounds,
                _pixelContent ??= TerminalGraphicContent.FromObject(this),
                ImageScaleMode.Stretch,
                false
            );
        }
    }

    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = GetData(Math.Clamp(constraints.MaxWidth, 1, 2000));
        MeasureFeatures();
        _ = Revision;
        _ = Minimum;
        _ = Maximum;
        _ = GraphKind;
        _ = SelectedIndex;
        _ = InspectionColumn;
        _ = Title;
        return SizeHints.Flex(new(0, 0), new(48, 8), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        if (!_explicitColors) {
            PixelInk = Palette.Accent;
            PixelBackground = Palette.Background;
        }

        for (int y = 0; y < Bounds.Height; y++) {
            for (int x = 0; x < Bounds.Width; x++) {
                buffer.SetCell(Bounds.X + x, Bounds.Y + y, new Rune(' '), Palette.Body.WithBackground(PixelBackground));
            }
        }

        var data = GetRenderData(Math.Max(1, Bounds.Width));
        if (Title.Length > 0 && Bounds.Width > 0 && Bounds.Height > 0) {
            buffer.WriteText(
                Bounds.X,
                Bounds.Y,
                Title.AsSpan(0, Math.Min(Title.Length, Bounds.Width)),
                Palette.AccentStyle
            );
        }

        if (!UseSixel && PlotBounds.Width > 0 && PlotBounds.Height > 0) {
            DrawCells(buffer, data);
        }

        RenderFeatures(buffer, data);
    }

    internal int Pick(int x, int y) {
        var selection = PickPoint(x, y);
        return selection?.PointIndex ?? -1;
    }

    private ChartSelection? PickPoint(int x, int y) {
        var data = GetData(Math.Max(1, Bounds.Width));
        var rendered = GetRenderData(Math.Max(1, Bounds.Width));
        var range = RenderRange(rendered);
        var plotBounds = PlotBounds;
        bool distribution = GraphKind is GraphKind.Histogram or GraphKind.BoxPlot;
        double best = double.PositiveInfinity;
        ChartSelection? selected = null;
        var stacks = GraphKind == GraphKind.StackedBars ? new Dictionary<double, (double Positive, double Negative)>() : null;
        for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
            var series = data[seriesIndex];
            if (!series.Visible) {
                continue;
            }

            foreach (var point in series.Span) {
                if (!point.Y.HasValue) {
                    continue;
                }

                double px = XAxis.Transform(distribution || GraphKind == GraphKind.HorizontalBars ? point.Y.Value : point.X), py = YAxis.Transform(point.Y.Value);
                if (!double.IsFinite(px) || !double.IsFinite(py)) {
                    continue;
                }

                double viewX = GraphKind == GraphKind.HorizontalBars ? point.Y.Value : point.X;
                if (MinimumX is { } low && viewX < low || MaximumX is { } high && viewX > high) {
                    continue;
                }

                double center = GraphKind == GraphKind.Bars ? BarCenter(px, range.MinX, range.MaxX, plotBounds.Width, series.Count) : Fraction(px, range.MinX, range.MaxX) * Math.Max(0, plotBounds.Width - 1);
                double dx = center - (x - plotBounds.X);
                double dy = distribution ? 0 : (1 - Fraction(py, range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1) - (y - plotBounds.Y);
                if (GraphKind == GraphKind.HorizontalBars) {
                    dx = Fraction(XAxis.Transform(point.Y.Value), range.MinX, range.MaxX) * Math.Max(0, plotBounds.Width - 1) - (x - plotBounds.X);
                    dy = BarCenter(point.X, 0, Math.Max(0, series.Count - 1), plotBounds.Height, series.Count) - (y - plotBounds.Y);
                }

                if (GraphKind == GraphKind.Heatmap) {
                    dy = (seriesIndex + .5) * plotBounds.Height / Math.Max(1, data.Length) - (y - plotBounds.Y);
                }

                if (stacks is not null) {
                    stacks.TryGetValue(point.X, out var total);
                    double start = point.Y.Value >= 0 ? total.Positive : total.Negative, end = start + point.Y.Value;
                    stacks[point.X] = point.Y.Value >= 0 ? (end, total.Negative) : (total.Positive, end);
                    double top = (1 - Fraction(Math.Max(start, end), range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1);
                    double bottom = (1 - Fraction(Math.Min(start, end), range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1);
                    double mouseY = y - plotBounds.Y;
                    dy = mouseY < top ? top - mouseY : mouseY > bottom ? bottom - mouseY : 0;
                    dx = BarCenter(px, range.MinX, range.MaxX, plotBounds.Width, series.Count) - (x - plotBounds.X);
                }

                double distance = dx * dx + dy * dy;
                if (distance < best) {
                    best = distance;
                    selected = new(
                        seriesIndex,
                        series.Property,
                        point.Index,
                        point.X,
                        point.Y.Value,
                        point.Label,
                        point.Source
                    );
                }
            }
        }

        return selected;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerEventArgs e) {
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview || e.Button != TerminalMouseButton.Left) {
            return;
        }

        if (HandleFeaturePointer(e)) {
            return;
        }

        if (!PlotBounds.Contains(e.UiX, e.UiY)) {
            return;
        }

        var selected = PickPoint(e.UiX, e.UiY);
        if (selected is null) {
            return;
        }

        Select(selected);
        if (e.ClickCount >= 2) {
            PointActivated?.Invoke(selected.PointIndex);
            PointInvoked?.Invoke(selected);
        }

        e.Handled = true;
    }

    private Style Ink(int index) =>
        Palette.Body.WithForeground(SeriesColor(index));
    private Color SeriesColor(int index) =>
        GetData(Math.Max(1, Bounds.Width)).ElementAtOrDefault(index)?.Color ?? DefaultSeriesColor(index);
    private Color DefaultSeriesColor(int index) =>
        (index % 5) switch {
            0 => PixelInk,
            1 => Palette.Success,
            2 => Palette.Warning,
            3 => Palette.Purple,
            _ => Palette.Error
        };
}

internal readonly record struct ChartPoint(int Index, double X, double? Y, string Label) {
    internal object? Source { get; init; }
    internal int[]? SourceIndices { get; init; }
    internal double? Lower { get; init; }
    internal double? Upper { get; init; }
}

internal sealed class ChartDataSeries(string property, ChartPoint[] points) {
    internal string Property { get; } = property;
    internal ChartPoint[] Points { get; } = points;
    internal int Count { get; set; } = points.Length;
    internal Color? Color { get; set; }
    internal bool Visible { get; set; } = true;
    internal ChartLineStyle LineStyle { get; set; }
    internal ChartMarker Marker { get; set; }
    internal ReadOnlySpan<ChartPoint> Span => Points.AsSpan(0, Count);
}

internal readonly record struct ChartSeriesVersion(
    int Values,
    int X,
    int Labels,
    int Sources,
    int Lower,
    int Upper,
    Color? Color,
    bool Visible,
    ChartLineStyle LineStyle,
    ChartMarker Marker
);
internal readonly record struct ChartPixelKey(
    Chart Source,
    long Version,
    double? Minimum,
    double? Maximum,
    Color Ink,
    Color Background,
    Themes.ThemePalette Palette,
    GraphKind Kind,
    int Selection,
    int InspectionColumn,
    ChartViewKey ViewVersion
);
internal readonly record struct ChartRange(double MinX, double MaxX, double MinY, double MaxY) {
    internal static ChartRange From(ChartDataSeries[] data, GraphKind kind, double? minimum, double? maximum) {
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (minimum.HasValue && !double.IsFinite(minimum.Value)
            || maximum.HasValue && !double.IsFinite(maximum.Value)) {
            throw new ArgumentException("Minimum and Maximum must be finite or null.");
        }

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        bool distribution = kind is GraphKind.Histogram or GraphKind.BoxPlot;
        foreach (var series in data) {
            if (!series.Visible) {
                continue;
            }

            foreach (var point in series.Span) {
                if (!point.Y.HasValue) {
                    continue;
                }

                double x = distribution ? point.Y.Value : point.X;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, point.Y.Value);
                maxY = Math.Max(maxY, point.Y.Value);
                if (kind == GraphKind.ErrorBars) {
                    if (point.Lower is { } lower && double.IsFinite(lower)) {
                        minY = Math.Min(minY, lower);
                    }

                    if (point.Upper is { } upper && double.IsFinite(upper)) {
                        maxY = Math.Max(maxY, upper);
                    }
                }
            }
        }

        if (kind == GraphKind.StackedBars) {
            var totals = new Dictionary<double, (double Positive, double Negative)>();
            foreach (var series in data) {
                if (!series.Visible) {
                    continue;
                }

                foreach (var point in series.Span) {
                    if (point.Y is not { } value) {
                        continue;
                    }

                    totals.TryGetValue(point.X, out var total);
                    total = value >= 0 ? (total.Positive + value, total.Negative) : (total.Positive, total.Negative + value);
                    if (!double.IsFinite(total.Positive) || !double.IsFinite(total.Negative)) {
                        throw new ArgumentException("Stack totals must fit in finite double values.");
                    }

                    totals[point.X] = total;
                }
            }

            minY = Math.Min(0, totals.Values.Select(total => total.Negative).DefaultIfEmpty(0).Min());
            maxY = Math.Max(0, totals.Values.Select(total => total.Positive).DefaultIfEmpty(0).Max());
        }

        if (!double.IsFinite(minX)) {
            minX = minY = 0;
            maxX = maxY = 1;
        }

        if (kind == GraphKind.Heatmap) {
            return new(minX, maxX, 0, Math.Max(0, data.Length - 1));
        }

        if (kind == GraphKind.HorizontalBars) {
            double valueMinimum = minimum ?? Math.Min(0, minY), valueMaximum = maximum ?? Math.Max(0, maxY);
            if (valueMinimum > valueMaximum) {
                throw new ArgumentException("Minimum must be less than or equal to Maximum.");
            }

            return new(
                valueMinimum,
                valueMaximum,
                0,
                Math.Max(0, data.Select(series => series.Count).DefaultIfEmpty(0).Max() - 1)
            );
        }

        if (kind is GraphKind.Area or GraphKind.Bars or GraphKind.Stem or GraphKind.StackedBars) {
            minY = Math.Min(0, minY);
            maxY = Math.Max(0, maxY);
        }

        if (minimum is { } low && double.IsFinite(low)) {
            minY = low;
            if (distribution) {
                minX = low;
            }
        }

        if (maximum is { } high && double.IsFinite(high)) {
            maxY = high;
            if (distribution) {
                maxX = high;
            }
        }

        if (minY > maxY) {
            throw new ArgumentException("Minimum must be less than or equal to Maximum.");
        }

        return new(minX, maxX, minY, maxY);
    }
}
