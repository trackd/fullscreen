<#
.SYNOPSIS
Cycle through every chart kind in one fullscreen window.
.DESCRIPTION
Shows deterministic synthetic data, one chart at a time, using a bound
TabControl. Tick callbacks advance the page without blocking input or
opening another window. The default cycle takes about 78 seconds and repeats.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome for recording.
.PARAMETER DefinitionOnly
Return the complete static layout without opening a terminal or starting a timer.
Callbacks stay in this script; a returned layout does not cycle on its own.
.PARAMETER SecondsPerChart
Seconds to display each chart, from 1 to 60. Defaults to 3.
.PARAMETER Once
Close successfully after displaying every chart once.
.EXAMPLE
.\ChartShowcase.ps1 -Theme Monokai -Minimal
.EXAMPLE
.\ChartShowcase.ps1 -SecondsPerChart 5 -Once
.EXAMPLE
.\ChartShowcase.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A portable TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. No files, services, jobs or external data are required.
Use Previous, Pause / Resume and Next to control playback; Close or F10 exits.
Hover for 200 ms for point details; selecting a point pauses playback and shows
the original record below the chart. Resume restarts automatic playback.
Sixel-capable terminals use pixel rendering; other terminals use cell rendering.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [ValidateRange(1, 60)]
    [double]$SecondsPerChart = 3,
    [switch]$Once
)
$ErrorActionPreference = 'Stop'

