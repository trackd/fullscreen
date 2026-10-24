---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-SystemMonitor
---

# Show-SystemMonitor

## SYNOPSIS

Opens the interactive fullscreen system monitor.

## SYNTAX

### Local (Default)

```
Show-SystemMonitor [-RefreshRate <int>] [-SampleInterval <int>] [-Filter <string>]
 [-SortBy <ProcessSort>] [-View <MonitorView>] [-GraphType <GraphKind>] [-ChartRenderer <ChartRenderer>] [-Tree] [-Column <ProcessColumn[]>]
 [-ExcludeColumn <ProcessColumn[]>] [-NetworkColumn <NetworkColumn[]>]
 [-ExcludeNetworkColumn <NetworkColumn[]>] [-GpuColumn <GpuColumn[]>]
 [-ExcludeGpuColumn <GpuColumn[]>] [-TimeoutSeconds <int>] [-Theme <Object>] [-Minimal]
 [<CommonParameters>]
```

### Session

```
Show-SystemMonitor -Session <PSSession> [-RefreshRate <int>] [-SampleInterval <int>]
 [-Filter <string>] [-SortBy <ProcessSort>] [-View <MonitorView>] [-GraphType <GraphKind>] [-ChartRenderer <ChartRenderer>] [-Tree]
 [-Column <ProcessColumn[]>] [-ExcludeColumn <ProcessColumn[]>] [-NetworkColumn <NetworkColumn[]>]
 [-ExcludeNetworkColumn <NetworkColumn[]>] [-GpuColumn <GpuColumn[]>]
 [-ExcludeGpuColumn <GpuColumn[]>] [-TimeoutSeconds <int>] [-Theme <Object>] [-Minimal]
 [<CommonParameters>]
```

### Computer

```
Show-SystemMonitor -ComputerName <string> [-RefreshRate <int>] [-SampleInterval <int>]
 [-Filter <string>] [-SortBy <ProcessSort>] [-View <MonitorView>] [-GraphType <GraphKind>] [-ChartRenderer <ChartRenderer>] [-Tree]
 [-Column <ProcessColumn[]>] [-ExcludeColumn <ProcessColumn[]>] [-NetworkColumn <NetworkColumn[]>]
 [-ExcludeNetworkColumn <NetworkColumn[]>] [-GpuColumn <GpuColumn[]>]
 [-ExcludeGpuColumn <GpuColumn[]>] [-Credential <pscredential>] [-ConfigurationName <string>]
 [-TimeoutSeconds <int>] [-Theme <Object>] [-Minimal] [<CommonParameters>]
```

## ALIASES

ftop


## DESCRIPTION

Shows Processes, Services, Network, GPU, Users and Dashboard views; starts on Processes.

Users shows account process totals and expandable process rows. L toggles process
collection, D opens session details, and +/− expands or collapses an account.
Windows session details include login/idle times, last input, connection times,
client name and protocol where available. Linux/macOS use who -u; dates without a
year stay as reported and unavailable fields display as unknown. Session timing
comes from login records, not from process start times. Custom TUIs can embed
UserList on its own; its IncludeProcesses property defaults to false.
Client, idle time and logon time also appear as table columns at 120 columns
with process totals, or 110 columns with processes hidden. Narrower terminals
retain selected-account details and D opens the full session list.
Includes process trees, usernames, status and optional cumulative CPU time.
Process CPU is normalized to 0-100 percent.
Header dropdowns sort and filter each table.
Press A or right-click a process/service for confirmed stop-process or service start/stop/restart actions.
Graphs use Sixel when supported and XenoAtom line charts otherwise; G cycles Sixel styles.
Windows disk and TCP/UDP attribution uses ETW when permitted by the current token; unavailable columns are hidden.
Remote sessions receive collector bytes in memory.
The same dependency-free .NET Standard 2.0 collector runs on modern targets and Windows PowerShell 5.1 on .NET Framework 4.7.2+.
The TUI requires PowerShell 7.6+ locally.

Use 1-6 to select Processes, Services, Network, GPU, Users or Dashboard. Press / to search,
C to choose the current table's columns, F6 to cycle sort columns, R to reverse,
T to toggle process trees, P to pause and Q to quit. Header dropdowns combine
column filters with the global search. G cycles chart styles.

