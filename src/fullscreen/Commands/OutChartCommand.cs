using System.Management.Automation;
using fullscreen.Charts;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Charts pipeline objects interactively and lets you change fields and graph types.</summary>
[Cmdlet(VerbsData.Out, "Chart", DefaultParameterSetName = "Objects")]
[OutputType(typeof(PSObject))]
public sealed class OutChartCommand : FullscreenCommand {
    private readonly List<PSObject> _objects = [];
    private ScriptBlock? _boundedRefresh;
    /// <summary>Select Auto, Sixel, Native or Regis. DRCS chart rendering is disabled. Regis is an
    /// experimental, explicitly selected Contour vector backend; Area, Histogram, BoxPlot, ColumnLine and radial kinds use cell
    /// fallback. Auto continues to select Sixel with cell fallback.</summary>
    [Parameter]
    public ChartRenderer ChartRenderer { get; set; }

    /// <summary>Complete objects to chart. Accepts pipeline input or an array.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0, ParameterSetName = "Objects")]
    [AllowNull]
    public PSObject[]? InputObject { get; set; }

    /// <summary>Separate diagram node records: unique NodeId, optional Name and Shape (Auto, Process, Decision or Terminal).</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Diagram")]
    [AllowEmptyCollection]
    public PSObject[] Nodes { get; set; } = [];

    /// <summary>Separate diagram edge records: SourceId, TargetId, optional Label and Boolean Directed (default true).</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Diagram")]
    [AllowEmptyCollection]
    public PSObject[] Edges { get; set; } = [];

    /// <summary>Initial X property name or calculated-property hashtable containing Name/Label and
    /// Expression. Omit to use input order. Can be changed inside the viewer.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    public object? XProperty { get; set; }

    /// <summary>Initial Y property names and/or calculated-property hashtables. Alias Property. Omit to
    /// choose a numeric default; the Y menu can add or remove properties and create calculated fields
    /// without rerunning the pipeline.</summary>
    [Parameter]
    [Alias("Property")]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    public object[]? YProperty { get; set; }

    /// <summary>Initial graph type: Line, Area, Bars, Step, Scatter, Stem, Histogram, BoxPlot,
    /// HorizontalBars, StackedBars, Heatmap, ErrorBars, ColumnLine, Pie, Gauge, RadialBars, Radar,
    /// Waterfall, Marimekko, Kagi, Flamegraph, Gantt, FlowChart, NetworkDiagram, StackedArea or PercentStackedArea. Multiple Y properties produce
    /// multiple lines, overlaid areas or grouped bars. ColumnLine uses columns for the first Y property
    /// and lines for the remaining properties on a shared Y axis. Pie/Gauge/RadialBars use one Y property
    /// and input rows as slices/lanes; Radar uses rows as category spokes and Y properties as compared series.
    /// Waterfall uses signed row increments from zero; Heatmap uses Y properties as matrix rows.
    /// Stacked areas require aligned, strictly increasing X coordinates and nonnegative components;
    /// missing components leave whole-stack gaps. PercentStackedArea has a fixed 0-100 percent Y range.</summary>
    [Parameter]
    [Alias("GraphKind")]
    public GraphKind GraphType { get; set; } = GraphKind.Line;

    /// <summary>One Auto, Column or Line role per selected Y property, in YProperty order.
    /// Used by ColumnLine; roles remain editable in Graph / Series styles and saved configuration.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public ChartSeriesRole[]? SeriesRole { get; set; }

    /// <summary>Display a wrapping, clickable series legend above the plot. Use -ShowLegend:$false
    /// to hide a legend restored from configuration.</summary>
    [Parameter]
    public SwitchParameter ShowLegend { get; set; }

    /// <summary>Lower shared value bound; cumulative endpoints for Waterfall, color scale for Heatmap. Gauge and RadialBars default to 0. Pie does not accept bounds.</summary>
    [Parameter]
    public double? Minimum { get; set; }

    /// <summary>Upper shared value bound; cumulative endpoints for Waterfall, color scale for Heatmap. Gauge and RadialBars default to 100.</summary>
    [Parameter]
    public double? Maximum { get; set; }

    /// <summary>Positive fixed price-unit reversal for Kagi; default 1. Prices below this move from the current extreme do not reverse the path.</summary>
    [Parameter]
    public double ReversalAmount { get; set; } = 1;

    /// <summary>Flamegraph unique frame-ID property; default FrameId.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string FrameIdProperty { get; set; } = "FrameId";

