---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-31-2026
PlatyPS schema version: 2024-05-01
title: Out-Chart
---

# Out-Chart

## SYNOPSIS

Charts pipeline objects interactively and lets you change fields and graph types.

## SYNTAX

### Objects (Default)

```
Out-Chart [[-InputObject] <psobject[]>] [-XProperty <Object>] [-YProperty <Object[]>]
 [-GraphType <GraphKind>] [-SeriesRole <ChartSeriesRole[]>] [-ShowLegend] [-ShowTooltips] [-EnableZoom] [-Minimum <double>] [-Maximum <double>] [-ReversalAmount <double>] [-ChartRenderer <ChartRenderer>] [-Title <string>] [-MaximumCount <int>] [-PassThru] [-Theme <Object>]
 [-FrameIdProperty <string>] [-ParentIdProperty <string>] [-FrameNameProperty <string>] [-FlameReverse] [-FlameFrameRows <int>]
 [-StartProperty <string>] [-EndProperty <string>] [-TaskNameProperty <string>] [-ProgressProperty <string>]
 [-GroupBy <Object>] [-Aggregate <ChartAggregate>] [-LowerProperty <Object>] [-UpperProperty <Object>]
 [-Refresh <scriptblock>] [-RefreshInterval <int>] [-Configuration <string>] [-Minimal] [<CommonParameters>]
```

### Diagram

```
Out-Chart -Nodes <psobject[]> -Edges <psobject[]> [-GraphType <GraphKind>]
 [-ChartRenderer <ChartRenderer>] [-Title <string>] [-ShowLegend] [-ShowTooltips] [-EnableZoom] [-PassThru]
 [-Configuration <string>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## DESCRIPTION

Explore pipeline objects or numeric values in an interactive chart. Choose
fields, graph type, filters and presentation settings from the menus. Charts
retain their original records for inspection. Graphics rendering falls back to
terminal cells when unavailable.

Hover for values, click to select, and press Enter or double-click to inspect.
T opens the data table. Wheel zoom and keyboard zoom/pan are disabled by default;
use `-EnableZoom` or enable zoom in the axes/view editor to opt in. Where supported,
Shift-drag selects a range. Space pauses refresh; the graph menu provides
axis, series, export and configuration options.

Numeric charts support multiple series, distributions and specialized layouts.
Flamegraphs use frame/parent records with exclusive self weights; Gantt charts
use task start/end instants. FlowChart and NetworkDiagram use separate node and
edge collections. See GraphType, the associated input parameters and examples
for each format.

With PassThru, Q or F10 accepts the current filtered objects; Escape cancels.
Otherwise the viewer produces no pipeline output.

## EXAMPLES

### Example 1: Choose properties interactively

```powershell
Get-Process | Out-Chart
```

Open the complete process objects and use X, Y and G to choose fields and style.

### Example 2: Compare two properties with labels

```powershell
Get-Process | Out-Chart -XProperty ProcessName -YProperty CPU, WorkingSet64 -GraphType Bars
```

Compare properties on a shared scale. Choose properties with comparable units
for a useful comparison, or toggle individual properties in the Y menu.

### Example 3: Plot scalar numbers

```powershell
1, 4, -2, 7, 3 | Out-Chart -GraphType Line -Title 'Samples'
```

Numbers use their input order as X. You can change the graph type in the viewer.

### Example 4: Return an interactively chosen subset

```powershell
$subset = Get-Process | Out-Chart -YProperty WorkingSet64 -PassThru
$subset | Select-Object ProcessName, CPU, WorkingSet64
```

Choose Top 30 (or another filter) inside the viewer, then press Q or F10 to
return those original Process objects. Escape cancels without returning rows.

### Example 5: Refresh a live snapshot

```powershell
Out-Chart -Refresh {
    Get-Process
} -RefreshInterval 2000 -XProperty ProcessName -YProperty CPU
```

Refresh runs on the UI thread; keep the script quick. Space pauses/resumes.
Opening the data table freezes refresh until it closes. Applying a table subset
or selecting a range pauses refresh for stable inspection.

## PARAMETERS

### -Nodes

Separate diagram node records with unique NodeId, optional Name and Shape
(Auto, Process, Decision or Terminal). Name defaults to NodeId. Used together
with Edges; GraphType defaults to FlowChart in this parameter set. Accepts a
`Nodes` property from pipeline records; collections from successive records are
combined, while an explicitly supplied collection is included only once.
Supply `-Edges` explicitly when piping `Nodes` properties to select the Diagram
parameter set rather than the default whole-object pipeline binding.
Alias `Vertices` also works for named arguments and property-name pipeline binding.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: None
SupportsWildcards: false
Aliases:
- Vertices
ParameterSets:
- Name: Diagram
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
```

