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

Opens a visual designer for building custom TUI layouts. Pick controls from the
palette on the right and click or drag them onto the canvas. Drop on a container
to add a child, or on another control to insert before or after it; drag existing
controls to move them. The preview updates as you edit but never runs PowerShell
handlers. Edits support undo and redo.

The property panel along the bottom groups the selected control's settings into
Content, Layout, Appearance and Behavior; Document holds the app title. Errors
appear beside the field. Use View to keep properties always visible, on demand
(F3) or hidden; the choice and panel height are remembered.

Right-click a canvas item or press F7 for actions such as select parent,
duplicate, wrap in a row, column or border, move and delete. F2 focuses the
palette, F4 expands the canvas, F5 rebuilds the preview and F10 closes the
designer. Hover the header icons for their names and shortcuts.

Save layouts as PSD1 or JSON; new layouts default to .tui.psd1. Saving also creates
a same-stem .ps1 scaffold if missing, with help and event/update stubs to edit in
your code editor. Existing scripts are never overwritten. Export app accepts
.ps1, .psd1 or .json; its linked option keeps a .ps1 export's layout in a sibling
.tui.psd1 so later exports update only layout data. Turn off that option for a
new, single portable script. Data paths always get linked companions. Replacing
layouts and closing with unsaved changes ask for confirmation.

120 columns by 40 rows is recommended; smaller windows remain usable. Use
PassThru to return the definition when the designer closes, then run it with
Invoke-Tui. Press F12 to toggle the diagnostic overlay.

## EXAMPLES

### Example 1: Design a new layout

Save targets the supplied literal path.

```powershell
Show-TuiDesigner -Path ./tool.tui.json
```

### Example 2: Edit an existing layout

F5 rebuilds the live preview.

```powershell
Show-TuiDesigner -LiteralPath './tool[1].tui.json' -Theme 'Light Modern'
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