    /// <summary>Flamegraph parent-ID property; null/empty values denote roots. Default ParentId.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string ParentIdProperty { get; set; } = "ParentId";

    /// <summary>Optional Flamegraph display-name property; omitted uses each frame ID. Equal named sibling prefixes aggregate.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? FrameNameProperty { get; set; }

    /// <summary>Draw flamegraph roots at the top instead of the bottom.</summary>
    [Parameter]
    public SwitchParameter FlameReverse { get; set; }

    /// <summary>Terminal rows per Flamegraph depth, from 1 through 8. Default 2; use 1 for compact deep profiles.</summary>
    [Parameter]
    [ValidateRange(1, 8)]
    public int FlameFrameRows { get; set; } = 2;

    /// <summary>Gantt absolute start-instant property; default Start. Accepts DateTimeOffset, UTC DateTime or ISO 8601 text with explicit timezone.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string StartProperty { get; set; } = "Start";

    /// <summary>Gantt absolute end-instant property; default End. End must not precede Start; equality denotes a milestone.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string EndProperty { get; set; } = "End";

    /// <summary>Gantt printable task-label property; default Task. Input rows retain separate lanes in input order.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string TaskNameProperty { get; set; } = "Task";

    /// <summary>Optional Gantt completion-fraction property, from 0 through 1. Null means unknown progress.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProgressProperty { get; set; }

    /// <summary>Title displayed above the chart.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Title { get; set; } = "Chart";

    /// <summary>Maximum retained object count, between 1 and 1000000. Excess input raises an error before
    /// the viewer opens; records are not silently dropped. Refresh collection stops at the first excess
    /// object and reports overflow too.</summary>
    [Parameter]
    [ValidateRange(1, 1000000)]
    public int MaximumCount { get; set; } = 100000;

    /// <summary>Return every original object in the current filtered view and selected range when Q or F10
    /// is pressed. Output follows view order, including Top/Bottom ranking. Escape or Ctrl+C cancels
    /// without output. The default is display-only. Calculated chart fields are not added to the returned
    /// objects. Output contains the same original object instances, never chart points, group summaries or
    /// reconstructed objects. Chart operations do not add, remove or overwrite input properties, even when
    /// a calculated field uses an existing property name.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Property name, scriptblock using $_, or calculated-property hashtable used to group
    /// filtered rows. GroupBy alone counts members. Scriptblock results are cached for the current input
    /// snapshot; the original objects are preserved. Example: Get-Process | Out-Chart -GroupBy {
    /// $_.ProcessName -replace &apos;\d+$&apos; }</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    public object? GroupBy { get; set; }

    /// <summary>None, Count, Sum, Average, Minimum or Maximum. Row filters run before grouping; missing
    /// numeric values are ignored except by Count.</summary>
    [Parameter]
    public ChartAggregate Aggregate { get; set; }

    /// <summary>Property name or calculated property containing absolute lower error bounds.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    public object? LowerProperty { get; set; }

    /// <summary>Property name or calculated property containing absolute upper error bounds.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    public object? UpperProperty { get; set; }

    /// <summary>Script returning replacement whole objects. Runs on the UI thread at RefreshInterval; a
    /// failure pauses refresh and preserves the previous snapshot.</summary>
    [Parameter]
    [ValidateNotNull]
    public ScriptBlock? Refresh { get; set; }

    /// <summary>Milliseconds between refreshes, from 100 to 60000.</summary>
    [Parameter]
    [ValidateRange(100, 60000)]
    public int RefreshInterval { get; set; } = 1000;

    /// <summary>Path to configuration JSON exported from the viewer. Restores fields, calculations, filters
    /// and presentation; explicit XProperty, YProperty and GraphType override saved choices.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Configuration { get; set; }

