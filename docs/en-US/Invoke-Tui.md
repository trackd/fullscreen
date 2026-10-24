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

Builds controls inside fullscreen's private ALC; callers use ordinary PowerShell data rather than XenoAtom types.
Definition accepts hashtables and PSCustomObjects directly, including nested native
control definitions. Path accepts JSON or .psd1 files. PSD1 is data-only; executable
logic and live objects are passed separately through callbacks and State.
Bind connects controls to state keys.
Native widget properties go in Options; Get-TuiControl exposes NativeProperties
with their accepted names, types and enum choices. Numeric, switch, color and enum
inputs update their bound state. Lists, trees and grids consume a bound collection
and publish selection separately with SelectionBind. Replace collections to refresh.
DataGridControl returns the original selected object. Native Table renders cells;
DataGridControl defaults to ReadOnly. With Options.ReadOnly = $false, Edit events
contain Original and a Values dictionary; apply edits explicitly in your handler.
FTable adds fullscreen's object identity, filtering, sorting and Submit behavior.
FLogView parses severity-colored log snapshots; LogControl is the native log widget.

Dialog, Popup, CommandPalette, Toast and SearchReplacePopup use a boolean Bind key
to open/close. Closing an overlay resets that key to false. Target optionally names
a Popup anchor or ToastHost; SearchReplacePopup requires Target to name a LogControl.
Define targets anywhere in the layout. Previews keep overlays closed and never run handlers.
Text inputs, text areas, checkboxes and selects update state; text, Code, Image and progress controls display updates.
Code highlights through the optional TextMate module; Image needs the optional Sixel module and the Sixel ChartRenderer.
OnEvent and optional OnTick execute synchronously on the invoking PowerShell/UI thread.
Callbacks emit no output or one hashtable containing State, Close and/or Value.
A Close button or callback closes successfully; F10, Escape or cancellation marks the result cancelled.
F10 exits the custom application consistently, including from embedded components.
Escape first follows the focused control's cancel/back behavior, such as closing
a selector or clearing an embedded monitor filter, then closes the application
when unhandled. Tab and Shift-Tab move focus; multiline TextArea inputs retain
Enter for newlines and allow Tab to leave the field.
TabTitle supplies a tab page's visible caption independently of its Id and Text.
Without TabTitle, the ID is used as the caption.
Returns State, Value, Cancelled and LastEvent.
Layout files are data and never execute handlers.
User-supplied script blocks execute as normal PowerShell code.
Long callbacks block the UI; keep them short and do not write terminal output.

For Spectre controls, keep the caller's renderable in State and bind it by name.
Only that name is serialized into layouts. The adapter uses the renderable's own
interface assembly, including a foreign AssemblyLoadContext. It buffers styled
text privately and preserves colors and decorations without forwarding cursor,
screen-clear, title or image escape sequences. Interactive Spectre prompts and live
displays are outside this control's contract. Render output is limited to 1 MiB;
page large documents. fullscreen never replaces the global Spectre console.

BackgroundJob.ps1 demonstrates polling an owned thread job and cleaning it up
when the TUI closes. HandlerMap.ps1 demonstrates controlId:event handlers plus
:Start and :Tick lifecycle handlers. Matching handlers run instead of the fallback
OnEvent or OnTick callback. Return one reply hashtable, or no output.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Run a saved layout

The layout file is data; no embedded script executes.

```powershell
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
Contour vectors). Applies to charts throughout the custom TUI. DRCS chart
rendering is disabled.

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
Start has an empty Id, and Click has a null Value.
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
