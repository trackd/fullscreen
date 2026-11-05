<#
.SYNOPSIS
Explore pie slices, semicircular gauges, radial bars and radar comparisons.
.DESCRIPTION
Generates four named rows. Pie, Gauge and RadialBars use Load as their one Y
property; Radar compares Load and Target across the same category spokes.
Gauge lanes use a green-to-red load gradient. Gauge and RadialBars use 0-100
unless Maximum is changed; a value above the range is marked, not silently lost.
Click a slice, lane or radar vertex/segment to select its original row, then
double-click to inspect it. Row legends select rows; radar legends toggle series.
.PARAMETER GraphType
Pie, Gauge, RadialBars or Radar.
.PARAMETER Maximum
Upper bound for Gauge, RadialBars and Radar. Ignored for Pie, which uses totals.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DataOnly
Return generated original rows without opening a terminal.
.PARAMETER DefinitionsOnly
Return a complete portable TUI definition without opening a terminal. DefinitionOnly is a compatibility alias.
.EXAMPLE
.\RadialCharts.ps1 -GraphType Pie
.EXAMPLE
.\RadialCharts.ps1 -GraphType Gauge -Theme Monokai
.EXAMPLE
.\RadialCharts.ps1 -GraphType RadialBars -Maximum 120
.EXAMPLE
.\RadialCharts.ps1 -GraphType Radar
.EXAMPLE
.\RadialCharts.ps1 -GraphType Radar -DefinitionsOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Original rows with DataOnly; a complete TUI definition with DefinitionsOnly; otherwise no output.
.NOTES
Import fullscreen first. No external inputs or extra modules are required.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [Alias('DefinitionOnly')]
    [switch]$DefinitionsOnly,
    [ValidateSet('Pie', 'Gauge', 'RadialBars', 'Radar')]
    [string]$GraphType = 'Gauge',
    [double]$Maximum = 100,
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'
$measurements = @(
    [pscustomobject]@{
        Category = 'CPU'
        Load = 25
        Target = 50
    }
    [pscustomobject]@{
        Category = 'Memory'
        Load = 50
        Target = 60
    }
    [pscustomobject]@{
        Category = 'Disk'
        Load = 75
        Target = 70
    }
    [pscustomobject]@{
        Category = 'Network'
        Load = 100
        Target = 80
    }
)
if ($DataOnly.IsPresent) {
    return $measurements
}
$options = @{
    XProperty = 'Category'
    YProperty = if ($GraphType -eq 'Radar') {
        @('Load', 'Target')
    }
    else {
        @('Load')
    }
    GraphType = $GraphType
    Title = 'Resource load'
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($GraphType -ne 'Pie') {
    $options.Minimum = 0
    $options.Maximum = $Maximum
}
if ($DefinitionsOnly.IsPresent) {
    $root = @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'measurements'
        GraphKind = $GraphType
        XProperty = $options.XProperty
        YProperty = @($options.YProperty)
        Text = $options.Title
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    }
    if ($GraphType -ne 'Pie') {
        $root.Minimum = 0
        $root.Maximum = $Maximum
    }
    return New-TuiDefinition -Title $options.Title -Root $root -State @{ measurements = $measurements }
}
$measurements | Out-Chart @options