$signal = foreach ($sample in 1..48) {
    [pscustomobject]@{
        Sample = $sample
        Latency = [math]::Round(45 + 18 * [math]::Sin($sample * 0.28) + 6 * [math]::Sin($sample * 0.9), 1)
        Target = 50
    }
}
$outcomes = @(
    [pscustomobject]@{
        Day = 'Mon'
        Completed = 45
        Retried = 15
        Failed = 5
    }
    [pscustomobject]@{
        Day = 'Tue'
        Completed = 60
        Retried = 20
        Failed = 10
    }
    [pscustomobject]@{
        Day = 'Wed'
        Completed = 50
        Retried = 10
        Failed = 8
    }
    [pscustomobject]@{
        Day = 'Thu'
        Completed = 70
        Retried = 25
        Failed = 5
    }
    [pscustomobject]@{
        Day = 'Fri'
        Completed = 65
        Retried = 15
        Failed = 12
    }
    [pscustomobject]@{
        Day = 'Sat'
        Completed = 40
        Retried = 12
        Failed = 4
    }
    [pscustomobject]@{
        Day = 'Sun'
        Completed = 55
        Retried = 18
        Failed = 7
    }
)
$resources = @(
    [pscustomobject]@{
        Resource = 'CPU'
        Load = 35
        Target = 60
    }
    [pscustomobject]@{
        Resource = 'Memory'
        Load = 72
        Target = 80
    }
    [pscustomobject]@{
        Resource = 'Disk'
        Load = 48
        Target = 70
    }
    [pscustomobject]@{
        Resource = 'Network'
        Load = 90
        Target = 85
    }
)
$changes = @(
    [pscustomobject]@{
        Stage = 'Opening'
        Change = 80
    }
    [pscustomobject]@{
        Stage = 'Sales'
        Change = 35
    }
    [pscustomobject]@{
        Stage = 'Returns'
        Change = -15
    }
    [pscustomobject]@{
        Stage = 'Services'
        Change = 25
    }
    [pscustomobject]@{
        Stage = 'Costs'
        Change = -40
    }
    [pscustomobject]@{
        Stage = 'Other'
        Change = 12
    }
)
$markets = @(
    [pscustomobject]@{
        Market = 'North'
        Alpha = 30
        Beta = 10
        Gamma = 20
    }
    [pscustomobject]@{
        Market = 'South'
        Alpha = 10
        Beta = 25
        Gamma = 5
    }
    [pscustomobject]@{
        Market = 'West'
        Alpha = 20
        Beta = 40
        Gamma = 30
    }
)
$closes = @(100, 112, 108, 104, 92, 101, 115, 110, 95, 87, 98, 117, 109, 94, 103)
$prices = foreach ($sample in 0..14) {
    [pscustomobject]@{
        Time = ([datetimeoffset]'2026-01-01T00:00:00Z').AddDays($sample).ToString('O')
        Close = $closes[$sample]
    }
}
$estimates = foreach ($sample in 1..10) {
    $mean = 40 + 15 * [math]::Sin($sample * 0.6)
    [pscustomobject]@{
        Sample = $sample
        Mean = [math]::Round($mean, 1)
        Low = [math]::Round($mean - 5 - $sample, 1)
        High = [math]::Round($mean + 5 + $sample, 1)
    }
}
$frames = @(
    [pscustomobject]@{
        FrameId = 'request'
        ParentId = ''
        Name = 'Request'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'parse'
        ParentId = 'request'
        Name = 'Parse'
        Weight = 8
    }
    [pscustomobject]@{
        FrameId = 'decode'
        ParentId = 'parse'
        Name = 'Decode'
        Weight = 15
    }
    [pscustomobject]@{
        FrameId = 'process'
        ParentId = 'request'
        Name = 'Process'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'query'
        ParentId = 'process'
        Name = 'Query'
        Weight = 12
    }
    [pscustomobject]@{
        FrameId = 'database'
        ParentId = 'query'
        Name = 'Database'
        Weight = 25
    }
    [pscustomobject]@{
        FrameId = 'serialize'
        ParentId = 'process'
        Name = 'Serialize'
        Weight = 10
    }
    [pscustomobject]@{
        FrameId = 'encode'
        ParentId = 'serialize'
        Name = 'Encode'
        Weight = 15
    }
    [pscustomobject]@{
        FrameId = 'write'
        ParentId = 'request'
        Name = 'Write'
        Weight = 5
    }
)
$tasks = @(
    [pscustomobject]@{
        Task = 'Build'
        Start = '2026-01-12T08:00:00Z'
        End = '2026-01-12T10:00:00Z'
        Progress = 1
    }
    [pscustomobject]@{
        Task = 'Test'
        Start = '2026-01-12T09:00:00Z'
        End = '2026-01-12T13:00:00Z'
        Progress = 0.7
    }
    [pscustomobject]@{
        Task = 'Review'
        Start = '2026-01-12T11:00:00Z'
        End = '2026-01-12T14:00:00Z'
        Progress = 0.4
    }
    [pscustomobject]@{
        Task = 'Approval'
        Start = '2026-01-12T14:00:00Z'
        End = '2026-01-12T14:00:00Z'
        Progress = 0
    }
    [pscustomobject]@{
        Task = 'Deploy'
        Start = '2026-01-12T14:00:00Z'
        End = '2026-01-12T17:00:00Z'
        Progress = 0.2
    }
)
$nodes = @(
    [pscustomobject]@{
        NodeId = 'start'
        Name = 'Receive'
        Shape = 'Terminal'
    }
    [pscustomobject]@{
        NodeId = 'check'
        Name = 'Valid?'
        Shape = 'Decision'
    }
    [pscustomobject]@{
        NodeId = 'process'
        Name = 'Process'
        Shape = 'Process'
    }
    [pscustomobject]@{
        NodeId = 'retry'
        Name = 'Review'
        Shape = 'Process'
    }
    [pscustomobject]@{
        NodeId = 'end'
        Name = 'Complete'
        Shape = 'Terminal'
    }
)
$edges = @(
    [pscustomobject]@{
        SourceId = 'start'
        TargetId = 'check'
    }
    [pscustomobject]@{
        SourceId = 'check'
        TargetId = 'process'
        Label = 'Yes'
    }
    [pscustomobject]@{
        SourceId = 'check'
        TargetId = 'retry'
        Label = 'No'
    }
    [pscustomobject]@{
        SourceId = 'retry'
        TargetId = 'check'
        Label = 'Retry'
    }
    [pscustomobject]@{
        SourceId = 'process'
        TargetId = 'end'
    }
)
$chartKinds = @(
    'Line'
    'Area'
    'Bars'
    'Step'
    'Scatter'
    'Stem'
    'Histogram'
    'BoxPlot'
    'HorizontalBars'
    'StackedBars'
    'Heatmap'
    'ErrorBars'
    'ColumnLine'
    'Pie'
    'Gauge'
    'RadialBars'
    'Radar'
    'Waterfall'
    'Marimekko'
    'Kagi'
    'Flamegraph'
    'Gantt'
    'FlowChart'
    'NetworkDiagram'
    'StackedArea'
    'PercentStackedArea'
)
$pages = foreach ($kind in $chartKinds) {
    $chart = @{
        Id = "chart_$kind"
        Type = 'Chart'
        SelectionBind = 'selectedPoint'
        Bind = 'signal'
        GraphKind = $kind
        XProperty = 'Sample'
        YProperty = @('Latency')
        ShowLegend = $true
        ShowAxes = $true
        VerticalAlignment = 'Stretch'
    }
    $description = 'Synthetic response time (ms)'
    switch ($kind) {
        { $_ -in 'Histogram', 'BoxPlot' } {
            $chart.XProperty = ''
            $description = 'Distribution of response times (ms)'
        }
        { $_ -in 'StackedBars', 'Heatmap', 'StackedArea', 'PercentStackedArea' } {
            $chart.Bind = 'outcomes'
            $chart.XProperty = 'Day'
            $chart.YProperty = @('Completed', 'Retried', 'Failed')
            $description = 'Disjoint request outcomes across one week'
        }
        'ErrorBars' {
            $chart.Bind = 'estimates'
            $chart.YProperty = @('Mean')
            $chart.LowerProperty = 'Low'
            $chart.UpperProperty = 'High'
            $description = 'Point estimates with explicit lower and upper bounds'
        }
        'ColumnLine' {
            $chart.YProperty = @('Latency', 'Target')
            $chart.SeriesRole = @('Column', 'Line')
            $description = 'Measured response time and a shared target (ms)'
        }
        { $_ -in 'Pie', 'Gauge', 'RadialBars', 'Radar' } {
            $chart.Bind = 'resources'
            $chart.XProperty = 'Resource'
            $chart.YProperty = @('Load')
            $description = 'Resource load (%)'
            if ($kind -ne 'Pie') {
                $chart.Minimum = 0
                $chart.Maximum = 100
            }
            if ($kind -eq 'Radar') {
                $chart.YProperty = @('Load', 'Target')
            }
            if ($kind -eq 'Pie') {
                $description = 'Relative resource weights (not percentages of a shared capacity)'
            }
        }
        'Waterfall' {
            $chart.Bind = 'changes'
            $chart.XProperty = 'Stage'
            $chart.YProperty = @('Change')
            $description = 'Signed changes accumulated from zero'
        }
        'Marimekko' {
            $chart.Bind = 'markets'
            $chart.XProperty = 'Market'
            $chart.YProperty = @('Alpha', 'Beta', 'Gamma')
            $description = 'Market totals determine width; components determine height'
        }
        'Kagi' {
            $chart.Bind = 'prices'
            $chart.XProperty = 'Time'
            $chart.YProperty = @('Close')
            $chart.ReversalAmount = 8
            $description = 'Closing prices with fixed eight-unit reversals'
        }
        'Flamegraph' {
            $chart.Bind = 'frames'
            $chart.XProperty = ''
            $chart.YProperty = @()
            $chart.FrameNameProperty = 'Name'
            $chart.FlameFrameRows = 3
            $description = 'Synthetic CPU samples: inclusive widths from exclusive self weights'
        }
        'Gantt' {
            $chart.Bind = 'tasks'
            $chart.XProperty = ''
            $chart.YProperty = @()
            $chart.ProgressProperty = 'Progress'
            $description = 'Release schedule (UTC), progress and an approval milestone'
        }
        { $_ -in 'FlowChart', 'NetworkDiagram' } {
            $chart.Bind = 'nodes'
            $chart.EdgeBind = 'edges'
            $chart.XProperty = ''
            $chart.YProperty = @()
            $description = 'Separate node and edge records, including a retry cycle'
        }
    }
    @{
        Id = "page_$kind"
        Type = 'VStack'
        TabTitle = $kind
        VerticalAlignment = 'Stretch'
        Spacing = 1
        Children = @(
            @{
                Id = "title_$kind"
                Type = 'Text'
                Text = '{0:D2} / {1}  {2}' -f ($chartKinds.IndexOf($kind) + 1), $chartKinds.Count, $kind
                Bold = $true
            }
            @{
                Id = "description_$kind"
                Type = 'Text'
                Text = $description
            }
            $chart
        )
    }
}
$layout = New-TuiDefinition -Title 'Chart showcase' -Root @{
    Id = 'root'
    Type = 'VStack'
    VerticalAlignment = 'Stretch'
    Spacing = 1
    Children = @(
        @{
            Id = 'pages'
            Type = 'TabControl'
            Bind = 'currentChart'
            VerticalAlignment = 'Stretch'
            Children = @($pages)
        }
        @{
            Id = 'selection'
            Type = 'Text'
            Bind = 'selectedDetails'
        }
        @{
            Id = 'status'
            Type = 'Text'
            Bind = 'playback'
        }
        @{
            Id = 'controls'
            Type = 'HStack'
            Spacing = 2
            Children = @(
                @{
                    Id = 'previous'
                    Type = 'Button'
                    Text = 'Previous'
                }
                @{
                    Id = 'pause'
                    Type = 'Button'
                    Text = 'Pause / Resume'
                }
                @{
                    Id = 'next'
                    Type = 'Button'
                    Text = 'Next'
                }
                @{
                    Id = 'close'
                    Type = 'Button'
                    Text = 'Close'
                    Close = $true
                }
            )
        }
    )
} -State @{
    currentChart = 0
    chartCount = $chartKinds.Count
    secondsPerChart = $SecondsPerChart
    once = $Once.IsPresent
    paused = $false
    slideStarted = 0
    playback = "Playing - $SecondsPerChart seconds per chart"
    selectedPoint = $null
    selectedDetails = 'Click a point to show its values; hover for details (200 ms).'
    signal = @($signal)
    outcomes = $outcomes
    resources = $resources
    changes = $changes
    markets = $markets
    prices = @($prices)
    estimates = @($estimates)
    frames = $frames
    tasks = $tasks
    nodes = $nodes
    edges = $edges
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$onTick = {
    param(
        $tuiEvent,
        $state
    )
    if ($tuiEvent.Kind -ne 'Tick' -or $state.paused) {
        return
    }
    $now = [System.Diagnostics.Stopwatch]::GetTimestamp()
    if ($state.slideStarted -eq 0) {
        $state.slideStarted = $now
        return
    }
    $elapsed = ($now - $state.slideStarted) / [System.Diagnostics.Stopwatch]::Frequency
    if ($elapsed -lt $state.secondsPerChart) {
        return
    }
    if ($state.once -and $state.currentChart -eq ($state.chartCount - 1)) {
        return @{ Close = $true }
    }
    $state.currentChart = ($state.currentChart + 1) % $state.chartCount
    $state.slideStarted = $now
}
$onEvent = {
    param(
        $tuiEvent,
        $state
    )
    if ($tuiEvent.Id -like 'chart_*' -and $tuiEvent.Kind -in 'Selection', 'Invoke') {
        $state.selectedDetails = (($tuiEvent.Value | Format-List * | Out-String).Trim() -split '\r?\n' |
            Where-Object { $_.Trim().Length -gt 0 }) -join ' | '
        $state.paused = $true
        $state.playback = 'Paused - inspecting selection'
        return
    }
    if ($tuiEvent.Id -eq 'pages' -and $tuiEvent.Kind -eq 'Change') {
        $state.selectedDetails = 'Click a point to show its values; hover for details (200 ms).'
        $state.slideStarted = [System.Diagnostics.Stopwatch]::GetTimestamp()
        return
    }
    if ($tuiEvent.Kind -ne 'Click') {
        return
    }
    switch ($tuiEvent.Id) {
        'previous' {
            $state.currentChart = ($state.currentChart + $state.chartCount - 1) % $state.chartCount
        }
        'next' {
            $state.currentChart = ($state.currentChart + 1) % $state.chartCount
        }
        'pause' {
            $state.paused = -not $state.paused
            $state.playback = if ($state.paused) {
                'Paused'
            }
            else {
                "Playing - $($state.secondsPerChart) seconds per chart"
            }
        }
    }
    $state.slideStarted = [System.Diagnostics.Stopwatch]::GetTimestamp()
}
$options = @{
    OnTick = $onTick
    OnEvent = $onEvent
    TickInterval = 100
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
$null = $layout | Invoke-Tui @options
