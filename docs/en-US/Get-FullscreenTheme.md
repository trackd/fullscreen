---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-24-2026
PlatyPS schema version: 2024-05-01
title: Get-FullscreenTheme
---

# Get-FullscreenTheme

## SYNOPSIS

Inspect built-in fullscreen palettes as native PowerShell data.

## SYNTAX

### __AllParameterSets

```
Get-FullscreenTheme [[-Name] <string[]>] [<CommonParameters>]
```

## ALIASES

None.


## DESCRIPTION

Lists built-in palette names and a Colors hashtable for each. The default table shows actual theme foreground, accent, success, warning and error swatches on the theme background. Preview contains the ANSI-styled swatch text; Colors retains the editable hex values. Redirected output follows PowerShell output-rendering settings.
Copy or edit Colors and pass it to any interactive cmdlet with Theme.
No private CLR or XenoAtom types are returned and no terminal opens.

## EXAMPLES

### Example 1: List built-in themes

Inspect the available built-in names and editable palettes.

```powershell
Get-FullscreenTheme
```

### Example 2: Customize a built-in palette

Colors is a native hashtable; editing it does not mutate the built-in.

```powershell
$colors = (Get-FullscreenTheme -Name Monokai).Colors
$colors.Accent = '#58d5e6'
Show-SystemMonitor -Theme $colors
```

### Example 3: Find light themes

Name accepts wildcard patterns without loading any theme files.

```powershell
Get-FullscreenTheme -Name 'Light*'
```

## PARAMETERS

### -Name

Built-in palette names or wildcard patterns.
Defaults to all themes.
Names and historical VS Code aliases are case-insensitive; Tab completes canonical names.

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

Each object has Name and an editable Colors hashtable suitable for the Theme parameter.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
