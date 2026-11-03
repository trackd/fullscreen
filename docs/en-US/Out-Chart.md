---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-31-2026
PlatyPS schema version: 2024-05-01
title: Out-Chart
---

# Out-Chart

## SYNOPSIS

Charts pipeline objects interactively and lets you change fields and graph types.

## SYNTAX

### __AllParameterSets

```
Out-Chart [[-InputObject] <psobject[]>] [-XProperty <Object>] [-YProperty <Object[]>]
 [-GraphType <GraphKind>] [-ChartRenderer <ChartRenderer>] [-Title <string>] [-MaximumCount <int>] [-PassThru] [-Theme <Object>]
 [-GroupBy <Object>] [-Aggregate <ChartAggregate>] [-LowerProperty <Object>] [-UpperProperty <Object>]
 [-Refresh <scriptblock>] [-RefreshInterval <int>] [-Configuration <string>] [-Minimal] [<CommonParameters>]
```

## DESCRIPTION

Opens an interactive chart of the original pipeline objects. Choose X/Y fields,
graph type and Top/Bottom/First/Last filters from the menu. Both axis parameters
accept property names or calculated properties such as
@{ Name = 'MB'; Expression = { $_.WorkingSet64 / 1MB } }; Custom… opens the same
editor in the UI. Sixel is automatic, with terminal-cell fallback.

Click a point to select it; double-click or press D/Enter for its properties.
T opens the filtered data table. Wheel zooms, brackets pan, Shift-drag selects
a range; Space pauses refresh. Chart settings include axes, series and export.
With -PassThru, Q/F10 returns the current filtered objects in view order;
Escape cancels. Without -PassThru, the viewer produces no pipeline output.

## EXAMPLES

### Example 1: Choose properties interactively

```powershell
Get-Process | Out-Chart
```

Open the complete process objects and use X, Y and G to choose fields and style.

### Example 2: Compare two properties with labels

```powershell
Get-Process | Out-Chart -XProperty ProcessName -YProperty CPU, WorkingSet64 -GraphType Bars
```

Compare properties on a shared scale. Choose properties with comparable units
for a useful comparison, or toggle individual properties in the Y menu.

### Example 3: Plot scalar numbers

```powershell
1, 4, -2, 7, 3 | Out-Chart -GraphType Line -Title 'Samples'
```

Numbers use their input order as X. You can change the graph type in the viewer.

### Example 4: Define calculated axes

```powershell
Get-Process | Out-Chart -XProperty @{ Label = 'Process'; Expression = { $_.ProcessName } } -YProperty @{ Name = 'Memory MB'; Expression = { $_.WorkingSet64 / 1MB } } -GraphType Bars
```

The viewer retains the complete Process objects. Add or edit calculated fields
inside the X/Y menus, or use Memory MB for Top/Bottom ranking.

### Example 5: Return an interactively chosen subset

```powershell
$subset = Get-Process | Out-Chart -YProperty WorkingSet64 -PassThru
$subset | Select-Object ProcessName, CPU, WorkingSet64
```

Choose Top 30 (or another filter) inside the viewer, then press Q or F10 to
return those original Process objects. Escape cancels without returning rows.

### Example 6: Aggregate filtered objects

```powershell
Get-Process | Out-Chart -GroupBy ProcessName -YProperty WorkingSet64 -Aggregate Sum -GraphType HorizontalBars
```

Grouping keeps the original member objects available for inspection and PassThru.
GroupBy without Aggregate counts members. Row filters run before aggregation.

### Example 7: Refresh a live snapshot

```powershell
Out-Chart -Refresh { Get-Process } -RefreshInterval 2000 -XProperty ProcessName -YProperty CPU
```

Refresh runs on the UI thread; keep the script quick. Space pauses/resumes.
Opening the data table freezes refresh until it closes. Applying a table subset
or selecting a range pauses refresh for stable inspection.

### Example 8: Show absolute error bounds

```powershell
$measurements | Out-Chart -XProperty Time -YProperty Mean -LowerProperty Low -UpperProperty High -GraphType ErrorBars
```

