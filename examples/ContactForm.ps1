<#
.SYNOPSIS
Build a native form with validation and a structured result.
.DESCRIPTION
Bind name and email fields, display validation feedback and return an accepted native object.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.EXAMPLE
./ContactForm.ps1 -Theme 'Dark Modern'
.EXAMPLE
./ContactForm.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./ContactForm.ps1 -Minimal
.OUTPUTS
A definition with DefinitionOnly; otherwise the accepted native configuration object.
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
$layout = New-TuiDefinition -Title 'Contact details' -State @{
    name = ''
    email = ''
    feedback = 'Enter your details, then Submit.'
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Code'
            Text = '# Contact details'
        }
        @{
            Id = 'nameLabel'
            Type = 'Text'
            Text = 'Name'
        }
        @{
            Id = 'name'
            Type = 'TextBox'
            Bind = 'name'
        }
        @{
            Id = 'emailLabel'
            Type = 'Text'
            Text = 'Email'
        }
        @{
            Id = 'email'
            Type = 'TextBox'
            Bind = 'email'
        }
        @{
            Id = 'feedback'
            Type = 'Text'
            Bind = 'feedback'
        }
        @{
            Id = 'submit'
            Type = 'Button'
            Text = 'Submit'
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$result = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Id -ne 'submit' -or $tuiEvent.Kind -ne 'Click') {
            return
        }
        if ([string]::IsNullOrWhiteSpace($state.name) -or $state.email -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') {
            return @{
                State = @{
                    feedback = 'Enter a name and a valid email address.'
                }
            }
        }
        @{
            Close = $true
            Value = [pscustomobject]@{
                Name = $state.name
                Email = $state.email
            }
        }
    }
if (-not $result.Cancelled) {
    $result.Value
}
