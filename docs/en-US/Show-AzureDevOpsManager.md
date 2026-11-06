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

Opens Azure DevOps Boards, Work items, Stories, Tasks, Backlog, Pull requests
and Pipelines in that order (Backlog is tab 5). Tabs load when opened. Lists use
the full width by default; click Preview or press V to show a resizable detail
pane. The command bar provides actions, view options, refresh and sprint selection.
Buttons share rows and wrap to fit narrower terminals.

Boards is a landing dashboard with current-sprint totals and a state chart,
using normal Boards REST access rather than requiring Analytics permissions.
The row above it is the selected backlog-level board, not a separate team.
Click **Board level** or press L for the dropdown. It starts at the requirement
level (Stories for Agile; the configured equivalent for other processes) and also
offers the team's available Features/Epics boards. Each board uses its own columns
and type mappings; there is no mixed-level All Kanban. The dashboard covers all
work-item types in the current sprint and team areas, independent of level and
list filters.

Use `-ExcludeTab Tasks` or `-ExcludeTab Tasks,PullRequests` to hide unwanted pages.
Tab names complete as you type; remaining tabs are renumbered. An excluded initial
View falls back to the first visible tab. At least one tab must remain visible.
Excluded pages are not loaded, but the Boards dashboard still queries its team
sprint snapshot.

Enter opens a work item with its fields, description, acceptance criteria,
discussion (latest 50 comments), attachments and linked items. Parent, child and
related work items open inside the app, with a Back button to return. Details
open in a unified, scrollable layout with wrapped text and grouped actions. Wider
windows place the description beside the item fields. In preview mode, drag the
divider or press Shift+Left/Right to resize the panes.

Work items, Stories and Tasks default to the team's current sprint, equivalent
to `@CurrentIteration` resolved for the selected/default team. Click Sprint or press I
to choose another sprint or **All sprints** to remove the iteration filter;
the selection applies to all three tabs. Backlog is always across all sprints.
If no current sprint is configured, all items are shown with an explicit notice.
Tasks contains actual Task records, not User Stories; teams that do not create
Tasks can leave this valid general-purpose view hidden with ExcludeTab.

Work items also defaults to the story type. Click **Type** or press T to select
Feature, Epic, Task, Bug, another enabled project-specific type, or **All types**.
The options come from the project's process rather than a fixed Agile-only list.
If the default User Story type is unavailable, the configured requirement type
is used for this view. `-StoryType` supplies an explicit story type.
Type selection replaces an opened board's type/column filter but retains its
team area scope and the current sprint/assignee selections. All types removes
only the type filter; choose All sprints separately to see cross-sprint work.
All types is list-only: choose a single type or open a board level to use Kanban.
The sprint and type selections are shown in the toolbar. Features and Epics
commonly span sprints and may not share their children's iteration, so a current
sprint filter can legitimately hide them.

Enter on a board opens its Kanban columns, filtered by the board's work item
types and team area paths, instead of the standalone Type selection. Bug inclusion
respects the team's **bugsBehavior**: bugs managed with tasks or off the backlog
are excluded from the Stories board; bugs managed as requirements remain eligible
when the board maps them. Explicit Bug or All types filters can still show them.
K shows the current list as a Kanban board; select a
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
confirmed edit and planning actions. Its default sort is **Order ascending**:
Order is the position in the team's returned backlog planning order, not ID,
priority, or a guessed story-point rank. Column sorting changes only the display,
not the team's backlog order.
Levels marked hidden in the API are available as explicitly labelled choices;
choosing one does not enable it in the team's shared web navigation. Only levels
returned by Azure DevOps are offered. Use C to show the Parent column and G to
group the current page by Parent; Enter provides Parent/Child links for navigating
the actual hierarchy. This is not a recursive cross-page rollup.

Pull request details show reviewers, discussion and changed file names. After
inspection, use Actions to comment, approve, approve with suggestions, wait for
the author, reject or reset your vote. A source or target change requires a fresh
inspection. Open in browser shows the full diff and supports inline review.
Pipeline runs show status, job and step durations and artifacts with browser links.
Press Enter on a run or click Monitor run to open a monitor with Flow and Artifacts
tabs. Flow retains the timeline's stage, phase, job and task hierarchy, including
nested timelines and the latest reported record attempt. Status and durations
refresh automatically every three seconds. Select a task to preview its published
log; selecting a job or stage follows an active descendant task when available.
Enter expands the log, / searches the loaded lines, and Backspace returns to the
flow (or closes the monitor from the flow). P pauses refresh, F toggles following
the tail, F5 refreshes and resumes, and Q closes just the monitor.
With follow off, refreshed text preserves the reading position and search.
Logs show up to the last 2,000 published lines; agent buffering may delay updates.
This polls the supported build-log API, not the web console's private event stream.

On Artifacts, Enter or D prompts for an explicit local destination; Download
confirms saving a ZIP archive. Files are never overwritten, failed downloads are
cleaned up, archives are not extracted, and each download is limited to 1 GiB.
ReadOnly mode still allows local downloads, but not remote changes.
Closing the monitor cancels its pending requests and downloads.
Actions retains Run overview and browser links; Run summary remains the current
page's result chart.

Run results and job/step details use green for success, red for failure, yellow
for pending/in-progress or partially succeeded, and grey for skipped/cancelled.
Pull request states use green for active, grey for draft, yellow for conflicts,
red for abandoned and purple for completed/merged. The list continues to load
active pull requests only.

Click Sprint review or press U from a work-item or backlog tab to review a team
sprint. Choose a sprint inside the review. Totals cover all its work-item types
and pages in the team's area paths, ignoring list and assignee filters. The review
charts completed/not-completed estimates by type and unit, workflow states, and shows recorded
completed/remaining hours. Missing estimates and hours are identified by their coverage;
unrecognized workflow states are reported, not assumed complete.

Sprint review is a current snapshot, not historical velocity or burndown. Removed-state
items are counted separately; items moved out of the sprint and changes in scope since
the sprint started require historical Azure DevOps Analytics data.

Click **Boards analytics** or press Y (also U on Boards) for historical cumulative
flow and velocity charts. Choose **Current sprint** (default) or **Last 30 days**,
choose the board level and refresh inside the view. Analytics uses the same
Azure CLI identity against the project's Azure DevOps Analytics OData service;
Analytics must be enabled and readable. An Analytics access failure does not
prevent the REST-only landing dashboard or sprint review from working.

Cumulative flow follows the board's configured column order and plots observed
daily column counts. Date boundaries use UTC; the organization's daily cutoff can
differ. Missing days are not fabricated as zero observations. Velocity shows item
counts for up to six eligible sprints in the chosen range: first-day planned scope
versus the latest observed completed/incomplete scope at or before sprint end.
It is a daily-snapshot approximation, not the web velocity widget's exact baseline
or Completed Late calculation. Sprints without a start-day baseline are omitted.

Sprint review and Boards analytics also compare the selected/current sprint's
configured capacity hours with current OriginalEstimate hours by work-item type.
Capacity accounts for working days and the union of team/member days off.
Estimate coverage is shown; missing capacity is reported, not assumed zero.
Types are kept separate to avoid parent/child double counting, and story points
are never converted to hours. Estimates include all current iteration assignments,
including removed items; they are not historical sprint-start commitments.
Capacity on the Last 30 days view remains explicitly the current sprint, not a
30-day projection. Pipeline **Run summary** remains a result chart of the current
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
