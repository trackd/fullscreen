<#
.SYNOPSIS
Start a styled custom TUI with editable event and chart callbacks.
.DESCRIPTION
Return the definition to the designer or run a small form with simulated graph data.
.PARAMETER Theme
fullscreen theme name or custom palette.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionsOnly
Return the layout without running the application.
.EXAMPLE
./DesignerStarter.ps1 -DefinitionsOnly | Show-TuiDesigner
.EXAMPLE
./DesignerStarter.ps1
.OUTPUTS
The layout with DefinitionsOnly; otherwise the accepted name. DefinitionOnly remains an alias.
.NOTES
Import fullscreen first. Samples are simulated and require no external source.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [Alias('DefinitionOnly')]
    [switch]$DefinitionsOnly
)
$ErrorActionPreference = 'Stop'
# Layout contains only data. IDs identify events; Bind names identify state values.
$layout = New-TuiDefinition -Title 'My first TUI' -State @{
    name = 'World'
    message = 'Ready'
    load = 50
    phase = 0
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'heading'
            Type = 'Text'
            Text = 'Your own tool starts here'
            Foreground = '#58D5E6'
            Bold = $true
        }
        @{
            Id = 'name'
            Type = 'TextBox'
            Bind = 'name'
        }
        @{
            Id = 'message'
            Type = 'Text'
            Bind = 'message'
        }
        @{
            Id = 'chart'
            Type = 'Chart'
            Bind = 'load'
            Text = 'Simulated load %'
            GraphKind = 'Area'
            Maximum = 100
            Height = 8
        }
        @{
            Id = 'buttons'
            Type = 'HStack'
            Spacing = 1
            Children = @(
                @{
                    Id = 'greet'
                    Type = 'Button'
                    Text = 'Greet'
                }
                @{
                    Id = 'accept'
                    Type = 'Button'
                    Text = 'Accept'
                }
            )
        }
    )
}
if ($DefinitionsOnly.IsPresent) {
    return $layout
}
# Callbacks run on the UI thread. Keep them short; assign command output to variables.
$onEvent = {
    param(
        $tuiEvent,
        $state
    )
    switch ("$($tuiEvent.Id):$($tuiEvent.Kind)") {
        'greet:Click' {
            $state.message = "Hello, $($state.name)!"
        }
        'accept:Click' {
            return @{
                Close = $true
                Value = $state.name
            }
        }
        'name:Change' {
            $state.message = 'Click Greet to update the message'
        }
    }
}
$onTick = {
    param(
        $tuiEvent,
        $state
    )
    # Replace this simulation with your measurement or a background-job result.
    $state.phase += 0.1
    $state.load = 50 + 35 * [Math]::Sin($state.phase)
}
$options = @{
    OnEvent = $onEvent
    OnTick = $onTick
    TickInterval = 250
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
$result = $layout | Invoke-Tui @options
if (-not $result.Cancelled) {
    $result.Value
}
