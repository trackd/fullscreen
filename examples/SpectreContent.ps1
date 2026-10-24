<#
.SYNOPSIS
Embed a Spectre renderable in a custom fullscreen TUI.
.DESCRIPTION
Accepts an IRenderable from an already loaded module such as TextMate. The object
stays in runtime State; only its binding is saved in the layout.
.PARAMETER Renderable
Spectre.Console.Rendering.IRenderable object to display, for example Format-TextMate output.
.PARAMETER Theme
fullscreen theme name or custom palette.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return the layout without requiring a renderable or opening a terminal.
.EXAMPLE
$code = 'Get-Process | Sort-Object CPU' | Format-TextMate -Language powershell
./SpectreContent.ps1 -Renderable $code
.EXAMPLE
./SpectreContent.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
The layout when DefinitionOnly is supplied; otherwise no output.
.NOTES
Import fullscreen first. TextMate/Spectre is optional and supplied by the caller.
#>
[CmdletBinding()]
param(
    [object]$Renderable,
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$layout = New-TuiDefinition -Title 'Spectre inside fullscreen' -Root @{
    Id = 'root'
    Type = 'VStack'
    Spacing = 1
    Children = @(
        @{
            Id = 'intro'
            Type = 'Text'
            Text = 'Spectre content · resize to reflow · Escape closes'
        }
        @{
            Id = 'content'
            Type = 'Spectre'
            Bind = 'renderable'
        }
        @{
            Id = 'close'
            Type = 'Button'
            Text = 'Close'
            Close = $true
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
if ($null -eq $Renderable) {
    throw 'Supply -Renderable from TextMate or another loaded Spectre module.'
}
$null = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State @{
        renderable = $Renderable
    }
