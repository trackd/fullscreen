---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-30-2026
PlatyPS schema version: 2024-05-01
title: Show-TuiDesigner
---

# Show-TuiDesigner

## SYNOPSIS

Design a custom fullscreen TUI with a live visual preview.

## SYNTAX

### __AllParameterSets

```
Show-TuiDesigner [[-Path] <string>] [-Definition <psobject>] [-PassThru] [-Theme <Object>]
 [-Minimal] [<CommonParameters>]
```

## ALIASES

fdesign


## DESCRIPTION

Build custom TUI layouts visually by adding controls to the canvas and editing
their properties. Preview changes without running PowerShell handlers; undo and
redo are supported.

Right-click or press F7 for control actions. F2 focuses the palette, F3 shows
properties, F5 rebuilds the preview and F10 closes the designer.

Save layouts as PSD1 or JSON, or export a runnable script. A companion script
provides a starting point for application behavior and is never overwritten.
Linked layouts allow later design changes without replacing custom code.
Unsaved changes require confirmation before closing.

## EXAMPLES

### Example 1: Design a new layout

Save targets the supplied literal path.

```powershell
Show-TuiDesigner -Path ./tool.tui.json
```

### Example 2: Edit an existing layout

F5 rebuilds the live preview.

```powershell
New-TuiDefinition -Title 'Editable tool' | Export-TuiDefinition -Path ./editable-tool.tui.json -Script:$false -Force
Show-TuiDesigner -Path ./editable-tool.tui.json -Theme 'Light Modern'
```

### Example 3: Edit native PowerShell data

PassThru returns the current valid definition after the designer closes.

```powershell
$layout = New-TuiDefinition -Title 'My form'
$edited = $layout | Show-TuiDesigner -PassThru
$edited | Export-TuiDefinition -Path ./edited.tui.json
```

## PARAMETERS

### -Definition

One version-1 definition as a hashtable, PSCustomObject or JSON string, directly or from the pipeline.
With Path, this definition takes precedence and Path is the save target.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Minimal

Hide surrounding designer chrome while editing the layout.
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

### -PassThru

Return the current valid layout as a plain PowerShell object after the designer closes, including edits not saved to disk.
No XenoAtom or Fullscreen-specific CLR instance is emitted.

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

### -Path

Literal local layout file to load and save.
If missing, opens a starter document targeted at this path.
Without Path, Save prompts for a filename.
LiteralPath is an alias.
The parent directory must exist.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- LiteralPath
ParameterSets:
- Name: (All)
  Position: 0
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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.Management.Automation.PSObject

An optional version-1 definition to edit.

## OUTPUTS

### System.Management.Automation.PSObject

With PassThru, the current valid definition, including unsaved edits. Otherwise no objects are emitted.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
