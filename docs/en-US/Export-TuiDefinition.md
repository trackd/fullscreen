---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-28-2026
PlatyPS schema version: 2024-05-01
title: Export-TuiDefinition
---

# Export-TuiDefinition

## SYNOPSIS

Export a TUI application scaffold or a layout with a companion script.

## SYNTAX

### __AllParameterSets

```
Export-TuiDefinition [-Definition] <psobject> [-Path] <string> [-PassThru] [-Script] [-LinkedLayout] [-Force] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

None.


## DESCRIPTION

Exports a validated layout as UTF-8 to the required literal Path. The extension
chooses the output: .ps1 creates a runnable script with the layout embedded;
.psd1 or .json saves layout data and creates a same-stem .ps1 companion if missing.
For example, MyTool.tui.json creates MyTool.tui.ps1, which loads that exact JSON
file. PSD1 files can also be read with Import-PowerShellDataFile (use
SkipLimitCheck for large layouts). The parent directory must exist. No terminal
opens and no handlers run during export.

Scaffolds include help, shared state, control-event stubs and an OnTick callback.
Edit the .ps1 to add application behavior. Generated scripts accept Theme,
Minimal, State, TickInterval and DefinitionOnly.
Runtime State overrides layout defaults; DefinitionOnly returns the complete
layout without running the UI. Omitted Theme/Minimal use host defaults; explicit
values, including Minimal:$false, are forwarded.

For .ps1 exports, LinkedLayout uses a sibling .tui.psd1 instead of embedding
the layout. Data-file exports are already linked to their companion. Script
remains accepted for compatibility but is no longer required; use Script:$false
with .psd1 or .json when you want data only.

Existing scripts are never overwritten, even with Force. Re-exporting layout data
requires Force; existing companion scripts keep all custom code. An inline .ps1
cannot be re-exported to the same path: choose a new path and transfer the layout
edits yourself, or use a linked layout for future updates. A linked .ps1 export
rejects an existing script that is not linked to its expected layout.
Existing companions are preserved as-is, so if one loads a different definition,
update its import path yourself. WhatIf changes nothing; Confirm shows the
affected targets. PassThru returns the validated definition only after saving.

## EXAMPLES

### Export a self-contained application

The script contains the layout, help and event/update stubs to customize.

```powershell
New-TuiDefinition -Title 'My tool' | Export-TuiDefinition -Path ./MyTool.ps1
./MyTool.ps1 -DefinitionOnly | Show-TuiDesigner
```

### Export a portable PowerShell data file

```powershell
$layout = New-TuiDefinition -Root @{ Id = 'hello'; Type = 'Text'; Text = 'Hello' }
$layout | Export-TuiDefinition -Path ./MyTool.tui.psd1
$layout = Import-PowerShellDataFile -LiteralPath ./MyTool.tui.psd1
./MyTool.tui.ps1
```

### Export a commented application scaffold

```powershell
New-TuiDefinition -Title 'My tool' |
    Export-TuiDefinition -Path ./MyTui.ps1 -LinkedLayout
./MyTui.ps1 -DefinitionOnly | Show-TuiDesigner
./MyTui.ps1 -Minimal -State @{ name = 'Ada' } -TickInterval 500
```

### Update a layout without changing application code

Force replaces only the layout file. MyTool.tui.ps1 remains unchanged.
WhatIf previews both the target and operation without changing files.

```powershell
$edited = New-TuiDefinition -Title 'Updated tool'
$edited | Export-TuiDefinition -Path ./MyTool.tui.psd1 -Force
$edited | Export-TuiDefinition -Path ./MyTool.tui.psd1 -Force -WhatIf
$saved = $edited | Export-TuiDefinition -Path ./MyTool.tui.psd1 -Force -PassThru
```

### Export layout data without a scaffold

```powershell
$layout = New-TuiDefinition -Title 'Layout only'
$layout | Export-TuiDefinition -Path ./layout.json -Script:$false
```

## PARAMETERS

### -LinkedLayout

For a .ps1 path, load a sibling .tui.psd1 instead of embedding the layout.
Script is not required. Data-file exports already use a companion script.
Existing linked scripts remain unchanged; replacing layout data requires Force.
Cannot be combined with Script:$false.

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
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Script

Retained for compatibility; scaffolding is now created by default according to
the output extension. Use -Script:$false with .psd1 or .json to export data only,
without creating a companion script. Existing scripts remain unchanged.

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

### -Force

Allow replacement of existing layout data. Never overwrites application scripts
and never bypasses WhatIf or Confirm.

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

### -Definition

One version-1 layout to validate and save.
Accepts pipeline input.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -PassThru

Return the validated layout as a plain PowerShell object after saving.
No output when WhatIf skips the write.

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

### -Path

Required literal .ps1, .psd1 or .json FileSystem path; LiteralPath is an alias.
There is no default filename. Scripts are never overwritten. Use Force to replace
existing layout data and WhatIf or Confirm to review the affected targets.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- LiteralPath
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
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

Version-1 definitions as hashtables, PSCustomObjects or JSON strings.

## OUTPUTS

### System.Management.Automation.PSObject with PassThru; otherwise none

With PassThru, a validated copy of the saved definition. Otherwise no objects are emitted.

### System.Management.Automation.PSObject

With PassThru, a validated copy of the saved definition. Otherwise no objects are emitted.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
