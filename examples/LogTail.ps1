<#
.SYNOPSIS
Follow the last 25 lines of a file with a short periodic handler.
.DESCRIPTION
Shows inline sample JSON when no Path is supplied. With Path, reads the last 25 lines
once per second and displays read errors. Preview edits are never written back.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.PARAMETER Path
Optional literal log-file path. Omit it to display the inline sample data.
.EXAMPLE
./LogTail.ps1 -Path ./app.log -Theme Monokai
.EXAMPLE
./LogTail.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./LogTail.ps1 -Minimal
.OUTPUTS
A definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. F12 toggles diagnostics; F10 exits the application.
#>
[CmdletBinding()]
param(
    [string]$Path,
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$sample = @'
{"timestamp":"2026-10-24T09:00:00Z","level":"Information","message":"Example application started"}
{"timestamp":"2026-10-24T09:00:01Z","level":"Warning","message":"An example retry was requested"}
{"timestamp":"2026-10-24T09:00:02Z","level":"Information","message":"Example request completed"}
'@
$sourcePath = if ($Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}
$content = if ($sourcePath) {
    'Waiting for first read'
}
else {
    $sample
}
$status = if ($sourcePath) {
    'Waiting for first read. No file changes are made.'
}
else {
    'Inline demo data. Supply Path to follow a log file. No file changes are made.'
}
$layout = New-TuiDefinition -Title 'Log tail' -State @{
    content = $content
    status = $status
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Text'
            Text = 'Recent log entries'
        }
        @{
            Id = 'content'
            Type = 'TextArea'
            Bind = 'content'
            Height = 15
        }
        @{
            Id = 'status'
            Type = 'Text'
            Bind = 'status'
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
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State @{
        sourcePath = $sourcePath
    } -TickInterval 1000 -OnTick {
        param(
            $tuiEvent,
            $state
        )
        if (-not $state.sourcePath) {
            return
        }
        try {
            $lines = Get-Content -LiteralPath $state.sourcePath -Tail 25 -ErrorAction Stop
            @{
                State = @{
                    content = $lines -join "`n"
                    status = "Read $((Get-Date).ToString('T')) · edits in this preview are never saved"
                }
            }
        }
        catch {
            @{
                State = @{
                    status = $_.Exception.Message
                }
            }
        }
    }
