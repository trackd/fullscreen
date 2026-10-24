<#
.SYNOPSIS
Browse assembly types and decompile a selected type inside fullscreen.
.DESCRIPTION
Prototype using the public ISpy Get-Type and Expand-Type cmdlets. It does not load
the target assembly into the host. TextMate is imported when available to provide
C# highlighting; otherwise source is displayed as plain text. No modules are installed.
.PARAMETER LiteralPath
Assembly path. Defaults to the assembly containing System.Math.
.PARAMETER NamePattern
ISpy wildcard type-name filter. Defaults to *Math* for a small starting view.
.PARAMETER MethodName
Optional method name passed to Expand-Type to narrow the decompilation.
.PARAMETER Theme
fullscreen theme name.
.PARAMETER Minimal
Hide the surrounding browser chrome.
.PARAMETER ListOnly
Return ISpy metadata without opening a terminal.
.PARAMETER SourceOnly
Return decompiled C# for the first matching type without opening a terminal.
.EXAMPLE
./ISpyBrowser.ps1
.EXAMPLE
./ISpyBrowser.ps1 -LiteralPath ./MyLibrary.dll -NamePattern '*Service*'
.EXAMPLE
./ISpyBrowser.ps1 -MethodName Abs -SourceOnly
.OUTPUTS
ISpy metadata with ListOnly; C# strings with SourceOnly; otherwise no output.
.NOTES
Import fullscreen first. Requires ISpy; TextMate is optional. This prototype caps
the inventory at 20 public types. Decompiled C# is reconstructed source.
#>
[CmdletBinding()]
param(
    [string]$LiteralPath = [Math].Assembly.Location,
    [string]$NamePattern = '*Math*',
    [string]$MethodName,
    [string]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$ListOnly,
    [switch]$SourceOnly
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Module -Name ISpy)) {
    Import-Module -Name ISpy -ErrorAction Stop
}
$assemblyPath = (Resolve-Path -LiteralPath $LiteralPath -ErrorAction Stop).ProviderPath
$types = @(
    ISpy\Get-Type -Path $assemblyPath -NamePattern $NamePattern -PublicOnly |
        Select-Object -First 20
)
if ($ListOnly.IsPresent) {
    return $types
}
if ($types.Count -eq 0) {
    throw 'No public types match NamePattern.'
}
$requestedMethod = $MethodName
$decompile = {
    param(
        $target
    )
    $arguments = @{
        InputType = $target
        ErrorAction = 'Stop'
    }
    if ($requestedMethod) {
        $arguments.MethodName = $requestedMethod
    }
    [string]::Join([Environment]::NewLine,
        [string[]]@(
            ISpy\Expand-Type @arguments
        ))
}.GetNewClosure()
if ($SourceOnly.IsPresent) {
    return (& $decompile $types[0])
}
try {
    if (-not (Get-Module -Name TextMate)) {
        Import-Module -Name TextMate -ErrorAction Stop
    }
}
catch {
    Write-Verbose 'TextMate is unavailable. Source will use plain text.'
}

$inventory = New-TuiControl FTable -Id types -Bind types -Text 'ASSEMBLY TYPES' -Property @{
    Columns = @(
        'Name',
        'Namespace',
        'Kind'
    )
    SelectionBind = 'selectedType'
    Width = 55
    Height = 28
}
$source = New-TuiControl Code -Id source -Bind source -Property @{
    Language = 'csharp'
    Height = 28
}
$panes = New-TuiControl HStack -Id panes -Child @(
    $inventory
    $source
) -Property @{
    Spacing = 1
}
$load = New-TuiControl Button -Id decompile -Text 'Decompile selected type'
$root = New-TuiControl VStack -Id root -Child @(
    $panes
    $load
)
$layout = New-TuiDefinition -Title 'ISpy assembly browser' -Root $root
$null = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State @{
        types = $types
        selectedType = $null
        source = '// Select a type and press Enter or Decompile. Source loads only when requested.'
        decompile = $decompile
    } -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Id -eq 'types' -and $tuiEvent.Kind -eq 'Submit') {
            $selected = $tuiEvent.Value
        }
        elseif ($tuiEvent.Id -eq 'decompile' -and $tuiEvent.Kind -eq 'Click') {
            $selected = $state.selectedType
        }
        else {
            return
        }
        if ($null -eq $selected) {
            return
        }
        try {
            $state.source = & $state.decompile $selected
        }
        catch {
            $state.source = '// Decompilation failed: ' + $_.Exception.Message
        }
    }
