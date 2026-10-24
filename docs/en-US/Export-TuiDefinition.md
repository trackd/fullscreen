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

Validate and save a native PowerShell TUI definition.

## SYNTAX

### __AllParameterSets

```
Export-TuiDefinition [-Definition] <psobject> [-Path] <string> [-PassThru] [-Script] [-LinkedLayout] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

None.


## DESCRIPTION

Writes versioned layout data atomically as UTF-8 without a BOM.
Paths ending in .psd1 select a native PowerShell data file; other paths retain JSON output.
PSD1 exports contain literal hashtables, arrays and scalar values and can be read
with Import-TuiDefinition or Import-PowerShellDataFile (SkipLimitCheck for large layouts).
Accepts a hashtable, PSCustomObject or JSON definition and preserves the original input object.
Supports WhatIf and Confirm.
The parent directory must exist.
No terminal opens and no handlers run.
With Script, writes a runnable PowerShell script with an event stub for each
control event, a shared state dictionary and a periodic-update stub. The default
script embeds its layout; exporting over an existing inline script replaces it.
Inline scaffolds omit default control properties while preserving IDs, types,
nondefault settings and all state defaults. Generated scripts accept Theme,
Minimal, State, TickInterval and DefinitionOnly. Runtime State overrides layout
defaults; DefinitionOnly returns portable data without applying live overrides.
Add LinkedLayout to put layout data in a sibling .tui.psd1 instead. Later linked
exports update that data and preserve the script byte for byte. An unrelated
existing script is rejected in linked mode.
WhatIf and Confirm identify both the script and sibling layout in linked mode.
An approved linked export can replace an existing sibling layout; use WhatIf
or Confirm when reviewing the target paths. Each file is replaced atomically;
the script and layout are separate writes.

## EXAMPLES

### Export a portable PowerShell data file

```powershell
$layout = New-TuiDefinition -Root @{ Id = 'hello'; Type = 'Text'; Text = 'Hello' }
$layout | Export-TuiDefinition -Path ./MyTool.tui.psd1
$layout = Import-PowerShellDataFile -LiteralPath ./MyTool.tui.psd1
$layout | Invoke-Tui
```

### Export a commented application scaffold

```powershell
New-TuiDefinition -Title 'My tool' |
    Export-TuiDefinition -Path ./MyTui.ps1 -Script -LinkedLayout
./MyTui.ps1 -DefinitionOnly | Show-TuiDesigner
./MyTui.ps1 -Minimal -State @{ name = 'Ada' } -TickInterval 500
```

### Example 1: Save a starter form

The parent directory must already exist.

```powershell
New-TuiDefinition -Title 'My tool' | Export-TuiDefinition -Path ./tool.tui.json
```

### Example 2: Preview a save

WhatIf validates the definition and skips the file write.

```powershell
New-TuiDefinition | Export-TuiDefinition -Path ./tool.tui.json -WhatIf
```

### Example 3: Save and retain the validated definition

PassThru returns a native definition after the atomic save.

```powershell
$layout = New-TuiDefinition |
    Export-TuiDefinition -LiteralPath './tool[1].tui.json' -PassThru
$layout | Invoke-Tui
```

## PARAMETERS

### -LinkedLayout

With Script, export a runnable .ps1 that loads a sibling .tui.psd1 layout.
Later exports update the layout and preserve the linked script byte for byte.
An unrelated existing script is rejected. Without LinkedLayout, Script embeds
the layout inline. All individual file writes use temporary-file replacement.

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

Export runnable, commented PowerShell scaffolding. Without LinkedLayout, the script
embeds a native hashtable and needs no external layout file. With LinkedLayout,
it loads a sibling .tui.psd1 and later exports preserve custom handlers.

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

### -Confirm

Prompts you for confirmation before running the cmdlet.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- cf
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

Literal output FileSystem path; LiteralPath is an alias.
Existing files are replaced atomically after ShouldProcess approves the write.

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

### -WhatIf

Runs the command in a mode that only reports what would happen without performing the actions.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- wi
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

Version-1 definitions as hashtables, PSCustomObjects or JSON strings.

## OUTPUTS

### System.Management.Automation.PSObject with PassThru; otherwise none

With PassThru, a validated copy of the saved definition. Otherwise no objects are emitted.

### System.Management.Automation.PSObject

With PassThru, a validated copy of the saved definition. Otherwise no objects are emitted.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
