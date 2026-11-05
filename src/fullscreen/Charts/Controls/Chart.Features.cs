using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
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
        SelectedRange
    );
    private int LegendRows => ShowLegend && GetData(Math.Max(1, Bounds.Width)).Length > 0 ? 1 : 0;
    private int TitleRows => Title.Length == 0 ? 0 : 1;
    private Rectangle ContentPlotBounds => new(
        Bounds.X + (ShowAxes ? _axisWidth : 0),
        Bounds.Y + TitleRows + LegendRows,
        Math.Max(0, Bounds.Width - (ShowAxes ? _axisWidth : 0)),
        Math.Max(0, Bounds.Height - TitleRows - LegendRows - (ShowAxes ? 1 : 0))
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

        if (ShowAxes) {
            var data = GetRenderData(Math.Max(1, Bounds.Width));
            var range = RenderRange(data);
            int labelWidth = Math.Max(
                FormatTick(YAxis, YAxis.Inverse(range.MinY), data, false).Length,
                FormatTick(YAxis, YAxis.Inverse(range.MaxY), data, false).Length
            );
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
        if (GraphKind is not (GraphKind.Histogram or GraphKind.BoxPlot)) {
            _distributionKey = null;
            _distributions = [];
        }

        bool transform = XAxis.Scale == ChartScale.Logarithmic || YAxis.Scale == ChartScale.Logarithmic;
        if (!transform && data.All(series => series.Visible) && MinimumX is null && MaximumX is null) {
            _projectionKey = null;
            _projection = [];
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
                bool visible = (!MinimumX.HasValue || viewX >= MinimumX.Value) && (!MaximumX.HasValue || viewX <= MaximumX.Value);
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
                Marker = series.Marker
            });
        }

        _projection = [.. projected];
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

        _range = new(XAxis.Transform(minX), XAxis.Transform(maxX), YAxis.Transform(minY), YAxis.Transform(maxY));
        _rangeKey = cacheKey;
        return _range;
    }

    /// <summary>Sets the horizontal viewport, using automatic bounds for null limits.</summary>
    /// <param name="minimumX">The lower X-coordinate limit, or null for automatic bounds.</param>
    /// <param name="maximumX">The upper X-coordinate limit, or null for automatic bounds.</param>
    public void SetView(double? minimumX, double? maximumX) {
        if (minimumX.HasValue && !double.IsFinite(minimumX.Value)
            || maximumX.HasValue && !double.IsFinite(maximumX.Value)
            || minimumX > maximumX) {
            throw new ArgumentException("X limits must be finite and ordered.");
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
        if (!double.IsFinite(factor) || factor <= 0) {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double min = vertical ? range.MinY : range.MinX, max = vertical ? range.MaxY : range.MaxX;
        double center = min / 2 + max / 2, half = Math.Max(1e-12, (max / 2 - min / 2) / factor);
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
            SetView(low, high);
        }
    }

    /// <summary>Moves the horizontal or vertical viewport by a fraction of its span.</summary>
    /// <param name="fraction">The signed portion of the current viewport span to pan.</param>
    /// <param name="vertical">Whether to change the Y viewport instead of the X viewport. Defaults to
    /// false.</param>
    public void Pan(double fraction, bool vertical = false) {
        if (!double.IsFinite(fraction)) {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double min = vertical ? range.MinY : range.MinX, max = vertical ? range.MaxY : range.MaxX;
        double delta = (max - min) * fraction;
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
        SelectRange(null);
    }

    /// <summary>Sets or clears the selected horizontal data range.</summary>
    /// <param name="range">The selected horizontal interval, or null to clear the selection.</param>
    public void SelectRange(ChartSelectedRange? range) {
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
        if (!Enum.IsDefined(style.LineStyle) || !Enum.IsDefined(style.Marker)) {
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
        }
        else {
            data[index].Color = style.Color;
            data[index].Visible = style.Visible;
            data[index].LineStyle = style.LineStyle;
            data[index].Marker = style.Marker;
        }

        Refresh();
    }

    private double PointerX(int x) {
        var range = RenderRange(GetRenderData(Math.Max(1, Bounds.Width)));
        double fraction = Math.Clamp((x - PlotBounds.X) / (double)Math.Max(1, PlotBounds.Width - 1), 0, 1);
        return XAxis.Inverse(range.MinX * (1 - fraction) + range.MaxX * fraction);
    }

    private bool HandleFeaturePointer(PointerEventArgs e) {
        if (ShowLegend && e.UiY == Bounds.Y + TitleRows) {
            var data = GetData(Math.Max(1, Bounds.Width));
            int column = Bounds.X;
            for (int index = 0; index < data.Length; index++) {
                int length = LegendLabel(data[index]).Length + 2;
                if (e.UiX >= column && e.UiX < column + length) {
                    SetSeriesVisible(index, !data[index].Visible);
                    break;
                }

                column += length;
            }

            e.Handled = true;
            return true;
        }

        if ((e.Modifiers & TerminalModifiers.Shift) != 0 && PlotBounds.Contains(e.UiX, e.UiY)) {
            _dragStart = PointerX(e.UiX);
            SelectRange(new(_dragStart.Value, _dragStart.Value));
            e.Handled = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e) {
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
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        Zoom(e.WheelDelta > 0 ? 1.25 : .8, (e.Modifiers & TerminalModifiers.Ctrl) != 0);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e) {
        if (!Interactive || e.RoutingPhase == RoutingPhase.Preview) {
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

        if (e.Key is not (TerminalKey.Left or TerminalKey.Right or TerminalKey.Home or TerminalKey.End)) {
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

    private static string LegendLabel(ChartDataSeries series) =>
        (series.Visible ? "● " : "○ ") + (series.Property.Length == 0 ? "Values" : series.Property);
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
        if (ShowLegend) {
            int x = Bounds.X, index = 0;
            foreach (var series in GetData(Math.Max(1, Bounds.Width))) {
                string label = LegendLabel(series);
                WriteFeature(
                    buffer,
                    x,
                    Bounds.Y + TitleRows,
                    label,
                    series.Visible ? Palette.Body.WithForeground(series.Color ?? DefaultSeriesColor(index)) : Palette.Muted,
                    label.Length
                );
                x += label.Length + 2;
                index++;
            }
        }

        if (ShowAxes) {
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
            for (int tick = 0; tick < XAxis.TickCount; tick++) {
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
            YAxis
        );
        foreach (var primitive in _featureGeometry.Primitives) {
            if (primitive.Text is not null) {
                WriteFeature(
                    buffer,
                    plotBounds.X + primitive.X0,
                    plotBounds.Y + primitive.Y0,
                    primitive.Text,
                    Palette.Body.WithForeground(primitive.Color ?? Palette.Accent),
                    plotBounds.Width - primitive.X0
                );
            }
        }
    }

    private string FormatTick(ChartAxis axis, double value, ChartDataSeries[] data, bool horizontal) {
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
            if (Annotations.Count == 0 && Thresholds.Count == 0) {
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
                YAxis
            );
            foreach (var primitive in _featureGeometry.Primitives) {
                if (primitive.Text is { Length: > 0 } text) {
                    yield return new(
                        PlotBounds.X + primitive.X0,
                        PlotBounds.Y + primitive.Y0,
                        Math.Min(text.Length, Math.Max(0, PlotBounds.Width - primitive.X0)),
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
    ChartSelectedRange? Selection
);
