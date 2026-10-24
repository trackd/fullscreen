<#
.SYNOPSIS
Browse local command help without downloading or evaluating commands.
.DESCRIPTION
Read installed command help on selection; no download or command execution is performed.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.EXAMPLE
./CommandHelp.ps1 -Theme 'Light Modern'
.EXAMPLE
./CommandHelp.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./CommandHelp.ps1 -Minimal
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
$layout = New-TuiDefinition -Title 'Command help' -State @{
    command = 'Get-Process'
    help = 'Choose a command.'
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'command'
            Type = 'Select'
            Bind = 'command'
            Items = @(
                'Get-Process',
                'Get-ChildItem',
                'Get-Item',
                'Get-Command'
            )
        }
        @{
            Id = 'help'
            Type = 'Code'
            Bind = 'help'
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
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Kind -ne 'Start' -and $tuiEvent.Id -ne 'command') {
            return
        }
        $command = Get-Command -Name $state.command -ErrorAction Stop
        $help = Get-Help -Name $command.Name -Full
        $description = $help.Description.Text -join "`n`n"
        @{
            State = @{
                help = "# $($command.Name)`n`n$($help.Synopsis)`n`n$description"
            }
        }
    }
