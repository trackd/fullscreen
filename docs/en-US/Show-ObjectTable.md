---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Show-ObjectTable
---

# Show-ObjectTable

## SYNOPSIS

Explore PowerShell objects in a fullscreen table.

## SYNTAX

### __AllParameterSets

```
Show-ObjectTable [[-InputObject] <psobject[]>] [-Property <string[]>] [-ExcludeProperty <string[]>]
 [-Title <string>] [-Filter <string>] [-SortBy <string>] [-GroupBy <string>] [-Descending]
 [-PassThru] [-View <ObjectView>] [-JsonDepth <int>] [-MaximumCount <int>] [-Theme <Object>]
 [-Minimal] [<CommonParameters>]
```

## ALIASES

fobject


## DESCRIPTION

Buffers pipeline input, then opens three lazy tabs: Formatted, Expanded and JSON (Properties is also accepted as a view name).
Formatted resolves Get-FormatData TableControl headers and DisplayEntry expressions directly using PSPropertyExpression, following Out-ConsoleGridView and Out-GridView.
It falls back to DefaultDisplayPropertySet, scalars or readable properties.
Expanded retains the complete readable property table at the root. Within an object it lists members; within an array it shows item properties as sortable columns.
JSON uses a bounded snapshot of the current inspected node, or the selected original object at the root. Circular references,
depth limits and failing getters are marked in the JSON instead of warning or
recursing indefinitely. Keys, strings, numbers, literals, brackets and braces use theme colors.
Each table is built only when opened and cached; JSON caches up to 16 visited objects.
Tab, Shift-Tab, 1/2/3 or mouse headers switch views.
Values and getters are evaluated on the PowerShell thread.
Enter or I inspects the selected item or member. M, F10 or right-click opens the menu, including property drill-down, view selection, search and sorting. The breadcrumb shows the current node. Backspace, Esc (after clearing any filter) or clicking Back returns to the parent, restoring its selection, filter and sorting. Changing view keeps the current node.
Use / to filter tables (plain text, wildcard: or regex:). S cycles sort columns, R reverses, and clicking a column sorts it; clicking again reverses. G groups; C selects columns; Space marks. In JSON, / searches and Enter or N finds the next matching line. Tables also color values by their actual type: property names and nested objects use Accent, strings use Success, numbers use Purple, and booleans, enums and nulls use Warning. Null values display as null; the inspector Type column uses the muted color. Property/display errors use Error. ANSI colors and text decorations from display values override the type color through the styled text parser; terminal commands are discarded.
Marks and selected original identity survive tab switches.
With PassThru, Enter accepts marked objects or the current selection at the root or JSON tab.
Q cancels.
Display expressions, getters and JSON serialization may run custom property code.

Press F12 to toggle the diagnostic overlay (frame timings, repaint work, host
output and keyboard focus). F12 works in minimal mode and while dialogs are open.
Press it again to hide diagnostics.

## EXAMPLES

### Example 1: Pick processes as original objects

PassThru returns the selected objects rather than formatted strings.

```powershell
$chosen = Get-Process |
    Show-ObjectTable -Property ProcessName,Id,CPU,WorkingSet -PassThru
$chosen | Select-Object ProcessName, Id
```

### Example 2: Inspect JSON lazily

The JSON view is generated only when opened.

```powershell
Get-Process | Show-ObjectTable -View Json -JsonDepth 3
```

### Example 3: Use native objects and property completion

Property completion uses explicitly bound input without evaluating upstream commands.

```powershell
$items = @(
    [pscustomobject]@{ Name = 'api'; Environment = 'prod'; Count = 3 }
    [pscustomobject]@{ Name = 'worker'; Environment = 'test'; Count = 1 }
)
Show-ObjectTable -InputObject $items -Property Name,Environment,Count -GroupBy Environment -Minimal
```

### Example 4: Explore nested arrays and properties

Open a row, then its Services member. The array shows Name and Port columns;
filter with /, click a header to sort, and open an individual service. Switch
between Expanded and JSON without leaving that node. Back returns one level.

```powershell
[pscustomobject]@{
    Name = 'production'
    Services = @(
        [pscustomobject]@{ Name = 'api'; Port = 443; Health = @{ Ready = $true } }
        [pscustomobject]@{ Name = 'worker'; Port = 8080; Health = @{ Ready = $false } }
    )
} | fobject -View Expanded
```

## PARAMETERS

### -Descending

Reverse the initial sort direction within groups.

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

### -ExcludeProperty

Literal property names initially hidden.
C can reveal them.
At least one column must remain visible.

```yaml
Type: System.String[]
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

Initial case-insensitive filter across displayed cell values. Plain text matches
substrings; regex: and wildcard: prefixes select regular-expression and wildcard
matching. Slash edits the filter. Regular expressions have a 50 ms timeout.

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

### -GroupBy

Literal property name used as the primary sort key, arranging equal values contiguously.
SortBy orders rows within each group.
G cycles grouping.

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

### -InputObject

Objects from the pipeline or an explicit array.
Null entries are skipped; input is collected before the UI opens.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: false
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -JsonDepth

Maximum depth of the bounded JSON snapshot, 1 through 20; default 4.
Only the selected object is snapshotted when first viewed. Circular references,
depth limits and failing getters become markers. Collections and property maps
are limited to 1000 entries each, with a shared budget of 10000 complex values.
Display is capped at 1 MiB after serialization; up to 16 visited objects are cached.

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

### -MaximumCount

Maximum buffered input objects, default 100000.
Exceeding the limit raises a terminating error rather than silently dropping records.

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

### -PassThru

Return original marked objects, or the current object, when Enter accepts at a root
table or the JSON tab. Enter inside a nested property inspection does not accept.
Q or Escape cancels with no output.

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

### -Property

Literal property names to display in both table tabs, in order.
Overrides automatic display metadata.
Missing properties produce empty cells.
Without Property, Formatted uses display metadata and Expanded uses the union of up to 256 readable properties.

```yaml
Type: System.String[]
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

### -SortBy

Initial displayed column label for typed sorting.
In Formatted this can be a custom label such as CPU(s); in Expanded it is a literal property name.
With Property, both use the supplied names.
Raw numeric and timestamp values sort by type.

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

### -Title

Table title.

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

### -View

Initial lazy tab: Formatted (default), Expanded or Json. Properties is accepted as another name for Expanded.
Unopened tabs perform no metadata lookup, getter evaluation or serialization.

```yaml
Type: fullscreen.Objects.ObjectView
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

### System.Management.Automation.PSObject

Original input objects are retained for selection and PassThru. Accepts individual objects from the pipeline or an explicit array through InputObject.

## OUTPUTS

### System.Management.Automation.PSObject

With PassThru, the selected original input objects. Without PassThru, no objects are emitted.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
