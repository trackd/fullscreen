using System.Management.Automation;
using fullscreen.Charts;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Charts pipeline objects interactively and lets you change fields and graph types.</summary>
[Cmdlet(VerbsData.Out, "Chart")]
[OutputType(typeof(PSObject))]
public sealed class OutChartCommand : FullscreenCommand {
    private readonly List<PSObject> _objects = [];
    private ScriptBlock? _boundedRefresh;
    /// <summary>Select Auto, Sixel, Native or Regis. DRCS chart rendering is disabled. Regis is an
    /// experimental, explicitly selected Contour vector backend; Area, Histogram and BoxPlot use cell
    /// fallback. Auto continues to select Sixel with cell fallback.</summary>
    [Parameter]
    public ChartRenderer ChartRenderer { get; set; }

    /// <summary>Complete objects to chart. Accepts pipeline input or an array.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [AllowNull]
    public PSObject[]? InputObject { get; set; }

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
    /// HorizontalBars, StackedBars, Heatmap or ErrorBars.</summary>
    [Parameter]
    [Alias("GraphKind")]
    [ValidateSet("Line", "Area", "Bars", "Step", "Scatter", "Stem", "Histogram", "BoxPlot", "HorizontalBars", "StackedBars", "Heatmap", "ErrorBars")]
    public GraphKind GraphType { get; set; } = GraphKind.Line;

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
    [ValidateSet("None", "Count", "Sum", "Average", "Minimum", "Maximum")]
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
            if (_objects.Count == 0 && Refresh is not null) {
                _objects.AddRange(ReadRefresh());
            }

            if ((_objects.Count == 0 && Refresh is null) || PipelineStopToken.IsCancellationRequested) {
                return;
            }

            using var app = new ObjectChartApp(_objects, XProperty, YProperty, GraphType, Title, PipelineStopToken);
            app.Surface.PassThru = PassThru.IsPresent;
            app.WorkingDirectory = SessionState.Path.CurrentFileSystemLocation.ProviderPath;
            if (Configuration is not null) {
                string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Configuration, out var provider, out _);
                if (!provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) {
                    throw new ArgumentException("Configuration requires a filesystem path.");
                }

                app.ImportConfiguration(File.ReadAllText(path));
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
}