### -Edges

Separate diagram edge records with SourceId, TargetId, optional Label and Boolean
Directed (default true). References must identify existing Nodes. Empty edges
are valid for isolated nodes. Accepts an `Edges` property from pipeline records;
collections from successive records are combined.
Supply `-Nodes` explicitly when piping `Edges` properties to select the Diagram
parameter set.
Aliases `Links` and `Connections` also work for named arguments and property-name
pipeline binding.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: None
SupportsWildcards: false
Aliases:
- Links
- Connections
ParameterSets:
- Name: Diagram
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
```

### -InputObject

Complete objects to chart. Accepts pipeline input or an array, with property-name
binding enabled for `InputObject`. PowerShell's normal binding precedence applies:
direct whole-object pipeline binding takes precedence over property-name binding.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Objects
  Position: 0
  IsRequired: false
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
```

### -XProperty

Initial X property name or calculated-property hashtable containing Name/Label
and Expression. Omit to use input order. Can be changed inside the viewer.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -YProperty

Initial Y property names and/or calculated-property hashtables. Alias Property.
Omit to choose a numeric default; the Y menu can add or remove properties and
create calculated fields without rerunning the pipeline.

```yaml
Type: System.Object[]
DefaultValue: None
SupportsWildcards: false
Aliases: [Property]
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -GraphType

Initial graph type: Line, Area, Bars, Step, Scatter, Stem, Histogram, BoxPlot,
HorizontalBars, StackedBars, Heatmap, ErrorBars, ColumnLine, Pie, Gauge,
RadialBars, Radar, Waterfall, Marimekko, Kagi, Flamegraph, Gantt, FlowChart,
NetworkDiagram, StackedArea or PercentStackedArea.
Multiple Y properties produce multiple lines, overlaid areas or grouped bars.
ColumnLine defaults to first-series columns and remaining-series lines on shared axes.
Pie, Gauge and RadialBars require one Y property and use rows as slices/lanes.
Radar compares Y-property series across ordered row spokes. Radial kinds use
linear numeric value scales; Cartesian thresholds/annotations are not supported.
Pie rejects negative values and does not accept Minimum/Maximum. Gauge and
RadialBars reject negatives and use nonnegative ordered bounds, defaulting to
0-100. Above/below-range values are marked rather than silently clamped.
Waterfall uses one Y series of signed increments from zero. Heatmap uses
Y series as matrix rows and input categories as columns with a shared color scale.
Marimekko stacks nonnegative composition to 100 percent with category-total
widths. Kagi uses one positive price series and a fixed ReversalAmount.
Flamegraph uses FrameId/ParentId records and exclusive self weights, default Weight.
Gantt uses start/end task records. Relationship diagrams use separate Nodes/Edges.
StackedArea adds aligned nonnegative components; PercentStackedArea uses the same
data with a fixed 0-100 percent Y range. Missing components gap the entire stack.

```yaml
Type: fullscreen.Charts.GraphKind
DefaultValue: Line
SupportsWildcards: false
Aliases: [GraphKind]
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -SeriesRole

One Auto, Column or Line role per selected Y property, in YProperty order.
Used by ColumnLine; roles remain editable in Graph / Series styles and saved
configuration. Omit for automatic roles, or specify Auto to restore a default.

