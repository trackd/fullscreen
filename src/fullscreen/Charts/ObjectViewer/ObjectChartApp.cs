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
    internal string FrameIdProperty { get; private set; }
    internal string ParentIdProperty { get; private set; }
    internal string? FrameNameProperty { get; private set; }
    internal TaskFields TaskProperties { get; private set; }

    private readonly CancellationToken _token;
    private readonly bool _automaticY;
    private readonly ChartTopology? _diagram;
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
    ) : this(objects, xProperty, yProperties, graphType, title, new(), token) { }

    internal sealed record FrameFields(string Id = "FrameId", string Parent = "ParentId", string? Name = null);
    internal sealed record TaskFields(string Start = "Start", string End = "End", string Name = "Task", string? Progress = null) {
        internal void Validate() {
            if (string.IsNullOrWhiteSpace(Start) || Start.Length > 128 || string.IsNullOrWhiteSpace(End) || End.Length > 128
                || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Progress is not null && (string.IsNullOrWhiteSpace(Progress) || Progress.Length > 128)) {
                throw new ArgumentException("Gantt selectors must contain 1-128 characters; omit Progress for unknown completion.");
            }
        }
    }

    internal ObjectChartApp(
        IReadOnlyList<PSObject> objects,
        object? xProperty,
        object[]? yProperties,
        GraphKind graphType,
        string title,
        FrameFields fields,
        CancellationToken token,
        TaskFields? tasks = null,
        bool initialize = true,
        ChartTopology? diagram = null
    ) {
        if (!Enum.IsDefined(graphType)) {
            throw new ArgumentException("Unknown graph type.", nameof(graphType));
        }

        _token = token;
        _diagram = diagram;
        TaskProperties = tasks ?? new();
        TaskProperties.Validate();
        FrameIdProperty = fields.Id;
        ParentIdProperty = fields.Parent;
        FrameNameProperty = fields.Name;
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
        YProperties = yProperties is null ? ChartTopology.Supports(graphType) || graphType == GraphKind.Gantt ? [] : graphType == GraphKind.Flamegraph ? [Data.Resolve("Weight")] : Data.DefaultYProperty is { } initialY ? [initialY] : [] : [.. keys.Skip(xProperty is null ? 0 : 1).Distinct(StringComparer.OrdinalIgnoreCase)];
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
        Surface.Plot.FlameSearchRequested += SearchFrames;
        Surface.SearchFrames = SearchFrames;
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
        if (initialize) {
            Replot();
        }
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
        if (ChartTopology.Supports(graphType) || _diagram is not null) {
            if (_diagram is null || !ChartTopology.Supports(graphType) || x is not null || y.Length != 0
                || mode != ObjectChartFilter.All || RowSubset is not null || Aggregate != ChartAggregate.None
                || LowerProperty is not null || UpperProperty is not null) {
                throw new ArgumentException("Relationship diagrams require separate Nodes/Edges, complete topology, no X/Y properties, row filters, aggregation or error bounds.");
            }
            _ = _diagram.Layout(graphType);
            Surface.GraphType = graphType;
            Surface.Plot.XAxis = new();
            Surface.Plot.YAxis = new();
            Surface.Update(_diagram.Data, null, [.. Enumerable.Range(0, Data.Objects.Count)], "Complete topology");
            return;
        }

        string? rank = rankProperty is null ? null : Data.Resolve(rankProperty);
        string? effectiveRank = rank ?? y.FirstOrDefault() ?? Data.NumericProperties.FirstOrDefault();
        int[] indices = [.. Data.Select(mode, count, effectiveRank, _token).Where(index => RowSubset is null || RowSubset.Contains(index))];
        if (graphType == GraphKind.Flamegraph && (y.Length != 1 || Aggregate != ChartAggregate.None || mode != ObjectChartFilter.All
            || RowSubset is not null || x is not null || LowerProperty is not null || UpperProperty is not null)) {
            throw new ArgumentException("Flamegraph requires one self-weight Y property, complete parent rows, no X property, row filters, grouping or error bounds.");
        }
        if (graphType == GraphKind.Gantt && (x is not null || y.Length != 0 || Aggregate != ChartAggregate.None
            || LowerProperty is not null || UpperProperty is not null)) {
            throw new ArgumentException("Gantt uses StartProperty/EndProperty/TaskNameProperty/ProgressProperty, not X/Y properties, grouping or error bounds.");
        }
        var series = graphType == GraphKind.Gantt ? Data.ProjectTasks(TaskProperties.Start, TaskProperties.End, TaskProperties.Name, TaskProperties.Progress, indices)
            : graphType == GraphKind.Flamegraph ? Data.ProjectFrames(FrameIdProperty, ParentIdProperty, FrameNameProperty, y[0], indices)
            : Aggregate == ChartAggregate.None ? Data.Project(graphType is GraphKind.Histogram or GraphKind.BoxPlot ? null : x, y, indices, _token) : Data.AggregateRows(indices, GroupProperty, y, Aggregate, _token);
        Data.ApplyErrorBounds(series, LowerProperty, UpperProperty);

        foreach (var item in series) {
            if (_styles.TryGetValue(item.Property, out var style)) {
                item.Color = style.Color;
                item.Visible = style.Visible;
                item.LineStyle = style.LineStyle;
                item.Marker = style.Marker;
                item.Role = style.Role;
            }
        }

        if (ChartGrid.Supports(graphType)) {
            ChartGrid.Validate(series, graphType);
            _ = ChartRange.From(series, graphType, Surface.Plot.Minimum, Surface.Plot.Maximum);
            ChartGrid.ValidateOptions(graphType,
                _automaticAxisFormatting || x != oldX ? Data.ScaleFor(x) : Surface.Plot.XAxis.Scale,
                _automaticAxisFormatting || x != oldX ? ChartScale.Linear : Surface.Plot.YAxis.Scale);
        }
        if (ChartMarket.Supports(graphType)) {
            ChartMarket.Validate(series, graphType);
            _ = ChartRange.From(series, graphType, Surface.Plot.Minimum, Surface.Plot.Maximum);
            ChartMarket.ValidateOptions(graphType, Surface.Plot.ReversalAmount, Surface.Plot.Minimum, Surface.Plot.Maximum,
                Surface.Plot.MinimumX, Surface.Plot.MaximumX,
                _automaticAxisFormatting || x != oldX ? Data.ScaleFor(x) : Surface.Plot.XAxis.Scale,
                _automaticAxisFormatting || x != oldX ? ChartScale.Linear : Surface.Plot.YAxis.Scale,
                Surface.Plot.Thresholds.Count, Surface.Plot.Annotations.Count);
        }
        if (ChartStackedArea.Supports(graphType)) {
            ChartStackedArea.Validate(series);
            _ = ChartStackedArea.Range(series, graphType, Surface.Plot.Minimum, Surface.Plot.Maximum);
            ChartStackedArea.ValidateOptions(graphType, Surface.Plot.Minimum, Surface.Plot.Maximum,
                _automaticAxisFormatting || x != oldX ? Data.ScaleFor(x) : Surface.Plot.XAxis.Scale,
                _automaticAxisFormatting || x != oldX ? ChartScale.Linear : Surface.Plot.YAxis.Scale);
        }
        if (ChartRadial.IsRadial(graphType)) {
            ChartRadial.Validate(series, graphType);
            ChartRadial.ValidateOptions(graphType, Surface.Plot.Minimum, Surface.Plot.Maximum,
                _automaticAxisFormatting || x != oldX ? Aggregate == ChartAggregate.None ? Data.ScaleFor(x) : ChartScale.Category : Surface.Plot.XAxis.Scale,
                _automaticAxisFormatting || x != oldX ? ChartScale.Linear : Surface.Plot.YAxis.Scale,
                Surface.Plot.Thresholds.Count, Surface.Plot.Annotations.Count);
        }
        if (graphType == GraphKind.Gantt) {
            ChartSchedule.ValidateOptions(Surface.Plot.Minimum, Surface.Plot.Maximum,
                _automaticAxisFormatting || x != oldX ? ChartScale.Time : Surface.Plot.XAxis.Scale,
                Surface.Plot.YAxis.Scale, Surface.Plot.Thresholds.Count, Surface.Plot.Annotations.Count, Surface.Plot.TaskOffset);
        }
        bool scheduleChanged = graphType == GraphKind.Gantt && Surface.GraphType != graphType;
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

        if (x != oldX || scheduleChanged) {
            Surface.Plot.ResetView();
            if (scheduleChanged) {
                Surface.Plot.TaskOffset = 0;
            }
        }

        if (_automaticAxisFormatting || x != oldX) {
            Surface.Plot.XAxis = Surface.Plot.XAxis with {
                Scale = graphType == GraphKind.Gantt ? ChartScale.Time : graphType is GraphKind.HorizontalBars or GraphKind.Histogram or GraphKind.BoxPlot ? ChartScale.Linear : Aggregate == ChartAggregate.None ? Data.ScaleFor(x) : ChartScale.Category
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

    private void ConfigureFromMenu(string? x, string[] y, GraphKind kind) {
        try {
            Configure(x, y, kind);
            Surface.ShowMessage("");
        }
        catch (ArgumentException failure) {
            Surface.ShowMessage(failure.Message);
        }
    }

    private void ChooseX() {
        if (_diagram is not null) {
            Surface.ShowMessage("Diagram coordinates come from topology layout; node/edge fields are defined by the supplied collections.");
            return;
        }
        if (Surface.GraphType == GraphKind.Gantt) {
            EditTaskFields();
            return;
        }
        Menu(new[] { new MenuItem("Input order", () => ConfigureFromMenu(null, YProperties, Surface.GraphType)) }.Concat(Data.Properties.Select(name => new MenuItem(Data.MenuLabel(name), () => ConfigureFromMenu(name, YProperties, Surface.GraphType)))).Concat(CustomEntries(true)));
    }
    private void ChooseY() {
        if (_diagram is not null) {
            ChooseX();
            return;
        }
        if (Surface.GraphType == GraphKind.Gantt) {
            EditTaskFields();
            return;
        }
        Menu(Data.Properties.Select(name => new MenuItem(
            (YProperties.Contains(name, StringComparer.OrdinalIgnoreCase) ? "✓ " : "  ") + Data.MenuLabel(name),
            () => ConfigureFromMenu(
                XProperty,
                YProperties.Contains(name, StringComparer.OrdinalIgnoreCase) ? [.. YProperties.Where(value => !value.Equals(name, StringComparison.OrdinalIgnoreCase))] : [
                    ..YProperties,
                    name
                ],
                Surface.GraphType
            )
        )).Concat(CustomEntries(false)));
    }
    private void EditTaskFields() {
        var start = new TextBox(TaskProperties.Start);
        var end = new TextBox(TaskProperties.End);
        var name = new TextBox(TaskProperties.Name);
        var progress = new TextBox(TaskProperties.Progress ?? "");
        Editor("Gantt task fields", [Field("Start", start), Field("End", end), Field("Task label", name), Field("Progress (0..1, optional)", progress)], () => {
            SetTaskFields(new(start.Text ?? "", end.Text ?? "", name.Text ?? "", string.IsNullOrEmpty(progress.Text) ? null : progress.Text));
        });
    }
    internal void SetTaskFields(TaskFields fields) {
        fields.Validate();
        var previous = TaskProperties;
        try {
            TaskProperties = fields;
            Replot();
        }
        catch {
            TaskProperties = previous;
            throw;
        }
    }
    private void EditFlameFrameRows() {
        var height = new TextBox(Surface.Plot.FlameFrameRows.ToString(CultureInfo.InvariantCulture));
        Editor("Flamegraph frame height", [Field("Terminal rows per depth (1-8)", height)], () => {
            int rows = int.Parse(height.Text ?? "", CultureInfo.InvariantCulture);
            if (rows is < 1 or > 8) {
                throw new ArgumentException("FlameFrameRows must be between 1 and 8 terminal rows.");
            }
            Surface.Plot.FlameFrameRows = rows;
        });
    }
    private void ChooseType() {
        if (_diagram is not null) {
            Menu([
                new MenuItem("FlowChart", () => ConfigureFromMenu(null, [], GraphKind.FlowChart)),
                new MenuItem("NetworkDiagram", () => ConfigureFromMenu(null, [], GraphKind.NetworkDiagram)),
                MenuItem.Separator(),
                new MenuItem("Export / save…", Export),
                new MenuItem("Load configuration…", LoadConfigurationDialog)
            ]);
            return;
        }
        Menu(Enum.GetValues<GraphKind>().Where(kind => ChartTopology.Supports(kind) == (_diagram is not null)).Select(kind => new MenuItem(kind.ToString(), () => ConfigureFromMenu(kind == GraphKind.Gantt ? null : XProperty, kind == GraphKind.Gantt ? [] : YProperties, kind))).Concat([
            MenuItem.Separator(),
            new MenuItem("Axes and view…", EditAxes),
            new MenuItem("Series styles…", EditSeries),
            new MenuItem("Gantt task fields…", EditTaskFields),
            new MenuItem("Flamegraph frame rows…", EditFlameFrameRows),
            new MenuItem("Error bounds…", EditErrorBounds),
            new MenuItem("Thresholds and annotations…", EditAnnotations),
            new MenuItem("Reset zoom / range", () => Surface.Plot.ResetView()),
            new MenuItem("Export / save…", Export),
            new MenuItem("Load configuration…", LoadConfigurationDialog),
            new MenuItem(Paused ? "Resume updates" : "Pause updates", () => Paused = !Paused)
        ]));
    }
    private void ChooseFilter() {
        if (_diagram is not null) {
            Surface.ShowMessage("Relationship diagrams retain the complete topology. Filter node and edge collections together before invoking Out-Chart.");
            return;
        }
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

    private void SearchFrames() {
        var input = new TextBox(Surface.Plot.FlameSearch);
        var error = new TextBlock("");
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Search frame names"),
            IsModal = true,
            Width = 60,
            Content = new VStack(new TextBlock("Case-insensitive substring; empty clears search"), input, error,
                new HStack(new Button("Cancel").Click(() => dialog!.Close()),
                    new Button("Apply").Click(() => {
                        string term = input.Text ?? "";
                        if (term.Length > 1024) {
                            error.Text = "Search must contain at most 1024 characters.";
                            return;
                        }
                        Surface.Plot.FlameSearch = term;
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
