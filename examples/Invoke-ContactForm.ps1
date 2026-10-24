<#
.SYNOPSIS
Demonstrate a custom fullscreen form with PowerShell handlers.
.DESCRIPTION
Defines the layout in PowerShell and handles named events in the invoking runspace.
This sample returns request data; it does not deploy or send notifications.
.PARAMETER Theme
Built-in fullscreen theme name. Theme files are not supported.
.PARAMETER DefinitionOnly
Return the layout without opening a terminal.
.EXAMPLE
./examples/Invoke-ContactForm.ps1 -Theme 'Light Modern'
.EXAMPLE
./examples/Invoke-ContactForm.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
System.Management.Automation.PSCustomObject
.NOTES
Import the built fullscreen module before running the sample.
#>
[CmdletBinding()]
param(
    [string]$Theme = 'Default',
    [switch]$DefinitionOnly
)

$layout = New-TuiDefinition -Title 'Deployment request' -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Code'
            Text = "# Request a deployment`nChoose an environment and enter a name."
        }
        @{
            Id = 'name'
            Type = 'TextBox'
            Bind = 'name'
            Text = 'My application'
            Width = 40
        }
        @{
            Id = 'environment'
            Type = 'Select'
            Bind = 'environment'
            Items = @(
                'Development'
                'Staging'
                'Production'
            )
            Width = 30
        }
        @{
            Id = 'notify'
            Type = 'CheckBox'
            Bind = 'notify'
            Text = 'Notify the team'
            Checked = $true
        }
        @{
            Id = 'message'
            Type = 'Text'
            Bind = 'message'
            Text = 'No request submitted.'
        }
        @{
            Id = 'buttons'
            Type = 'HStack'
            Spacing = 2
            Children = @(
                @{
                    Id = 'preview'
                    Type = 'Button'
                    Text = 'Preview request'
                }
                @{
                    Id = 'submit'
                    Type = 'Button'
                    Text = 'Accept'
                    Close = $true
                }
            )
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$layout | fullscreen\Invoke-Tui -Theme $Theme -OnEvent {
    param(
        $tuiEvent,
        $state
    )
    switch ($tuiEvent.Id) {
        'preview' {
            @{
                State = @{
                    message = "$($state.name) → $($state.environment)"
                }
            }
        }
        'submit' {
            @{
                Close = $true
                Value = [pscustomobject]@{
                    Name = $state.name
                    Environment = $state.environment
                    Notify = $state.notify
                }
            }
        }
    }
}
