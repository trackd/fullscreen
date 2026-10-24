<#
.SYNOPSIS
Demonstrate numeric chart collections with simulated metrics and state bindings.
.DESCRIPTION
Select a plot style for both bound charts. Sixel is selected automatically when
supported; other terminals use XenoAtom graphs. Samples are simulated locally.
.PARAMETER GraphKind
Plot style for both charts. Histogram and BoxPlot summarize the sampled values.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return the layout without opening a terminal or running callbacks.
.EXAMPLE
./LiveChart.ps1 -Theme 'Dark Modern'
.EXAMPLE
./LiveChart.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./LiveChart.ps1 -GraphKind Step
.OUTPUTS
A TUI definition when DefinitionOnly is supplied; otherwise no output.
.NOTES
Import fullscreen before running this example. No additional dependencies.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [ValidateSet('Line',
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
        'ErrorBars')]
    [string]$GraphKind = 'Area'
)
$ErrorActionPreference = 'Stop'
$layout = New-TuiDefinition -Title 'Live charts · simulated metrics' -State @{
    phase = 0
    load = @(50.0)
    queue = @(10.0)
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'intro'
            Type = 'Text'
            Text = 'Simulated metrics · replace OnTick with your own data source'
        }
        @{
            Id = 'load'
            Type = 'Chart'
            Text = 'Load %'
            Bind = 'load'
            GraphKind = $GraphKind
            Maximum = 100
            Height = 10
        }
        @{
            Id = 'queue'
            Type = 'Chart'
            Text = 'Queue depth · automatic scale'
            Bind = 'queue'
            GraphKind = $GraphKind
            Height = 8
        }
        @{
            Id = 'close'
            Type = 'Button'
            Text = 'Close'
            Close = $true
        }
    )
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
        $phase = $state.phase + 0.15
        @{
            State = @{
                phase = $phase
                load = @($state.load | Select-Object -Last 119) + (50.0 + 40.0 * [math]::Sin($phase))
                queue = @($state.queue | Select-Object -Last 119) + (10.0 + 8.0 * [math]::Sin($phase * 0.4))
            }
        }
    }
