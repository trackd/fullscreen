using System.Globalization;
using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using static fullscreen.Charts.ChartMath;

namespace fullscreen.Charts;
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
            _inspection.IsVisible = value && ShowTooltips;
            if (!value) {
                ClearInspection();
            }
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
    internal bool PreferTerminalCells { get; set; }
    internal bool UseSixel => _exportingPixels || !PreferTerminalCells && ChartPresenters.Supports(App, GraphKind);
    internal Rectangle PlotBounds => ContentPlotBounds;
    internal virtual bool UsesWidthForData => false;
    internal ChartPixelKey PixelKey {
        get {
            UpdateAutomaticColors();
            return new(
                this, DataVersion, Minimum, Maximum, PixelInk, PixelBackground, Palette,
                GraphKind, SelectedIndex, InspectionColumn, ViewVersion
            );
        }
    }

    internal virtual long DataVersion {
        get {
            _ = GetData(Math.Max(1, Bounds.Width));
            return _dataVersion;
        }
    }

    /// <summary>Initializes a new Chart instance.</summary>
    public Chart() {
        ReversalAmount = 1;
        FlameFocusId = "";
        FlameFrameRows = 2;
        Title = "";
        XAxis = new();
        YAxis = new();
        Interactive = true;
        SelectedIndex = InspectionColumn = -1;
        FlameSearch = "";
        Values = new(this, nameof(Values));
        XValues = new(this, nameof(XValues));
        Series = new(this, nameof(Series), series => series.Changed += Refresh, series => series.Changed -= Refresh);
        Labels = new(this, nameof(Labels));
        Sources = new(this, nameof(Sources));
        Frames = new(this, nameof(Frames));
        Tasks = new(this, nameof(Tasks));
        Nodes = new(this, nameof(Nodes));
        Edges = new(this, nameof(Edges));
        Thresholds = new(this, nameof(Thresholds));
        Annotations = new(this, nameof(Annotations));
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        _inspection.Content = new InspectionAnchor(this) {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        AttachChild(_inspection);
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

    private void UpdateAutomaticColors() {
        if (!_explicitColors) {
            PixelInk = Palette.Accent;
            PixelBackground = Palette.Background;
        }
    }

    internal void SetData(ChartDataSeries[] series) {
        InvalidateDerivedData();
        _adapted = series;
        for (int index = 0; index < series.Length; index++) {
            series[index].SourceIndex = index;
        }
        Revision = unchecked((int)++_dataVersion);
    }

    internal virtual ChartDataSeries[] GetData(int width) {
        if (_adapted is not null) {
            return _adapted;
        }
        if (ChartTopology.Supports(GraphKind)) {
            if (_topology is null || _nodeVersion != Nodes.Version || _edgeVersion != Edges.Version) {
                _topology = new(Nodes, Edges);
                _nodeVersion = Nodes.Version;
                _edgeVersion = Edges.Version;
                InvalidateDerivedData();
                _dataVersion++;
            }
            return _topology.Data;
        }
        if (GraphKind == GraphKind.Gantt) {
            if (_schedule is null || _taskVersion != Tasks.Version) {
                _schedule = new(Tasks);
                _taskVersion = Tasks.Version;
                InvalidateDerivedData();
                _dataVersion++;
            }
            return _schedule.Data;
        }
        if (GraphKind == GraphKind.Flamegraph) {
            if (_flameTree is null || _frameVersion != Frames.Version) {
                _flameTree = new(Frames);
                _frameVersion = Frames.Version;
                InvalidateDerivedData();
                _dataVersion++;
            }
            return _flameTree.Data;
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
            primary.Role = _primaryStyle.Role;
            primary.SourceIndex = 0;
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
            projected.Role = series.Role;
            projected.SourceIndex = data.Count;
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
        UpdateAutomaticColors();
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
            YAxis,
            ReversalAmount,
            FlameReverse,
            GraphKind == GraphKind.Flamegraph ? FlameFocus(GetFlameTree()) : -1,
            GraphKind is GraphKind.Flamegraph or GraphKind.Gantt
                ? Math.Max(1, PlotBounds.Height > 0 ? height / PlotBounds.Height : 16) * (GraphKind == GraphKind.Flamegraph ? FlameFrameRows : 1)
                : 1,
            TaskOffset
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
        MeasureInspection(constraints);
        return SizeHints.Flex(new(0, 0), new(48, 8), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        UpdateAutomaticColors();

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
        if (ChartRadial.IsRadial(GraphKind)) {
            return PickRadial(rendered, x, y);
        }
        if (ChartGrid.Supports(GraphKind) || ChartStackedArea.Supports(GraphKind)) {
            return PickGrid(rendered, x, y);
        }
        if (ChartMarket.Supports(GraphKind)) {
            return PickGrid(rendered, x, y);
        }
        if (ChartTopology.Supports(GraphKind)) {
            return PickDiagram(rendered, x, y);
        }
        if (GraphKind is GraphKind.Flamegraph or GraphKind.Gantt) {
            return PickGrid(rendered, x, y);
        }
        var range = RenderRange(rendered);
        var plotBounds = PlotBounds;
        bool distribution = GraphKind is GraphKind.Histogram or GraphKind.BoxPlot;
        double best = double.PositiveInfinity;
        ChartSelection? selected = null;
        var stacks = GraphKind == GraphKind.StackedBars ? new Dictionary<double, (double Positive, double Negative)>() : null;
        var columns = new ChartColumns(data, GraphKind);
        for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
            var series = data[seriesIndex];
            if (!series.Visible) {
                continue;
            }

            int boxRow = GraphKind == GraphKind.BoxPlot ? Array.FindIndex(rendered, visible => visible.SourceIndex == seriesIndex) : 0;
            ChartPoint? previous = null;
            foreach (var point in series.Span) {
                if (!point.Y.HasValue) {
                    previous = null;
                    continue;
                }

                double px = XAxis.Transform(distribution || GraphKind == GraphKind.HorizontalBars ? point.Y.Value : point.X), py = YAxis.Transform(point.Y.Value);
                if (!double.IsFinite(px) || !double.IsFinite(py)) {
                    previous = null;
                    continue;
                }

                double viewX = GraphKind == GraphKind.HorizontalBars ? point.Y.Value : point.X;
                if (MinimumX is { } low && viewX < low || MaximumX is { } high && viewX > high) {
                    previous = null;
                    continue;
                }

                double center = GraphKind is GraphKind.Bars or GraphKind.ColumnLine ? columns.Center(px, range, plotBounds.Width) : Fraction(px, range.MinX, range.MaxX) * Math.Max(0, plotBounds.Width - 1);
                double dx = center - (x - plotBounds.X);
                double dy = distribution ? 0 : (1 - Fraction(py, range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1) - (y - plotBounds.Y);
                if (GraphKind == GraphKind.BoxPlot) {
                    dy = Math.Min(plotBounds.Height - 1, boxRow * Math.Max(1, plotBounds.Height / rendered.Length) + plotBounds.Height / (rendered.Length * 2)) - (y - plotBounds.Y);
                }
                if (GraphKind == GraphKind.ColumnLine) {
                    dy = Math.Round(dy + y - plotBounds.Y) - (y - plotBounds.Y);
                }
                if (GraphKind is GraphKind.Bars or GraphKind.ColumnLine && ChartColumns.Kind(series, GraphKind, seriesIndex) == GraphKind.Bars) {
                    var column = columns.Bounds(seriesIndex, px, range, plotBounds.Width);
                    double mouseX = x - plotBounds.X, mouseY = y - plotBounds.Y;
                    dx = mouseX < column.X ? column.X - mouseX : mouseX >= column.Right ? mouseX - (column.Right - 1) : 0;
                    double baseline = (1 - Fraction(0, range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1);
                    double valueY = dy + mouseY;
                    double top = Math.Min(baseline, valueY), bottom = Math.Max(baseline, valueY);
                    dy = mouseY < top ? top - mouseY : mouseY > bottom ? bottom - mouseY : 0;
                }
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
                var candidate = point;
                if (GraphKind == GraphKind.ColumnLine && ChartColumns.Kind(series, GraphKind, seriesIndex) == GraphKind.Line
                    && previous is { } before) {
                    double x0 = columns.Center(XAxis.Transform(before.X), range, plotBounds.Width);
                    double y0 = Math.Round((1 - Fraction(YAxis.Transform(before.Y!.Value), range.MinY, range.MaxY)) * Math.Max(0, plotBounds.Height - 1));
                    double x1 = center, y1 = dy + y - plotBounds.Y;
                    double sx = x1 - x0, sy = y1 - y0, length = sx * sx + sy * sy;
                    double t = length == 0 ? 0 : Math.Clamp(((x - plotBounds.X - x0) * sx + (y - plotBounds.Y - y0) * sy) / length, 0, 1);
                    double segmentX = x0 + t * sx - (x - plotBounds.X), segmentY = y0 + t * sy - (y - plotBounds.Y);
                    double segmentDistance = segmentX * segmentX + segmentY * segmentY;
                    if (segmentDistance <= .5 || segmentDistance < distance) {
                        distance = segmentDistance <= .5 ? 0 : segmentDistance;
                        candidate = t < .5 ? before : point;
                    }
                }

                previous = point;
                if (distance < best || distance == best && GraphKind == GraphKind.ColumnLine
                    && ChartColumns.Kind(series, GraphKind, seriesIndex) == GraphKind.Line) {
                    best = distance;
                    selected = new(
                        seriesIndex,
                        series.Property,
                        candidate.Index,
                        candidate.X,
                        candidate.Y!.Value,
                        candidate.Label,
                        candidate.Source
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

        ClearInspection();
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
        if (GraphKind == GraphKind.Flamegraph && e.ClickCount < 2) {
            FocusSelectedFrame();
        }
        if (e.ClickCount >= 2) {
            PointActivated?.Invoke(selected.PointIndex);
            PointInvoked?.Invoke(selected);
        }

        e.Handled = true;
    }

    private Color DefaultSeriesColor(int index) =>
        AutomaticSeriesColor(Palette, PixelInk, GraphKind, index);

    internal static bool UsesStackedPalette(GraphKind kind) =>
        kind is GraphKind.StackedBars or GraphKind.StackedArea or GraphKind.PercentStackedArea or GraphKind.Marimekko;

    internal static Color AutomaticSeriesColor(Themes.ThemePalette palette, Color ink, GraphKind kind, int index) {
        if (index >= 0 && UsesStackedPalette(kind)) {
            var primary = ink.ToRgb();
            var purple = palette.Purple.ToRgb();
            bool errorFirst = (primary.R - purple.R) * (primary.R - purple.R)
                + (primary.G - purple.G) * (primary.G - purple.G)
                + (primary.B - purple.B) * (primary.B - purple.B) < 10_000;
            return (index % 5) switch {
                0 => ink,
                1 => errorFirst ? palette.Error : palette.Purple,
                2 => errorFirst ? palette.Purple : palette.Warning,
                3 => errorFirst ? palette.Warning : palette.Success,
                _ => errorFirst ? palette.Success : palette.Error
            };
        }
        return (index % 5) switch {
            0 => ink,
            1 => palette.Success,
            2 => palette.Warning,
            3 => palette.Purple,
            _ => palette.Error
        };
    }
}

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
