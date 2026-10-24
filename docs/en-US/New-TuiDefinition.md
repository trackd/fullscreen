---
document type: cmdlet
external help file: fullscreen.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: fullscreen
ms.date: 10-24-2026
PlatyPS schema version: 2024-05-01
title: New-TuiDefinition
---

# New-TuiDefinition

## SYNOPSIS

Create or validate a TUI definition as native PowerShell data.

## SYNTAX

### Parts (Default)

```
New-TuiDefinition [[-Title] <string>] [-Root <psobject>] [-State <hashtable>] [<CommonParameters>]
```

### Definition

```
New-TuiDefinition -Definition <psobject> [<CommonParameters>]
```

## ALIASES

None.


## DESCRIPTION

Without arguments, returns a starter layout.
Supply Title, Root and State to build a layout from hashtables or PSCustomObjects.
Pipe existing definitions through Definition to validate and normalize them.
Returns a plain PowerShell object with hashtable/array children, ready for Invoke-Tui, the designer or Export-TuiDefinition.
No terminal opens and no handlers run.

## EXAMPLES

### Example 1: Create a starter form

The returned definition can be edited without opening a terminal.

```powershell
$layout = New-TuiDefinition -Title 'My tool' -State @{ name = 'World' }
```

### Example 2: Build a native control tree

Root accepts ordinary hashtables or PSCustomObjects.

```powershell
$layout = New-TuiDefinition -Title 'Greeting' -Root @{
    Id = 'root'; Type = 'VStack'; Children = @(
        @{ Id = 'hello'; Type = 'Text'; Text = 'Hello PowerShell' }
        @{ Id = 'close'; Type = 'Button'; Text = 'Done'; Close = $true }
    )
}
$layout | Invoke-Tui
```

### Example 3: Validate and clone a definition

The input object remains unchanged; the result is a normalized copy.

```powershell
$layout = New-TuiDefinition
$copy = $layout | New-TuiDefinition
$copy.Title = 'A separate copy'
```

## PARAMETERS

### -Definition

Existing version-1 definition as a hashtable, PSCustomObject or JSON string.
Accepts pipeline input and returns a validated copy.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Definition
  Position: Named
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Root

Root control as a hashtable or PSCustomObject.
Omit to use the starter form.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Parts
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -State

Serializable default state as a hashtable.
Supply live values separately to Invoke-Tui.

```yaml
Type: System.Collections.Hashtable
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Parts
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

Application title, up to 512 characters.
Defaults to My application.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Parts
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

### System.Management.Automation.PSObject

Existing version-1 definition as a hashtable, PSCustomObject or JSON string.

## OUTPUTS

### System.Management.Automation.PSObject

A validated PSCustomObject definition with native hashtable and array children.

## NOTES

## RELATED LINKS

- [fullscreen documentation](https://github.com/trackd/fullscreen)
