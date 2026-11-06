---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-GitHubManager
---

# Show-GitHubManager

## SYNOPSIS

Monitor and review GitHub development activity in a fullscreen TUI.

## SYNTAX

### __AllParameterSets

```
Show-GitHubManager [[-Path] <string>] [-Repository <string>] [-HostName <string>]
 [-View <GitHubView>] [-ReadOnly] [-RefreshInterval <int>] [-PageSize <int>] [-Theme <Object>]
 [-Minimal] [<CommonParameters>]
```

## ALIASES

fgh


## DESCRIPTION

Opens GitHub Issues, Pull requests, Runs and Workflows for a repository in a
fullscreen terminal app. Tabs load when first opened. Issues show their body and
comments; pull requests show diffs, checks and reviews. Runs, jobs and steps show
a green check, red cross or progress marker for their status. Workflows show their
recent runs, and run details list jobs, steps, durations, pending deployments and
artifacts with links that open in the browser. Results are colored consistently
in tables, previews and expanded details: green for success, red for failure,
yellow for pending/in-progress, and grey for skipped/cancelled/neutral. Each
recent workflow run uses its own result color. Pull requests use green for open,
grey for draft, yellow for blocked/conflicting when reported by GitHub, red for
closed and purple for merged. The list continues to load open pull requests only.

Press Enter on a run to open its monitor. Flow shows status-colored jobs and steps
for the current run attempt, with durations and automatic refresh every three seconds.
Select a row to preview its log; Enter expands the log, and Backspace returns to
the flow (or closes the monitor from the flow). P pauses status/log refresh,
F toggles following the log tail, F5 refreshes and resumes, and / searches the
loaded log when expanded. With follow off, refresh preserves the reading position.
Q closes just the monitor. Actions still provides the original Run overview.

The Artifacts tab lists available and expired artifacts. Enter or D prompts for
an explicit local destination; Download confirms saving the ZIP archive.
Existing files are never overwritten, failed downloads are cleaned up, and
archives are not extracted. Downloads are limited to 1 GiB and remain available
in ReadOnly mode, which prevents remote changes rather than local file downloads.
Closing the monitor cancels its pending requests and downloads.

Like gh run watch, the monitor follows live job/step status, not a streaming
GitHub console feed. GitHub normally publishes downloadable job logs after the
job completes; unavailable logs are identified explicitly and retried.
Steps share their job's combined log rather than separate live step streams.
Logs larger than 4 MiB show only their beginning with an explicit notice.
The REST API does not expose a resolved needs/dependency graph: Flow is an
honest job/step hierarchy, not a reconstructed dependency DAG.

Press A or right-click for actions: comment on issues, approve, request changes
or comment on pull requests, view failed-step logs, rerun failed jobs, cancel
runs, approve pending deployments and dispatch a workflow on a chosen branch or
tag. Changes are confirmed before they are sent. A review is rejected if the pull
request changed after you opened it, and GitHub permissions and protection rules
still apply.

Requires the GitHub CLI (gh), already signed in with gh auth login; the app does
not sign in for you. The cmdlet writes no pipeline output. Press F12 to toggle
the diagnostic overlay.

## EXAMPLES

### Example 1: Review repository pull requests

Uses the existing gh CLI authentication.

```powershell
Show-GitHubManager -Repository trackd/fullscreen -View PullRequests
```

### Example 2: Monitor workflow runs

ReadOnly disables write actions such as approvals and reruns.

```powershell
Show-GitHubManager -Repository trackd/fullscreen -View Runs -RefreshInterval 15 -ReadOnly
```

### Example 3: Browse issues fullscreen

PageSize bounds each request.

```powershell
Show-GitHubManager -Repository trackd/fullscreen -View Issues -PageSize 25 -Minimal -Theme Monokai
```

## PARAMETERS

### -HostName

GitHub hostname, default github.com.
Supports authenticated GitHub Enterprise hosts through gh.

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

List results per page, 1 through 100; default 50.
Bracket keys move pages.
The issues endpoint also returns PRs, which are excluded from the issue grid.

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

Literal filesystem working directory for gh repository inference.
Defaults to the current filesystem directory; not required to be a Git checkout when Repository is provided.

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

Hide all GitHub mutations; retain inspection and failed-step log viewing.

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

### -Repository

GitHub owner/name.
Repo is an alias.
When omitted, gh infers it from the current Git checkout.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- Repo
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

Initial tab: Issues, PullRequests, Runs or Workflows.
Defaults to PullRequests.

```yaml
Type: fullscreen.Commands.GitHubView
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
