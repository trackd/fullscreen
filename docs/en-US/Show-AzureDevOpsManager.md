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
 [-TenantId <string>] [-View <AzureDevOpsView>] [-ExcludeTab <string[]>] [-StoryType <string>] [-ReadOnly]
 [-RefreshInterval <int>] [-PageSize <int>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

fado


## DESCRIPTION

Browse Azure DevOps boards, work items, backlogs, pull requests and pipelines in
a fullscreen terminal app. Inspect linked work items, use Kanban boards, review
pull requests, and monitor pipeline jobs, logs and artifacts. Actions are
confirmed before changes are sent.

Work items, Stories and Tasks start in the team's current sprint. Use Sprint to
choose another or All sprints, and Type to include Features, Epics or other work
item types. Backlog spans all sprints and starts in the team's planning order.
Board and backlog level selectors use the team's configured levels.

Boards provides a sprint dashboard. Sprint review shows current estimates,
hours and capacity; Boards analytics adds historical cumulative flow and
snapshot-based velocity, which may differ from the web velocity widget.
Historical charts require Azure DevOps Analytics access; the dashboard and
sprint review do not.

Enter opens details, V toggles preview, A opens actions, / filters the loaded
page and F5 refreshes. Pipeline monitors follow published logs, which may lag
behind the live console, and can download artifacts as ZIP files.

Requires Azure CLI (az) with the azure-devops extension. Uses your CLI identity
and prompts for browser sign-in when needed. Personal access tokens are not
supported. Produces no pipeline output.

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

### Example 4: Plan the team backlog

Open the team's backlog, use L to choose its level, and open an item to edit or plan it.
Press U for the team's sprint review.

```powershell
Show-AzureDevOpsManager -Organization contoso -Project Atlas -Team Core -View Backlog
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
Boards and sprint selection use the default team when omitted.

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

Initial tab: Boards (default), WorkItems, Stories, Tasks, PullRequests, Pipelines or Backlog.

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

### -ExcludeTab

Hide selected resource pages without loading them. Valid names are Boards,
WorkItems, Stories, Tasks, Backlog, PullRequests and Pipelines, with tab completion.
For example, `-ExcludeTab Tasks` hides a view that is empty for teams using only
stories. At least one tab must remain visible. If View names an excluded tab,
the first visible tab is selected instead.

```yaml
Type: System.String[]
DefaultValue: '[]'
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
- Boards
- WorkItems
- Stories
- Tasks
- Backlog
- PullRequests
- Pipelines
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