F8 or 5 selects Users; Dashboard is always the final tab (6). Session inventory is collected only while this view is
selected, cached for five seconds, and transported as structured records from
the monitored target. Windows uses WTS; Linux/macOS use /usr/bin/who. No
TerminalSessions module is required. Empty login databases and errors remain
explicit; process ownership can still be shown without login records.
Each account groups its known login sessions and owned processes, with CPU and
RSS totals. Summed RSS includes shared pages. Plus/Right expands, Minus/Left
collapses; Enter toggles an account or opens actions for a process. A and
right-click also open confirmed process actions, retaining PID/start-time checks
and target permissions. Slash filters accounts or child process names/IDs.
On an account, A or right-click opens session details and a Log off entry for
each Windows session. Confirmation identifies the account, session ID and target;
logging off closes that session's applications and can lose unsaved work. The
target rechecks the account and logon time before requesting logoff. This works
locally and through the monitored PSSession, using the target account's permissions.
Sessions with unknown identity and non-Windows targets cannot be logged off here.
Accounts without login records and unknown process owners are labeled separately;
processes are grouped by owner, not assigned to an inferred login session.

Service actions use the current account's permissions without requesting elevation.
Windows reads Service Control Manager; Linux lists loaded systemd service units;
macOS lists launchd jobs in the current bootstrap domain. Missing providers show
an unavailable state. Service collection runs only while that view is visible,
at most once every five seconds. Remote service data and actions use the target.

CPU percentages measure 0-100 percent of total logical-processor capacity.
CpuTime sums user and kernel time across threads and can advance faster than wall
time. RSS includes shared pages; summing process RSS does not measure physical RAM.
Denied and unsupported counters remain unknown rather than becoming zero.

Use a dedicated remote session that permits in-memory assembly loading. A supplied
Session stays open after the monitor closes; ComputerName creates and removes its
own session. The remote collector assembly remains loaded until that process exits.
Remote sampling failures preserve the last snapshot, display its age and retry
with bounded backoff. All metrics come from the target.

Auto selects Sixel or native charts. DRCS chart rendering is disabled; its font
and encoder code is retained for reuse.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Monitor local processes

Use the process tree with optional cumulative CPU time.

```powershell
Show-SystemMonitor -Tree -Column Name,Id,UserName,Cpu,CpuTime,Memory
```

### Example 2: Show a fullscreen network table

Minimal mode hides surrounding UI and requests the active view's telemetry.

```powershell
Show-SystemMonitor -View Network -Minimal -SampleInterval 250
```

### Example 3: Monitor a remote session

The caller owns the PSSession and removes it when finished.

```powershell
$session = New-PSSession -ComputerName server01
try {
    Show-SystemMonitor -Session $session -View Dashboard -SampleInterval 2000
}
finally { Remove-PSSession -Session $session }
```

## PARAMETERS


### -ChartRenderer

Select Auto (Sixel with native fallback), Sixel, Native or Regis (experimental
Contour vectors). Use GraphType independently to choose the plot shape.
DRCS chart rendering is disabled.

```yaml
Type: fullscreen.Charts.ChartRenderer
DefaultValue: Auto
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
- Auto
- Sixel
- Native
- Regis
HelpMessage: ''
```

### -Column

Ordered process columns.
Id, ParentId, UserName, Cpu, Memory, MemoryPercent, Gpu, GpuMemory, Threads, Name, CpuTime, Status, Disk, Network.
CpuTime is cumulative CPU time and is off by default.
Unsupported disk/network columns are hidden.
C opens the current table column chooser.

```yaml
Type: fullscreen.Monitoring.ProcessColumn[]
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

### -ComputerName

A WinRM target.
Creates a temporary session and removes it on exit.
The target must already have a suitable endpoint configured.
Cannot combine with Session.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Computer
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ConfigurationName

Existing WinRM endpoint used with ComputerName.
Default PowerShell.7.
Windows PowerShell endpoints can use Microsoft.PowerShell.
The endpoint must permit assembly loading.

```yaml
Type: System.String
DefaultValue: PowerShell.7
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Computer
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Credential

Optional credential for ComputerName.
Otherwise uses the caller identity.
For SSH authentication, create a PSSession yourself and use Session.

