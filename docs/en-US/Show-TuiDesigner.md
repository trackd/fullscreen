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

Opens a large design canvas, a slim control palette on the right, and a
shallow property panel across the bottom.
The design prioritizes terminals with at least 40 rows; 120 columns by 40 rows is
recommended. Smaller windows remain editable without a blocking size notice.
The sidebar contains only controls to add; select existing items on the canvas.
Items share their category's theme-aware color. Bold uppercase category headings
use solid color bars to distinguish groups from addable controls. The palette shows its total
control count (or matching/total counts when searching); scroll for additional
categories. Hover a control for its full name and description. Search includes
names, categories and descriptions; Enter prefers an exact control name.
There is no footer. Brief action/error messages appear in the single-row header.
F2 focuses the right control list, F3 opens/focuses properties, F4 expands the
canvas, F5 rebuilds the preview, and F7 opens actions for the selected item.
Tab and Shift-Tab move focus, including out of multiline fields. Escape closes
menus/dialogs first, or hides properties when editing them; F10 closes the designer.
Properties follow the selection by default. View lets you choose Always visible,
On demand (F3), or Hidden (close again when selection changes). The panel's minus
and plus buttons change its maximum height. The borderless panel shrinks to its
content, normally four or five rows, without reserved blank rows. Visibility and height are remembered in
Fullscreen/designer.json under the local application-data directory, separately
from layout files. Content, Layout, Appearance and Behavior group the selected
control's fields; Document contains the app title. The question-mark button shows
help for the current group. Blank width/height fields show Automatic.
The heading shows the current filename and saved/unsaved state.
The header occupies one row. Its icon buttons provide controls (☰), properties
(≡), item actions (⋮), view preferences (⚙), undo/redo (↶/↷), save (▣), export
(↗) and close (×). Hover an icon for its name and shortcut.
Drag controls from the right palette into the preview. Drop onto a container to
append a child, or onto a leaf to insert before/after it. Drag existing preview
controls to move them. Click preview controls to select their properties.
Placement follows VStack/HStack/Tabs layout rather than absolute screen positions.
Right-click a canvas item or press F7 for properties, parent selection, duplicate,
wrap in a row/column/border, move and delete actions. Duplicate assigns fresh IDs
and preserves existing state bindings; layout changes support undo/redo.
Edit text/fill colors (#RRGGBB), bold text, alignment, dimensions and spacing.
Export app writes a commented runnable scaffold with event and periodic-update
stubs. The default linked export keeps handlers in the script and layout data in
a sibling .tui.psd1. Re-exporting a linked app updates its layout and preserves the
script byte for byte; unrelated existing scripts are rejected. Disable the linked
layout option for a portable inline script; overwriting inline scripts requires
confirmation. Replacing an existing linked layout also requires confirmation,
with the layout and script paths shown. Failed saves/exports keep the filename
and options open for correction and retry. Writes use temporary-file replacement.
Add, delete, reorder, indent and outdent controls; undo/redo edits; edit IDs, text, state bindings, dimensions and values.
Changes update the preview after a short debounce.
Select a container on the canvas (or use Select parent), then click a palette item to add a control,
or drag it onto the preview. Drag existing preview controls to move them.
Property selectors open menus; overlapping menus do not select the canvas underneath.
The preview never executes PowerShell handlers.
Save writes versioned PSD1 or JSON atomically according to the file extension;
new layouts default to .tui.psd1. Inline script export embeds a native hashtable and
commented callbacks in one script. Closing unsaved edits, including invalid or
not-yet-applied property drafts, offers confirmation. Undo commits a valid draft
as an undoable edit; invalid drafts stay in the inspector until corrected.
Property errors appear beside the relevant field; Save and navigation reveal
and focus that field. Set TabTitle on a tab page to separate its caption from its ID.
The palette includes layouts, inputs, charts, tables, logs, embedded applications
and the optional Spectre adapter. Use Get-TuiControl for the current catalog.
Layouts contain data only, with no serialized CLR types or scripts.
PassThru returns a plain PowerShell definition after closing.
Invoke-Tui loads the result or saved PSD1/JSON.
Theme and theme completion are shared with other apps.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

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
