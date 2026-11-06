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
 [-GraphType <GraphKind>] [-SeriesRole <ChartSeriesRole[]>] [-ShowLegend] [-ShowTooltips] [-Minimum <double>] [-Maximum <double>] [-ReversalAmount <double>] [-ChartRenderer <ChartRenderer>] [-Title <string>] [-MaximumCount <int>] [-PassThru] [-Theme <Object>]
 [-FrameIdProperty <string>] [-ParentIdProperty <string>] [-FrameNameProperty <string>] [-FlameReverse] [-FlameFrameRows <int>]
 [-StartProperty <string>] [-EndProperty <string>] [-TaskNameProperty <string>] [-ProgressProperty <string>]
 [-GroupBy <Object>] [-Aggregate <ChartAggregate>] [-LowerProperty <Object>] [-UpperProperty <Object>]
 [-Refresh <scriptblock>] [-RefreshInterval <int>] [-Configuration <string>] [-Minimal] [<CommonParameters>]
```

### Diagram

```
Out-Chart -Nodes <psobject[]> -Edges <psobject[]> [-GraphType <GraphKind>]
 [-ChartRenderer <ChartRenderer>] [-Title <string>] [-ShowLegend] [-ShowTooltips] [-PassThru]
 [-Configuration <string>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## DESCRIPTION

Opens an interactive chart of the original pipeline objects. Choose X/Y fields,
graph type and Top/Bottom/First/Last filters from the menu. Both axis parameters
accept property names or calculated properties such as
@{ Name = 'MB'; Expression = { $_.WorkingSet64 / 1MB } }; Custom… opens the same
editor in the UI. Sixel is automatic, with terminal-cell fallback.

Hover over a point for 200 ms to see its series, value and label in a native
tooltip. Distribution charts show bin counts or box statistics; task, flame and
relationship charts show their contextual details. Click selects a point;
Enter or double-click inspects the original record.
Use `-ShowTooltips:$false` or clear Show tooltips in Graph / Axes and view to
disable hover without affecting selection or inspection. This option is saved
with chart configuration.

`FlowChart` and `NetworkDiagram` instead take **separate** `-Nodes` and `-Edges`
collections. Nodes have unique ordinal NodeId, optional Name (defaults to NodeId)
and Shape (Auto, Process, Decision or Terminal). Edges reference SourceId and
TargetId and optionally contain Label and Boolean Directed (default true).
FlowChart requires directed edges and uses top-to-bottom ranked layout, with
cyclic groups in the same rank and return/long connections routed outside node
columns. NetworkDiagram uses a deterministic bounded spring layout and allows
mixed directed/undirected edges. Neither uses profiling parent relationships,
numeric weights, timeline axes or row aggregation.

Cycles, isolated nodes, self-loops and parallel edges retain their identities.
Click painted nodes, connectors or connection labels; Enter inspects the original
record. Left/Right/Home/End navigate nodes followed by edges. Empty space does
not select nearest data. PassThru returns the original node records followed by
edge records on acceptance. The graph menu switches between compatible diagram
kinds and supports existing export/configuration actions. Settings contain
presentation, not the node/edge data: supply both collections again when loading.
ReGIS uses cell fallback; SVG is static and PNG is plot-only. Diagrams fit the
viewport, with clipped labels in constrained sizes; numeric zoom/pan/overlays,
pipeline InputObject, refresh and row filters are not supported. Maximum size is
256 nodes and 4096 edges; this is not a replacement for Graphviz's advanced
crossing-minimization or interactive diagram editing.

Sixel contains graphics pixels only. Diagram labels remain ordinary terminal
text in reserved cells: the presenter omits those pixels and uses transparent
Sixel background mode (P2=1). This is not protocol-native text or a portable
text-over-image layer. Use `-ChartRenderer Native` if a terminal does not preserve
these cells reliably. SVG can layer real text independently; PNG contains only
the plot geometry.

Multiple Y properties produce multiple lines (`Line`), overlaid areas (`Area`)
or grouped columns (`Bars`). `ColumnLine` combines columns for the first selected
Y property and lines for the remaining properties, all on one shared Y axis.
Use comparable units. `-SeriesRole` or Graph / Series styles overrides individual
roles. The shared legend wraps above the plot and toggles series on click;
`-ShowLegend:$false` hides it. Narrow/short layouts summarize overflow rather
than covering the plot.
Multi-series is selected by the data (`-YProperty Actual, Target, Forecast`),
not a separate MultiLine/MultiArea graph type. Line, Area and Bars already
supported multiple series; this batch adds mixed roles, shared responsive legends
and aligned grouped columns for differing series lengths.

StackedBars, Marimekko and stacked areas use a theme-based categorical color
cycle that separates the first adjacent components instead of pairing primary
cyan with success-green. Subdued palettes can use the error hue before purple
for stronger separation. Legend and band colors share this selection, and
explicit series style colors still take precedence. Hiding a series
does not renumber its original color identity.

`StackedArea` instead adds nonnegative components from zero.
`PercentStackedArea` normalizes visible components at each sample to a fixed
0-100 percent Y axis. Both require equal-length series with identical finite,
strictly increasing X coordinates. Missing components gap the whole stack and
adjacent interpolated intervals; they are never converted to zero. A zero-total
sample is blank in percent mode because no proportions are defined.
Hiding a series recomputes composition for the remaining visible series.
Numeric X zoom/pan clips crossing intervals without discarding their endpoints;
percent mode does not allow Y limits or vertical zoom/pan. Y must be linear and
X cannot be logarithmic. Clicks return the nearest endpoint of a painted interval
with the original source row and raw component value, not an invented interpolated
record; empty space is not selectable. Ordinary `Area` and signed `StackedBars`
retain their existing behavior.

`Waterfall` uses one Y property as ordered signed increments from zero.
Bars float between cumulative endpoints; connectors show the running total.
Missing rows leave a gap without adding zero, and zero remains a flat selectable
bar. No total/subtotal rows are generated. `Heatmap` is the existing matrix mode:
Y properties are matrix rows and input categories are columns. Unequal lengths
leave missing tails. Its five-step shared color scale supports signed values;
Minimum/Maximum set color bounds, with endpoint colors for out-of-range values.
Blank cells are missing, not zero. Numeric category coordinates must be strictly
increasing and aligned between series; labels can repeat when using input order.
Click only painted bars/cells to inspect the original row; empty space does not
select nearest data.

`Marimekko` uses nonnegative Y properties as stacked components. Category widths
encode observed totals; segment heights encode each component's percentage of
its category, from 0 to 100. Missing segments contribute no observed amount;
zero-total categories have no area. Negative values are rejected.
`Kagi` uses one positive closing-price series in strictly increasing time/input
order. `-ReversalAmount` is a fixed amount in price units, default 1; equality
triggers reversal, smaller retracements are omitted. A new event column appears
only on reversal, not every time interval. Thick green yang changes to thin red
yin below the previous waist; yin changes to yang above the previous shoulder.
Missing prices break the path. Click a painted segment for the original price
row that caused it, not an interpolated price. Percentage/ATR reversals and OHLC
high/low processing are not implemented. Both modes use intrinsic horizontal
spacing, disable viewport navigation/coordinate overlays, and fall back to cells
for ReGIS. Marimekko does not accept numeric bounds; Kagi bounds apply to prices.

`Flamegraph` takes frame records, not stack strings or ordinary numeric series.
FrameId is unique; ParentId is null/empty for roots and otherwise references
another row. IDs are compared ordinally after invariant string conversion.
One Y property supplies finite nonnegative **exclusive self weight** (default
Weight); inclusive width is the node's self weight plus all descendants.
Do not supply inclusive profiling weights, which would double-count descendants.
Optional FrameNameProperty supplies function names; otherwise IDs are displayed.
Equal sibling names and their matching descendant prefixes are aggregated.
Click selects the first original record contributing to the frame; details show
self/inclusive weights and the contributing row count. The original records are
retained. Zero-only trees have no painted frames; invalid parents, duplicate IDs,
cycles, missing/negative/nonfinite weights and overflowing totals are rejected.
Input order need not be parent-first. Limits are 1,000,000 rows and 1,024 depths.
Roots appear at the bottom; `-FlameReverse` puts roots at the top.
Each depth occupies `FlameFrameRows` terminal rows (default 2, range 1-8),
converted to the actual cell pixel height in raster output. Graph / Flamegraph
frame rows changes it live; use 1 for compact deep profiles or 3-4 for larger
frames. Shallow profiles leave empty space rather than stretching to fill height.
Clipped deeper levels remain accessible through subtree focus.
Call paths are sorted alphabetically: horizontal position is stack population,
not time. Warm theme colors distinguish frames; colors do not encode duration.
Click zooms into a frame's subtree horizontally and hover shows source details.
F also focuses a selected frame, U moves to its parent, 0 returns to all roots,
and R reverses depth direction. Focus makes deeper clipped levels accessible.
Press / to search function names by case-insensitive substring; matches are
highlighted and the legend reports whole-profile sample percentage, counting
nested matching frames only once. Apply an empty search to clear highlighting.
Blank self-weight space does not select a nearest frame. Numeric axis bounds,
grouping, partial row filters and coordinate overlays are unsupported.
ReGIS falls back to cells. SVG/Sixel/cells include labels when frames are wide
enough; plot-only PNG preserves frame geometry/colors without text labels.
Exclusive self samples precede child paths, matching sorted folded-stack prefix
ordering. Raster/SVG corners round by at most two pixels; narrow tips reflect
their true inclusive weights and discrete pixel resolution, not smoothed widths.

Click a point to select it; double-click or press D/Enter for its properties.
`Gantt` uses separate task rows with absolute Start/End instants and printable
Task labels. Override fields with StartProperty, EndProperty and TaskNameProperty;
do not supply XProperty/YProperty. DateTimeOffset, UTC DateTime and ISO 8601 text
with Z/explicit offset are accepted; timezone-free dates and numeric durations
are rejected. All displayed time is UTC. Input order defines independent lanes,
so overlapping tasks remain distinct. End must not precede Start; equality is a
selectable `*` milestone. Optional ProgressProperty holds a fraction from 0 to 1:
the green portion is completed, null is unknown, and zero is known no progress.
Each task has a label row and an interval row. Up/Down/PageUp/PageDown scroll
lanes, wheel/+/- zoom time, brackets pan, and 0 resets. Click only an interval
to select its original object; empty space and labels do not pick nearest data.
Shift-drag/PassThru includes every task intersecting the selected time interval.
Grouping, dependencies, resource scheduling, Y bounds and coordinate overlays
are not implemented. ReGIS falls back to cells; PNG is plot-only.

T opens the filtered data table. Wheel zooms, brackets pan, Shift-drag selects
a range; Space pauses refresh. Chart settings include axes, series and export.
The bottom footer shows the current GraphType and follows graph-menu changes,
including in Minimal mode.

With -PassThru, Q/F10 returns the current filtered objects in view order;
Escape cancels. Without -PassThru, the viewer produces no pipeline output.

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

### Example 4: Define calculated axes

```powershell
Get-Process | Out-Chart -XProperty @{
    Label = 'Process'
    Expression = {
        $_.ProcessName
    }
} -YProperty @{
    Name = 'Memory MB'
    Expression = {
        $_.WorkingSet64 / 1MB
    }
} -GraphType Bars
```

The viewer retains the complete Process objects. Add or edit calculated fields
inside the X/Y menus, or use Memory MB for Top/Bottom ranking.

### Example 5: Return an interactively chosen subset

```powershell
$subset = Get-Process | Out-Chart -YProperty WorkingSet64 -PassThru
$subset | Select-Object ProcessName, CPU, WorkingSet64
```

Choose Top 30 (or another filter) inside the viewer, then press Q or F10 to
return those original Process objects. Escape cancels without returning rows.

### Example 6: Aggregate filtered objects

```powershell
Get-Process | Out-Chart -GroupBy ProcessName -YProperty WorkingSet64 -Aggregate Sum -GraphType HorizontalBars
```

Grouping keeps the original member objects available for inspection and PassThru.
GroupBy without Aggregate counts members. Row filters run before aggregation.

### Example 7: Refresh a live snapshot

```powershell
Out-Chart -Refresh {
    Get-Process
} -RefreshInterval 2000 -XProperty ProcessName -YProperty CPU
```

Refresh runs on the UI thread; keep the script quick. Space pauses/resumes.
Unchanged, reused PowerShell records containing only scalar note properties
reuse their existing chart data. Replacement objects, calculated expressions,
adapted getters and nested mutable values retain conservative refresh behavior
so inspection sources and externally computed values do not become stale.
Opening the data table freezes refresh until it closes. Applying a table subset
or selecting a range pauses refresh for stable inspection.

### Example 8: Show absolute error bounds

```powershell
$measurements = 1..12 | ForEach-Object {
    $mean = 20 + ($_ % 4) * 5
    [pscustomobject]@{
        Time = $_
        Mean = $mean
        Low = $mean - 3
        High = $mean + 4
    }
}
$measurements | Out-Chart -XProperty Time -YProperty Mean -LowerProperty Low -UpperProperty High -GraphType ErrorBars
```

Low and High are absolute lower/upper values. The Chart menu can export SVG,
plot-only PNG, CSV or configuration JSON. Reopen saved settings with
Out-Chart's Configuration parameter (using the file saved in the viewer).

### Example 9: Compare columns with target and forecast lines

```powershell
$measurements = 1..12 | ForEach-Object {
    [pscustomobject]@{
        Sample = $_
        Actual = 20 + ($_ % 4) * 5
        Target = 30
        Forecast = 22 + ($_ % 3) * 4
    }
}
$measurements | Out-Chart -XProperty Sample -YProperty Actual, Target, Forecast -GraphType ColumnLine -SeriesRole Column, Line, Line -ShowLegend
```

All three measurements share one Y axis. Change roles and colors in Graph /
Series styles; click a legend entry to toggle visibility without losing source
rows. Change GraphType to Line, Area or Bars to compare the same generated
response-time data. Areas overlay rather than stack.

### Example 10: Show proportional pie slices

```powershell
$weights = @(
    [pscustomobject]@{
        Category = 'CPU'
        Weight = 10
    }
    [pscustomobject]@{
        Category = 'Memory'
        Weight = 20
    }
    [pscustomobject]@{
        Category = 'Disk'
        Weight = 30
    }
    [pscustomobject]@{
        Category = 'Network'
        Weight = 40
    }
)
$weights | Out-Chart -XProperty Category -YProperty Weight -GraphType Pie -ShowLegend
```

One selected Y property supplies nonnegative weights; input rows are slices.
Zero/missing weights have no slice; an all-zero total displays an empty state.
Row legend entries show weights and percentages and select the original row.

### Example 11: Compare semicircular load lanes

```powershell
$loads = @(
    [pscustomobject]@{
        Resource = 'CPU'
        Load = 25
    }
    [pscustomobject]@{
        Resource = 'Memory'
        Load = 50
    }
    [pscustomobject]@{
        Resource = 'Disk'
        Load = 75
    }
    [pscustomobject]@{
        Resource = 'Network'
        Load = 100
    }
)
$loads | Out-Chart -XProperty Resource -YProperty Load -GraphType Gauge -Minimum 0 -Maximum 100 -ShowLegend
```

Input rows become concentric semicircular lanes with a green-to-red load
gradient. Change GraphType to RadialBars for full-circle lanes colored by row.
Both modes default to 0-100, reject negatives, and visibly mark values outside
the configured range with an exclamation mark. Missing lanes retain their slot.

### Example 12: Compare resource profiles on radar spokes

```powershell
$profiles = @(
    [pscustomobject]@{
        Resource = 'CPU'
        Actual = 35
        Target = 60
    }
    [pscustomobject]@{
        Resource = 'Memory'
        Actual = 50
        Target = 60
    }
    [pscustomobject]@{
        Resource = 'Disk'
        Actual = 65
        Target = 60
    }
    [pscustomobject]@{
        Resource = 'Network'
        Actual = 80
        Target = 60
    }
)
$profiles | Out-Chart -XProperty Resource -YProperty Actual, Target -GraphType Radar -Minimum 0 -Maximum 100 -ShowLegend
```

At least three ordered input rows form category spokes; Y properties are
compared series using one shared numeric scale. Signed values are supported
with automatic bounds. Missing values break polygon edges rather than becoming
zero. Click a vertex or segment to select its nearest original row, or click
a series legend entry to toggle visibility.

### Example 13: Inspect signed cumulative changes

```powershell
$changes = @(
    [pscustomobject]@{
        Category = 'Sales'
        Change = 20
    }
    [pscustomobject]@{
        Category = 'Costs'
        Change = -5
    }
    [pscustomobject]@{
        Category = 'Flat'
        Change = 0
    }
    [pscustomobject]@{
        Category = 'Missing'
        Change = $null
    }
    [pscustomobject]@{
        Category = 'Refunds'
        Change = -25
    }
    [pscustomobject]@{
        Category = 'Other'
        Change = 15
    }
)
$changes | Out-Chart -XProperty Category -YProperty Change -GraphType Waterfall -ShowLegend
```

Starts at zero and draws each signed increment between its cumulative start
and end. Positive/negative bars use success/error colors. Bounds refer to
cumulative endpoints, not the individual increments.

### Example 14: Inspect a signed matrix with missing cells

```powershell
$matrix = @(
    [pscustomobject]@{
        Category = 'Sales'
        Actual = 20
        Target = -20
    }
    [pscustomobject]@{
        Category = 'Costs'
        Actual = -5
        Target = 0
    }
    [pscustomobject]@{
        Category = 'Flat'
        Actual = 0
        Target = 15
    }
    [pscustomobject]@{
        Category = 'Missing'
        Actual = $null
        Target = 25
    }
    [pscustomobject]@{
        Category = 'Refunds'
        Actual = -25
        Target = $null
    }
    [pscustomobject]@{
        Category = 'Other'
        Actual = 15
        Target = 10
    }
)
$matrix | Out-Chart -XProperty Category -YProperty Actual, Target -GraphType Heatmap -Minimum -25 -Maximum 25 -ShowLegend
```

Y properties are named matrix rows; the six objects are ordered columns.
The shared legend shows the color range and missing-cell convention. Zero is
colored normally, while missing values stay blank. Click a cell to inspect
its original object and selected series.

### Example 15: Compare market size and composition

```powershell
$markets = @(
    [pscustomobject]@{
        Market = 'North'
        Alpha = 30
        Beta = 10
    }
    [pscustomobject]@{
        Market = 'South'
        Alpha = 10
        Beta = 10
    }
    [pscustomobject]@{
        Market = 'West'
        Alpha = 20
        Beta = 40
    }
)
$markets | Out-Chart -XProperty Market -YProperty Alpha, Beta -GraphType Marimekko -ShowLegend
```

Widths encode totals 40, 20 and 60; each column stacks to 100 percent.
Legend entries toggle components; selection retains the original market row.

### Example 16: Inspect fixed-amount price reversals

```powershell
$day = [datetime]::UtcNow.Date
$prices = foreach ($price in 100, 110, 108, 104, 95, 100, 111, 106, 90) {
    [pscustomobject]@{
        Time = $day
        Close = $price
    }
    $day = $day.AddDays(1)
}
$prices | Out-Chart -XProperty Time -YProperty Close -GraphType Kagi -ReversalAmount 5 -ShowLegend
```

The two-unit retracement at 108 is omitted. Five-unit reversals create new
columns; original dates remain available through axis labels and inspection.

### Example 17: Aggregate exclusive profiling samples by frame hierarchy

```powershell
$frames = @(
    [pscustomobject]@{
        FrameId = 'main'
        ParentId = ''
        Name = 'main'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'a'
        ParentId = 'main'
        Name = 'worker'
        Weight = 3
    }
    [pscustomobject]@{
        FrameId = 'b'
        ParentId = 'a'
        Name = 'parse'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'c'
        ParentId = 'main'
        Name = 'worker'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'd'
        ParentId = 'c'
        Name = 'parse'
        Weight = 4
    }
)
$frames | Out-Chart -GraphType Flamegraph -FrameNameProperty Name -ShowLegend
```

Main has 15 inclusive samples, worker has 13 (4 self), and parse has 9.
These three depths occupy three rows, leaving empty height in a large terminal.
Click zooms into a frame; U/0 returns to its parent/all roots and / searches names.
Add FlameReverse to put roots at the top. No setup or external profiler is needed.

### Example 18: Inspect overlapping schedule intervals and a release milestone

```powershell
$tasks = @(
    [pscustomobject]@{
        Task = 'Build'
        Start = [datetimeoffset]'2026-01-12T08:00:00Z'
        End = [datetimeoffset]'2026-01-12T12:00:00Z'
        Progress = 0.75
    }
    [pscustomobject]@{
        Task = 'Test'
        Start = [datetimeoffset]'2026-01-12T10:00:00Z'
        End = [datetimeoffset]'2026-01-12T15:00:00Z'
        Progress = 0.25
    }
    [pscustomobject]@{
        Task = 'Release'
        Start = [datetimeoffset]'2026-01-12T15:00:00Z'
        End = [datetimeoffset]'2026-01-12T15:00:00Z'
        Progress = $null
    }
)
$tasks | Out-Chart -GraphType Gantt -ProgressProperty Progress -ShowLegend -Title 'Schedule (UTC)'
```

Input rows keep separate lanes. Completed fractions color the left portion of
each interval; Release is a zero-duration milestone. Use Up/Down to scroll tasks,
wheel to zoom time, and click an interval then D to inspect the original record.

### Example 20: Draw a flow with separate nodes and connections

```powershell
$nodes = @(
    [pscustomobject]@{
        NodeId = 'check'
        Name = 'Approved?'
        Shape = 'Decision'
    }
    [pscustomobject]@{
        NodeId = 'ship'
        Name = 'Ship order'
        Shape = 'Process'
    }
    [pscustomobject]@{
        NodeId = 'review'
        Name = 'Review'
        Shape = 'Process'
    }
)
$edges = @(
    [pscustomobject]@{
        SourceId = 'check'
        TargetId = 'ship'
        Label = 'Yes'
    }
    [pscustomobject]@{
        SourceId = 'check'
        TargetId = 'review'
        Label = 'No'
    }
)
Out-Chart -Nodes $nodes -Edges $edges -GraphType FlowChart -Title 'Approval' -ShowLegend
```

Connections are separate records, not nested Links or a parent hierarchy.
Click a node or edge and press Enter to inspect its original record.

### Example 21: Draw an undirected network

```powershell
$nodes = @(
    [pscustomobject]@{
        NodeId = 'router'
        Name = 'Router'
    }
    [pscustomobject]@{
        NodeId = 'server'
        Name = 'Server'
    }
    [pscustomobject]@{
        NodeId = 'backup'
        Name = 'Backup'
    }
)
$edges = @(
    [pscustomobject]@{
        SourceId = 'router'
        TargetId = 'server'
        Label = 'LAN'
        Directed = $false
    }
    [pscustomobject]@{
        SourceId = 'router'
        TargetId = 'backup'
        Directed = $false
    }
)
Out-Chart -Nodes $nodes -Edges $edges -GraphType NetworkDiagram -Title 'LAN' -ShowLegend
```

Directed defaults to true. Set it to false for an undirected connection;
NetworkDiagram can show both kinds. Layout is deterministic for unchanged data.

### Example 22: Stack additive outcome counts

```powershell
$outcomes = @(
    [pscustomobject]@{
        Day = 'Mon'
        NoRetry = 500
        Retried = 160
        Failed = 30
    }
    [pscustomobject]@{
        Day = 'Tue'
        NoRetry = 580
        Retried = 100
        Failed = 20
    }
    [pscustomobject]@{
        Day = 'Wed'
        NoRetry = 680
        Retried = 120
        Failed = 20
    }
)
$outcomes | Out-Chart -XProperty Day -YProperty NoRetry, Retried, Failed -GraphType StackedArea -ShowLegend
```

These are disjoint nonnegative request outcomes. The total area is the number
of requests; individual bands preserve each outcome's contribution.

### Example 23: Compare normalized composition with missing and idle samples

```powershell
$outcomes = @(
    [pscustomobject]@{
        Day = 'Mon'
        NoRetry = 500
        Retried = 160
        Failed = 30
    }
    [pscustomobject]@{
        Day = 'Tue'
        NoRetry = 580
        Retried = 100
        Failed = 20
    }
    [pscustomobject]@{
        Day = 'Wed (missing)'
        NoRetry = $null
        Retried = 120
        Failed = 20
    }
    [pscustomobject]@{
        Day = 'Thu (idle)'
        NoRetry = 0
        Retried = 0
        Failed = 0
    }
    [pscustomobject]@{
        Day = 'Fri'
        NoRetry = 610
        Retried = 220
        Failed = 70
    }
)
$outcomes | Out-Chart -XProperty Day -YProperty NoRetry, Retried, Failed -GraphType PercentStackedArea -ShowLegend -Theme Monokai
```

The complete nonzero samples total 100 percent. Missing and zero-total samples
leave gaps rather than inventing shares. Inspection still returns raw counts.

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

Display the wrapping, clickable series legend above the plot.
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
