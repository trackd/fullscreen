<#
.SYNOPSIS
Explore all twelve graph styles in a custom TUI.
.DESCRIPTION
Builds charts with New-TuiControl and binds every tab to the same numeric collection.
Each tick appends a simulated value and retains the latest 120 samples.
Sixel-capable terminals draw pixel plots; other terminals use terminal cells.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return the layout without opening a terminal or generating samples.
.EXAMPLE
./GraphGallery.ps1 -Theme 'Dark Modern'
.EXAMPLE
./GraphGallery.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A TUI definition when DefinitionOnly is supplied; otherwise no output.
.NOTES
Import fullscreen first. Uses only simulated data and existing module controls.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$plots = foreach ($kind in 'Line',
    'Area',
    'Bars',
    'Step',
    'Scatter',
    'Stem',
    'Histogram',
    'BoxPlot',
    'HorizontalBars',
    'StackedBars',
    'Heatmap',
    'ErrorBars') {
    New-TuiControl Chart -Id $kind -Text "$kind · simulated response time (ms)" -Bind latency -Property @{
        GraphKind = $kind
        Maximum = 100
        Height = 12
    }
}
$tabs = New-TuiControl Tabs -Id plots -Child $plots
$intro = New-TuiControl Text -Id intro -Text 'Same numeric values, twelve styles · select a tab'
$close = New-TuiControl Button -Id close -Text Close -Property @{
    Close = $true
}
$root = New-TuiControl VStack -Id root -Child @(
    $intro
    $tabs
    $close
) -Property @{
    Spacing = 1
}
$layout = New-TuiDefinition -Title 'Graph gallery · custom TUI' -Root $root -State @{
    phase = 0
    latency = @(45.0)
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$null = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -TickInterval 250 -OnTick {
        param(
            $tuiEvent,
            $state
        )
        $state.phase += 0.17
        $nextLatency = 45.0 + 25.0 * [Math]::Sin($state.phase) + 12.0 * [Math]::Sin($state.phase * 3.7)
        $state.latency = @($state.latency | Select-Object -Last 119) + $nextLatency
    }