```yaml
Type: fullscreen.Charts.ChartSeriesRole[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ShowLegend

Display the wrapping, clickable legend above the plot. Single-series Bars and
HorizontalBars use a different theme color per category, with matching category
legend entries that select the original row. Grouped and stacked bars retain
per-series colors and visibility toggles. An explicit series color keeps
single-series bars uniform.
The object viewer shows it by default; use `-ShowLegend:$false` to hide it,
including a legend restored from configuration.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ShowTooltips

Show point details after a 200 ms hover. Enabled by default; use
`-ShowTooltips:$false` to disable, including tooltips restored from configuration.
Click selection and Enter inspection remain available.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -EnableZoom

Opt in to mouse-wheel zoom, +/- zoom, and bracket-key panning on supported numeric
charts. Disabled by default so embedded charts leave wheel input to their parent
scroll viewer. Selection, hover, and Shift-drag range selection remain available.
Use `-EnableZoom:$false` to disable zoom restored from a configuration file.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Minimum

Lower shared numeric value bound. Gauge and RadialBars default to 0; Radar uses
automatic bounds that can include signed values. Pie proportions use their
positive total and do not accept Minimum/Maximum. Waterfall bounds apply to
cumulative endpoints; Heatmap bounds apply to its shared color scale.

```yaml
Type: System.Nullable[System.Double]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Maximum

Upper shared numeric value bound. Gauge and RadialBars default to 100 and
visibly mark overflow; Radar uses automatic bounds. Must be greater than
Minimum for gauges/radial bars. Pie does not accept Minimum/Maximum.
Waterfall bounds apply to cumulative endpoints; Heatmap bounds apply to colors.

```yaml
Type: System.Nullable[System.Double]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ReversalAmount

Positive, finite fixed reversal amount for Kagi in the same units as closing
prices; default 1. Retracements smaller than this amount do not reverse the
current leg. Percentage and ATR modes are not supported. Ignored by other kinds.

```yaml
Type: System.Double
DefaultValue: 1
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -FrameIdProperty

Flamegraph unique frame-ID field; default FrameId. IDs are converted to invariant
strings and compared ordinally. Cycles, duplicates and missing parents are rejected.

```yaml
Type: System.String
DefaultValue: FrameId
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ParentIdProperty

Flamegraph parent-ID field; default ParentId. Null/empty denotes a root;
other values must reference an existing frame ID.

```yaml
Type: System.String
DefaultValue: ParentId
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -FrameNameProperty

Optional Flamegraph display-name field. Omit to display IDs. Equal sibling names
and their matching descendant prefixes aggregate while retaining original records.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -FlameReverse

Start Flamegraph roots at the top rather than the bottom. The R key changes
direction in the viewer; direction and subtree focus are saved in configuration.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Title

Title displayed above the chart.

```yaml
Type: System.String
DefaultValue: Chart
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -MaximumCount

Maximum retained object count, between 1 and 1000000. Excess input raises an error
before the viewer opens; records are not silently dropped.
Refresh collection stops at the first excess object and reports overflow too.

```yaml
Type: System.Int32
DefaultValue: 100000
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -PassThru

Return every original object in the current filtered view and selected range when Q or F10 is
pressed. Output follows view order, including Top/Bottom ranking. Escape or
Ctrl+C cancels without output. The default is display-only. Calculated chart
fields are not added to the returned objects. Output contains the same original
object instances, never chart points, group summaries or reconstructed objects.
Chart operations do not add, remove or overwrite input properties, even when a
calculated field uses an existing property name.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -FlameFrameRows

Terminal rows per Flamegraph depth, from 1 through 8. Default 2. Use 1 for compact
deep profiles or a larger value for easier reading. Height changes never alter
inclusive frame widths; clipped levels can be reached by subtree focus.

```yaml
Type: System.Int32
DefaultValue: 2
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -StartProperty

Gantt absolute start-instant property; default Start. Accepts DateTimeOffset,
UTC DateTime or ISO 8601 text with Z/explicit offset, not numeric durations.

```yaml
Type: System.String
DefaultValue: Start
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -EndProperty

