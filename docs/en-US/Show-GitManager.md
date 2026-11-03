---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-GitManager
---

# Show-GitManager

## SYNOPSIS

Interact with a Git repository in a fullscreen TUI.

## SYNTAX

### __AllParameterSets

```
Show-GitManager [[-Path] <string>] [-View <GitView>] [-ReadOnly] [-RefreshInterval <int>]
 [-PageSize <int>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

fgit


## DESCRIPTION

Tabs show Changes, Staged, History, Branches, Remotes, Worktrees, Stashes and Tags.
Local/remote branches show tracking, ahead/behind and checkout paths; actions create/switch/rename/delete merged branches, set upstreams, compare, merge or rebase.
Worktrees can be added on existing/new branches or detached, opened in this TUI, moved, locked/unlocked and removed when Git permits clean removal.
Stashes support save/apply/pop/drop; tags support create/delete local/push selected.
History supports cherry-pick/revert, with continue/abort actions for in-progress operations.
Stage/unstage selected files or all changes, commit, fetch, fast-forward-only pull and explicit upstream push.
Mutations require in-app confirmation and stale targets are checked.
ReadOnly retains worktree navigation.
Git hooks/signing apply; continuation uses existing messages without an editor.
Enter expands previews, Backspace returns, Tab or numbers switch views, slash filters, F5 refreshes, brackets page history, and A/right-click opens actions.
No pipeline objects are emitted.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Inspect a checkout

Uses the existing Git CLI; write actions are reviewed in the TUI.

```powershell
Show-GitManager -Path .
```

### Example 2: Inspect worktrees

ReadOnly keeps write actions unavailable.

```powershell
Show-GitManager -Path . -View Worktrees -ReadOnly
```

### Example 3: Browse branches with a custom palette

The active tab fills the terminal in Minimal mode.

```powershell
Show-GitManager -View Branches -Minimal -Theme @{ Base = 'Dark Modern'; Accent = '#58d5e6' }
```

## PARAMETERS

### -Minimal

Hide surrounding navigation, summaries and help bars while retaining the active view.
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

### -PageSize

History entries per page, 1 through 100; default 50.
Bracket keys move pages.

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

### -Path

Literal filesystem directory in a Git repository.
Defaults to the current filesystem directory; LiteralPath is an alias.

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

### -ReadOnly

Hide mutating Git actions; retain inspection and navigation.

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

### -RefreshInterval

Refresh the active tab every 1 through 3600 seconds, default 30.
Expanded details pause automatic refresh.
F5 refreshes manually.

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

### -View

Initial tab: Changes, Staged, History, Branches, Remotes, Worktrees, Stashes or Tags.
Defaults to Changes.

```yaml
Type: fullscreen.Commands.GitView
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
