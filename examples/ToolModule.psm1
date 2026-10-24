function Select-TuiItem {
    <#
    .SYNOPSIS
    Select an original PowerShell object using a reusable custom TUI.
    .DESCRIPTION
    Demonstrates a module built on fullscreen's native definition, state and result contracts.
    Display labels are separate from the objects retained in runtime state. Escape emits no result.
    .PARAMETER InputObject
    Objects to choose from. Accepts pipeline input or an explicit array, up to 1000 objects.
    .PARAMETER Property
    Property to use for display labels. Defaults to Name.
    .PARAMETER Theme
    Built-in theme name or native custom palette.
    .PARAMETER DefinitionOnly
    Return the layout for validation or editing instead of opening the terminal.
    .EXAMPLE
    Get-Process | Select-TuiItem
    .EXAMPLE
    Get-Command -Module fullscreen | Select-TuiItem -Theme Monokai
    .EXAMPLE
    Get-Process | Select-TuiItem -DefinitionOnly | Show-TuiDesigner
    .OUTPUTS
    Original selected object, or a native definition with DefinitionOnly.
    .NOTES
    Import fullscreen and this example module before calling the function.
    #>
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline,
            Position = 0)]
        [AllowNull()]
        [psobject[]]$InputObject,
        [ValidateNotNullOrEmpty()]
        [string]$Property = 'Name',
        [object]$Theme = 'Default',
        [switch]$DefinitionOnly
    )
    begin {
        $items = [System.Collections.Generic.List[object]]::new()
    }
    process {
        foreach ($item in $InputObject) {
            if ($null -eq $item) {
                continue
            }
            if ($items.Count -ge 1000) {
                $PSCmdlet.ThrowTerminatingError([System.Management.Automation.ErrorRecord]::new([ArgumentException]::new('Choose at most 1000 items.'),
                        'TuiItemLimit',
                        [System.Management.Automation.ErrorCategory]::LimitsExceeded,
                        $item))
            }
            $items.Add($item)
        }
    }
    end {
        if ($items.Count -eq 0) {
            return
        }
        $labels = for ($index = 0; $index -lt $items.Count; $index++) {
            '{0}: {1}' -f ($index + 1),
            $items[$index].$Property
        }
        $layout = New-TuiDefinition -Title 'Choose an item' -State @{
            selected = $labels[0]
        } -Root @{
            Id = 'root'
            Type = 'VStack'
            Spacing = 1
            Children = @(
                @{
                    Id = 'selection'
                    Type = 'Select'
                    Bind = 'selected'
                    Items = @(
                        $labels
                    )
                }
                @{
                    Id = 'choose'
                    Type = 'Button'
                    Text = 'Choose'
                }
            )
        }
        if ($DefinitionOnly.IsPresent) {
            return $layout
        }
        $result = $layout |
            Invoke-Tui -Theme $Theme -State @{
                items = $items.ToArray()
                labels = @(
                    $labels
                )
            } -OnEvent {
                param(
                    $tuiEvent,
                    $state
                )
                if ($tuiEvent.Id -eq 'choose' -and $tuiEvent.Kind -eq 'Click') {
                    $index = [array]::IndexOf($state.labels,
                        $state.selected)
                    if ($index -ge 0) {
                        @{
                            Close = $true
                            Value = $state.items[$index]
                        }
                    }
                }
            }
        if (-not $result.Cancelled) {
            $PSCmdlet.WriteObject($result.Value,
                $false)
        }
    }
}
Export-ModuleMember -Function Select-TuiItem