```yaml
Type: System.Management.Automation.PSCredential
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Computer
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ExcludeColumn

Remove columns from the requested or default process selection.
At least one column must remain.
Tree mode requires Name.

```yaml
Type: fullscreen.Monitoring.ProcessColumn[]
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

### -ExcludeGpuColumn

Hide GPU device columns from the requested or default selection.

```yaml
Type: fullscreen.Monitoring.GpuColumn[]
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

### -ExcludeNetworkColumn

Hide network interface columns from the requested or default selection.

```yaml
Type: fullscreen.Monitoring.NetworkColumn[]
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

### -Filter

Initial case-insensitive name, PID or username substring.
Press / to edit and Enter to finish. Escape clears the search and active table's column filters; Ctrl+U clears only the search text and keeps editing when the search is open.
Header dropdown filters combine with this text filter.

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

### -GpuColumn

GPU device columns: Device, Provider, Utilization, UsedMemory, TotalMemory, Temperature.
C changes this table independently.

```yaml
Type: fullscreen.Monitoring.GpuColumn[]
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

Fill the terminal with the active view, hiding navigation, summaries, previews and help bars.
F4 toggles this presentation during the session; existing view shortcuts remain available.
The performance monitor collects metrics needed by the visible view, columns, sorts and filters.

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

### -NetworkColumn

Network interface columns: Interface, Receive, Send, Link, TotalReceived, TotalSent, Errors.
C changes this table independently.

```yaml
Type: fullscreen.Monitoring.NetworkColumn[]
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

### -RefreshRate

Maximum data-presentation refresh rate, 1 to 60 Hz.
Default 30.
The framework redraws when state changes.

```yaml
Type: System.Int32
DefaultValue: 30
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

### -SampleInterval

Delay in milliseconds between completed samples, 100 to 60000.
Default 1000 locally or 2000 remotely.
Sampling runs independently of input/rendering.

```yaml
Type: System.Int32
DefaultValue: 1000 local, 2000 remote
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

### -Session

A dedicated open PSSession created through WinRM or SSH.
The caller owns the session; closing the monitor removes its collector but keeps the session open.
Cannot combine with ComputerName.

```yaml
Type: System.Management.Automation.Runspaces.PSSession
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Session
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -SortBy

Cpu, Memory, Id, Name, Threads, Gpu, GpuMemory, UserName, ParentId, CpuTime, Status, Disk or Network.
Default Cpu. Use SortBy Memory to sort processes by memory usage.
Missing counters stay last in either direction.

```yaml
Type: fullscreen.Monitoring.ProcessSort
DefaultValue: Cpu
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

### -TimeoutSeconds

Remote request timeout, 1 to 120 seconds.
Default 15.
Collection errors preserve the last snapshot and display stale/error status while retrying with backoff.

```yaml
Type: System.Int32
DefaultValue: 15
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

### -Tree

Group processes by parent.
T toggles the tree; Left collapses, Right expands, Enter toggles a branch.
Filtering retains matching processes and their ancestors.

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

### -GraphType

Selects Line, Area (default), Bars, Step, Scatter or Stem for all monitor histories.
Press G to cycle these styles while the monitor is open. Applies to local and remote
monitoring, dashboard panels and detail/header graphs. Sixel is selected automatically
when supported; other terminals use native XenoAtom line/sparkline controls.
Histogram and BoxPlot summarize distributions and are available on reusable Chart
controls rather than monitor panels with time axes.

```yaml
Type: fullscreen.Charts.GraphKind
DefaultValue: Area
SupportsWildcards: false
Aliases:
- GraphKind
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues:
- Line
- Area
- Bars
- Step
- Scatter
- Stem
HelpMessage: ''
```

### -View

Processes, Services, Network, Gpu, Users or Dashboard.
Overview remains an alias for Processes.
Keys 1 through 6 switch to Processes, Services, Network, GPU, Users and Dashboard respectively. F8 also opens Users.
Processes is the default. Press A or right-click a process/service for confirmed actions.
Service headers provide sorting and column filters; F6 cycles sort columns and R reverses.
P pauses; Q or Ctrl+Q exits.

```yaml
Type: fullscreen.Monitoring.MonitorView
DefaultValue: Processes
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