Gantt absolute end-instant property; default End. End must not precede Start;
equality denotes a zero-duration milestone.

```yaml
Type: System.String
DefaultValue: End
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -TaskNameProperty

Gantt printable task-label property; default Task. Input rows retain separate
lanes in input order.

```yaml
Type: System.String
DefaultValue: Task
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ProgressProperty

Optional Gantt completion-fraction property, from 0 through 1. Null means
unknown progress; zero means known no progress.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -GroupBy

Property name, scriptblock using $_, or calculated-property hashtable used to group
filtered rows. GroupBy alone counts members. Scriptblock results are cached for
the current input snapshot; the original objects are preserved.

Example: `Get-Process | Out-Chart -GroupBy { $_.ProcessName -replace '\d+$' }`

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Aggregate

None, Count, Sum, Average, Minimum or Maximum. Row filters run before grouping; missing numeric values are ignored except by Count.

```yaml
Type: fullscreen.Charts.ChartAggregate
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -LowerProperty

Property name or calculated property containing absolute lower error bounds.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -UpperProperty

Property name or calculated property containing absolute upper error bounds.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Refresh

Script returning replacement whole objects. Runs on the UI thread at RefreshInterval; a failure pauses refresh and preserves the previous snapshot.

```yaml
Type: System.Management.Automation.ScriptBlock
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -RefreshInterval

Milliseconds between refreshes, from 100 to 60000.

```yaml
Type: System.Int32
DefaultValue: 1000
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Configuration

Path to configuration JSON exported from the viewer. Restores fields, calculations, filters and presentation; explicit XProperty, YProperty and GraphType override saved choices.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Theme

Built-in theme name or custom palette, as supported by the other fullscreen apps.
Use `(Get-FullscreenTheme Monokai).Colors` to inspect or copy all editable colors.
Partial hashtables inherit unspecified colors from Base (Default when omitted):

`-Theme @{ Base = 'Monokai'; ChartBlue = '#58d5e6'; ChartGrid = '#555555' }`

Eight series colors repeat in order: ChartBlue, ChartOrange, ChartPurple, ChartGreen, ChartCyan, ChartRed, ChartYellow, ChartPink.
Explicit series colors take precedence. Success, Warning, and Error remain status colors, not automatic series colors.
ChartGrid, ChartAxis, and ChartLabel control lines, axes, and labels.
Heatmap shades interpolate between ChartSequentialLow and ChartSequentialHigh.
Gauge colors interpolate through ChartPositive, ChartNeutral, and ChartNegative as load increases.
The gauge uses 16 precomputed color steps along the arc; RadialBars retains one solid series color per ring.
All color overrides accept #RGB or #RRGGBB hex strings; setting one does not change the other roles.
Syntax colors for the embedded object inspector are also editable; see Get-FullscreenTheme for the complete role list.

```yaml
Type: System.Object
DefaultValue: Default
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Minimal

Hide the bottom keyboard hint row. Field and graph selection remain available.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ChartRenderer

Select Auto, Sixel, Native or Regis.
Regis is an experimental,
explicitly selected Contour vector backend; Area, Histogram, BoxPlot and ColumnLine use cell
fallback. Auto continues to select Sixel with cell fallback.

```yaml
Type: fullscreen.Charts.ChartRenderer
DefaultValue: Auto
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
```

### CommonParameters

This cmdlet supports the common parameters. For more information, see
about_CommonParameters.

## INPUTS

### System.Management.Automation.PSObject

Complete pipeline objects or numeric samples.

## OUTPUTS

### System.Management.Automation.PSObject

With PassThru, accepted original objects in filtered view order. Otherwise no output.

## NOTES

The viewer uses the common fullscreen host and requires PowerShell 7.6 or later.

## RELATED LINKS

[Show-ObjectTable](Show-ObjectTable.md)

- [Chart cookbook](https://github.com/trackd/fullscreen/blob/main/docs/guides/charts.md)
