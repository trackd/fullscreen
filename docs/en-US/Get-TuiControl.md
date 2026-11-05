---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-25-2026
PlatyPS schema version: 2024-05-01
title: Get-TuiControl
---

# Get-TuiControl

## SYNOPSIS

Discover reusable fullscreen controls as native PowerShell data.

## SYNTAX

### __AllParameterSets

```
Get-TuiControl [[-Name] <string[]>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Lists the controls that can be used in custom TUI layouts, with their category,
state binding, events and properties. Use it to discover what a control does and
which properties it accepts before adding it with New-TuiControl or in the
designer. Discovery never opens a terminal.

The default view groups controls by category and shows Name, Binding and
Description. Use Format-List * for the full metadata: PropertyDetails gives each
property's type, default and allowed values, and NativeProperties lists the
settings a native widget accepts in Options. An unknown literal name writes an
ObjectNotFound error; an unmatched wildcard returns nothing.

Native widgets keep their XenoAtom names. FTable and FLogView are fullscreen's
object table and log view; Table and LogControl are the native widgets. Grid
accepts Options.Rows and Options.Columns arrays such as @('Auto', '*', '2*').
UserList shows login sessions; set IncludeProcesses to add account processes and
process actions. Use New-TuiControl to compose controls with native sample data.

## EXAMPLES

### Example 1: List reusable controls

Inspect the catalog without opening a terminal.

```powershell
Get-TuiControl
```

### Example 2: Inspect table properties and events

Find the native binding and event contract before composing a table.

```powershell
Get-TuiControl -Name FTable | Select-Object -Property *
```

Expand property details to see allowed values without collection truncation:

```powershell
(Get-TuiControl FTable).PropertyDetails |
    Format-Table Name, Type, DefaultValue, Choices, Description -Wrap
```

### Example 3: Find controls by name

Wildcard matching is case-insensitive and literal names support tab completion.

```powershell
Get-TuiControl -Name '*View', '*Monitor'
```

## PARAMETERS

### -Name

Control names or wildcard patterns. Defaults to all controls. Literal names complete without I/O.

```yaml
Type: System.String[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### System.Management.Automation.PSObject

The default table groups controls by Category and shows Name, Binding and Description. Use Format-List * to see the full metadata.

Returns Name, Category, Description, ReusedBy, Binding, Events, Properties and Live metadata. Discovery never opens a terminal, reads directories or starts collectors. Unknown literal names emit an ObjectNotFound error; unmatched wildcards return no output.

## NOTES

All output is native PowerShell data. Saved definitions contain only layout and default values; keep live objects in Invoke-Tui -State.

## RELATED LINKS

[Invoke-Tui](Invoke-Tui.md)
