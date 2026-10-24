<#
.SYNOPSIS
Explore native XenoAtom widgets without additional modules.
.DESCRIPTION
Shows working native controls with populated data. Use Name to narrow the gallery,
or All for every native widget except CodeEditor. Options holds native properties;
inspect (Get-TuiControl Name).NativeProperties for names, types and enum choices.
.PARAMETER Name
Widget names or wildcard patterns to display.
.PARAMETER All
Include every native widget except CodeEditor.
.PARAMETER Theme
fullscreen palette name or custom palette.
.PARAMETER DefinitionOnly
Return the data-only definition without opening a terminal.
.EXAMPLE
./NativeControls.ps1
.EXAMPLE
./NativeControls.ps1 -Name @(
    'Grid'
    'HSplitter'
    'NumberBox'
    'TreeView'
) -Theme Monokai
.EXAMPLE
./NativeControls.ps1 -All -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A TUI definition with DefinitionOnly; otherwise the Invoke-Tui result.
.NOTES
Import fullscreen first. Use 130 by 40 cells. Each widget has its own tab.
F12 opens diagnostics. The gallery does not install or import optional modules.
#>
[CmdletBinding()]
param(
    [string[]]$Name = @(
        'Grid',
        'HSplitter',
        'TreeView',
        'DataGridControl',
        'Slider',
        'LogControl',
        'Dialog'
    ),
    [switch]$All,
    [object]$Theme = 'Dark Modern',
    [switch]$DefinitionOnly
)

