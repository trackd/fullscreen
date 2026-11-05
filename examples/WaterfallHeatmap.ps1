<#
.SYNOPSIS
Explore cumulative waterfall changes or a signed matrix heatmap.
.DESCRIPTION
Generates six ordered categories. Waterfall uses Change as signed increments
from zero. Heatmap compares Change and Target as matrix rows with a shared color
scale. Missing cells stay blank, while zero is a real value. Click a filled bar
or cell to select its original row; double-click to inspect it.
.PARAMETER GraphType
Waterfall or the existing Heatmap mode.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DataOnly
Return generated original rows without opening a terminal.
.PARAMETER DefinitionOnly
Return a complete portable TUI definition without opening a terminal.
.EXAMPLE
.\WaterfallHeatmap.ps1 -GraphType Waterfall
.EXAMPLE
.\WaterfallHeatmap.ps1 -GraphType Heatmap -Theme Monokai
.EXAMPLE
.\WaterfallHeatmap.ps1 -DataOnly
.EXAMPLE
.\WaterfallHeatmap.ps1 -GraphType Heatmap -DefinitionOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Original rows with DataOnly; a complete TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. No additional inputs or modules are required.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [ValidateSet('Waterfall', 'Heatmap')]
    [string]$GraphType = 'Waterfall',
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'

$measurements = @(
    [pscustomobject]@{
        Category = 'Sales'
        Change = 20
        Target = -20
    }
    [pscustomobject]@{
        Category = 'Costs'
        Change = -5
        Target = 0
    }
    [pscustomobject]@{
        Category = 'Flat'
        Change = 0
        Target = 15
    }
    [pscustomobject]@{
        Category = 'Missing'
        Change = $null
        Target = 25
    }
    [pscustomobject]@{
        Category = 'Refunds'
        Change = -25
        Target = 0
    }
    [pscustomobject]@{
        Category = 'Other'
        Change = 15
        Target = 10
    }
)

if ($DataOnly.IsPresent) {
    return $measurements
}
$options = @{
    XProperty  = 'Category'
    YProperty  = if ($GraphType -eq 'Heatmap') {
        @('Change', 'Target')
    }
    else {
        'Change'
    }
    GraphType  = $GraphType
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($DefinitionOnly.IsPresent) {
    return New-TuiDefinition -Title 'Waterfall and heatmap' -Root @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'measurements'
        GraphKind = $GraphType
        XProperty = $options.XProperty
        YProperty = @($options.YProperty)
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    } -State @{ measurements = $measurements }
}
$measurements | Out-Chart @options
