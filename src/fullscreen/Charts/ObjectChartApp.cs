using System.Management.Automation;
using System.Globalization;
using fullscreen.Hosting;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartApp : IFullscreenApp {
    internal ObjectChartData Data { get; private set; }
    internal ObjectChartSurface Surface { get; }
    public Visual Root => Surface;

    public void RequestClose() =>
        Surface.Cancel();
    internal IEnumerable<PSObject> Chosen {
        get {
            if (!Surface.PassThru || !Surface.Accepted || _token.IsCancellationRequested) {
                yield break;
            }

            foreach (int index in ChosenIndices()) {
                yield return Data.Objects[index];
            }
        }
    }

    internal string? XProperty { get; private set; }
    internal string[] YProperties { get; private set; }

    private readonly CancellationToken _token;
    private readonly bool _automaticY;
    internal ObjectChartFilter FilterMode { get; private set; }
    internal int Count { get; private set; } = 30;
    internal string? RankProperty { get; private set; }

    internal ObjectChartApp(
        IReadOnlyList<PSObject> objects,
        object? xProperty = null,
        object[]? yProperties = null,
        GraphKind graphType = GraphKind.Line,
        string title = "Chart",
        CancellationToken token = default
    ) {
        if (!Enum.IsDefined(graphType)) {
            throw new ArgumentException("Unknown graph type.", nameof(graphType));
        }

        _token = token;
        _automaticY = yProperties is null;
        Data = new(objects, token);
        var specifications = new List<object>();
        if (xProperty is not null) {
            specifications.Add(xProperty);
        }

        if (yProperties is not null) {
            specifications.AddRange(yProperties);
        }

        string[] keys = Data.BindProperties(specifications, token);
        XProperty = xProperty is null ? null : keys[0];
        YProperties = yProperties is null ? Data.DefaultYProperty is { } initialY ? [initialY] : [] : [.. keys.Skip(xProperty is null ? 0 : 1).Distinct(StringComparer.OrdinalIgnoreCase)];
        Surface = new(Data) {
            Title = title,
            GraphType = graphType,
            ChooseX = ChooseX,
            ChooseY = ChooseY,
            ChooseType = ChooseType,
            ChooseFilter = ChooseFilter,
            Inspect = Inspect,
            ShowData = () => ShowData()
        };
        Surface.Plot.SelectionChanged += selection => Surface.SelectOriginal(SourceIndices(selection).FirstOrDefault(-1));
        Surface.Plot.PointInvoked += _ => Inspect();
        Surface.Plot.PauseRequested += () => Paused = !Paused;
        Surface.Plot.RangeSelected += range => {
            if (range is not null && RefreshSource is not null) {
                Paused = true;
            }
        };
        Surface.Plot.SeriesVisibilityChanged += (index, visible) => {
            if ((uint)index < (uint)Surface.Series.Length) {
                var series = Surface.Series[index];
                _styles[series.Property] = new(series.Color, visible, series.LineStyle, series.Marker);
            }
        };
        Replot();
    }

    internal void Configure(string? xProperty, string[] yProperties, GraphKind graphType) {
        Apply(xProperty, yProperties, graphType, FilterMode, Count, RankProperty);
    }

    internal void SetFilter(ObjectChartFilter mode, int count, string? rankProperty = null) {
        Apply(XProperty, YProperties, Surface.GraphType, mode, count, rankProperty);
    }

    private void Apply(
        string? xProperty,
        string[] yProperties,
        GraphKind graphType,
        ObjectChartFilter mode,
        int count,
        string? rankProperty
    ) {
        string? oldX = XProperty;
        string? x = xProperty is null ? null : Data.Resolve(xProperty);
        string[] y = [.. yProperties.Select(Data.Resolve).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (!Enum.IsDefined(graphType)) {
            throw new ArgumentException("Unknown graph type.", nameof(graphType));
        }

        string? rank = rankProperty is null ? null : Data.Resolve(rankProperty);
        string? effectiveRank = rank ?? y.FirstOrDefault() ?? Data.NumericProperties.FirstOrDefault();
        int[] indices = [.. Data.Select(mode, count, effectiveRank, _token).Where(index => RowSubset is null || RowSubset.Contains(index))];
        var series = Aggregate == ChartAggregate.None ? Data.Project(graphType is GraphKind.Histogram or GraphKind.BoxPlot ? null : x, y, indices, _token) : Data.AggregateRows(indices, GroupProperty, y, Aggregate, _token);
        if (LowerProperty is not null || UpperProperty is not null) {
            foreach (var item in series) {
                for (int position = 0; position < item.Points.Length; position++) {
                    var point = item.Points[position];
                    if (point.SourceIndices is null) {
                        item.Points[position] = point with {
                            Lower = LowerProperty is null ? null : ObjectChartData.Number(Data.Value(point.Index, LowerProperty)),
                            Upper = UpperProperty is null ? null : ObjectChartData.Number(Data.Value(point.Index, UpperProperty))
                        };
                    }
                }
            }
        }

        foreach (var item in series) {
            if (_styles.TryGetValue(item.Property, out var style)) {
                item.Color = style.Color;
                item.Visible = style.Visible;
                item.LineStyle = style.LineStyle;
                item.Marker = style.Marker;
            }
        }

        XProperty = x;
        YProperties = y;
        FilterMode = mode;
        Count = count;
        RankProperty = rank;
        Surface.GraphType = graphType;
        string label = mode == ObjectChartFilter.All ? "All" : $"{mode} {count}" + (mode is ObjectChartFilter.Top or ObjectChartFilter.Bottom ? $" by {(effectiveRank is null ? "(choose a numeric field)" : Data.Label(effectiveRank))}" : "");
        if (Aggregate != ChartAggregate.None) {
            label += $" · {Aggregate} by {(GroupProperty is null ? "all rows" : Data.Label(GroupProperty))}";
        }

        if (x != oldX) {
            Surface.Plot.ResetView();
        }

        if (_automaticAxisFormatting || x != oldX) {
            Surface.Plot.XAxis = Surface.Plot.XAxis with {
                Scale = graphType is GraphKind.HorizontalBars or GraphKind.Histogram or GraphKind.BoxPlot ? ChartScale.Linear : Aggregate == ChartAggregate.None ? Data.ScaleFor(x) : ChartScale.Category
            };
            Surface.Plot.YAxis = Surface.Plot.YAxis with {
                Scale = graphType is GraphKind.HorizontalBars or GraphKind.Heatmap ? ChartScale.Category : ChartScale.Linear
            };
        }

        if (graphType is GraphKind.HorizontalBars or GraphKind.StackedBars or GraphKind.Heatmap
            && Surface.Plot.YAxis.Scale == ChartScale.Logarithmic) {
            Surface.Plot.YAxis = Surface.Plot.YAxis with {
                Scale = graphType == GraphKind.StackedBars ? ChartScale.Linear : ChartScale.Category
            };
        }

        Surface.Update(series, x is null ? null : Data.Label(x), indices, label);
    }

    private void Replot() =>
        Configure(XProperty, YProperties, Surface.GraphType);
    private void Menu(IEnumerable<MenuItem> items) {
        var anchor = Surface.MenuAnchor ?? new XenoAtom.Terminal.UI.Geometry.Rectangle(Surface.Bounds.X, Surface.Bounds.Y, 1, 1);
        ContextMenuService.Show(Surface, items, anchor.X, anchor.Y);
    }

    private void ChooseX() =>
        Menu(new[] { new MenuItem("Input order", () => Configure(null, YProperties, Surface.GraphType)) }.Concat(Data.Properties.Select(name => new MenuItem(Data.MenuLabel(name), () => Configure(name, YProperties, Surface.GraphType)))).Concat(CustomEntries(true)));
    private void ChooseY() =>
        Menu(Data.Properties.Select(name => new MenuItem(
            (YProperties.Contains(name, StringComparer.OrdinalIgnoreCase) ? "✓ " : "  ") + Data.MenuLabel(name),
            () => Configure(
                XProperty,
                YProperties.Contains(name, StringComparer.OrdinalIgnoreCase) ? [.. YProperties.Where(value => !value.Equals(name, StringComparison.OrdinalIgnoreCase))] : [
                    ..YProperties,
                    name
                ],
                Surface.GraphType
            )
        )).Concat(CustomEntries(false)));
    private void ChooseType() =>
        Menu(Enum.GetValues<GraphKind>().Select(kind => new MenuItem(kind.ToString(), () => Configure(XProperty, YProperties, kind))).Concat([
            MenuItem.Separator(),
            new MenuItem("Axes and view…", EditAxes),
            new MenuItem("Series styles…", EditSeries),
            new MenuItem("Error bounds…", EditErrorBounds),
            new MenuItem("Thresholds and annotations…", EditAnnotations),
            new MenuItem("Reset zoom / range", () => Surface.Plot.ResetView()),
            new MenuItem("Export / save…", Export),
            new MenuItem("Load configuration…", LoadConfigurationDialog),
            new MenuItem(Paused ? "Resume updates" : "Pause updates", () => Paused = !Paused)
        ]));
    private void ChooseFilter() {
        var counts = new MenuItem($"Count: {Count}");
        foreach (int count in new[]
        {
            10,
            30,
            50,
            100
        }

        ) {
            counts.Items.Add(new MenuItem(
                count.ToString(CultureInfo.InvariantCulture),
                () => SetFilter(FilterMode, count, RankProperty)
            ));
        }

        counts.Items.Add(new MenuItem("Custom…", ChooseCount));
        var rank = new MenuItem("Rank by: " + (RankProperty is null ? "first selected Y" : Data.Label(RankProperty)));
        rank.Items.Add(new MenuItem("First selected Y", () => SetFilter(FilterMode, Count)));
        foreach (string name in Data.Properties) {
            rank.Items.Add(new MenuItem(Data.MenuLabel(name), () => SetFilter(FilterMode, Count, name)));
        }

        Menu([new MenuItem("All objects", () => SetFilter(ObjectChartFilter.All, Count, RankProperty)), new MenuItem($"First {Count}", () => SetFilter(ObjectChartFilter.First, Count, RankProperty)), new MenuItem($"Last {Count}", () => SetFilter(ObjectChartFilter.Last, Count, RankProperty)), new MenuItem($"Top {Count}", () => SetFilter(ObjectChartFilter.Top, Count, RankProperty)), new MenuItem($"Bottom {Count}", () => SetFilter(ObjectChartFilter.Bottom, Count, RankProperty)), counts, rank, MenuItem.Separator(), new MenuItem("Group / aggregate…", EditAggregation), new MenuItem("Clear table subset", () =>
        {
            RowSubset = null;
            Replot();
        })]);
    }

    private void ChooseCount() {
        var input = new TextBox(Count.ToString(CultureInfo.InvariantCulture));
        var error = new TextBlock("");
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Object count"),
            IsModal = true,
            Width = 50,
            Content = new VStack(new TextBlock("Number of objects (1–1000000)"), input, error, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Apply").Click(() => {
                if (!int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                    || count is < 1 or > 1000000) {
                    error.Text = "Enter a whole number from 1 to 1000000.";
                    return;
                }

                SetFilter(FilterMode, count, RankProperty);
                dialog!.Close();
            })))
        };
        dialog.Show();
    }

    private void Inspect() {
        if (Surface.Plot.Selection?.Source is ChartGroup) {
            ShowData([.. SourceIndices(Surface.Plot.Selection)]);
            return;
        }

        if (Surface.SelectedIndex < 0) {
            return;
        }

        var model = Objects.ObjectSnapshot.Inspect(Data.Objects[Surface.SelectedIndex]);
        string text = string.Join("\n", model.Rows.Select(row => string.Join(": ", row.Cells.Take(2))));
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock($"Object {Surface.SelectedIndex + 1}"),
            IsModal = true,
            Width = Math.Max(20, Math.Min(100, Surface.Bounds.Width - 2)),
            Content = new VStack(
                new ScrollViewer(new Paragraph(text) { Wrap = true }) { MaxHeight = Math.Max(3, Surface.Bounds.Height - 6) },
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        dialog.Show();
    }

    public bool Tick() {
        if (Surface.ExitRequested || _token.IsCancellationRequested) {
            return false;
        }

        if (!Paused && !HasDataOverlay && RefreshSource is not null
            && _clock.GetElapsedTime(_lastRefresh).TotalMilliseconds >= RefreshInterval) {
            _lastRefresh = _clock.GetTimestamp();
            try {
                ReplaceObjects(RefreshSource());
            }
            catch (PipelineStoppedException) {
                throw;
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) {
                throw;
            }
            catch (Exception failure) {
                Paused = true;
                Surface.ShowMessage("Refresh paused: " + failure.Message);
            }
        }

        return true;
    }

    public void Dispose() {
    }
}