$catalog = @(
    Get-TuiControl |
        Where-Object {
            $_.NativeProperties.Count -gt 0
        }
)
$selected = @(
    $catalog |
        Where-Object {
            $controlName = $_.Name
            $All.IsPresent -or @(
                $Name |
                    Where-Object {
                        $controlName -like $_
                    }
                ).Count -gt 0
            }
)
# Search popups refer to the same LogControl used by the log page.
if ($selected.Name -contains 'SearchReplacePopup' -and $selected.Name -notcontains 'LogControl') {
    $selected += $catalog |
        Where-Object Name -eq 'LogControl'
}
$state = @{
    native_records = @(
        @{
            Label = 'API'
            Value = 65
        },
        @{
            Label = 'Worker'
            Value = 35
        }
    )
    native_series = @(
        12,
        28,
        19,
        43,
        32,
        56,
        48,
        67
    )
    native_lines = @(
        'Build started',
        'Compiled 12 projects',
        'Tests passed',
        'Ready to deploy'
    )
    native_tree = @(
        @{
            Name = 'Production'
            Children = @(
                @{
                    Name = 'API'
                    Children = @(
                        @{
                            Name = 'replica-1'
                        },
                        @{
                            Name = 'replica-2'
                        }
                    )
                },
                @{
                    Name = 'Worker'
                }
            )
        }
    )
}
$pages = foreach ($control in $selected) {
    $id = 'native_' + $control.Name.ToLowerInvariant()
    $node = @{
        Id = $id
        Type = $control.Name
        Text = $control.Name + ' example'
    }
    switch ($control.Name) {
        {
            $_ -in 'NumberBox',
            'Slider'
        } {
            $node.Bind = $id + '_value'
            $state[$node.Bind] = 25
            if ($_ -eq 'Slider') {
                $node.Options = @{
                    Minimum = 0
                    Maximum = 100
                    Step = 5
                    ShowValueLabel = $true
                }
            }
        }
        {
            $_ -in 'ListBox',
            'OptionList',
            'RadioButtonList',
            'SelectionList',
            'Select'
        } {
            $node.Items = @(
                'Development',
                'Staging',
                'Production'
            )
            $node.Height = 6
            if ($_ -ne 'Select') {
                $node.SelectionBind = $id + '_selection'
            }
        }
        {
            $_ -in 'LineChart',
            'Sparkline'
        } {
            $node.Bind = 'native_series'
            $node.Height = 8
        }
        {
            $_ -in 'BarChart',
            'BreakdownChart',
            'ProgressTaskGroup'
        } {
            $node.Bind = 'native_records'
            $node.Height = 8
        }
        {
            $_ -in 'Table',
            'DataGridControl'
        } {
            $node.Bind = 'native_records'
            $node.Columns = @(
                'Label',
                'Value'
            )
            $node.Height = 10
        }
        {
            $_ -in 'LogControl',
            'DocumentFlow'
        } {
            $node.Bind = 'native_lines'
            $node.Height = 10
        }
        'TreeView' {
            $node.Bind = 'native_tree'
            $node.Height = 12
        }
        'ColorPicker' {
            $node.Bind = $id + '_value'
            $state[$node.Bind] = '#4DA6FF'
            $node.Height = 15
        }
        'EnumSelect' {
            $node.EnumType = 'DayOfWeek'
            $node.Bind = $id + '_value'
            $state[$node.Bind] = 'Monday'
        }
        'MaskedInput' {
            $node.Text = '5551234567'
            $node.Options = @{
                Template = '(999) 999-9999'
            }
        }
        'Link' {
            $node.Text = 'fullscreen on GitHub'
            $node.Options = @{
                Uri = 'https://github.com/trackd/fullscreen'
            }
        }
        'Markup' {
            $node.Text = '[bold cyan]Native markup[/] with [green]success[/] and [yellow]warning[/]'
        }
        'Paragraph' {
            $node.Text = 'A wrapping paragraph uses the available width. Resize the terminal to see the native layout respond.'
            $node.Options = @{
                Wrap = $true
            }
        }
        'PromptEditor' {
            $node.Text = 'Get-Process | Sort-Object CPU -Descending'
            $node.Height = 4
        }
        'Canvas' {
            $node.Text = "Deployment progress`nAPI       ########## 100%`nWorker    #######---  70%"
            $node.Height = 5
        }
        'TextFiglet' {
            $node.Text = 'TUI'
            $node.Height = 8
        }
        {
            $_ -in 'ProgressBar',
            'Progress'
        } {
            $node.Bind = $id + '_value'
            $state[$node.Bind] = 0.65
        }
        {
            $_ -in 'HScrollBar',
            'VScrollBar'
        } {
            $node.Options = @{
                Minimum = 0
                Maximum = 100
                Value = 25
                ViewportSize = 20
            }
            if ($_ -eq 'VScrollBar') {
                $node.Height = 10
            }
        }
        {
            $_ -in 'Switch',
            'CheckBox',
            'RadioButton'
        } {
            $node.Bind = $id + '_value'
            $state[$node.Bind] = $true
        }
        {
            $_ -in 'MenuBar',
            'CommandBar',
            'CommandPalette'
        } {
            $node.Items = @(
                'Build',
                'Test',
                'Deploy'
            )
        }
        'Grid' {
            $node.Options = @{
                Rows = @(
                    'Auto',
                    '*'
                )
                Columns = @(
                    '2*',
                    '*'
                )
                RowGap = 1
                ColumnGap = 2
            }
            $node.Children = @(
                @{
                    Id = $id + '_first'
                    Type = 'GridCell'
                    Options = @{
                        Row = 0
                        Column = 0
                    }
                    Children = @(
                        @{
                            Id = $id + '_text1'
                            Type = 'TextBlock'
                            Text = 'Wide column'
                        }
                    )
                }
                @{
                    Id = $id + '_second'
                    Type = 'GridCell'
                    Options = @{
                        Row = 1
                        Column = 1
                    }
                    Children = @(
                        @{
                            Id = $id + '_text2'
                            Type = 'TextBlock'
                            Text = 'Second row, second column'
                        }
                    )
                }
            )
        }
        'Accordion' {
            $node.Children = foreach ($index in 1..2) {
                @{
                    Id = $id + '_' + $index
                    Type = 'Collapsible'
                    Text = 'Section ' + $index
                    Options = @{
                        IsExpanded = $index -eq 1
                    }
                    Children = @(
                        @{
                            Id = $id + '_body' + $index
                            Type = 'TextBlock'
                            Text = 'Details for section ' + $index
                        }
                    )
                }
            }
        }
        {
            $_ -in 'Header',
            'Footer',
            'DockLayout'
        } {
            $node.Children = foreach ($index in 1..3) {
                @{
                    Id = $id + '_' + $index
                    Type = 'TextBlock'
                    Text = 'Region ' + $index
                }
            }
        }
        {
            $_ -in 'HSplitter',
            'VSplitter',
            'ContentSwitcher',
            'TabControl',
            'HStack',
            'VStack',
            'WrapHStack',
            'WrapVStack',
            'ZStack'
        } {
            $node.Children = foreach ($index in 1..2) {
                @{
                    Id = $id + '_' + $index
                    Type = 'Border'
                    TabTitle = 'Pane ' + $index
                    Children = @(
                        @{
                            Id = $id + '_text' + $index
                            Type = 'TextBlock'
                            Text = 'Pane ' + $index
                        }
                    )
                }
            }
            $node.Height = 10
        }
        default {
            if ($control.Properties -contains 'Children') {
                $node.Children = @(
                    @{
                        Id = $id + '_body'
                        Type = 'TextBlock'
                        Text = 'Content inside ' + $control.Name
                    }
                )
            }
        }
    }
    $children = @(
        @{
            Id = $id + '_description'
            Type = 'Text'
            Text = $control.Description
        },
        $node
    )
    if ($control.Name -in 'Dialog',
        'Popup',
        'CommandPalette',
        'Toast',
        'SearchReplacePopup') {
        $node.Bind = $id + '_open'
        $state[$node.Bind] = $false
        if ($control.Name -eq 'SearchReplacePopup') {
            $node.Target = 'native_logcontrol'
        }
        $children += @{
            Id = $id + '_show'
            Type = 'Button'
            Text = 'Open ' + $control.Name
        }
    }
    @{
        Id = $id + '_page'
        Type = 'VStack'
        TabTitle = $control.Name
        Spacing = 1
        Children = $children
    }
}
if ($pages.Count -eq 0) {
    throw 'No matching native widgets. Use Get-TuiControl to inspect names.'
}
$layout = New-TuiDefinition -Title 'Native widgets' -State $state -Root @{
    Id = 'native_pages'
    Type = 'Tabs'
    Children = @(
        $pages
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}

$layout |
    Invoke-Tui -Theme $Theme -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Kind -eq 'Click' -and $tuiEvent.Id.EndsWith('_show')) {
            $state[$tuiEvent.Id.Substring(0,
                $tuiEvent.Id.Length - 5) + '_open'] = $true
        }
    }
