<#
.SYNOPSIS
Keep callbacks short while native PowerShell thread jobs do work.
.DESCRIPTION
Poll a bounded thread job from OnTick and clean up only that owned job when the TUI closes.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.EXAMPLE
./BackgroundJob.ps1 -Theme 'Dark Modern'
.EXAMPLE
./BackgroundJob.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./BackgroundJob.ps1 -Minimal
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
$layout = New-TuiDefinition -Title 'Background work' -State @{
    progress = 0.0
    status = 'Press Start'
    output = ''
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Code'
            Text = '# Native PowerShell background job'
        }
        @{
            Id = 'start'
            Type = 'Button'
            Text = 'Start'
        }
        @{
            Id = 'progress'
            Type = 'Progress'
            Bind = 'progress'
        }
        @{
            Id = 'status'
            Type = 'Text'
            Bind = 'status'
        }
        @{
            Id = 'output'
            Type = 'TextArea'
            Bind = 'output'
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
$runtimeState = @{
    job = $null
}
try {
    $null = $layout |
        Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State $runtimeState -OnEvent {
            param(
                $tuiEvent,
                $state
            )
            if ($tuiEvent.Id -eq 'start' -and $tuiEvent.Kind -eq 'Click' -and $null -eq $state.job) {
                # Thread jobs run in-process; no child PowerShell profile or process is launched.
                $state.job = 1..20 |
                    ForEach-Object -Parallel {
                        Start-Sleep -Milliseconds 150
                        "Finished item $_"
                    } -ThrottleLimit 2 -AsJob
                    $runtimeState.job = $state.job
                    @{
                        State = @{
                            status = 'Working…'
                        }
                    }
                }
            } -TickInterval 100 -OnTick {
                param(
                    $tuiEvent,
                    $state
                )
                if ($null -eq $state.job) {
                    return
                }
                $lines = @(
                    Receive-Job -Job $state.job -Keep
                )
                @{
                    State = @{
                        output = $lines -join "`n"
                        progress = $lines.Count / 20.0
                        status = "$($state.job.State) · $($lines.Count)/20"
                    }
                }
            }
}
finally {
    # Dispose only the job owned by this invocation, including when Escape closes the UI.
    if ($null -ne $runtimeState.job) {
        if ($runtimeState.job.State -eq 'Running') {
            Stop-Job -Job $runtimeState.job
        }
        Remove-Job -Job $runtimeState.job -Force
    }
}
