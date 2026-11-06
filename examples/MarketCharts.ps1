<#
.SYNOPSIS
Compare market composition or inspect fixed-price Kagi reversals.
.DESCRIPTION
Marimekko uses variable category widths and stacked nonnegative components.
Kagi uses positive closing prices, fixed-price reversals and shoulder/waist
stroke changes. Click geometry to select its original row; double-click to
inspect. Import fullscreen before opening charts.
.PARAMETER GraphType
Marimekko or Kagi.
.PARAMETER ReversalAmount
Positive fixed Kagi reversal amount in price units; default 5 for this example.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DataOnly
Return generated objects without starting the interactive viewer.
.PARAMETER DefinitionOnly
Return a complete portable TUI definition without opening a terminal.
.EXAMPLE
.\MarketCharts.ps1 -GraphType Marimekko
.EXAMPLE
.\MarketCharts.ps1 -GraphType Kagi -ReversalAmount 5
.EXAMPLE
.\MarketCharts.ps1 -GraphType Kagi -DataOnly
.EXAMPLE
.\MarketCharts.ps1 -DefinitionOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Original rows with DataOnly; a complete TUI definition with DefinitionOnly; otherwise no output.
.NOTES
No external data sources or additional modules are required.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [ValidateSet('Marimekko', 'Kagi')]
    [string]$GraphType = 'Marimekko',
    [ValidateRange(0.000001, [double]::MaxValue)]
    [double]$ReversalAmount = 5,
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'
if ($GraphType -eq 'Marimekko') {
    $rows = @(
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
}
else {
    $day = [datetime]::UtcNow.Date
    $rows = foreach ($price in 100, 110, 108, 104, 95, 100, 111, 106, 90) {
        [pscustomobject]@{
            Time = $day
            Close = $price
        }
        $day = $day.AddDays(1)
    }
}
if ($DataOnly.IsPresent) {
    return $rows
}
$options = @{
    GraphType = $GraphType
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($GraphType -eq 'Marimekko') {
    $options.XProperty = 'Market'
    $options.YProperty = @('Alpha', 'Beta')
}
else {
    $options.XProperty = 'Time'
    $options.YProperty = @('Close')
    $options.ReversalAmount = $ReversalAmount
}
if ($DefinitionOnly.IsPresent) {
    $portableRows = if ($GraphType -eq 'Kagi') {
        foreach ($row in $rows) {
            @{
                Time = $row.Time.ToString('O', [cultureinfo]::InvariantCulture)
                Close = $row.Close
            }
        }
    }
    else {
        $rows
    }
    return New-TuiDefinition -Title 'Market charts' -Root @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'rows'
        GraphKind = $GraphType
        XProperty = $options.XProperty
        YProperty = $options.YProperty
        ReversalAmount = $ReversalAmount
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    } -State @{ rows = @($portableRows) }
}
$rows | Out-Chart @options
