<#
.SYNOPSIS
Browse expandable service cards in a scrollable dashboard.
.DESCRIPTION
Composes the existing ScrollViewer, Accordion and Collapsible controls. Multiple
cards can stay expanded; each card keeps its own expansion state through Bind.
Uses fictional data and makes no network calls. This is ordinary control
composition, not a virtualized list: all card controls are created up front.
.PARAMETER Theme
Fullscreen palette name or custom palette.
.PARAMETER DefinitionOnly
Return the layout without opening the terminal.
.EXAMPLE
./ServiceCards.ps1
.EXAMPLE
./ServiceCards.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. Tab moves between cards, Enter or Space toggles the
focused header, and the mouse wheel scrolls. F12 toggles diagnostics.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$DefinitionOnly
)

$cardState = @{}
$cards = foreach ($index in 1..12) {
    $cardId = 'service' + $index
    $cardState[$cardId] = $index -eq 1
    $status = if ($index -eq 4) {
        'Degraded'
    }
    else {
        'Healthy'
    }
    $latency = if ($index -eq 4) {
        240
    }
    else {
        10 + $index * 3
    }
    @{
        Id = $cardId
        Type = 'Collapsible'
        Text = "Service $index | $status | $latency ms"
        Bind = $cardId
        Children = @(
            @{
                Id = $cardId + 'details'
                Type = 'TextBlock'
                Text = "Region: Example North | Active jobs: $index`nLast check: sample data | Owner: Platform team`nExpand other cards to compare; collapse this card to reclaim space."
            }
        )
    }
}
$layout = New-TuiDefinition -Title 'Expandable service cards' -State $cardState -Root @{
    Id = 'scroll'
    Type = 'ScrollViewer'
    VerticalAlignment = 'Stretch'
    Options = @{
        HorizontalScrollEnabled = $false
    }
    Children = @(
        @{
            Id = 'cards'
            Type = 'Accordion'
            Options = @{
                SingleExpanded = $false
                Spacing = 1
            }
            Children = @($cards)
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$null = $layout | Invoke-Tui -Theme $Theme