    private PSObject[] ReadRefresh() {
        PipelineStopToken.ThrowIfCancellationRequested();
        // Invoke the supplied block itself so closures remain intact. Stop the
        // producer after one excess non-null object rather than collecting all output.
        var reader = _boundedRefresh ??= ScriptBlock.Create("""
            param([scriptblock]$FullscreenRefreshSource, [int]$FullscreenRefreshLimit)
            & $FullscreenRefreshSource | Microsoft.PowerShell.Core\Where-Object { $null -ne $_ } |
                Microsoft.PowerShell.Utility\Select-Object -First $FullscreenRefreshLimit
            """);
        var objects = reader.Invoke(Refresh!, MaximumCount + 1).ToArray();
        if (objects.Length > MaximumCount) {
            throw new InvalidOperationException("Refresh input exceeds MaximumCount.");
        }

        return objects;
    }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        foreach (var item in InputObject ?? []) {
            if (item is null) {
                continue;
            }

            if (_objects.Count >= MaximumCount) {
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException("Input exceeds MaximumCount; limit the upstream pipeline or increase MaximumCount."),
                    "FullscreenChartLimit",
                    ErrorCategory.LimitsExceeded,
                    MaximumCount
                ));
            }

            _objects.Add(item);
        }
    }

    /// <inheritdoc/>
    protected override void EndProcessing() {
        try {
            if (ParameterSetName == "Diagram") {
                RunDiagram();
                return;
            }
            if (ChartTopology.Supports(GraphType)) {
                throw new ArgumentException("FlowChart and NetworkDiagram require separate -Nodes and -Edges collections, not pipeline InputObject.");
            }
            if (_objects.Count == 0 && Refresh is not null) {
                _objects.AddRange(ReadRefresh());
            }

            if ((_objects.Count == 0 && Refresh is null) || PipelineStopToken.IsCancellationRequested) {
                return;
            }

            using var app = new ObjectChartApp(_objects, XProperty, YProperty, GraphType, Title,
                new(FrameIdProperty, ParentIdProperty, FrameNameProperty), PipelineStopToken,
                new(StartProperty, EndProperty, TaskNameProperty, ProgressProperty), Configuration is null);
            app.Surface.PassThru = PassThru.IsPresent;
            app.WorkingDirectory = SessionState.Path.CurrentFileSystemLocation.ProviderPath;
            if (Configuration is not null) {
                string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Configuration, out var provider, out _);
                if (!provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) {
                    throw new ArgumentException("Configuration requires a filesystem path.");
                }

                app.ImportConfiguration(File.ReadAllText(path));
                if (MyInvocation.BoundParameters.ContainsKey(nameof(StartProperty))
                    || MyInvocation.BoundParameters.ContainsKey(nameof(EndProperty))
                    || MyInvocation.BoundParameters.ContainsKey(nameof(TaskNameProperty))
                    || MyInvocation.BoundParameters.ContainsKey(nameof(ProgressProperty))) {
                    var fields = app.TaskProperties;
                    app.SetTaskFields(new(
                        MyInvocation.BoundParameters.ContainsKey(nameof(StartProperty)) ? StartProperty : fields.Start,
                        MyInvocation.BoundParameters.ContainsKey(nameof(EndProperty)) ? EndProperty : fields.End,
                        MyInvocation.BoundParameters.ContainsKey(nameof(TaskNameProperty)) ? TaskNameProperty : fields.Name,
                        MyInvocation.BoundParameters.ContainsKey(nameof(ProgressProperty)) ? ProgressProperty : fields.Progress
                    ));
                }
                if (MyInvocation.BoundParameters.ContainsKey(nameof(XProperty))
                    || MyInvocation.BoundParameters.ContainsKey(nameof(YProperty))
                    || MyInvocation.BoundParameters.ContainsKey(nameof(GraphType))) {
                    string? x = MyInvocation.BoundParameters.ContainsKey(nameof(XProperty)) ? app.Data.BindProperties([XProperty!], PipelineStopToken)[0] : app.XProperty;
                    string[] y = MyInvocation.BoundParameters.ContainsKey(nameof(YProperty)) ? app.Data.BindProperties(YProperty!, PipelineStopToken) : app.YProperties;
                    app.Configure(
                        x,
                        y,
                        MyInvocation.BoundParameters.ContainsKey(nameof(GraphType)) ? GraphType : app.Surface.GraphType
                    );
                }
            }

            if (GroupBy is not null || MyInvocation.BoundParameters.ContainsKey(nameof(Aggregate))) {
                string? group = GroupBy is null ? app.GroupProperty : app.Data.BindGroupBy(GroupBy, PipelineStopToken);
                app.SetAggregation(
                    group,
                    GroupBy is not null && Aggregate == ChartAggregate.None ? ChartAggregate.Count : Aggregate
                );
            }


            if (LowerProperty is not null || UpperProperty is not null) {
                app.SetErrorBounds(LowerProperty, UpperProperty);
            }

            if (SeriesRole is not null) {
                app.SetSeriesRoles(SeriesRole);
            }

            if (MyInvocation.BoundParameters.ContainsKey(nameof(ShowLegend))) {
                app.Surface.Plot.ShowLegend = ShowLegend.IsPresent;
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(Minimum))) {
                app.Surface.Plot.Minimum = Minimum;
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(Maximum))) {
                app.Surface.Plot.Maximum = Maximum;
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(ReversalAmount))) {
                app.Surface.Plot.ReversalAmount = ReversalAmount;
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(FlameReverse))) {
                app.Surface.Plot.FlameReverse = FlameReverse.IsPresent;
            }
            if (MyInvocation.BoundParameters.ContainsKey(nameof(FlameFrameRows))) {
                app.Surface.Plot.FlameFrameRows = FlameFrameRows;
            }
            if (ChartRadial.IsRadial(app.Surface.GraphType) || ChartGrid.Supports(app.Surface.GraphType)
                || ChartMarket.Supports(app.Surface.GraphType) || ChartStackedArea.Supports(app.Surface.GraphType)
                || app.Surface.GraphType is GraphKind.Flamegraph or GraphKind.Gantt || MyInvocation.BoundParameters.ContainsKey(nameof(ReversalAmount))) {
                _ = app.Surface.Plot.RenderRange(app.Surface.Plot.GetRenderData(100));
            }

            if (Refresh is not null) {
                app.RefreshSource = ReadRefresh;
                app.RefreshInterval = RefreshInterval;
            }

            FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent, ChartRenderer);
            if (PassThru.IsPresent) {
                foreach (var item in app.Chosen) {
                    WriteObject(item, false);
                }
            }

        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (OperationCanceledException) when (PipelineStopToken.IsCancellationRequested) {
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenChartFailed", ErrorCategory.InvalidOperation, null));
        }
        finally {
            _objects.Clear();
        }
    }

    private void RunDiagram() {
        GraphKind kind = MyInvocation.BoundParameters.ContainsKey(nameof(GraphType)) ? GraphType : GraphKind.FlowChart;
        if (!ChartTopology.Supports(kind)) {
            throw new ArgumentException("-Nodes/-Edges require GraphType FlowChart or NetworkDiagram (default FlowChart).");
        }
        foreach (string parameter in new[] { nameof(XProperty), nameof(YProperty), nameof(GroupBy), nameof(Aggregate),
            nameof(LowerProperty), nameof(UpperProperty), nameof(Refresh), nameof(RefreshInterval), nameof(SeriesRole),
            nameof(StartProperty), nameof(EndProperty), nameof(TaskNameProperty), nameof(ProgressProperty),
            nameof(FrameIdProperty), nameof(ParentIdProperty), nameof(FrameNameProperty), nameof(FlameReverse), nameof(FlameFrameRows), nameof(ReversalAmount) }) {
            if (MyInvocation.BoundParameters.ContainsKey(parameter)) {
                throw new ArgumentException($"Diagram input does not support -{parameter}; it uses separate node and edge records.");
            }
        }
        ChartTopology.ValidateOptions(Minimum, Maximum, null, null, ChartScale.Linear, ChartScale.Linear, 0, 0, null);
        if ((long)Nodes.Length + Edges.Length > MaximumCount) {
            throw new ArgumentException("Diagram node and edge records exceed MaximumCount.");
        }
        var topology = ChartTopology.FromObjects(Nodes, Edges);
        _ = topology.Layout(kind);
        using var app = new ObjectChartApp([.. Nodes, .. Edges], null, null, kind, Title, new(), PipelineStopToken,
            initialize: Configuration is null, diagram: topology);
        app.Surface.PassThru = PassThru.IsPresent;
        app.WorkingDirectory = SessionState.Path.CurrentFileSystemLocation.ProviderPath;
        if (Configuration is not null) {
            string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Configuration, out var provider, out _);
            if (!provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Configuration requires a filesystem path.");
            }
            app.ImportConfiguration(File.ReadAllText(path));
            if (MyInvocation.BoundParameters.ContainsKey(nameof(GraphType))) {
                app.Configure(null, [], kind);
            }
        }
        if (MyInvocation.BoundParameters.ContainsKey(nameof(ShowLegend))) {
            app.Surface.Plot.ShowLegend = ShowLegend.IsPresent;
        }
        _ = app.Surface.Plot.GetRenderData(100);
        FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent, ChartRenderer);
        if (PassThru.IsPresent) {
            foreach (var item in app.Chosen) {
                WriteObject(item, false);
            }
        }
    }
}
