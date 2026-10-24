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

PropertyDetails contains native PowerShell metadata for each applicable property:
its type, default value, enum choices, group and explanation. The designer uses
the same catalog to display applicable fields.

The default table groups controls by Category and shows Name, Binding and Description. Use Format-List * to see the full metadata.

Returns Name, Category, Description, ReusedBy, Binding, Events, Properties, PropertyDetails, NativeProperties and Live metadata. Discovery never opens a terminal, reads directories or starts collectors. Unknown literal names emit an ObjectNotFound error; unmatched wildcards return no output.

The catalog contains 90 controls: 76 native widgets from the bundled XenoAtom UI
version, plus fullscreen controls and convenience names. CodeEditor is excluded. Run
`examples/ControlGallery.ps1` from the repository to explore every catalog
control, or add DefinitionOnly to inspect its layout without opening a terminal.

Native widgets keep their framework names. FTable and FLogView are fullscreen's
object table and parsed log snapshot; Table and LogControl are the native widgets.
NativeProperties lists the scalar properties accepted in Options, including enum
choices. Grid also accepts Options.Rows/Columns arrays such as @('Auto', '*', '2*').
EnumSelect accepts EnumType from its PropertyDetails choices. No native widget
requires an additional module. CodeEditor is intentionally excluded.

Use examples/NativeControls.ps1 -Name Grid,Slider,TreeView for a focused native
gallery, or -All for all native widgets. F12 opens diagnostics.

UserList collects login sessions on its own. IncludeProcesses enables account-owned
process totals, expansion and confirmed process actions; L toggles this at runtime.
SelectionBind receives a UserViewRow with typed Logins and an optional Process.
FileCommander exposes the dual-pane file manager. ObjectExplorer exposes the same
formatted, expanded and JSON inspection with back navigation as fobject.

## EXAMPLES

### Example 1: List reusable controls

Inspect the catalog without opening a terminal or starting workers.

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

All output is native PowerShell data. Saved definitions contain only layout and default values; keep live objects in Invoke-Tui -State. See the custom TUI cookbook for composition examples.

## RELATED LINKS

[Invoke-Tui](Invoke-Tui.md)

[Custom TUI examples](../../README.md#examples)
