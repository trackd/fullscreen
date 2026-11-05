<#
.SYNOPSIS
Show additive request outcomes as stacked or 100-percent stacked areas.
.DESCRIPTION
NoRetry, Retried and Failed are disjoint nonnegative request counts.
Each row has one shared ordered category coordinate. A missing component
leaves a gap in the entire stack; it is not treated as zero. The idle row
has a zero total, which is also undefined and blank in PercentStackedArea.
Hiding a legend series recomputes totals and percentages for visible series.
Point inspection returns the original counts, not invented interpolated rows.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionsOnly
Return a complete portable TUI definition without opening a terminal.
DefinitionOnly is a compatibility alias.
.PARAMETER GraphType
StackedArea uses counts; PercentStackedArea uses a fixed 0 to 100 percent Y axis.
.PARAMETER DataOnly
Return the original outcome records without opening a terminal.
.EXAMPLE
.\StackedAreas.ps1 -Theme Monokai
.EXAMPLE
.\StackedAreas.ps1 -GraphType PercentStackedArea -Minimal
.EXAMPLE
.\StackedAreas.ps1 -GraphType PercentStackedArea -DefinitionsOnly | Invoke-Tui -Theme Monokai
.OUTPUTS
Original records with DataOnly; a portable TUI definition with DefinitionsOnly;
otherwise no output.
.NOTES
Import fullscreen first. No dependencies or external data sources.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [Alias('DefinitionOnly')]
    [switch]$DefinitionsOnly,
    [ValidateSet('StackedArea', 'PercentStackedArea')]
    [string]$GraphType = 'StackedArea',
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'
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
    [pscustomobject]@{
        Day = 'Thu'
        NoRetry = $null
        Retried = 180
        Failed = 40
    }
    [pscustomobject]@{
        Day = 'Fri'
        NoRetry = 610
        Retried = 220
        Failed = 70
    }
    [pscustomobject]@{
        Day = 'Sat (idle)'
        NoRetry = 0
        Retried = 0
        Failed = 0
    }
    [pscustomobject]@{
        Day = 'Sun'
        NoRetry = 420
        Retried = 90
        Failed = 10
    }
    [pscustomobject]@{
        Day = 'Next Mon'
        NoRetry = 660
        Retried = 180
        Failed = 60
    }
)
if ($DataOnly.IsPresent) {
    return $outcomes
}
$title = if ($GraphType -eq 'PercentStackedArea') {
    'Request outcomes (%)'
}
else {
    'Request outcomes (count)'
}
if ($DefinitionsOnly.IsPresent) {
    $root = @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'outcomes'
        GraphKind = $GraphType
        XProperty = 'Day'
        YProperty = @('NoRetry', 'Retried', 'Failed')
        Text = $title
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    }
    return New-TuiDefinition -Title $title -Root $root -State @{ outcomes = $outcomes }
}
$options = @{
    XProperty = 'Day'
    YProperty = @('NoRetry', 'Retried', 'Failed')
    GraphType = $GraphType
    Title = $title
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
$outcomes | Out-Chart @options
