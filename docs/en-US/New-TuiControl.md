---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-25-2026
PlatyPS schema version: 2024-05-01
title: New-TuiControl
---

# New-TuiControl

## SYNOPSIS

Create a validated native control definition for composition in custom TUIs.

## SYNTAX

### __AllParameterSets

```
New-TuiControl [-Type] <string> -Id <string> [-Text <string>] [-Bind <string>] [-Child <psobject[]>]
 [-Property <hashtable>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Builds an independent PSCustomObject containing a validated control node. Combine it with other controls using Child, or place it in New-TuiDefinition and Invoke-Tui. Property contains control-specific options listed by Get-TuiControl; it cannot override named base parameters. Creating definitions does not access files or start background work.

Common styling properties include Foreground and Background (#RRGGBB or empty for
the theme), Bold for text labels, HorizontalAlignment and VerticalAlignment
(Start, Center, End or Stretch). The optional Spectre control binds a runtime
IRenderable supplied through Invoke-Tui State; fullscreen does not bundle Spectre.

## EXAMPLES

### Example 1: Add a live graph to a custom TUI

```powershell
$chart = New-TuiControl Chart -Id load -Text 'Load %' -Bind load -Property @{
    GraphKind = 'Step'
    Minimum = 0
    Maximum = 100
    Height = 10
}
$layout = New-TuiDefinition -Title 'My chart' -Root $chart -State @{
    load = @(50.0)
}
$layout | Invoke-Tui -TickInterval 500 -OnTick {
    param($tuiEvent, $state)
    $state.load = @($state.load | Select-Object -Last 119) + (Get-Random -Minimum 0 -Maximum 101)
}
```

GraphKind accepts Line, Area, Bars, Step, Scatter, Stem, Histogram, BoxPlot,
HorizontalBars, StackedBars, Heatmap, ErrorBars, ColumnLine, Pie, Gauge, RadialBars
Radar, Waterfall, Marimekko, Kagi, Flamegraph, Gantt, FlowChart, NetworkDiagram,
StackedArea and PercentStackedArea. `ShowLegend = $true`
reserves a wrapping, clickable legend above the plot. `ShowAxes = $true` enables
supported coordinate axes and tick labels; it defaults to false for compatibility.
Sixel is automatic on supported terminals, with terminal-cell fallback. Minimum
and Maximum are optional numeric bounds; omit them for automatic scaling. Null
or NaN entries leave gaps. Scalar bindings retain the timestamped-history adapter.
ErrorBars without
lower/upper values shows the measurements themselves; C# ChartSeries supports
LowerValues/UpperValues, and Out-Chart accepts LowerProperty/UpperProperty.
For named multi-series object rows, bind the collection with `Bind`, choose an
optional `XProperty` and supply `YProperty` names. `SeriesRole` optionally assigns
ColumnLine roles in the same order. Original runtime rows remain selection sources.
Numeric collections and scalar history bindings remain supported.
Stacked areas require aligned nonnegative components; missing values gap the
whole stack. PercentStackedArea uses a fixed 0-100 percent Y range, with undefined
zero-total samples left blank.
These two modes use collection bindings rather than the scalar history adapter.
Stacked compositions use a shared categorical theme color cycle; explicit
series colors take precedence, and visibility changes preserve source colors.
For Pie, values are nonnegative slice weights; zeros/missing values have no
slice. Gauge uses semicircular lanes with green-to-red load colors, while
RadialBars uses full-circle lanes; both default to 0-100 and mark out-of-range
values. Radar uses at least three ordered category values on one shared scale.
Minimum/Maximum customize lane/radar bounds; Pie does not accept those bounds.
Cartesian logarithmic scales, thresholds and annotations are not supported on
these radial modes. ReGIS falls back to cells.
Waterfall uses the collection as signed increments from zero with cumulative
connectors; missing entries break connectors without changing the running total.
Heatmap uses the collection as one matrix row with a five-step shared color
scale. Its Minimum/Maximum bounds control colors. For multiple named heatmap
rows, use Out-Chart YProperty or C# Chart.Series.
Marimekko uses a numeric collection as one nonnegative component; multiple
components require Out-Chart or C# Chart.Series. Kagi uses an ordered positive
closing-price collection and `ReversalAmount = 5` sets a fixed five-price-unit
reversal. Both require collection bindings, not the scalar history adapter.
They use intrinsic horizontal spacing; Marimekko composition is 0-100 percent.
Flamegraph binds frame records with unique FrameId, ParentId (null/empty roots),
and finite nonnegative exclusive Weight. Optional FrameNameProperty groups
identical sibling names and matching descendants. Inclusive widths sum self
weights and all descendants; do not supply already-inclusive values. Field
selectors and WeightProperty customize the schema. FlameReverse starts roots
at the top. Each depth uses FlameFrameRows terminal rows (default 2), with unused height for shallow
profiles. Call paths sort alphabetically, not by time; warm colors differentiate
frames, not duration. Click zooms to a subtree, hover shows details, and F/U/0
focuses/returns to parent/all roots; R reverses direction. C# Chart.FlameSearch
highlights a case-insensitive name substring and FlameMatchPercent reports the
whole-profile sample share without double-counting nested matching frames.
Frame selection/invocation events retain the original record. Missing parents,
cycles, duplicate IDs and invalid weights are rejected; numeric bounds and
the scalar history adapter are unsupported.

### Example 2: Compose a native object picker

The selected value remains the original PowerShell object.

```powershell
$table = New-TuiControl FTable -Id processes -Bind processes -Property @{
    Columns = @('ProcessName', 'Id')
    SelectionBind = 'selected'
    Height = 16
}
$layout = New-TuiDefinition -Title 'Process picker' -Root $table
$layout | Invoke-Tui -State @{
    processes = @(Get-Process)
} -OnEvent {
    param($tuiEvent, $state)
    if ($tuiEvent.Kind -eq 'Submit') {
        @{
            Close = $true
            Value = $tuiEvent.Value
        }
    }
}
```

### Example 3: Combine multiline input with a Code (Markdown) heading

Child accepts ordinary definitions and the builder returns an independent native object.

```powershell
$heading = New-TuiControl Code -Id heading -Text '# My workbench'
$editor = New-TuiControl TextArea -Id source -Bind source -Property @{
    Height = 14
}
$stack = New-TuiControl VStack -Id root -Child $heading, $editor -Property @{
    Spacing = 1
}
New-TuiDefinition -Title 'Workbench' -Root $stack -State @{
    source = 'Get-Process'
} | Invoke-Tui
```

### Example 4: Reuse the file browser and monitor in tabs

Each component starts collecting data only while it is displayed and stops when hidden or closed.

```powershell
$files = New-TuiControl FileBrowser -Id files -Property @{
    Path = $PWD.Path
    Height = 24
}
$monitor = New-TuiControl PerformanceMonitor -Id monitor -Property @{
    View = 'Dashboard'
    RefreshInterval = 1000
    Height = 24
}
$tabs = New-TuiControl Tabs -Id root -Child $files, $monitor
New-TuiDefinition -Title 'Operations' -Root $tabs | Invoke-Tui
```

### Example 5: Standalone user sessions with optional process expansion

```powershell
$users = New-TuiControl UserList -Id users -Property @{
    IncludeProcesses = $true
    SelectionBind = 'selectedUser'
    Height = 30
}
New-TuiDefinition -Title 'User sessions' -Root $users | Invoke-Tui
```

Omit IncludeProcesses for session-only collection. L toggles process loading;
D opens all session details. Windows reports logon, idle, last-input, connection
times, client name and protocol where permitted. Linux/macOS use who -u; unavailable
or ambiguous values remain unknown. Process totals are per account, not per login.

FileCommander accepts Path and SecondaryPath for its two panes. ObjectExplorer
accepts Bind, Columns and SelectionBind for deep inspection of an object collection.

### Example 6: Configure a native slider

```powershell
$slider = New-TuiControl Slider -Id amount -Bind amount -Property @{
    Options = @{
        Minimum = 0
        Maximum = 100
        Step = 5
        ShowValueLabel = $true
    }
}
New-TuiDefinition -Root $slider -State @{
    amount = 25
} | Invoke-Tui
(Get-TuiControl Slider).NativeProperties | Format-Table Name, Type, Choices
```

Native widgets retain XenoAtom names. Use FTable/FLogView for fullscreen's table
and parsed log view; Table/LogControl select the native versions. EnumSelect adds
EnumType. Grid accepts Options.Rows/Columns lengths and GridCell children with
Options.Row/Column/RowSpan/ColumnSpan. Padding and Margin accept a number, two
horizontal/vertical numbers, or four left/top/right/bottom numbers.

### Example 7: Highlight source and show an image

Code uses the optional TextMate module and Image the optional Sixel module; each shows plain text or an install notice when its module is missing. Image needs a Sixel terminal.
This example creates a one-pixel PNG in the temporary directory, so it needs no
repository assets.

```powershell
$imagePath = Join-Path ([IO.Path]::GetTempPath()) ("fullscreen-preview-{0}.png" -f [guid]::NewGuid())
[IO.File]::WriteAllBytes($imagePath, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII='))
$code = New-TuiControl Code -Id code -Bind source -Property @{
    Language = 'powershell'
    Height = 10
}
$image = New-TuiControl Image -Id logo -Bind logo -Property @{
    Height = 12
}
$stack = New-TuiControl VStack -Id root -Child $code, $image
try {
    New-TuiDefinition -Title 'Preview' -Root $stack -State @{
        source = 'Get-Process | Sort-Object CPU'
        logo = $imagePath
    } | Invoke-Tui
}
finally {
    Remove-Item -LiteralPath $imagePath
}
```

### Example 8: Display three semicircular load lanes

```powershell
$chart = New-TuiControl Chart -Id resources -Bind loads -Property @{
    GraphKind = 'Gauge'
    Minimum = 0
    Maximum = 100
    ShowLegend = $true
    Height = 18
}
New-TuiDefinition -Title 'Resource load' -Root $chart -State @{
    loads = @(25, 60, 90)
} | Invoke-Tui
```

### Example 9: Display signed cumulative changes

```powershell
$chart = New-TuiControl Chart -Id changes -Bind increments -Property @{
    GraphKind = 'Waterfall'
    ShowLegend = $true
    Height = 18
}
New-TuiDefinition -Title 'Cumulative changes' -Root $chart -State @{
    increments = @(20, -5, 0, -25, 15)
} | Invoke-Tui
```

### Example 10: Inspect a frame hierarchy with exclusive samples

```powershell
$frames = @(
    [pscustomobject]@{
        FrameId = 'main'
        ParentId = ''
        Name = 'main'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'worker'
        ParentId = 'main'
        Name = 'worker'
        Weight = 3
    }
    [pscustomobject]@{
        FrameId = 'parse'
        ParentId = 'worker'
        Name = 'parse'
        Weight = 5
    }
)
$chart = New-TuiControl Chart -Id flame -Bind frames -Property @{
    GraphKind = 'Flamegraph'
    FrameNameProperty = 'Name'
    FlameReverse = $true
    SelectionBind = 'selectedFrame'
    Height = 18
}
$layout = New-TuiDefinition -Title 'Profiling samples' -Root $chart -State @{
    frames = $frames
}
$layout | Invoke-Tui
```

Main's inclusive width is 10 samples, worker's is 8, and parse's is 5.
WeightProperty defaults to Weight; FrameIdProperty and ParentIdProperty default
to FrameId and ParentId. The selected original record is stored in selectedFrame.

### Example 11: Bind a schedule to original task records

```powershell
$tasks = @(
    [pscustomobject]@{
        Task = 'Build'
        Start = [datetimeoffset]'2026-01-12T08:00:00Z'
        End = [datetimeoffset]'2026-01-12T12:00:00Z'
        Progress = 0.5
    }
    [pscustomobject]@{
        Task = 'Release'
        Start = [datetimeoffset]'2026-01-12T12:00:00Z'
        End = [datetimeoffset]'2026-01-12T12:00:00Z'
        Progress = $null
    }
)
$chart = New-TuiControl Chart -Id schedule -Text 'Schedule (UTC)' -Bind tasks -Property @{
    GraphKind = 'Gantt'
    ProgressProperty = 'Progress'
    ShowLegend = $true
    SelectionBind = 'selectedTask'
}
$definition = New-TuiDefinition -Root $chart
Invoke-Tui -Definition $definition -State @{ tasks = $tasks }
```

StartProperty, EndProperty and TaskNameProperty default to Start, End and Task.
SelectionBind receives the exact original task record when its interval is
selected. Schedule bindings require collections, not scalar history samples.

### Example 12: Bind separate network node and edge collections

```powershell
$nodes = @(
    [pscustomobject]@{
        NodeId = 'web'
        Name = 'Web'
    }
    [pscustomobject]@{
        NodeId = 'db'
        Name = 'Database'
    }
)
$edges = @(
    [pscustomobject]@{
        SourceId = 'web'
        TargetId = 'db'
        Label = 'Queries'
    }
)
$chart = New-TuiControl Chart -Id network -Text 'Services' -Bind nodes -Property @{
    GraphKind = 'NetworkDiagram'
    EdgeBind = 'edges'
    SelectionBind = 'selectedRecord'
    ShowLegend = $true
    Height = 24
}
$definition = New-TuiDefinition -Root $chart
Invoke-Tui -Definition $definition -State @{
    nodes = $nodes
    edges = $edges
}
```

Bind supplies nodes; EdgeBind supplies a separate edge collection. FlowChart
uses directed ranked flow, while NetworkDiagram supports directed/undirected
connections with bounded deterministic spring layout. NodeId values are unique;
SourceId/TargetId must reference them. SelectionBind and Selection/Invoke events
preserve the exact original node or edge. Empty edges allow isolated nodes.
Numeric bounds and scalar history input are not supported for either diagram.
Sixel labels use native terminal text in reserved, omitted pixel cells, not text
encoded in the image. This relies on the terminal's transparent-Sixel behavior;
use `Invoke-Tui -ChartRenderer Native` where compositing is unreliable.

### Example 13: Bind named object series without losing source records

```powershell
$rows = @(
    [pscustomobject]@{
        Category = 'North'
        Actual = 30
        Target = 25
    }
    [pscustomobject]@{
        Category = 'South'
        Actual = 20
        Target = 35
    }
)
$chart = New-TuiControl Chart -Id response -Bind rows -Property @{
    GraphKind = 'ColumnLine'
    XProperty = 'Category'
    YProperty = @('Actual', 'Target')
    SeriesRole = @('Column', 'Line')
    SelectionBind = 'selectedRow'
    ShowLegend = $true
    Height = 24
}
$definition = New-TuiDefinition -Title 'Response' -Root $chart -State @{ rows = $rows }
$definition | Invoke-Tui -Theme Monokai -Minimal
```

YProperty projects up to 64 unique named numeric fields from Bind's object rows.
XProperty optionally selects category labels or ordered numeric/date coordinates;
without it, input order supplies the X axis. Null numeric fields remain gaps.
SeriesRole optionally assigns one Auto/Column/Line role per field for ColumnLine.
These literal field selectors reuse Out-Chart's projection; calculated fields
and aggregations are not part of this bounded custom binding.
SelectionBind and Selection/Invoke events carry the original runtime row, not a
numeric projection object. Portable JSON/PSD1 definitions retain sample values
and selectors; as with all serialized data, reloaded rows are new objects.

## PARAMETERS

### -Bind

State key for the control value. FTable and FLogView read a collection; text controls read strings; Chart reads a numeric collection. Scalar Chart bindings use the timestamped-history adapter. Defaults to no binding.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Child

Child definitions for a VStack, HStack, Border or Tabs. Children is an alias. Noncontainers reject children; Border accepts at most one.
Set TabTitle in a child's Property hashtable to give its tab a readable caption
without changing the child's event ID or content Text.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: ''
SupportsWildcards: false
Aliases:
- Children
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Id

Unique control ID: 1–64 characters, beginning with a letter or underscore. IDs must be unique in the composed document.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Property

Hashtable of control-specific schema properties, for example Columns, SelectionBind, Path, View, RefreshInterval, Height, Width, Minimal, Maximum or TabTitle. TabTitle accepts up to 128 characters and is used when the control is a tab page; blank uses its Id. Unknown keys and invalid values are rejected.

```yaml
Type: System.Collections.Hashtable
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Text

Display text, title, initial input text or Markdown. Defaults to an empty string.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Type

A supported control type. Validated names are case-insensitive and tab-complete from the current catalog.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### System.Management.Automation.PSObject

Builds an independent PSCustomObject containing a validated control node. Combine it with other controls using Child, or place it in New-TuiDefinition and Invoke-Tui. Property contains control-specific options listed by Get-TuiControl; it cannot override named base parameters. Creating definitions does not access files or start background work.

## NOTES

All output is native PowerShell data. Saved definitions contain only layout and default values; keep live objects in Invoke-Tui -State.

## RELATED LINKS

[Invoke-Tui](Invoke-Tui.md)
