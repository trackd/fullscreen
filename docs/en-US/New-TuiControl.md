---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-25-2026
PlatyPS schema version: 2024-05-01
title: New-TuiControl
---

# New-TuiControl

## SYNOPSIS

Create a validated native control definition for composition in custom TUIs.

## SYNTAX

### __AllParameterSets

```
New-TuiControl [-Type] <string> -Id <string> [-Text <string>] [-Bind <string>] [-Child <psobject[]>]
 [-Property <hashtable>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Builds an independent, validated control definition. Combine controls using Child,
then run them with New-TuiDefinition and Invoke-Tui. Creating a definition does
not access files or start background work.

Property supplies control-specific options listed by Get-TuiControl and cannot
override named base parameters. See the control cookbook for styling, charts,
composite widgets and specialized input formats.

## EXAMPLES

### Example 1: Create a bound text control

```powershell
$name = New-TuiControl TextBlock -Id name -Bind name
$layout = New-TuiDefinition -Title 'Hello' -Root $name -State @{ name = 'Ada' }
$layout | Invoke-Tui
```

### Example 2: Compose controls

```powershell
$root = New-TuiControl VStack -Id root -Child @(
    (New-TuiControl TextBlock -Id heading -Text 'My application')
    (New-TuiControl Button -Id accept -Text 'Accept')
)
New-TuiDefinition -Title 'My tool' -Root $root | Invoke-Tui
```

### Example 3: Configure a chart

```powershell
New-TuiControl Chart -Id load -Bind samples -Property @{
    GraphKind = 'Line'
    Minimum = 0
    Maximum = 100
}
```

Supply samples through Invoke-Tui State. Get-TuiControl Chart lists its properties.

## PARAMETERS

### -Bind

State key for the control value. FTable and FLogView read a collection; text controls read strings; Chart reads a numeric collection. Scalar Chart bindings use the timestamped-history adapter. Defaults to no binding.

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

### -Child

Child definitions for a VStack, HStack, Border or Tabs. Children is an alias. Noncontainers reject children; Border accepts at most one.
Set TabTitle in a child's Property hashtable to give its tab a readable caption
without changing the child's event ID or content Text.

```yaml
Type: System.Management.Automation.PSObject[]
DefaultValue: ''
SupportsWildcards: false
Aliases:
- Children
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

### -Id

Unique control ID: 1–64 characters, beginning with a letter or underscore. IDs must be unique in the composed document.

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

### -Property

Hashtable of control-specific schema properties, for example Columns, SelectionBind, Path, View, RefreshInterval, Height, Width, Minimal, Maximum or TabTitle. FTable accepts an optional ColumnWidths array with one width of 2–1000 terminal cells per entry in Columns. Omit ColumnWidths to size columns automatically. TabTitle accepts up to 128 characters and is used when the control is a tab page; blank uses its Id. Unknown keys and invalid values are rejected.

```yaml
Type: System.Collections.Hashtable
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

### -Text

Display text, title, initial input text or Markdown. Defaults to an empty string.

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

### -Type

A supported control type. Validated names are case-insensitive and tab-complete from the current catalog.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
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

### System.Management.Automation.PSObject

Builds an independent PSCustomObject containing a validated control node. Combine it with other controls using Child, or place it in New-TuiDefinition and Invoke-Tui. Property contains control-specific options listed by Get-TuiControl; it cannot override named base parameters. Creating definitions does not access files or start background work.

## NOTES

All output is native PowerShell data. Saved definitions contain only layout and default values; keep live objects in Invoke-Tui -State.

## RELATED LINKS

[Invoke-Tui](Invoke-Tui.md)

- [Control cookbook](https://github.com/trackd/fullscreen/blob/main/docs/guides/controls.md)
