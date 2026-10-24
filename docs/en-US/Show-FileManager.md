---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-FileManager
---

# Show-FileManager

## SYNOPSIS

Opens a fullscreen file manager with rich previews.

## SYNTAX

### __AllParameterSets

```
Show-FileManager [[-Path] <string>] [-ShowHidden] [-SortBy <FileSort>] [-Descending] [-NoPreview]
 [-PassThru] [-SetLocation] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

ffile


## DESCRIPTION

Browse parent/current/preview panes with hjkl or arrows.
Supports filtering, hidden files, multi-selection, history, copy/move buffers, rename and creating folders.
File operations are confirmed in the UI and never overwrite destinations.
Preview text, Markdown, directory contents and binary hex. Image previews use the
optional Sixel module in a Sixel-capable terminal; otherwise a notice is shown.
File contents are read locally on a worker; no file is executed.
Q closes the app and restores the terminal.

Use Space to mark an item and lowercase a to toggle visible marks. Lowercase y and x
set copy and move buffers; p opens the paste confirmation. F2 renames and lowercase n
creates a directory. Use [ and ] for history, ~ for home and lowercase g for a path
dialog. / filters names, . toggles hidden files, lowercase s selects the sort field
and r reverses it. F5 refreshes. Letter shortcuts are case-sensitive.
Lowercase t opens a tab, { and } switch tabs, and w closes the current tab.
Lowercase b opens session bookmarks; uppercase B toggles the current directory bookmark.
Tab or Enter focuses a rich preview; Escape returns to the file list. Press ? for
the in-app key reference. Paths are treated literally, including wildcard characters.

The optional TextMate module supplies syntax highlighting; L selects a language
or extension for the current text preview. Content is read on a worker and never
executed. Obsolete reads are discarded after navigation. Minimal mode hides the
parent and preview panes.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Browse the current directory

PathInfo values transform to literal FileSystem paths.

```powershell
Show-FileManager -Path (Get-Location)
```

### Example 2: Return selected paths

PassThru emits literal paths for downstream PowerShell commands.

```powershell
$paths = Show-FileManager -Path . -PassThru
$paths | ForEach-Object { Get-Item -LiteralPath $_ }
```

### Example 3: Focus on a single file list

Minimal mode avoids loading hidden preview panes.

```powershell
Show-FileManager -Minimal -ShowHidden -SortBy Modified -Descending
```

## PARAMETERS

### -Descending

Initially reverse sorting.
Lowercase r toggles the direction in the TUI.

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

### -Minimal

Hide surrounding chrome, the parent pane and the preview pane.
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

### -NoPreview

Disable the preview pane and content reads.

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

### -Path

Initial literal file or directory path; LiteralPath is an alias.
Defaults to the current filesystem location.
A file path selects that file in its parent.
Only FileSystem paths are accepted.

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
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
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

### -SortBy

Name, Size, Modified or Type.
Folders stay first.
Lowercase s cycles sorting inside the TUI.

```yaml
Type: fullscreen.Files.FileSort
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
