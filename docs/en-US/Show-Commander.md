---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-24-2026
PlatyPS schema version: 2024-05-01
title: Show-Commander
---

# Show-Commander

## SYNOPSIS

Opens a fullscreen Midnight Commander-inspired dual-pane file manager.

## SYNTAX

### __AllParameterSets

```
Show-Commander [[-LeftPath] <string>] [[-RightPath] <string>] [-ShowHidden] [-PassThru] [-SetLocation] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

fmc

## DESCRIPTION

Browse two independent directory panes with Tab, arrows or hjkl. F3 opens a text or Markdown viewer; F4 toggles minimal mode. F5/F6 copy or move to the opposite pane, F7 creates folders and F8 moves items to recoverable .fullscreen-trash folders. Delete permanently removes selected files or directory contents after confirmation, with Cancel focused by default. F9 also offers permanent delete; F10/Q closes the app. Right-click an item for a menu of these actions. Transfers never overwrite destinations. Unix copy preserves permission bits. File operations are confirmed in the UI. No shell commands are run. The default palette is classic blue; Theme can override it.

Press F12 to toggle diagnostics.

## EXAMPLES

### Example 1: Browse two directories

Open independent source and destination panes.

```powershell
Show-Commander -LeftPath . -RightPath $HOME
```

### Example 2: Return the active selection

After closing, return selected literal paths from the active pane.

```powershell
$paths = fmc . $HOME -PassThru
$paths | ForEach-Object { Get-Item -LiteralPath $_ }
```

### Example 3: Use a custom palette and compact layout

Hide chrome and show dotfiles in both panes.

```powershell
Show-Commander -Theme 'Dark Modern' -Minimal -ShowHidden
```

## PARAMETERS

### -Minimal

Hide the top menu and function-key footer while retaining both panes. F4 toggles this presentation.

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

After closing, emit marked literal paths or the current selected path as strings.

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

### -LeftPath

Initial literal FileSystem directory for the left pane. Defaults to the current location. Path is an alias. Native DirectoryInfo and PathInfo values are accepted.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- Path
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

### -RightPath

Initial literal FileSystem directory for the right pane. Defaults to the left directory. Native DirectoryInfo and PathInfo values are accepted.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -SetLocation

After closing, change the caller location to the currently displayed directory.
Omitted by default.

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

### -ShowHidden

Initially show dotfiles and hidden entries.
The period key toggles this in the TUI.

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

### -Theme

Built-in theme name or hashtable of palette colors.
Tab completes built-in names.
Omitting Theme selects the classic blue Commander palette. Explicit Default uses the normal fullscreen colors.
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

## OUTPUTS

### System.String

With PassThru, marked paths or the current selection after closing.
Otherwise no output.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
