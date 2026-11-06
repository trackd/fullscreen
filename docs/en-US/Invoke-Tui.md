---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Invoke-Tui
---

# Invoke-Tui

## SYNOPSIS

Run a custom fullscreen TUI from a native PowerShell definition, JSON or PSD1 file.

## SYNTAX

### Definition (Default)

```
Invoke-Tui [-Definition] <psobject> [-State <hashtable>] [-Handler <hashtable>] [-OnEvent <scriptblock>]
 [-OnTick <scriptblock>] [-TickInterval <int>] [-ChartRenderer <ChartRenderer>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

### Path

```
Invoke-Tui [-Path] <string> [-State <hashtable>] [-Handler <hashtable>] [-OnEvent <scriptblock>] [-OnTick <scriptblock>]
 [-TickInterval <int>] [-ChartRenderer <ChartRenderer>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

ftui


## DESCRIPTION

Run a custom terminal app from a layout created with New-TuiDefinition,
Export-TuiDefinition or Show-TuiDesigner. Layouts contain data, not executable
handlers; behavior comes from the script blocks passed to this command.

Controls bind to the shared State dictionary. Inputs update their bound values;
lists, grids and charts publish selections through SelectionBind. Replace bound
collections to refresh their contents. Get-TuiControl describes each control's
properties, options and events.

Charts prefer Sixel when supported and otherwise render terminal cells. Wheel zoom
and keyboard zoom/pan are disabled by default; set `EnableZoom = $true` on a Chart
to opt in, including scalar-bound history charts. Embedded charts otherwise leave
the wheel to their surrounding scroll viewer.
Single-series Bars and HorizontalBars automatically color each category and show
matching category legends. Grouped and stacked bars retain per-series colors;
explicit series colors take precedence.

Handlers run on the UI thread and can update State or request that the app close.
Keep callbacks short, avoid host output, and use background jobs for long work.
When the app closes, the command returns State, Value, Cancelled and LastEvent.

## EXAMPLES

### Example 1: Run a saved layout

The layout file is data; no embedded script executes.

```powershell
New-TuiDefinition -Title 'Saved tool' | Export-TuiDefinition -Path ./tool.tui.json -Script:$false -Force
Invoke-Tui -Path ./tool.tui.json -Theme 'Dark Modern'
```

### Example 2: Return a native object from a handler

Handlers emit one reply hashtable. Value preserves the returned object.

```powershell
$layout = New-TuiDefinition -Title 'Your name'
$result = $layout | Invoke-Tui -OnEvent {
    param($tuiEvent, $state)
    if ($tuiEvent.Id -eq 'submit' -and $tuiEvent.Kind -eq 'Click') {
        @{ Close = $true; Value = [pscustomobject]@{ Name = $state.name } }
    }
}
if (-not $result.Cancelled) { $result.Value }
```

### Example 3: Refresh bound display state

Keep tick handlers short; the display binds to state keys.

```powershell
$layout = New-TuiDefinition -Title 'Clock' -Root @{
    Id = 'clock'; Type = 'Text'; Bind = 'now'
}
$layout | Invoke-Tui -Minimal -TickInterval 1000 -OnTick {
    param($tuiEvent, $state)
    @{ State = @{ now = (Get-Date).ToString('T') } }
}
```

## PARAMETERS

### -Handler

Map control events to scriptblocks, for example `@{ 'save:Click' = { param($tuiEvent, $state) ... } }`.
Keys are case-insensitive and validated against the layout before opening a terminal.
Use `:Start` and `:Tick` for lifecycle handlers. A matching entry replaces OnEvent
or OnTick for that event; unmatched events use those callbacks as before.
Handlers share the same state, reply contract and UI thread as OnEvent.

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

### -ChartRenderer

Select Auto (Sixel with native fallback), Sixel, Native or Regis (experimental
Contour vectors). Applies to charts throughout the custom TUI.

```yaml
Type: fullscreen.Charts.ChartRenderer
DefaultValue: Auto
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
AcceptedValues:
- Auto
- Sixel
- Native
- Regis
HelpMessage: ''
```

### -Definition

One version-1 hashtable, PSCustomObject or JSON string.
Can be passed through the pipeline.
Mutually exclusive with Path.
No runtime assembly-qualified types are accepted in saved documents.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Definition
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Minimal

Hide the custom application title, status and footer while keeping its layout visible.
F4 toggles this presentation during the session; existing view shortcuts remain available.

```yaml
Type: System.Management.Automation.SwitchParameter
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

### -OnEvent

Script block receiving param($tuiEvent, $state).
Event contains Id, Kind and Value. Kinds include Start, Change, Click, Selection,
Submit and Close; use Get-TuiControl to see each control's supported events.
Start has an empty Id. Menu Click events identify the selected action in Value;
ordinary button Click events have a null Value.
Mutate state directly or return one hashtable with State updates, Close and optional Value.
Other success-stream output is rejected.
Handlers execute only in Invoke-Tui, never in the designer preview.

```yaml
Type: System.Management.Automation.ScriptBlock
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

### -OnTick

Optional periodic script block receiving param($tuiEvent, $state), with Kind Tick.
Same response contract as OnEvent; runs on the PowerShell/UI thread, without a background runspace.

```yaml
Type: System.Management.Automation.ScriptBlock
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

### -Path

Literal PSD1 or JSON/JSONC layout file. PSD1 files are parsed as data without executing code.
LiteralPath is an alias.
Mutually exclusive with Definition.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- LiteralPath
ParameterSets:
- Name: Path
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -State

Initial hashtable values overriding definition State.
State is copied into app-owned storage; inputs with Bind populate missing keys.

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

### -Theme

Built-in theme name or hashtable of palette colors.
Tab completes built-in names.
Default preserves the current fullscreen colors.
Hashtable keys: Base (built-in name, default Default), Name, Background, Foreground, Panel, Muted, Accent, Selection, SelectedText, Success, Warning, Error and Purple.
Individual syntax and chart colors can also be overridden; unspecified colors inherit from Base.
For example: `-Theme @{ Base = 'Monokai'; Keyword = '#ff79c6'; ChartBlue = '#58d5e6' }`.
Use `(Get-FullscreenTheme Monokai).Colors` to discover all editable keys. See Get-FullscreenTheme for the syntax, series, grid/axis/label and ramp roles.
Colors accept #RGB or #RRGGBB.
Get-FullscreenTheme returns editable Colors hashtables.
Theme files are not supported.

```yaml
Type: System.Object
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

### -TickInterval

Minimum periodic callback and chart-sampling interval in milliseconds, 16–60000,
default 250. Tick callbacks use Handler[':Tick'] when supplied, otherwise OnTick.
Without either callback, chart sampling still runs at this interval.

```yaml
Type: System.Int32
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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.Management.Automation.PSObject

One version-1 definition. Handlers are supplied separately as scriptblocks.

## OUTPUTS

### System.Management.Automation.PSObject

A native PowerShell object with State, Value, Cancelled and LastEvent. Value preserves the object returned by the handler.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
