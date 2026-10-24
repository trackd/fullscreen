---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-AzureDevOpsManager
---

# Show-AzureDevOpsManager

## SYNOPSIS

Browse Azure DevOps boards, work items and development activity in a fullscreen TUI.

## SYNTAX

```
Show-AzureDevOpsManager [[-Path] <string>] -Organization <string> -Project <string> [-Team <string>]
 [-TenantId <string>] [-View <AzureDevOpsView>] [-StoryType <string>] [-ReadOnly]
 [-RefreshInterval <int>] [-PageSize <int>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

fado


## DESCRIPTION

Lazy Boards, Work items, Stories, Tasks, Pull requests and Pipelines tabs use Azure DevOps Services REST API 7.1 directly.
Boards open cards filtered by board work item types and team area paths, including child areas where configured.
Work items include state, board column, assignee, parent, priority, remaining work, iteration and tags.
Enter opens work-item panels for metadata, description, acceptance criteria,
discussion (latest 50 comments), and related work-item links that open inside the TUI.
Enter on a board opens its cards. K shows Kanban columns for the current filtered
page; select a card to inspect or move it. M toggles the server-side Assigned to me
filter using the authenticated identity. Slash filters the loaded page.
Actions create stories/tasks, add child tasks, rename, assign, change state, set remaining hours,
edit descriptions/tags and add discussion. Description and discussion editors support multiple lines.
Writes require an in-app confirmation; updates atomically test the displayed revision.
Azure process rules apply.
Pull requests and pipeline timelines are currently read-only.
Tabs load only when opened; F5 refreshes; slash filters the loaded page; C selects columns; G groups; A or right-click opens actions; brackets page.
Azure CLI (`az`) and its `azure-devops` extension on PATH are required. Reuses the signed-in account and opens browser sign-in before the TUI when needed. No application registration is required. Tokens refresh through Azure CLI; fullscreen does not store refresh credentials. Ctrl+C cancels sign-in (five-minute login timeout). PAT environment variables and explicit token parameters are not supported.
No pipeline objects are emitted.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Browse a team's stories

Requires Azure CLI and its `azure-devops` extension; reuses the CLI sign-in, prompting for browser login when needed.

```powershell
Show-AzureDevOpsManager -Organization contoso -Project Atlas -Team Core -View Stories
```

### Example 2: Browse pipelines without editing

Uses the Azure CLI account with read-only development views.

```powershell
Show-AzureDevOpsManager -Organization contoso -Project Atlas -View Pipelines -ReadOnly
```

### Example 3: Select the organization tenant

For guest access, choose the organization tenant using its GUID or domain.

```powershell
Show-AzureDevOpsManager -Organization contoso -Project Atlas -TenantId contoso.onmicrosoft.com -View Tasks -Minimal
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

### -Organization

Required Azure DevOps Services organization name or HTTPS dev.azure.com organization URL.
Legacy organization.visualstudio.com URLs are accepted.
Azure DevOps Server is not supported.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -PageSize

Rows per page, 1 through 100, default 50.
Work items use descending-ID cursors; builds use server continuation tokens.
Visit pages sequentially; refreshing the first page resets paging.

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

Existing literal filesystem directory inherited from the developer-app host.
Defaults to the current filesystem directory; Azure DevOps does not require a Git checkout.

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

### -Project

Required project name or ID.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ReadOnly

Hide remote editing actions.
Board navigation and inspection remain available.

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

Active-tab refresh interval in seconds, 1 through 3600, default 30.
Expanded details pause automatic refresh.

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

### -StoryType

Story work item type, default User Story for Agile.
Use Product Backlog Item for Scrum or Issue for Basic/custom processes.
Applies to the Stories tab and create-story action.

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

### -Team

Optional team name or ID.
Limits work item queries to configured team area paths.
Boards use the default team when omitted; opening a board applies that team scope.

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

### -TenantId

Entra tenant GUID or domain. When omitted, use the Azure CLI selected account tenant.
FULLSCREEN_AZURE_TENANT_ID supplies a configured default.
Use the Azure DevOps organization tenant for guest access.

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

Initial lazy tab: Boards (default), WorkItems, Stories, Tasks, PullRequests or Pipelines.

```yaml
Type: fullscreen.Commands.AzureDevOpsView
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
