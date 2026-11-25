# Chart cookbook

Use [Out-Chart](../en-US/Out-Chart.md) for parameters and pipeline contracts. These recipes cover calculated axes, selections and specialized chart inputs. See [terminal graphics](terminal-graphics.md) for renderer setup.

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
wheel to zoom time when `-EnableZoom` is set, and click an interval then D to inspect the original record.

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
