<#
.SYNOPSIS
Compare named series as lines, overlaid/stacked areas, grouped bars or column/line combinations.
.DESCRIPTION
Uses simulated response-time measurements in milliseconds on one shared Y axis.
Line, Area and Bars are the existing rendering modes: multiple Y properties
make them multi-series. There are no separate MultiLine/MultiArea enum aliases.
ColumnLine is the mixed-role mode. Area overlays; StackedArea stacks nonnegative
values, and PercentStackedArea normalizes each sample to 100 percent. Forecast's
missing sample leaves a gap in the whole stack, rather than pretending it is zero.
Click the wrapping legend to hide or show a series. Use Graph / Series styles to
change column/line roles and colors; double-click a point to inspect its original row.
.PARAMETER GraphType
Line, Area, Bars, ColumnLine, StackedArea or PercentStackedArea.
.PARAMETER SeriesRole
Optional Auto, Column or Line roles for Actual, Target and Forecast, in that order.
ColumnLine defaults to first-series columns and remaining-series lines.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DataOnly
Return the simulated original objects without opening a terminal.
.PARAMETER DefinitionsOnly
Return a complete portable TUI definition without opening a terminal. DefinitionOnly is a compatibility alias.
.EXAMPLE
.\ChartCombinations.ps1 -GraphType Line
.EXAMPLE
.\ChartCombinations.ps1 -GraphType Area -Theme Monokai
.EXAMPLE
.\ChartCombinations.ps1 -GraphType Bars
.EXAMPLE
.\ChartCombinations.ps1 -GraphType ColumnLine -SeriesRole Column, Line, Line
.EXAMPLE
.\ChartCombinations.ps1 -DefinitionsOnly | Invoke-Tui -Theme Monokai -Minimal
.EXAMPLE
.\ChartCombinations.ps1 -GraphType StackedArea -Theme Monokai
.EXAMPLE
.\ChartCombinations.ps1 -GraphType PercentStackedArea -Minimal
.OUTPUTS
Original measurements with DataOnly; a complete TUI definition with DefinitionsOnly; otherwise no output.
.NOTES
Import fullscreen first. No additional dependencies or external data sources.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [Alias('DefinitionOnly')]
    [switch]$DefinitionsOnly,
    [ValidateSet('Line', 'Area', 'Bars', 'ColumnLine', 'StackedArea', 'PercentStackedArea')]
    [string]$GraphType = 'ColumnLine',
    [ValidateSet('Auto', 'Column', 'Line')]
    [string[]]$SeriesRole,
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'
$measurements = foreach ($sample in 0..11) {
    [pscustomobject]@{
        Sample = 'Sample {0:D2}' -f ($sample + 1)
        Actual = [math]::Round(40 + 20 * [math]::Sin($sample * 0.7), 1)
        Target = 45.0
        Forecast = if ($sample -eq 5) {
            $null
        }
        else {
            [math]::Round(42 + 15 * [math]::Sin($sample * 0.7 + 0.4), 1)
        }
    }
}
if ($DataOnly.IsPresent) {
    return $measurements
}
$options = @{
    XProperty = 'Sample'
    YProperty = @('Actual', 'Target', 'Forecast')
    GraphType = $GraphType
    Title = 'Response time (ms)'
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($PSBoundParameters.ContainsKey('SeriesRole')) {
    $options.SeriesRole = $SeriesRole
}
if ($DefinitionsOnly.IsPresent) {
    $root = @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'measurements'
        GraphKind = $GraphType
        XProperty = 'Sample'
        YProperty = @('Actual', 'Target', 'Forecast')
        Text = $options.Title
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    }
    if ($PSBoundParameters.ContainsKey('SeriesRole')) {
        $root.SeriesRole = $SeriesRole
    }
    return New-TuiDefinition -Title $options.Title -Root $root -State @{ measurements = $measurements }
}
$measurements | Out-Chart @options
