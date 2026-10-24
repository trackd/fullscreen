<#
.SYNOPSIS
Edit settings in tabs and return a validated native configuration object.
.DESCRIPTION
Edit bound settings in tabs, validate the name and return an object without writing configuration files.
.PARAMETER Theme
Built-in palette name or custom fullscreen palette.
.PARAMETER Minimal
Hide surrounding application chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return portable layout data without opening a terminal or starting work.
.EXAMPLE
$settings = ./SettingsTabs.ps1
.EXAMPLE
./SettingsTabs.ps1 -DefinitionOnly | Export-TuiDefinition -Path ./settings.tui.json
.EXAMPLE
./SettingsTabs.ps1 -Minimal
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
$layout = New-TuiDefinition -Title 'Settings' -State @{
    name = 'My tool'
    mode = 'ReadOnly'
    tracing = $false
    feedback = 'Edit settings and press Apply.'
} -Root @{
    Id = 'tabs'
    Type = 'Tabs'
    Children = @(
        @{
            Id = 'General'
            Type = 'VStack'
            TabTitle = 'General settings'
            Spacing = 1
            Children = @(
                @{
                    Id = 'nameLabel'
                    Type = 'Text'
                    Text = 'Application name'
                }
                @{
                    Id = 'name'
                    Type = 'TextBox'
                    Bind = 'name'
                }
                @{
                    Id = 'mode'
                    Type = 'Select'
                    Bind = 'mode'
                    Items = @(
                        'ReadOnly',
                        'Interactive',
                        'Review'
                    )
                }
                @{
                    Id = 'tracing'
                    Type = 'CheckBox'
                    Bind = 'tracing'
                    Text = 'Enable diagnostic tracing'
                }
                @{
                    Id = 'feedback'
                    Type = 'Text'
                    Bind = 'feedback'
                }
                @{
                    Id = 'apply'
                    Type = 'Button'
                    Text = 'Apply'
                }
            )
        }
        @{
            Id = 'About'
            Type = 'Code'
            Text = '# Settings example

Tab switches focus. F10 exits; Escape cancels the focused control or closes the app. Apply returns a native object; this example writes no configuration files.'
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
        if ($tuiEvent.Id -ne 'apply' -or $tuiEvent.Kind -ne 'Click') {
            return
        }
        if ([string]::IsNullOrWhiteSpace($state.name)) {
            return @{
                State = @{
                    feedback = 'Application name is required.'
                }
            }
        }
        @{
            Close = $true
            Value = [pscustomobject]@{
                Name = $state.name
                Mode = $state.mode
                Tracing = [bool]$state.tracing
            }
        }
    }
if (-not $result.Cancelled) {
    $result.Value
}