Low and High are absolute lower/upper values. The Chart menu can export SVG,
plot-only PNG, CSV or configuration JSON. Reopen saved settings with
`$measurements | Out-Chart -Configuration ./chart.json`.

## PARAMETERS

### -InputObject

Complete objects to chart. Accepts pipeline input or an array.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: false
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -XProperty

Initial X property name or calculated-property hashtable containing Name/Label
and Expression. Omit to use input order. Can be changed inside the viewer.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -YProperty

Initial Y property names and/or calculated-property hashtables. Alias Property.
Omit to choose a numeric default; the Y menu can add or remove properties and
create calculated fields without rerunning the pipeline.

```yaml
Type: System.Object[]
DefaultValue: None
SupportsWildcards: false
Aliases: [Property]
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -GraphType

Initial graph type: Line, Area, Bars, Step, Scatter, Stem, Histogram, BoxPlot,
HorizontalBars, StackedBars, Heatmap or ErrorBars.

```yaml
Type: fullscreen.Charts.GraphKind
DefaultValue: Line
SupportsWildcards: false
Aliases: [GraphKind]
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Title

Title displayed above the chart.

```yaml
Type: System.String
DefaultValue: Chart
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -MaximumCount

Maximum retained object count, between 1 and 1000000. Excess input raises an error
before the viewer opens; records are not silently dropped.
Refresh collection stops at the first excess object and reports overflow too.

```yaml
Type: System.Int32
DefaultValue: 100000
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -PassThru

Return every original object in the current filtered view and selected range when Q or F10 is
pressed. Output follows view order, including Top/Bottom ranking. Escape or
Ctrl+C cancels without output. The default is display-only. Calculated chart
fields are not added to the returned objects. Output contains the same original
object instances, never chart points, group summaries or reconstructed objects.
Chart operations do not add, remove or overwrite input properties, even when a
calculated field uses an existing property name.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -GroupBy

Property name, scriptblock using $_, or calculated-property hashtable used to group
filtered rows. GroupBy alone counts members. Scriptblock results are cached for
the current input snapshot; the original objects are preserved.

Example: `Get-Process | Out-Chart -GroupBy { $_.ProcessName -replace '\d+$' }`

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Aggregate

None, Count, Sum, Average, Minimum or Maximum. Row filters run before grouping; missing numeric values are ignored except by Count.

```yaml
Type: fullscreen.Charts.ChartAggregate
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -LowerProperty

Property name or calculated property containing absolute lower error bounds.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -UpperProperty

Property name or calculated property containing absolute upper error bounds.

```yaml
Type: System.Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Refresh

Script returning replacement whole objects. Runs on the UI thread at RefreshInterval; a failure pauses refresh and preserves the previous snapshot.

```yaml
Type: System.Management.Automation.ScriptBlock
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -RefreshInterval

Milliseconds between refreshes, from 100 to 60000.

```yaml
Type: System.Int32
DefaultValue: 1000
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Configuration

Path to configuration JSON exported from the viewer. Restores fields, calculations, filters and presentation; explicit XProperty, YProperty and GraphType override saved choices.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Theme

Built-in theme name or custom palette, as supported by the other fullscreen apps.

```yaml
Type: System.Object
DefaultValue: Default
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -Minimal

Hide the bottom keyboard hint row. Field and graph selection remain available.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```

### -ChartRenderer

Select Auto, Sixel, Native or Regis.
Regis is an experimental,
explicitly selected Contour vector backend; Area, Histogram and BoxPlot use cell
fallback. Auto continues to select Sixel with cell fallback.

```yaml
Type: fullscreen.Charts.ChartRenderer
DefaultValue: Auto
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
```

### CommonParameters

This cmdlet supports the common parameters. For more information, see
about_CommonParameters.

## INPUTS

### System.Management.Automation.PSObject

Complete pipeline objects or numeric samples.

## OUTPUTS

### System.Management.Automation.PSObject

With PassThru, accepted original objects in filtered view order. Otherwise no output.

## NOTES

The viewer uses the common fullscreen host and requires PowerShell 7.6 or later.

## RELATED LINKS

[Show-ObjectTable](Show-ObjectTable.md)
