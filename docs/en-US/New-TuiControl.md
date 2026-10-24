---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-25-2026
PlatyPS schema version: 2024-05-01
title: New-TuiControl
---

# New-TuiControl

## SYNOPSIS

Create a validated native control definition for composition in custom TUIs.

## SYNTAX

### __AllParameterSets

```
New-TuiControl [-Type] <string> -Id <string> [-Text <string>] [-Bind <string>] [-Child <psobject[]>]
 [-Property <hashtable>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Builds an independent PSCustomObject containing a validated control node. Combine it with other controls using Child, or place it in New-TuiDefinition and Invoke-Tui. Property contains control-specific options listed by Get-TuiControl; it cannot override named base parameters. No XenoAtom types cross the private ALC boundary. Creating definitions does not perform file operations or start workers.

Common styling properties include Foreground and Background (#RRGGBB or empty for
the theme), Bold for text labels, HorizontalAlignment and VerticalAlignment
(Start, Center, End or Stretch). The optional Spectre control binds a runtime
IRenderable supplied through Invoke-Tui State; fullscreen does not bundle Spectre.

`examples/ControlGallery.ps1` covers every supported control in a small
tabbed application. Use its DefinitionOnly switch to edit the data in the designer.

## EXAMPLES

### Example 1: Add a live graph to a custom TUI

```powershell
$chart = New-TuiControl Chart -Id load -Text 'Load %' -Bind load -Property @{
    GraphKind = 'Step'; Minimum = 0; Maximum = 100; Height = 10
}
$layout = New-TuiDefinition -Title 'My chart' -Root $chart -State @{ load = @(50.0) }
$layout | Invoke-Tui -TickInterval 500 -OnTick {
    param($tuiEvent, $state)
    $state.load = @($state.load | Select-Object -Last 119) + (Get-Random -Minimum 0 -Maximum 101)
}
```

GraphKind accepts Line, Area, Bars, Step, Scatter, Stem, Histogram, BoxPlot,
HorizontalBars, StackedBars, Heatmap and ErrorBars.
Sixel is automatic on supported terminals, with terminal-cell fallback. Minimum
and Maximum are optional numeric bounds; omit them for automatic scaling. Null
or NaN entries leave gaps. Scalar bindings retain the timestamped-history adapter.
See `examples/GraphGallery.ps1` for all twelve styles in tabs. ErrorBars without
lower/upper values shows the measurements themselves; C# ChartSeries supports
LowerValues/UpperValues, and Out-Chart accepts LowerProperty/UpperProperty.

### Example 2: Compose a native object picker

The selected value remains the original PowerShell object.

```powershell
$table = New-TuiControl FTable -Id processes -Bind processes -Property @{
    Columns = @('ProcessName', 'Id'); SelectionBind = 'selected'; Height = 16
}
$layout = New-TuiDefinition -Title 'Process picker' -Root $table
$layout | Invoke-Tui -State @{ processes = @(Get-Process) } -OnEvent {
    param($tuiEvent, $state)
    if ($tuiEvent.Kind -eq 'Submit') { @{ Close = $true; Value = $tuiEvent.Value } }
}
```

### Example 3: Combine multiline input with a Code (Markdown) heading

Child accepts ordinary definitions and the builder returns an independent native object.

```powershell
$heading = New-TuiControl Code -Id heading -Text '# My workbench'
$editor = New-TuiControl TextArea -Id source -Bind source -Property @{ Height = 14 }
$stack = New-TuiControl VStack -Id root -Child $heading, $editor -Property @{ Spacing = 1 }
New-TuiDefinition -Title 'Workbench' -Root $stack -State @{ source = 'Get-Process' } | Invoke-Tui
```

### Example 4: Reuse the file browser and monitor in tabs

Workers start only when their component is displayed and stop when hidden or closed.

```powershell
$files = New-TuiControl FileBrowser -Id files -Property @{ Path = $PWD.Path; Height = 24 }
$monitor = New-TuiControl PerformanceMonitor -Id monitor -Property @{ View = 'Dashboard'; RefreshInterval = 1000; Height = 24 }
$tabs = New-TuiControl Tabs -Id root -Child $files, $monitor
New-TuiDefinition -Title 'Operations' -Root $tabs | Invoke-Tui
```

### Example 5: Standalone user sessions with optional process expansion

```powershell
$users = New-TuiControl UserList -Id users -Property @{
    IncludeProcesses = $true; SelectionBind = 'selectedUser'; Height = 30
}
New-TuiDefinition -Title 'User sessions' -Root $users | Invoke-Tui
```

Omit IncludeProcesses for session-only collection. L toggles process loading;
D opens all session details. Windows reports logon, idle, last-input, connection
times, client name and protocol where permitted. Linux/macOS use who -u; unavailable
or ambiguous values remain unknown. Process totals are per account, not per login.

FileCommander accepts Path and SecondaryPath for its two panes. ObjectExplorer
accepts Bind, Columns and SelectionBind for deep inspection of an object collection.

### Example 6: Configure a native slider

```powershell
$slider = New-TuiControl Slider -Id amount -Bind amount -Property @{
    Options = @{ Minimum = 0; Maximum = 100; Step = 5; ShowValueLabel = $true }
}
New-TuiDefinition -Root $slider -State @{ amount = 25 } | Invoke-Tui
(Get-TuiControl Slider).NativeProperties | Format-Table Name, Type, Choices
```

Native widgets retain XenoAtom names. Use FTable/FLogView for fullscreen's table
and parsed log view; Table/LogControl select the native versions. EnumSelect adds
EnumType. Grid accepts Options.Rows/Columns lengths and GridCell children with
Options.Row/Column/RowSpan/ColumnSpan. Padding and Margin accept a number, two
horizontal/vertical numbers, or four left/top/right/bottom numbers.

### Example 7: Highlight source and show an image

Code uses the optional TextMate module and Image the optional Sixel module; each shows plain text or an install notice when its module is missing. Image needs a Sixel terminal.
Run this example from the repository root to use its dashboard screenshot.

```powershell
$code = New-TuiControl Code -Id code -Bind source -Property @{ Language = 'powershell'; Height = 10 }
$image = New-TuiControl Image -Id logo -Bind logo -Property @{ Height = 12 }
$stack = New-TuiControl VStack -Id root -Child $code, $image
New-TuiDefinition -Title 'Preview' -Root $stack -State @{ source = 'Get-Process | Sort-Object CPU'; logo = './docs/images/dashboard.png' } | Invoke-Tui
```

## PARAMETERS

### -Bind

State key for the control value. FTable and FLogView read a collection; text controls read strings; Chart reads a numeric collection. Scalar Chart bindings use the timestamped-history adapter. Defaults to no binding.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Child

Child definitions for a VStack, HStack, Border or Tabs. Children is an alias. Noncontainers reject children; Border accepts at most one.
Set TabTitle in a child's Property hashtable to give its tab a readable caption
without changing the child's event ID or content Text.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: ''
SupportsWildcards: false
Aliases:
- Children
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Id

Unique control ID: 1–64 characters, beginning with a letter or underscore. IDs must be unique in the composed document.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Property

Hashtable of control-specific schema properties, for example Columns, SelectionBind, Path, View, RefreshInterval, Height, Width, Minimal, Maximum or TabTitle. TabTitle accepts up to 128 characters and is used when the control is a tab page; blank uses its Id. Unknown keys and invalid values are rejected.

```yaml
Type: System.Collections.Hashtable
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Text

Display text, title, initial input text or Markdown. Defaults to an empty string.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Type

A supported control type. Validated names are case-insensitive and tab-complete from the current catalog.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### System.Management.Automation.PSObject

Builds an independent PSCustomObject containing a validated control node. Combine it with other controls using Child, or place it in New-TuiDefinition and Invoke-Tui. Property contains control-specific options listed by Get-TuiControl; it cannot override named base parameters. No XenoAtom types cross the private ALC boundary. Creating definitions does not perform file operations or start workers.

## NOTES

All output is native PowerShell data. Saved definitions contain only layout and default values; keep live objects in Invoke-Tui -State. See the custom TUI cookbook for composition examples.

## RELATED LINKS

[Invoke-Tui](Invoke-Tui.md)

[Custom TUI examples](../../README.md#examples)
