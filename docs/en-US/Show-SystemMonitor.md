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

Monitor local or remote processes, services, network traffic, GPUs and user
sessions in a fullscreen terminal app. Dashboard histories stay current across
views until paused.

Use 1-6 to switch views, / to search, C to choose columns, T to toggle process
trees, P to pause and Q to quit. Enter or D opens details; A or right-click opens
available actions. Press ? for the full key reference.

Actions use your existing permissions and require confirmation. Logging off a
Windows session closes its applications and can lose unsaved work. Unsupported
metrics are hidden; temporarily inaccessible values appear as unknown.
Per-process network and GPU metrics require supported Windows providers.
CPU is a percentage of total processor capacity; RSS includes shared pages.

Remote metrics and actions apply to the target computer. Targets support
PowerShell 7 or Windows PowerShell 5.1 with .NET Framework 4.7.2 or later and
must allow in-memory assembly loading. If sampling fails, the last data remains
visible while the monitor retries.

## EXAMPLES

### Example 1: Monitor local processes

Use the process tree with optional cumulative CPU time.

```powershell
Show-SystemMonitor -Tree -Column Name,Id,UserName,Cpu,CpuTime,Memory
```

### Example 2: Show a fullscreen network table

Minimal mode hides surrounding UI.

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

### Example 4: Keep dashboard histories current while managing sessions

Start in Users and continue sampling dashboard metrics when switching views.

```powershell
ftop -View Users -SampleInterval 2000
```

## PARAMETERS

### -ChartRenderer

Select Auto (Sixel with native fallback), Sixel, Native or Regis (experimental
Contour vectors). Use GraphType independently to choose the plot shape.

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
Defaults to Name, UserName, Cpu, Memory, Network, Gpu, Threads, Id.
CpuTime is cumulative CPU time and is off by default. Unsupported target columns
are hidden; supported counters with missing samples show as unknown. C opens the chooser. Left/Right scrolls overflow columns
(Shift+Left/Right in tree mode); column widths remain fixed between samples.

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
