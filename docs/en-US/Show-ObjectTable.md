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

Collects pipeline input and opens it in an interactive table.
Formatted uses the object's normal PowerShell table formatting. Inspect shows
the selected object's property kinds, short types, values and highlighted method declarations,
or collection items and dictionary entries. JSON shows a highlighted snapshot of the current object.
Browsing methods never invokes them. Circular references,
depth limits and failing properties are marked rather than causing errors. Switch
tabs with Tab, Shift+Tab, 1-3 or the tab headers.

Enter or I inspects the selected item, and Backspace, Escape or Back returns to
the parent with its selection, filter and sort restored. Filter with / using
plain text, wildcard: or regex: patterns; sort by clicking a column or with S,
and reverse with R. G groups, C chooses columns, Space marks rows, and M, F10 or
right-click opens the menu. In JSON, / searches and Enter or N finds the next
match. Drag or use Shift+arrows to select JSON text, Ctrl+A selects all, and Ctrl+C copies.
Values are colored by type, and ANSI colors in displayed values are kept.

With PassThru, Enter in the root Formatted table returns the marked objects or the current selection; Q
cancels. Displaying and serializing objects runs their property getters, which can
execute custom code.

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

Open the Services property in Inspect. The array shows Name and Port columns;
filter with /, click a header to sort, and open an individual service. Switch
between Inspect and JSON without leaving that node. Back returns one level.

```powershell
[pscustomobject]@{
    Name = 'production'
    Services = @(
        [pscustomobject]@{ Name = 'api'; Port = 443; Health = @{ Ready = $true } }
        [pscustomobject]@{ Name = 'worker'; Port = 8080; Health = @{ Ready = $false } }
    )
} | fobject -View Inspect
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
Display is capped at 1 MiB.

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

Return original marked objects, or the current object, when Enter accepts in the root
Formatted table. Enter inside Inspect or JSON does not accept.
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

Literal property names for the Formatted table, in order.
Overrides automatic display metadata.
Missing properties produce empty cells.
Without Property, Formatted uses display metadata.

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
In Formatted this can be a custom label such as CPU(s).
With Property, use the supplied names.
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
Individual syntax and chart colors can also be overridden; unspecified colors inherit from Base.
For example: `-Theme @{ Base = 'Monokai'; Keyword = '#ff79c6'; ChartBlue = '#58d5e6' }`.
Use `(Get-FullscreenTheme Monokai).Colors` to discover all editable keys. See Get-FullscreenTheme for the syntax, series, grid/axis/label and ramp roles.
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

Initial tab: Formatted (default), Inspect or Json.
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
