[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ModulePath,

    [Parameter(Mandatory)]
    [ValidateSet('Name', 'Hashtable', 'Palette', 'Ordered', 'PSCustomObject')]
    [string]$ThemeCase
)

$ErrorActionPreference = 'Stop'
Import-Module $ModulePath -Force
$definition = New-TuiDefinition
$theme = switch ($ThemeCase) {
    Name { 'Monokai' }
    Hashtable { @{ Base = 'Dark Modern'; Accent = '#123'; ChartBlue = '#456'; Keyword = '#789'; Operator = '#abc' } }
    Palette { (Get-FullscreenTheme Monokai).Colors }
    Ordered { [ordered]@{ Accent = '#abcdef'; ChartSequentialHigh = '#123456'; DefaultToken = '#654321' } }
    PSCustomObject { [pscustomobject]@{ Base = 'Monokai'; Accent = '#123'; Command = '#456'; Variable = '#789' } }
}
$failure = $null
try { $null = Invoke-Tui -Definition $definition -Theme $theme -ErrorAction Stop }
catch { $failure = $_.Exception.Message }
[pscustomobject]@{
    InputRedirected = [Console]::IsInputRedirected
    OutputRedirected = [Console]::IsOutputRedirected
    Message = $failure
} | ConvertTo-Json -Compress
