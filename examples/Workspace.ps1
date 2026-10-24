<#
.SYNOPSIS
Compose the file browser, local monitor and log view in one tabbed workspace.
.DESCRIPTION
Reuses fullscreen app components. File and monitor workers start only when displayed;
the log view displays explicit demonstration data. Switch tabs to stop hidden workers.
.PARAMETER Path
Literal starting directory for the file browser. Defaults to the current PowerShell location.
.PARAMETER Theme
Built-in palette or native custom theme hashtable.
.PARAMETER Minimal
Hide the custom application's surrounding chrome.
.PARAMETER DefinitionOnly
Return the layout without reading directories, sampling processes or opening a terminal.
.EXAMPLE
./Workspace.ps1 -Path $HOME -Theme 'Dark Modern'
.EXAMPLE
./Workspace.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
The native Invoke-Tui result, including the last selected file path in State.file.
.NOTES
File operations retain the embedded file browser's confirmation behavior.
#>
[CmdletBinding()]
param(
    [string]$Path = $PWD.Path,
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$files = New-TuiControl FileBrowser -Id Files -Property @{
    Path = $Path
    SelectionBind = 'file'
    Height = 24
}
$monitor = New-TuiControl PerformanceMonitor -Id Monitor -Property @{
    View = 'Dashboard'
    RefreshInterval = 1000
    Height = 24
}
$logs = New-TuiControl FLogView -Id Logs -Text 'Demonstration log snapshot' -Bind lines -Property @{
    Height = 20
}
$tabs = New-TuiControl Tabs -Id root -Child @(
    $files
    $monitor
    $logs
)
$layout = New-TuiDefinition -Title 'Workspace' -Root $tabs -State @{
    lines = @(
        '{"level":"Information","message":"Demonstration workspace ready"}'
        '{"level":"Warning","message":"Demonstration warning; no external service was contacted"}'
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent
