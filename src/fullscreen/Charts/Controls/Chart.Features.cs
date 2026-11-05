using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;
using static fullscreen.Charts.ChartMath;

namespace fullscreen.Charts;

public partial class Chart {
    /// <summary>Gets or sets the positive fixed price change required for a Kagi reversal; default 1 price unit.</summary>
    [Bindable]
    public partial double ReversalAmount { get; set; }

    /// <summary>Gets or sets whether axis labels and tick marks are displayed.</summary>
    [Bindable]
    public partial bool ShowAxes { get; set; }

    /// <summary>Gets or sets whether the series legend is displayed.</summary>
    [Bindable]
    public partial bool ShowLegend { get; set; }

    /// <summary>Gets or sets the lower X-coordinate limit, or null for automatic bounds.</summary>
    [Bindable]
    public partial double? MinimumX { get; set; }

    /// <summary>Gets or sets the upper X-coordinate limit, or null for automatic bounds.</summary>
    [Bindable]
    public partial double? MaximumX { get; set; }

    /// <summary>Gets or sets the horizontal axis presentation settings.</summary>
    [Bindable]
    public partial ChartAxis XAxis { get; set; }

    /// <summary>Gets or sets the vertical axis presentation settings.</summary>
    [Bindable]
    public partial ChartAxis YAxis { get; set; }

    /// <summary>Gets or sets the selected chart point and its original source reference.</summary>
    [Bindable]
    public partial ChartSelection? Selection { get; set; }

    /// <summary>Gets or sets the selected X-coordinate interval, or null when no range is
    /// selected.</summary>
    [Bindable]
    public partial ChartSelectedRange? SelectedRange { get; set; }

    /// <summary>Gets the display labels associated with data points.</summary>
    [Bindable]
    public BindableList<string> Labels { get; }

    /// <summary>Gets the original source objects associated with data points.</summary>
    [Bindable]
    public BindableList<object?> Sources { get; }

    /// <summary>Gets the horizontal and vertical reference lines.</summary>
    [Bindable]
    public BindableList<ChartThreshold> Thresholds { get; }

    /// <summary>Gets the labels anchored to data coordinates.</summary>
    [Bindable]
    public BindableList<ChartAnnotation> Annotations { get; }

    /// <summary>Raised with the selected point and its associated source object.</summary>
    public event Action<ChartSelection>? SelectionChanged;
    /// <summary>Raised when the user invokes a point&apos;s associated action.</summary>
    public event Action<ChartSelection>? PointInvoked;
    /// <summary>Raised when the selected X range changes or is cleared.</summary>
    public event Action<ChartSelectedRange?>? RangeSelected;
    /// <summary>Raised when interactive navigation changes the chart viewport.</summary>
    public event Action? ViewChanged;
    /// <summary>Raised when the user requests pausing or resuming live data.</summary>
    public event Action? PauseRequested;
    /// <summary>Raised with the series index and its new visibility.</summary>
    public event Action<int, bool>? SeriesVisibilityChanged;
    private int _labelsVersion = -1, _sourcesVersion = -1;
    private double? _dragStart;
    private int _axisWidth;
    private readonly ChartGeometry _featureGeometry = new();
    internal ChartViewKey ViewVersion => new(
        MinimumX,
        MaximumX,
        XAxis,
        YAxis,
        ShowAxes,
        ShowLegend,
        Thresholds.Version,
        Annotations.Version,
        SelectedRange,
        ReversalAmount,
        FlameReverse,
        FlameFocusId,
        FlameSearch,
        TaskOffset,
        FlameFrameRows
    );
    private int LegendRows => ShowLegend ? GetLegend().Rows : 0;
    private int TitleRows => Title.Length == 0 ? 0 : 1;
    private bool CartesianAxes => ShowAxes && !ChartTopology.Supports(GraphKind) && !ChartRadial.IsRadial(GraphKind) && GraphKind is not (GraphKind.Flamegraph or GraphKind.Gantt);
    private Rectangle ContentPlotBounds => new(
        Bounds.X + (CartesianAxes ? _axisWidth : 0),
        Bounds.Y + TitleRows + LegendRows,
        Math.Max(0, Bounds.Width - (CartesianAxes ? _axisWidth : 0)),
        Math.Max(0, Bounds.Height - TitleRows - LegendRows - (ShowAxes ? 1 : 0) - ((ChartGrid.Supports(GraphKind) || GraphKind == GraphKind.Kagi) && ShowLegend ? 1 : 0))
    );

    private void MeasureFeatures() {
        _ = ShowAxes;
        _ = ShowLegend;
        _ = MinimumX;
        _ = MaximumX;
        _ = XAxis;
        _ = YAxis;
        _ = SelectedRange;
        _ = Thresholds.Version;
        _ = Annotations.Version;
        if (XAxis.TickCount is < 2 or > 20 || YAxis.TickCount is < 2 or > 20) {
            throw new ArgumentException("Axis TickCount must be between 2 and 20.");
        }

        if (CartesianAxes) {
            var data = GetRenderData(Math.Max(1, Bounds.Width));
            var range = RenderRange(data);
            int labelWidth = Math.Max(
                FormatTick(YAxis, YAxis.Inverse(range.MinY), data, false).Length,
                FormatTick(YAxis, YAxis.Inverse(range.MaxY), data, false).Length
            );
            if (GraphKind is GraphKind.Marimekko or GraphKind.PercentStackedArea) {
                for (int tick = 0; tick < YAxis.TickCount; tick++) {
                    labelWidth = Math.Max(labelWidth, FormatTick(YAxis, 100d * tick / (YAxis.TickCount - 1), data, false).Length);
                }
            }
            if (GraphKind == GraphKind.Heatmap) {
                labelWidth = Math.Max(labelWidth, data.Select(series => series.Property.Length).DefaultIfEmpty(0).Max());
            }

            if (YAxis.Scale == ChartScale.Category) {
                foreach (var series in data) {
                    foreach (var point in series.Span) {
                        labelWidth = Math.Max(labelWidth, point.Label.Length);
                    }
                }
            }

            _axisWidth = Math.Clamp(labelWidth + 1, 3, 14);
        }
    }

    internal ChartDataSeries[] GetRenderData(int width) {
        if (GraphKind is GraphKind.StackedBars or GraphKind.Heatmap && YAxis.Scale == ChartScale.Logarithmic) {
            throw new ArgumentException("Stacked bars and heatmap rows require a linear or categorical Y axis.");
        }

        if (GraphKind == GraphKind.HorizontalBars && YAxis.Scale == ChartScale.Logarithmic) {
            throw new ArgumentException("Horizontal bar rows require a linear or categorical Y axis; choose X for a logarithmic value axis.");
        }

        var data = GetData(width);
        ChartStackedArea.ValidateOptions(GraphKind, Minimum, Maximum, XAxis.Scale, YAxis.Scale);
        if (ChartStackedArea.Supports(GraphKind)) {
            ChartStackedArea.Validate(data);
        }
        if (ChartTopology.Supports(GraphKind)) {
            ChartTopology.ValidateOptions(Minimum, Maximum, MinimumX, MaximumX, XAxis.Scale, YAxis.Scale,
                Thresholds.Count, Annotations.Count, SelectedRange);
            _ = Range(data);
            return data;
        }
        if (GraphKind == GraphKind.Gantt) {
            ChartSchedule.ValidateOptions(Minimum, Maximum, XAxis.Scale, YAxis.Scale, Thresholds.Count, Annotations.Count, TaskOffset);
            _ = Range(data);
            return data;
        }
        if (GraphKind == GraphKind.Flamegraph) {
            ValidateFlameOptions();
            return data;
        }
        ChartMarket.ValidateOptions(GraphKind, ReversalAmount, Minimum, Maximum, MinimumX, MaximumX,
            XAxis.Scale, YAxis.Scale, Thresholds.Count, Annotations.Count);
        if (ChartMarket.Supports(GraphKind)) {
            ChartMarket.Validate(data, GraphKind);
        }
        ChartGrid.ValidateOptions(GraphKind, XAxis.Scale, YAxis.Scale);
        ChartRadial.ValidateOptions(GraphKind, Minimum, Maximum, XAxis.Scale, YAxis.Scale, Thresholds.Count, Annotations.Count);
        if (GraphKind is not (GraphKind.Histogram or GraphKind.BoxPlot)) {
            _distributionKey = null;
            _distributions = [];
        }

        bool transform = XAxis.Scale == ChartScale.Logarithmic || YAxis.Scale == ChartScale.Logarithmic;
        if (!transform && data.All(series => series.Visible) && MinimumX is null && MaximumX is null) {
            _projectionKey = null;
            _projection = [];
            if (ChartRadial.IsRadial(GraphKind)) {
                ChartRadial.Validate(data, GraphKind);
            }
            if (ChartGrid.Supports(GraphKind)) {
                ChartGrid.Validate(data, GraphKind);
            }
            return data;
        }

        var cacheKey = new ProjectionKey(
            data,
            DataVersion,
            UsesWidthForData ? width : 0,
            GraphKind,
            XAxis,
            YAxis,
            MinimumX,
            MaximumX,
            Palette,
            PixelInk
        );
        if (_projectionKey == cacheKey) {
            return _projection;
        }

        var projected = new List<ChartDataSeries>();
        for (int index = 0; index < data.Length; index++) {
            var series = data[index];
            if (!series.Visible) {
                continue;
            }

            var points = new ChartPoint[series.Count];
            for (int position = 0; position < points.Length; position++) {
                var point = series.Points[position];
                bool horizontal = GraphKind == GraphKind.HorizontalBars;
                double x = horizontal ? point.X : XAxis.Transform(point.X), y = point.Y.HasValue ? (horizontal ? XAxis : YAxis).Transform(point.Y.Value) : double.NaN;
                double viewX = horizontal ? point.Y ?? double.NaN : point.X;
                bool visible = GraphKind == GraphKind.Waterfall || ChartStackedArea.Supports(GraphKind)
                    || (!MinimumX.HasValue || viewX >= MinimumX.Value) && (!MaximumX.HasValue || viewX <= MaximumX.Value);
                points[position] = point with {
                    X = x,
                    Y = visible && double.IsFinite(x) && double.IsFinite(y) ? y : null,
                    Lower = point.Lower.HasValue ? YAxis.Transform(point.Lower.Value) : null,
                    Upper = point.Upper.HasValue ? YAxis.Transform(point.Upper.Value) : null
                };
            }

            projected.Add(new(series.Property, points) {
                Color = series.Color ?? DefaultSeriesColor(index),
                LineStyle = series.LineStyle,
                Marker = series.Marker,
                Role = series.Role,
                SourceIndex = series.SourceIndex
            });
        }

        _projection = [.. projected];
        if (ChartRadial.IsRadial(GraphKind)) {
            ChartRadial.Validate(_projection, GraphKind);
        }
        if (ChartGrid.Supports(GraphKind)) {
            ChartGrid.Validate(_projection, GraphKind);
        }
        _projectionKey = cacheKey;
        return _projection;
    }

