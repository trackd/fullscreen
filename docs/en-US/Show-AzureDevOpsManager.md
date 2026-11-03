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

Opens Azure DevOps Boards, Work items, Stories, Tasks, Pull requests,
Pipelines and Backlog in a fullscreen terminal app. Tabs load when first opened. Lists use
the full width by default; click Preview or press V to show a resizable detail
pane. The command bar provides actions, view options, refresh and sprint selection.
Buttons share rows and wrap to fit narrower terminals.

Enter opens a work item with its fields, description, acceptance criteria,
discussion (latest 50 comments), attachments and linked items. Parent, child and
related work items open inside the app, with a Back button to return. Details
open in a unified, scrollable layout with wrapped text and grouped actions. Wider
windows place the description beside the item fields. In preview mode, drag the
divider or press Shift+Left/Right to resize the panes.

Stories and Tasks default to the team's current sprint. Click Sprint or press I
to choose another sprint or All sprints; the selection applies to both tabs.
If no current sprint is configured, all items are shown with an explicit notice.

Enter on a board opens its Kanban columns, filtered by the board's work item
types and team area paths. K shows the current list as a Kanban board; select a
card to inspect or move it. M or **Filter list: assigned to me** returns to the list
and filters by your signed-in identity; it does not assign or change a work item.
Use **Assign** to change an item's assignee.
/ filters the loaded page, C chooses columns, G groups, brackets change page
and F5 refreshes.

Press A or right-click for actions: create stories and tasks, add child tasks,
rename, assign, change state, plan into an iteration, set area, priority or remaining hours, edit descriptions and tags, and
add comments. Changes are confirmed before saving and are rejected if the work
item changed since it was displayed; Azure DevOps process rules still apply.

The Backlog tab shows the team's configured backlog levels, including portfolio
levels such as Features and Epics, in Azure DevOps planning order across all
sprints. Click Backlog level or press L to switch levels. Create the work-item
types configured for that level, inspect items, add child tasks, or use the same
confirmed edit and planning actions. Column sorting changes only the display,
not the team's backlog order.

Pull request details show reviewers, discussion and changed file names. After
inspection, use Actions to comment, approve, approve with suggestions, wait for
the author, reject or reset your vote. A source or target change requires a fresh
inspection. Open in browser shows the full diff and supports inline review.
Pipeline runs show status, job and step durations and artifacts with browser links.

Click Sprint review or press U from a work-item or backlog tab to review a team
sprint. Choose a sprint inside the review. Totals cover all its work-item types
and pages in the team's area paths, ignoring list and assignee filters. The review
shows completed/not-completed work, workflow states, estimates by type and recorded
completed/remaining hours. Missing estimates and hours are identified by their coverage;
unrecognized workflow states are reported, not assumed complete.

Sprint review is a current snapshot, not historical velocity or burndown. Removed-state
items are counted separately; items moved out of the sprint and changes in scope since
the sprint started require historical Azure DevOps Analytics data. No separate Analytics
OData connection is used. Pipeline **Run summary** remains a result chart of the current
filtered page only.

Requires Azure CLI (az) with the azure-devops extension on PATH. The app reuses
your Azure CLI sign-in and opens browser sign-in before starting when needed;
Ctrl+C cancels sign-in. Personal access tokens are not supported. The cmdlet
writes no pipeline output. Press F12 to toggle the diagnostic overlay.

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
