<#
.SYNOPSIS
Wire a small TUI with a map of named event handlers.
.DESCRIPTION
Demonstrates shared state, a button callback and a successful result.
.PARAMETER Theme
fullscreen theme name or palette.
.PARAMETER DefinitionOnly
Return editable layout data without opening the TUI.
.PARAMETER Minimal
Hide application chrome.
.EXAMPLE
./HandlerMap.ps1
.EXAMPLE
./HandlerMap.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
Layout data with DefinitionOnly; otherwise the accepted name.
.NOTES
Requires fullscreen. Handlers run on the invoking PowerShell thread.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [object]$Theme = 'Default',

    [Parameter()]
    [switch]$DefinitionOnly,

    [Parameter()]
    [switch]$Minimal
)

$layout = @{
    Title = 'A small custom TUI'
    State = @{
        name = 'World'
        greeting = 'Enter your name, then choose Greet.'
    }
    Root = @{
        Id = 'root'
        Type = 'VStack'
        Spacing = 1
        Children = @(
            @{
                Id = 'name'
                Type = 'TextBox'
                Bind = 'name'
            }
            @{
                Id = 'greet'
                Type = 'Button'
                Text = 'Greet'
            }
            @{
                Id = 'message'
                Type = 'Text'
                Bind = 'greeting'
            }
            @{
                Id = 'accept'
                Type = 'Button'
                Text = 'Accept'
            }
        )
    }
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
if (-not (Get-Module fullscreen)) {
    Import-Module fullscreen -ErrorAction Stop
}
$handler = @{
    'greet:Click' = {
        param(
            $tuiEvent,
            $state
        )
        $state.greeting = "Hello, $($state.name)!"
    }
    'accept:Click' = {
        param(
            $tuiEvent,
            $state
        )
        return @{
            Close = $true
            Value = $state.name
        }
    }
}
$result = $layout |
    Invoke-Tui -Theme $Theme -Handler $handler -Minimal:$Minimal.IsPresent
if (-not $result.Cancelled) {
    $result.Value
}