    internal ChartRange RenderRange(ChartDataSeries[] rendered) {
        var cacheKey = new RangeKey(
            rendered,
            DataVersion,
            rendered.FirstOrDefault()?.Count ?? 0,
            GraphKind,
            Minimum,
            Maximum,
            MinimumX,
            MaximumX,
            XAxis,
            YAxis
        );
        if (_rangeKey == cacheKey) {
            return _range;
        }

        var original = XAxis.Scale == ChartScale.Logarithmic || YAxis.Scale == ChartScale.Logarithmic ? GetData(Math.Max(1, Bounds.Width)) : rendered;
        var range = Range(original);
        double minX = MinimumX ?? range.MinX, maxX = MaximumX ?? range.MaxX, minY = range.MinY, maxY = range.MaxY;
        if (MinimumX.HasValue && !double.IsFinite(MinimumX.Value)
            || MaximumX.HasValue && !double.IsFinite(MaximumX.Value)
            || minX > maxX) {
            throw new ArgumentException("X limits must be finite and ordered.");
        }
        if (GraphKind == GraphKind.Gantt && (minX < 0 || maxX > ChartSchedule.Seconds(DateTimeOffset.MaxValue) || minX >= maxX)) {
            throw new ArgumentException("Gantt time bounds must fit UTC calendar instants with a positive span.");
        }

        if (XAxis.Scale == ChartScale.Logarithmic && minX <= 0 && !MinimumX.HasValue) {
            minX = original.Where(series => series.Visible).SelectMany(series => series.Points.Take(series.Count)).Where(point => point.Y.HasValue).Select(point => GraphKind == GraphKind.HorizontalBars ? point.Y!.Value : point.X).Where(value => value > 0).DefaultIfEmpty(1).Min();
            maxX = Math.Max(minX, maxX);
        }

        if (YAxis.Scale == ChartScale.Logarithmic && minY <= 0 && !Minimum.HasValue) {
            minY = original.SelectMany(series => series.Points.Take(series.Count)).Where(point => point.Y > 0).Select(point => point.Y!.Value).DefaultIfEmpty(1).Min();
            maxY = Math.Max(minY, maxY);
        }

        if (XAxis.Scale == ChartScale.Logarithmic && (minX <= 0 || maxX <= 0)
            || YAxis.Scale == ChartScale.Logarithmic && (minY <= 0 || maxY <= 0)) {
            throw new ArgumentException("Logarithmic axis limits must be positive.");
        }

        _range = new(XAxis.Transform(minX), XAxis.Transform(maxX), YAxis.Transform(minY), YAxis.Transform(maxY), range.MinColor, range.MaxColor);
        _rangeKey = cacheKey;
        return _range;
    }

    /// <summary>Sets the horizontal viewport, using automatic bounds for null limits.</summary>
    /// <param name="minimumX">The lower X-coordinate limit, or null for automatic bounds.</param>
    /// <param name="maximumX">The upper X-coordinate limit, or null for automatic bounds.</param>
    public void SetView(double? minimumX, double? maximumX) {
        if (ChartTopology.Supports(GraphKind) && (minimumX is not null || maximumX is not null)) {
            throw new ArgumentException("Relationship diagrams use intrinsic layout, not numeric viewport bounds.");
        }
        if (minimumX.HasValue && !double.IsFinite(minimumX.Value)
            || maximumX.HasValue && !double.IsFinite(maximumX.Value)
            || minimumX > maximumX) {
            throw new ArgumentException("X limits must be finite and ordered.");
        }
        if (GraphKind == GraphKind.Gantt && (minimumX < 0 || maximumX > ChartSchedule.Seconds(DateTimeOffset.MaxValue)
            || minimumX is not null && maximumX is not null && maximumX <= minimumX)) {
            throw new ArgumentException("Gantt time bounds must fit UTC calendar instants with a positive span.");
        }

        MinimumX = minimumX;
        MaximumX = maximumX;
        ViewChanged?.Invoke();
    }

