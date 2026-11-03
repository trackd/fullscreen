---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-LogViewer
---

# Show-LogViewer

## SYNOPSIS

Browse and follow text or JSON log files in a fullscreen table.

## SYNTAX

### Path (Default)

```
Show-LogViewer [-Path] <string[]> [-Follow] [-MaximumCount <int>] [-PollInterval <int>]
 [-MinimumLevel <LogLevel>] [-Filter <string>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

### LiteralPath

```
Show-LogViewer -LiteralPath <string[]> [-Follow] [-MaximumCount <int>] [-PollInterval <int>]
 [-MinimumLevel <LogLevel>] [-Filter <string>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

flog


## DESCRIPTION

Opens up to 64 UTF-8 text or JSON log files in a fullscreen viewer that merges
entries by timestamp and colors them by severity. Common JSON and Serilog compact
fields and ISO timestamp prefixes are recognized; entries without a timestamp use
their arrival time, and malformed JSON and unknown severities stay visible as
plain entries.

Filter with /, choose a minimum severity with L, bookmark entries with Space and
show only bookmarks with B, and press Enter or I for the source and raw line. With
Follow, new lines are read as they are written, and truncated or replaced files
are detected. F toggles scrolling to new entries; moving the selection stops it.
Q closes the viewer.

Initial reads are limited to the last 8 MiB of each file, and lines longer than
16 KiB characters are truncated. When old entries are discarded, their bookmarks
are removed too.

## EXAMPLES

### Example 1: Browse existing log files

Path supports FileSystem wildcards.

```powershell
Show-LogViewer -Path ./logs/*.log
```

### Example 2: Follow warnings and errors

The viewer retains bounded recent entries and detects rotation.

```powershell
Show-LogViewer -LiteralPath ./app.log -Follow -MinimumLevel Warning -MaximumCount 20000
```

### Example 3: Pass native file objects

LiteralPath accepts FileInfo arrays without wildcard expansion.

```powershell
$files = Get-ChildItem -Path ./logs -Filter '*.log' -File
Show-LogViewer -LiteralPath $files -Filter timeout -Minimal -Theme Monokai
```

## PARAMETERS

### -Filter

Initial case-insensitive filter across table cells. Plain text matches substrings;
`regex:^error.*timeout` uses a regular expression and `wildcard:*timeout*` uses
`*` (any characters) and `?` (one character). The interactive `/` filter supports
the same prefixes. Invalid regex patterns display an error and can be edited;
regular expression matching has a 50 ms timeout.
Slash edits it inside the UI.

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

### -Follow

Keep polling for appended lines and file rotation.
Initially scrolls to the newest entry; F toggles tail scrolling.

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

### -LiteralPath

Exact filesystem paths without wildcard expansion; PSPath is an alias.
With Follow, missing files are retried.

```yaml
Type: System.String[]
DefaultValue: ''
SupportsWildcards: false
Aliases:
- PSPath
ParameterSets:
- Name: LiteralPath
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -MaximumCount

Maximum retained entries across all files, default 10000.
Oldest arrival entries are evicted as new entries arrive.

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

### -Minimal

Hide surrounding navigation, summaries and help bars while retaining the log table.
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

### -MinimumLevel

Initial minimum severity.
Trace, Debug, Information, Warning, Error, Critical or Unknown.
Unknown entries remain visible at every threshold.

```yaml
Type: fullscreen.Logs.LogLevel
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

One or more filesystem log paths, supporting PowerShell wildcards.
Choose at most 64 resolved files.

```yaml
Type: System.String[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Path
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -PollInterval

File polling interval in milliseconds, 50 through 60000; default 250.

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

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
