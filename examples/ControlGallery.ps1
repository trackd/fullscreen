<#
.SYNOPSIS
Explore every control exposed by Get-TuiControl in a small tabbed application.
.DESCRIPTION
Shows forms, styling, shared state, table selection, code, charts, logs and embedded
tools. Demo metrics and rows are simulated. File and monitor workers run only while
their page is displayed. Keep definitions data-only and behavior in the handler map.
.PARAMETER Path
Starting directory for the file browser. Defaults to the current location.
.PARAMETER ImagePath
Optional image path. Pixel images require the optional Sixel module and a capable terminal.
.PARAMETER Renderable
Optional Spectre IRenderable from an already loaded module, such as TextMate.
.PARAMETER Theme
fullscreen palette name or custom palette.
.PARAMETER Minimal
Hide surrounding chrome; F4 toggles it at runtime.
.PARAMETER DefinitionOnly
Return the layout without starting workers or opening a terminal.
.EXAMPLE
./ControlGallery.ps1
.EXAMPLE
./ControlGallery.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./ControlGallery.ps1 -Path $HOME -ImagePath ./logo.png -Theme 'Dark Modern'
.OUTPUTS
A definition with DefinitionOnly; otherwise the accepted form or original selected row.
.NOTES
Import fullscreen first. Use 120 by 40 cells; F12 shows diagnostics, F10 exits.
Optional image/Spectre content can be omitted. Every catalog control still has a page.
#>
[CmdletBinding()]
param(
    [string]$Path = $PWD.Path,
    [string]$ImagePath = '',
    [object]$Renderable,
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'

# Demonstrates fullscreen controls; NativeControls.ps1 covers native widgets. TabTitle is independent of ID and Text.
$layout = New-TuiDefinition -Title 'Control gallery' -State @{
    name = 'World'
    notes = 'Tab leaves this multiline field.'
    enabled = $true
    environment = 'Development'
    progress = 0.35
    phase = 0
    load = 45.0
    message = 'Edit the form, then Greet or Accept.'
    details = '# Select an inventory row'
    rows = @(
        @{
            Name = 'gateway'
            Environment = 'Production'
            Instances = 3
        }
        @{
            Name = 'catalog'
            Environment = 'Development'
            Instances = 1
        }
    )
    lines = @(
        'Information: demonstration log',
        'Warning: simulated warning, no service contacted'
    )
} -Root @{
    Id = 'pages'
    Type = 'Tabs'
    Children = @(
        @{
            Id = 'form'
            Type = 'Border'
            TabTitle = 'Form'
            Text = 'Bindings and events'
            Children = @(
                @{
                    Id = 'fields'
                    Type = 'VStack'
                    Spacing = 1
                    Children = @(
                        @{
                            Id = 'intro'
                            Type = 'Text'
                            Text = 'Inputs update shared state before callbacks'
                            Foreground = '#58D5E6'
                            Bold = $true
                        }
                        @{
                            Id = 'name'
                            Type = 'TextBox'
                            Bind = 'name'
                            Width = 40
                        }
                        @{
                            Id = 'notes'
                            Type = 'TextArea'
                            Bind = 'notes'
                            Height = 3
                        }
                        @{
                            Id = 'enabled'
                            Type = 'CheckBox'
                            Bind = 'enabled'
                            Text = 'Enable greeting'
                        }
                        @{
                            Id = 'environment'
                            Type = 'Select'
                            Bind = 'environment'
                            Items = @(
                                'Development',
                                'Production'
                            )
                        }
                        @{
                            Id = 'progress'
                            Type = 'Progress'
                            Bind = 'progress'
                        }
                        @{
                            Id = 'rule'
                            Type = 'Rule'
                        }
                        @{
                            Id = 'message'
                            Type = 'Text'
                            Bind = 'message'
                        }
                        @{
                            Id = 'actions'
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
            )
        }
        @{
            Id = 'data'
            Type = 'VStack'
            TabTitle = 'Data'
            Spacing = 1
            Children = @(
                @{
                    Id = 'inventory'
                    Type = 'FTable'
                    Bind = 'rows'
                    SelectionBind = 'selected'
                    Columns = @(
                        'Name',
                        'Environment',
                        'Instances'
                    )
                    Height = 7
                }
                @{
                    Id = 'details'
                    Type = 'Code'
                    Bind = 'details'
                    Height = 4
                }
                @{
                    Id = 'trend'
                    Type = 'Chart'
                    Bind = 'load'
                    Text = 'Simulated utilization %'
                    Maximum = 100
                    Height = 7
                }
            )
        }
        @{
            Id = 'logs'
            Type = 'FLogView'
            TabTitle = 'Logs'
            Bind = 'lines'
            Height = 20
        }
        @{
            Id = 'files'
            Type = 'FileBrowser'
            TabTitle = 'Files'
            Path = $Path
            SelectionBind = 'file'
            Height = 22
        }
        @{
            Id = 'monitor'
            Type = 'PerformanceMonitor'
            TabTitle = 'Monitor'
            View = 'Dashboard'
            Height = 22
        }
        @{
            Id = 'users'
            Type = 'UserList'
            TabTitle = 'Users'
            SelectionBind = 'selectedUser'
            IncludeProcesses = $true
            Height = 30
        }
        @{
            Id = 'commander'
            Type = 'FileCommander'
            TabTitle = 'Commander'
            Path = $Path
            Height = 30
        }
        @{
            Id = 'explorer'
            Type = 'ObjectExplorer'
            TabTitle = 'Objects'
            Bind = 'rows'
            SelectionBind = 'inspected'
            Height = 30
        }
        @{
            Id = 'media'
            Type = 'VStack'
            TabTitle = 'Media'
            Spacing = 1
            Children = @(
                @{
                    Id = 'mediaHelp'
                    Type = 'Text'
                    Text = 'Optional: supply ImagePath or Renderable. Without them, these views are empty.'
                }
                @{
                    Id = 'image'
                    Type = 'Image'
                    Path = $ImagePath
                    Height = 8
                }
                @{
                    Id = 'spectre'
                    Type = 'Spectre'
                    Bind = 'renderable'
                    Height = 8
                }
            )
        }
    )
}
$nativeGallery = & (Join-Path $PSScriptRoot 'NativeControls.ps1') -All -DefinitionOnly
$layout.Root.Children += @(
    $nativeGallery.Root.Children
)
foreach ($key in $nativeGallery.State.Keys) {
    $layout.State[$key] = $nativeGallery.State[$key]
}
$layout = $layout |
    New-TuiDefinition
if ($DefinitionOnly.IsPresent) {
    return $layout
}

# Small handlers demonstrate state updates and returning the original selected object.
$handler = @{
    'greet:Click' = {
        param(
            $tuiEvent,
            $state
        )
        $state.message = if ($state.enabled) {
            "Hello $($state.name), from $($state.environment)!"
        }
        else {
            'Greeting disabled'
        }
    }
    'accept:Click' = {
        param(
            $tuiEvent,
            $state
        )
        @{
            Close = $true
            Value = [pscustomobject]@{
                Name = $state.name
                Notes = $state.notes
                Environment = $state.environment
                Enabled = $state.enabled
            }
        }
    }
    'inventory:Selection' = {
        param(
            $tuiEvent,
            $state
        )
        if ($null -ne $tuiEvent.Value) {
            $state.details = "# $($tuiEvent.Value.Name)`nEnvironment: $($tuiEvent.Value.Environment)"
        }
    }
    'inventory:Submit' = {
        param(
            $tuiEvent,
            $state
        )
        @{
            Close = $true
            Value = $tuiEvent.Value
        }
    }
}
$result = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State @{
        renderable = $Renderable
    } -Handler $handler -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Kind -eq 'Click' -and $tuiEvent.Id.EndsWith('_show')) {
            $state[$tuiEvent.Id.Substring(0,
                $tuiEvent.Id.Length - 5) + '_open'] = $true
        }
    } -OnTick {
        param(
            $tuiEvent,
            $state
        )
        $state.phase += 0.17
        $state.load = 45.0 + 25.0 * [Math]::Sin($state.phase)
    }
if (-not $result.Cancelled) {
    $result.Value
}