    /// <summary>Changes the horizontal or vertical viewport around its current center.</summary>
    /// <param name="factor">The zoom factor; values greater than one narrow the viewport.</param>
    /// <param name="vertical">Whether to change the Y viewport instead of the X viewport. Defaults to
    /// false.</param>
    public void Zoom(double factor, bool vertical = false) {
        if (ChartTopology.Supports(GraphKind)) {
            throw new ArgumentException("Relationship diagrams fit their topology to the viewport; numeric zoom is unsupported.");
        }
        if (GraphKind == GraphKind.Gantt && vertical) {
            throw new ArgumentException("Gantt task lanes scroll vertically; only schedule time can zoom.");
        }
        if (GraphKind == GraphKind.PercentStackedArea && vertical) {
            throw new ArgumentException("PercentStackedArea retains its fixed 0 to 100 percent Y range; only X can zoom.");
        }
        if (!double.IsFinite(factor) || factor <= 0) {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double min = vertical ? range.MinY : range.MinX, max = vertical ? range.MaxY : range.MaxX;
        double center = min / 2 + max / 2, half = Math.Max(GraphKind == GraphKind.Gantt ? .5 : 1e-12, (max / 2 - min / 2) / factor);
        var axis = vertical ? YAxis : XAxis;
        double low = axis.Inverse(center - half), high = axis.Inverse(center + half);
        if (!double.IsFinite(low) || !double.IsFinite(high)) {
            return;
        }

        if (vertical) {
            Minimum = low;
            Maximum = high;
            ViewChanged?.Invoke();
        }
        else {
            if (GraphKind == GraphKind.Gantt) {
                low = Math.Max(0, low);
                high = Math.Min(ChartSchedule.Seconds(DateTimeOffset.MaxValue), high);
            }
            SetView(low, high);
        }
    }

    /// <summary>Moves the horizontal or vertical viewport by a fraction of its span.</summary>
    /// <param name="fraction">The signed portion of the current viewport span to pan.</param>
    /// <param name="vertical">Whether to change the Y viewport instead of the X viewport. Defaults to
    /// false.</param>
    public void Pan(double fraction, bool vertical = false) {
        if (ChartTopology.Supports(GraphKind)) {
            throw new ArgumentException("Relationship diagrams fit their topology to the viewport; numeric pan is unsupported.");
        }
        if (GraphKind == GraphKind.Gantt && vertical) {
            throw new ArgumentException("Gantt task lanes scroll vertically; only schedule time can pan.");
        }
        if (GraphKind == GraphKind.PercentStackedArea && vertical) {
            throw new ArgumentException("PercentStackedArea retains its fixed 0 to 100 percent Y range; only X can pan.");
        }
        if (!double.IsFinite(fraction)) {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double min = vertical ? range.MinY : range.MinX, max = vertical ? range.MaxY : range.MaxX;
        double delta = (max - min) * fraction;
        if (GraphKind == GraphKind.Gantt) {
            delta = Math.Clamp(delta, -min, ChartSchedule.Seconds(DateTimeOffset.MaxValue) - max);
        }
        var axis = vertical ? YAxis : XAxis;
        double low = axis.Inverse(min + delta), high = axis.Inverse(max + delta);
        if (!double.IsFinite(low) || !double.IsFinite(high)) {
            return;
        }

        if (vertical) {
            Minimum = low;
            Maximum = high;
            ViewChanged?.Invoke();
        }
        else {
            SetView(low, high);
        }
    }

    /// <summary>Restores automatic viewport bounds.</summary>
    public void ResetView() {
        SetView(null, null);
        if (GraphKind == GraphKind.Gantt) {
            TaskOffset = 0;
        }
        SelectRange(null);
    }

    /// <summary>Sets or clears the selected horizontal data range.</summary>
    /// <param name="range">The selected horizontal interval, or null to clear the selection.</param>
    public void SelectRange(ChartSelectedRange? range) {
        if (ChartTopology.Supports(GraphKind) && range is not null) {
            throw new ArgumentException("Relationship diagrams do not use numeric range selection.");
        }
        if (range is not null
            && (!double.IsFinite(range.MinimumX) || !double.IsFinite(range.MaximumX)
            || range.MinimumX > range.MaximumX)) {
            throw new ArgumentException("Selection limits must be finite and ordered.");
        }

        SelectedRange = range;
        RangeSelected?.Invoke(range);
    }

    private void Select(ChartSelection selection) {
        Selection = selection;
        SelectedIndex = selection.PointIndex;
        PointSelected?.Invoke(selection.PointIndex);
        SelectionChanged?.Invoke(selection);
    }

    /// <summary>Changes whether a series participates in chart presentation.</summary>
    /// <param name="index">The zero-based series index; zero identifies the primary series.</param>
    /// <param name="visible">Whether the series participates in rendering.</param>
    public void SetSeriesVisible(int index, bool visible) {
        int offset = Values.Count > 0 ? 1 : 0;
        if (offset == 1 && index == 0) {
            _primaryStyle = _primaryStyle with {
                Visible = visible
            };
            Refresh();
        }
        else if (index >= offset && index - offset < Series.Count) {
            Series[index - offset].Visible = visible;
        }
        else {
            var data = GetData(Math.Max(1, Bounds.Width));
            if ((uint)index >= (uint)data.Length) {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            data[index].Visible = visible;
            Refresh();
        }

        SeriesVisibilityChanged?.Invoke(index, visible);
    }

    /// <summary>Applies display options to a series.</summary>
    /// <param name="index">The zero-based series index; zero identifies the primary series.</param>
    /// <param name="style">The chart series display options.</param>
    public void SetSeriesStyle(int index, ChartSeriesStyle style) {
        ArgumentNullException.ThrowIfNull(style);
        if (!Enum.IsDefined(style.LineStyle) || !Enum.IsDefined(style.Marker) || !Enum.IsDefined(style.Role)) {
            throw new ArgumentException("Invalid series style.");
        }

        var data = GetData(Math.Max(1, Bounds.Width));
        if ((uint)index >= (uint)data.Length) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int offset = Values.Count > 0 ? 1 : 0;
        if (offset == 1 && index == 0) {
            _primaryStyle = style;
        }
        else if (index >= offset && index - offset < Series.Count) {
            var series = Series[index - offset];
            series.Color = style.Color;
            series.Visible = style.Visible;
            series.LineStyle = style.LineStyle;
            series.Marker = style.Marker;
            series.Role = style.Role;
        }
        else {
            data[index].Color = style.Color;
            data[index].Visible = style.Visible;
            data[index].LineStyle = style.LineStyle;
            data[index].Marker = style.Marker;
            data[index].Role = style.Role;
        }

        Refresh();
    }

    private double PointerX(int x) {
        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double fraction = Math.Clamp((x - PlotBounds.X) / (double)Math.Max(1, PlotBounds.Width - 1), 0, 1);
        return XAxis.Inverse(range.MinX * (1 - fraction) + range.MaxX * fraction);
    }

    private bool HandleFeaturePointer(PointerEventArgs e) {
        if (ShowLegend && e.UiY >= Bounds.Y + TitleRows && e.UiY < Bounds.Y + TitleRows + LegendRows) {
            var data = GetData(Math.Max(1, Bounds.Width));
            foreach (var entry in GetLegend().Entries) {
                if (entry.Index >= 0 && entry.Bounds.Contains(e.UiX - Bounds.X, e.UiY - Bounds.Y - TitleRows)) {
                    if (entry.Point >= 0) {
                        var series = data[entry.Index];
                        var point = series.Points[entry.Point];
                        if (point.Y is { } value) {
                            var selection = new ChartSelection(entry.Index, series.Property, point.Index, point.X, value, point.Label, point.Source);
                            Select(selection);
                            if (e.ClickCount >= 2) {
                                PointActivated?.Invoke(point.Index);
                                PointInvoked?.Invoke(selection);
                            }
                        }
                    }
                    else {
                        SetSeriesVisible(entry.Index, !data[entry.Index].Visible);
                    }
                    break;
                }
            }

            e.Handled = true;
            return true;
        }

        if (!ChartTopology.Supports(GraphKind) && !ChartRadial.IsRadial(GraphKind) && !ChartMarket.Supports(GraphKind) && GraphKind != GraphKind.Flamegraph && (e.Modifiers & TerminalModifiers.Shift) != 0 && PlotBounds.Contains(e.UiX, e.UiY)) {
            _dragStart = PointerX(e.UiX);
            SelectRange(new(_dragStart.Value, _dragStart.Value));
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e) {
        if (Interactive && GraphKind == GraphKind.Flamegraph && e.RoutingPhase != RoutingPhase.Preview) {
            FlameHover = PlotBounds.Contains(e.UiX, e.UiY) ? PickPoint(e.UiX, e.UiY) : null;
            return;
        }
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview || !_dragStart.HasValue) {
            return;
        }

        double x = PointerX(e.UiX);
        SelectRange(new(Math.Min(_dragStart.Value, x), Math.Max(_dragStart.Value, x)));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerEventArgs e) {
        if (_dragStart.HasValue && e.RoutingPhase != RoutingPhase.Preview) {
            OnPointerMoved(e);
            _dragStart = null;
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerWheel(PointerEventArgs e) {
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview || ChartTopology.Supports(GraphKind) || ChartRadial.IsRadial(GraphKind) || ChartMarket.Supports(GraphKind) || GraphKind == GraphKind.Flamegraph) {
            return;
        }

        Zoom(e.WheelDelta > 0 ? 1.25 : .8, GraphKind != GraphKind.Gantt && (e.Modifiers & TerminalModifiers.Ctrl) != 0);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e) {
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }
        if (GraphKind == GraphKind.Flamegraph) {
            if (ChartTopology.Supports(GraphKind)) {
                return;
            }
            switch (e.Text) {
                case "f":
                    FocusSelectedFrame();
                    break;
                case "u":
                    FlameParent();
                    break;
                case "0":
                    FlameFocusId = "";
                    break;
                case "r":
                    FlameReverse = !FlameReverse;
                    break;
                case "/":
                    FlameSearchRequested?.Invoke();
                    break;
                case " ":
                    PauseRequested?.Invoke();
                    break;
                default:
                    return;
            }
            e.Handled = true;
            return;
        }
        if ((ChartRadial.IsRadial(GraphKind) || ChartMarket.Supports(GraphKind)) && e.Text != " ") {
            return;
        }

        switch (e.Text) {
            case "+":
            case "=":
                Zoom(1.5);
                break;
            case "-":
                Zoom(1 / 1.5);
                break;
            case "[":
                Pan(-.2);
                break;
            case "]":
                Pan(.2);
                break;
            case "0":
                ResetView();
                break;
            case " ":
                PauseRequested?.Invoke();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) {
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (e.Key == TerminalKey.Enter && Selection is { } selection) {
            PointActivated?.Invoke(selection.PointIndex);
            PointInvoked?.Invoke(selection);
            e.Handled = true;
            return;
        }
        if (GraphKind == GraphKind.Flamegraph && e.Key == TerminalKey.Backspace) {
            FlameParent();
            e.Handled = true;
            return;
        }

        if (GraphKind == GraphKind.Gantt && e.Key is TerminalKey.Up or TerminalKey.Down or TerminalKey.PageUp or TerminalKey.PageDown) {
            int capacity = Math.Max(1, PlotBounds.Height / 2);
            int delta = e.Key switch {
                TerminalKey.Up => -1,
                TerminalKey.Down => 1,
                TerminalKey.PageUp => -capacity,
                _ => capacity
            };
            int count = GetData(Math.Max(1, Bounds.Width))[0].Count;
            TaskOffset = (int)Math.Clamp((long)TaskOffset + delta, 0, Math.Max(0, count - 1));
            e.Handled = true;
            return;
        }
        if (e.Key is not (TerminalKey.Left or TerminalKey.Right or TerminalKey.Home or TerminalKey.End)) {
            return;
        }
        if (ChartTopology.Supports(GraphKind)) {
            NavigateDiagram(e.Key);
            e.Handled = true;
            return;
        }

        if (GraphKind == GraphKind.Flamegraph) {
            var tree = GetFlameTree();
            _pickGeometry.Build(tree.Data, RenderRange(tree.Data), GraphKind, PlotBounds.Width, PlotBounds.Height,
                flameReverse: FlameReverse, flameFocus: FlameFocus(tree), flameFrameHeight: FlameFrameRows);
            int first = -1, last = -1, current = -1, previous = -1, next = -1;
            for (int shapeIndex = 0; shapeIndex < _pickGeometry.Primitives.Count; shapeIndex++) {
                var shape = _pickGeometry.Primitives[shapeIndex];
                if (shape.Kind != ChartPrimitiveKind.Rectangle) {
                    continue;
                }
                if (first < 0) {
                    first = shapeIndex;
                }
                if (current >= 0 && next < 0) {
                    next = shapeIndex;
                }
                if (tree.Data[0].Points[shape.Point].Index == SelectedIndex) {
                    current = shapeIndex;
                    previous = last;
                }
                last = shapeIndex;
            }
            int target = e.Key switch {
                TerminalKey.Home => first,
                TerminalKey.End => last,
                TerminalKey.Left => previous >= 0 ? previous : current >= 0 ? current : first,
                _ => next >= 0 ? next : current >= 0 ? current : first
            };
            if (target >= 0) {
                var shape = _pickGeometry.Primitives[target];
                var picked = PickGrid(tree.Data, PlotBounds.X + shape.X0, PlotBounds.Y + shape.Y0);
                if (picked is not null) {
                    Select(picked);
                }
            }
            e.Handled = true;
            return;
        }

        var data = GetData(Math.Max(1, Bounds.Width));
        int seriesIndex = Math.Clamp(Selection?.SeriesIndex ?? 0, 0, Math.Max(0, data.Length - 1));
        if (data.Length == 0) {
            return;
        }

        var points = data[seriesIndex].Span;
        int index = NavigationIndex(points, SelectedIndex, e.Key);
        if (index < 0) {
            return;
        }

        var point = points[index];
        if (GraphKind == GraphKind.Gantt) {
            int capacity = Math.Max(1, PlotBounds.Height / 2);
            if (index < TaskOffset || index >= TaskOffset + capacity) {
                TaskOffset = index < TaskOffset ? index : Math.Max(0, index - capacity + 1);
            }
        }
        Select(new(
            seriesIndex,
            data[seriesIndex].Property,
            point.Index,
            point.X,
            point.Y!.Value,
            point.Label,
            point.Source
        ));
        e.Handled = true;
    }

    private void WriteFeature(CellBuffer buffer, int x, int y, string text, Style style, int width) {
        if (width <= 0 || y < Bounds.Y || y >= Bounds.Bottom) {
            return;
        }

        buffer.PushClip(new(
            Math.Max(Bounds.X, x),
            y,
            Math.Max(0, Math.Min(width, Bounds.Right - Math.Max(Bounds.X, x))),
            1
        ));
        buffer.WriteText(x, y, text, style);
        buffer.PopClip();
    }

    private void RenderFeatures(CellBuffer buffer, ChartDataSeries[] data) {
        var plotBounds = PlotBounds;
        if (plotBounds.Width <= 0 || plotBounds.Height <= 0) {
            return;
        }

        var range = RenderRange(data);
        if (GraphKind == GraphKind.Heatmap && ShowLegend) {
            RenderHeatmapScale(buffer, range);
        }
        if (GraphKind == GraphKind.Waterfall && ShowLegend) {
            RenderWaterfallKey(buffer);
        }
        if (GraphKind == GraphKind.Kagi && ShowLegend) {
            RenderKagiKey(buffer);
        }
        if (ShowLegend) {
            var original = GetData(Math.Max(1, Bounds.Width));
            foreach (var entry in GetLegend().Entries) {
                var style = entry.Index < 0 ? Palette.Muted : entry.Point >= 0
                    ? Palette.Body.WithForeground(GraphKind == GraphKind.Gauge ? Palette.Accent : DefaultSeriesColor(entry.Point))
                    : original[entry.Index].Visible
                    ? Palette.Body.WithForeground(original[entry.Index].Color ?? DefaultSeriesColor(entry.Index)) : Palette.Muted;
                WriteFeature(
                    buffer,
                    Bounds.X + entry.Bounds.X,
                    Bounds.Y + TitleRows + entry.Bounds.Y,
                    entry.Label,
                    style,
                    entry.Bounds.Width
                );
            }
        }

        if (ChartRadial.IsRadial(GraphKind)) {
            RenderRadialLabels(buffer, data, range);
            return;
        }

        if (GraphKind == GraphKind.Gantt && ShowAxes) {
            int row = plotBounds.Bottom;
            for (int tick = 0; tick < XAxis.TickCount; tick++) {
                double fraction = tick / (double)(XAxis.TickCount - 1);
                string label = FormatTick(XAxis, range.MinX * (1 - fraction) + range.MaxX * fraction, data, true);
                int width = Math.Max(1, plotBounds.Width / XAxis.TickCount);
                int column = plotBounds.X + (int)Math.Round(fraction * Math.Max(0, plotBounds.Width - width));
                WriteFeature(buffer, column, row, label, Palette.Muted, width);
            }
        }
        if (ShowAxes && !ChartTopology.Supports(GraphKind) && GraphKind is not (GraphKind.Flamegraph or GraphKind.Gantt)) {
            bool categoricalRows = GraphKind == GraphKind.Heatmap
                || GraphKind == GraphKind.HorizontalBars && YAxis.Scale == ChartScale.Category;
            int categories = GraphKind == GraphKind.Heatmap ? data.Length : data.FirstOrDefault()?.Count ?? 0;
            int yTicks = categoricalRows ? Math.Min(categories, plotBounds.Height) : YAxis.TickCount;
            for (int tick = 0; tick < yTicks; tick++) {
                if (categoricalRows) {
                    int index = yTicks <= 1 ? 0 : (int)Math.Round(tick * (categories - 1d) / (yTicks - 1));
                    int center = GraphKind == GraphKind.Heatmap ? (int)((index + .5) * plotBounds.Height / Math.Max(1, categories)) : BarCenter(index, 0, Math.Max(0, categories - 1), plotBounds.Height, categories);
                    string label = GraphKind == GraphKind.Heatmap ? data[index].Property : data[0].Points[index].Label;
                    WriteFeature(buffer, Bounds.X, plotBounds.Y + center, label, Palette.Muted, _axisWidth - 1);
                    WriteFeature(buffer, plotBounds.X - 1, plotBounds.Y + center, "┤", Palette.Muted, 1);
                    continue;
                }

                double fraction = tick / (double)(YAxis.TickCount - 1), value = YAxis.Inverse(range.MaxY * (1 - fraction) + range.MinY * fraction);
                int y = plotBounds.Y + (int)Math.Round(fraction * (plotBounds.Height - 1));
                WriteFeature(buffer, Bounds.X, y, FormatTick(YAxis, value, data, false), Palette.Muted, _axisWidth - 1);
                WriteFeature(buffer, plotBounds.X - 1, y, "┤", Palette.Muted, 1);
            }

            int row = plotBounds.Bottom;
            WriteFeature(
                buffer,
                plotBounds.X - 1,
                row,
                "└" + new string('─', plotBounds.Width),
                Palette.Muted,
                plotBounds.Width + 1
            );
            if (ChartGrid.Supports(GraphKind)) {
                int count = 0;
                foreach (var series in data) {
                    count = Math.Max(count, series.Count);
                }
                int visibleCount = 0;
                for (int index = 0; index < count; index++) {
                    var point = GridPoint(data, index);
                    if (point.X >= range.MinX && point.X <= range.MaxX) {
                        visibleCount++;
                    }
                }
                int slot = 0, stride = Math.Max(1, (int)Math.Ceiling(visibleCount / (double)XAxis.TickCount));
                for (int index = 0; index < count; index++) {
                    var point = GridPoint(data, index);
                    if (point.X < range.MinX || point.X > range.MaxX) {
                        continue;
                    }
                    if (slot % stride == 0) {
                        int column = plotBounds.X + (int)((long)slot * plotBounds.Width / Math.Max(1, visibleCount));
                        WriteFeature(buffer, column, row, point.Label, Palette.Muted, Math.Max(1, (int)((long)stride * plotBounds.Width / Math.Max(1, visibleCount))));
                    }
                    slot++;
                }
            }
            if (ChartMarket.Supports(GraphKind)) {
                RenderMarketCategories(buffer, data, row);
            }
            for (int tick = 0; !ChartGrid.Supports(GraphKind) && !ChartMarket.Supports(GraphKind) && tick < XAxis.TickCount; tick++) {
                double fraction = tick / (double)(XAxis.TickCount - 1), value = XAxis.Inverse(range.MinX * (1 - fraction) + range.MaxX * fraction);
                string label = FormatTick(XAxis, value, data, true);
                int width = Math.Max(1, plotBounds.Width / XAxis.TickCount), column = plotBounds.X + (int)Math.Round(fraction * Math.Max(0, plotBounds.Width - width));
                WriteFeature(buffer, column, row, label, Palette.Muted, width);
            }

            if (YAxis.Title.Length > 0) {
                WriteFeature(buffer, Bounds.X, plotBounds.Y, YAxis.Title, Palette.AccentStyle, _axisWidth - 1);
            }

            if (XAxis.Title.Length > 0) {
                WriteFeature(
                    buffer,
                    plotBounds.X + plotBounds.Width / 3,
                    row,
                    XAxis.Title,
                    Palette.AccentStyle,
                    plotBounds.Width / 3
                );
            }
        }

        _featureGeometry.Build(
            data,
            range,
            GraphKind,
            plotBounds.Width,
            plotBounds.Height,
            Thresholds,
            Annotations,
            XAxis,
            YAxis,
            ReversalAmount,
            FlameReverse,
            GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
            flameFrameHeight: GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1,
            taskOffset: TaskOffset
        );
        foreach (var primitive in _featureGeometry.Primitives) {
            if (primitive.Text is not null) {
                WriteFeature(
                    buffer,
                    plotBounds.X + primitive.X0,
                    plotBounds.Y + primitive.Y0,
                    primitive.Text,
                    ChartTopology.Supports(GraphKind) && primitive.Series == 0
                        ? Palette.Body.WithBackground(Color.Mix(PixelBackground, PixelInk,
                            data[primitive.Series].Points[primitive.Point].Index == SelectedIndex ? .8f : .6f)).WithForeground(PixelBackground)
                        : GraphKind == GraphKind.Flamegraph && primitive.Point >= 0
                        ? Palette.Body.WithBackground(FlameColor(primitive.Shade)).WithForeground(PixelBackground)
                        : Palette.Body.WithForeground(primitive.Color ?? Palette.Accent),
                    ChartTopology.Supports(GraphKind) || GraphKind is GraphKind.Flamegraph or GraphKind.Gantt ? primitive.X1 - primitive.X0 + 1 : plotBounds.Width - primitive.X0
                );
            }
        }
    }

    private string FormatTick(ChartAxis axis, double value, ChartDataSeries[] data, bool horizontal) {
        if (horizontal && GraphKind == GraphKind.Gantt) {
            long ticks = Math.Clamp((long)(value * TimeSpan.TicksPerSecond), 0, DateTimeOffset.MaxValue.UtcTicks);
            return new DateTimeOffset(ticks, TimeSpan.Zero).ToString(axis.Format == "G4" ? "MM-dd HH:mm" : axis.Format, CultureInfo.InvariantCulture);
        }
        if (!horizontal && GraphKind is GraphKind.Marimekko or GraphKind.PercentStackedArea) {
            return axis.FormatValue(value) + "%";
        }
        if (!horizontal && GraphKind == GraphKind.Heatmap) {
            return data.Length == 0 ? "" : data[Math.Clamp(data.Length - 1 - (int)Math.Round(value), 0, data.Length - 1)].Property;
        }

        if (axis.Scale == ChartScale.Category) {
            ReadOnlySpan<ChartPoint> points = data.Length == 0 ? [] : data[0].Span;
            if (!horizontal && GraphKind == GraphKind.HorizontalBars) {
                value = points.Length - 1 - value;
            }

            if (points.IsEmpty) {
                return "";
            }

            int nearest = 0;
            double distance = Math.Abs(points[0].X - value);
            for (int index = 1; index < points.Length; index++) {
                double candidate = Math.Abs(points[index].X - value);
                if (Comparer<double>.Default.Compare(candidate, distance) < 0) {
                    nearest = index;
                    distance = candidate;
                }
            }

            return points[nearest].Label;
        }

        return axis.FormatValue(value);
    }

    internal IEnumerable<Rectangle> AnnotationBounds {
        get {
            if (ChartRadial.IsRadial(GraphKind)) {
                foreach (var label in GetRadialLabels(GetRenderData(Math.Max(1, Bounds.Width)))) {
                    yield return label.Bounds;
                }
                yield break;
            }
            if (Annotations.Count == 0 && Thresholds.Count == 0 && !ChartTopology.Supports(GraphKind) && GraphKind is not (GraphKind.Flamegraph or GraphKind.Gantt)) {
                yield break;
            }

            var data = GetRenderData(Math.Max(1, Bounds.Width));
            _featureGeometry.Build(
                data,
                RenderRange(data),
                GraphKind,
                PlotBounds.Width,
                PlotBounds.Height,
                Thresholds,
                Annotations,
                XAxis,
                YAxis,
                ReversalAmount,
                FlameReverse,
                GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
                flameFrameHeight: GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1,
                taskOffset: TaskOffset
            );
            foreach (var primitive in _featureGeometry.Primitives) {
                if (primitive.Text is { Length: > 0 } text) {
                    yield return new(
                        PlotBounds.X + primitive.X0,
                        PlotBounds.Y + primitive.Y0,
                        Math.Min(text.Length, ChartTopology.Supports(GraphKind) || GraphKind is GraphKind.Flamegraph or GraphKind.Gantt ? primitive.X1 - primitive.X0 + 1 : Math.Max(0, PlotBounds.Width - primitive.X0)),
                        1
                    );
                }
            }
        }
    }
}

internal readonly record struct ChartViewKey(
    double? MinimumX,
    double? MaximumX,
    ChartAxis XAxis,
    ChartAxis YAxis,
    bool Axes,
    bool Legend,
    int Thresholds,
    int Annotations,
    ChartSelectedRange? Selection,
    double ReversalAmount = 1,
    bool FlameReverse = false,
    string FlameFocusId = "",
    string FlameSearch = "",
    int TaskOffset = 0,
    int FlameFrameRows = 2
);
