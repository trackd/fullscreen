<#
.SYNOPSIS
Display native process data using bound text and progress controls.
.DESCRIPTION
Sample local processes once per second and show working-set totals. Summed working sets include shared pages.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.EXAMPLE
./ProcessDashboard.ps1 -Minimal
.EXAMPLE
./ProcessDashboard.ps1 -DefinitionOnly | Export-TuiDefinition -Path ./dashboard.tui.json
.EXAMPLE
./ProcessDashboard.ps1 -Minimal
.OUTPUTS
A definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. F12 toggles diagnostics; F10 exits the application.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$layout = New-TuiDefinition -Title 'Process dashboard' -State @{
    summary = 'Waiting for first sample'
    fraction = 0.0
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Code'
            Text = '# Process dashboard'
        }
        @{
            Id = 'summary'
            Type = 'Code'
            Bind = 'summary'
        }
        @{
            Id = 'label'
            Type = 'Text'
            Text = 'Largest process as a fraction of total process working sets'
        }
        @{
            Id = 'usage'
            Type = 'Progress'
            Bind = 'fraction'
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
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -TickInterval 1000 -OnTick {
        param(
            $tuiEvent,
            $state
        )
        $processes = @(
            Get-Process
        )
        $total = ($processes |
                Measure-Object -Property WorkingSet64 -Sum).Sum
            $largest = $processes |
                Sort-Object -Property WorkingSet64 -Descending |
                Select-Object -First 1
                @{
                    State = @{
                        summary = "**Processes:** $($processes.Count)`n`n**Working sets:** $([math]::Round($total / 1GB, 2)) GiB`n`n**Largest:** $($largest.ProcessName)`n`nUpdated: $((Get-Date).ToString('T'))"
                        fraction = if ($total -gt 0) {
                            $largest.WorkingSet64 / $total
                        }
                        else {
                            0.0
                        }
                    }
                }
            }
