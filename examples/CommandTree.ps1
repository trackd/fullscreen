<#
.SYNOPSIS
Explore shared commands, lazy tree expansion and linked chart ranges.
.DESCRIPTION
Uses fictional data. Expanding a service requests its children once through the
Expand callback. Menu, command bar, palette and shortcuts share named commands.
Shift-drag either chart to select the same interval in both. Callbacks run on
the UI thread; use a background job for slow remote child discovery.
.PARAMETER Theme
Fullscreen palette name or custom palette.
.PARAMETER DefinitionOnly
Return the layout without opening the terminal or loading tree children.
.EXAMPLE
./CommandTree.ps1
.EXAMPLE
./CommandTree.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A layout with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. Ctrl+P opens the palette. Ctrl+S saves the fictional
draft while the editor is focused and has unsaved changes. F12 shows diagnostics.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$DefinitionOnly
)

$layout = New-TuiDefinition -Title 'Shared actions and lazy data' -State @{
    services = @(
        @{
            Name = 'Example API'
            HasChildren = $true
        }
        @{
            Name = 'Example worker'
            HasChildren = $true
        }
    )
    draft = 'Edit this fictional configuration'
    dirty = $false
    paletteOpen = $false
    window = $null
    latency = @(10, 20, 15, 60, 40, 25, 12, 18)
    status = 'Expand a service; children are requested only once.'
} -Command @(
    @{
        Id = 'save'
        Label = 'Save draft'
        Gesture = 'Ctrl+S'
        Scope = 'editor'
        EnabledBind = 'dirty'
    }
    @{
        Id = 'paletteAction'
        Label = 'Commands'
        Gesture = 'Ctrl+P'
    }
    @{
        Id = 'clearRange'
        Label = 'Clear range'
        Gesture = 'Ctrl+Shift+R'
    }
) -Root @{
    Id = 'root'
    Type = 'VStack'
    VerticalAlignment = 'Stretch'
    Children = @(
        @{
            Id = 'menu'
            Type = 'MenuBar'
            CommandIds = @('save', 'paletteAction', 'clearRange')
        }
        @{
            Id = 'actions'
            Type = 'CommandBar'
        }
        @{
            Id = 'tree'
            Type = 'TreeView'
            Bind = 'services'
            Lazy = $true
            Height = 7
        }
        @{
            Id = 'editor'
            Type = 'TextBox'
            Bind = 'draft'
        }
        @{
            Id = 'chart1'
            Type = 'Chart'
            Bind = 'latency'
            GraphKind = 'Line'
            RangeBind = 'window'
            Height = 7
        }
        @{
            Id = 'chart2'
            Type = 'Chart'
            Bind = 'latency'
            GraphKind = 'Bars'
            RangeBind = 'window'
            Height = 7
        }
        @{
            Id = 'status'
            Type = 'Text'
            Bind = 'status'
        }
        @{
            Id = 'palette'
            Type = 'CommandPalette'
            Bind = 'paletteOpen'
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$null = $layout | Invoke-Tui -Theme $Theme -OnEvent {
    param($tuiEvent, $state)
    switch ($tuiEvent.Kind) {
        'Expand' {
            if ($tuiEvent.Id -eq 'tree') {
                $state.status = 'Loaded ' + $tuiEvent.Value.Name
                return @{
                    Value = @(
                        @{
                            Name = 'Status: healthy'
                            HasChildren = $false
                        }
                        @{
                            Name = 'Region: Example North'
                            HasChildren = $false
                        }
                    )
                }
            }
        }
        'Change' {
            if ($tuiEvent.Id -eq 'editor') {
                $state.dirty = $true
            }
        }
        'Command' {
            switch ($tuiEvent.Id) {
                'save' {
                    $state.dirty = $false
                    $state.status = 'Fictional draft saved in memory.'
                }
                'paletteAction' {
                    $state.paletteOpen = $true
                }
                'clearRange' {
                    $state.window = $null
                }
            }
        }
        'Range' {
            $state.status = if ($null -eq $tuiEvent.Value) {
                'Range cleared'
            }
            else {
                'Selected {0:N1} to {1:N1}' -f $tuiEvent.Value.MinimumX, $tuiEvent.Value.MaximumX
            }
        }
    }
}
