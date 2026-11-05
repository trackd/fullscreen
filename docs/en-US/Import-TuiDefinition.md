---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-24-2026
PlatyPS schema version: 2024-05-01
title: Import-TuiDefinition
---

# Import-TuiDefinition

## SYNOPSIS

Load and validate a saved TUI definition as native PowerShell data.

## SYNTAX

### __AllParameterSets

```
Import-TuiDefinition [-Path] <string> [<CommonParameters>]
```

## ALIASES

None.


## DESCRIPTION

Reads versioned layout JSON or a .psd1 PowerShell data file into a plain PowerShell
object suitable for editing, Invoke-Tui, the designer or Export-TuiDefinition.
PSD1 files contain one literal hashtable and are read using PowerShell's safe AST
evaluator, without invoking code. Comments and multiline strings are supported.
fullscreen applies its own 1 MiB, 100000 syntax-node and document bounds rather
than the built-in reader's default small key-count limit.
Applies the same schema and size limits as Invoke-Tui.
Does not open a terminal or execute handlers.

## EXAMPLES

### Load a native PowerShell layout

```powershell
New-TuiDefinition -Title 'Native layout' | Export-TuiDefinition -Path ./MyTool.tui.psd1 -Script:$false -Force
$layout = Import-TuiDefinition -LiteralPath ./MyTool.tui.psd1
$layout | Show-TuiDesigner
```

### Example 1: Load a saved layout

The path is literal and the file contains data, not executable handlers.

```powershell
New-TuiDefinition -Title 'Saved layout' | Export-TuiDefinition -Path ./tool.tui.json -Script:$false -Force
$layout = Import-TuiDefinition -Path ./tool.tui.json
```

### Example 2: Import FileInfo objects

FullName binds to Path by property name.

```powershell
$directory = Join-Path ([IO.Path]::GetTempPath()) ("fullscreen-layouts-{0}" -f [guid]::NewGuid())
$null = New-Item -ItemType Directory -Path $directory
New-TuiDefinition -Title 'Sample' | Export-TuiDefinition -Path (Join-Path $directory 'sample.tui.json') -Script:$false
Get-ChildItem -LiteralPath $directory -Filter '*.tui.json' |
    Import-TuiDefinition |
    Select-Object Title, Version
```

### Example 3: Edit and run a layout

Imported layouts use native PowerShell data.

```powershell
New-TuiDefinition -Title 'Editable layout' | Export-TuiDefinition -Path './tool[1].tui.json' -Script:$false -Force
$layout = Import-TuiDefinition -LiteralPath './tool[1].tui.json'
$layout.Title = 'My customized tool'
$layout | Invoke-Tui -Theme 'Dark Modern'
```

## PARAMETERS

### -Path

Literal local FileSystem path.
LiteralPath and FullName are aliases.
Accepts pipeline input by property name, including FileInfo.FullName.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- LiteralPath
- FullName
ParameterSets:
- Name: (All)
  Position: 0
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

### System.String

FileInfo objects bind FullName to Path by property name.

## OUTPUTS

### System.Management.Automation.PSObject

A validated PSCustomObject definition with native hashtable and array children.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
