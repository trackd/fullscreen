#!/usr/bin/env pwsh
#Requires -Version 7.6
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests,
    [switch]$BuildAndTestOnly,
    [string]$Task
)

$ErrorActionPreference = 'Stop'
$requiredModules = @{
    InvokeBuild = '5.14.23'
    'Microsoft.PowerShell.PlatyPS' = '1.0.1'
    Pester = '6.2.0'
    'Microsoft.PowerShell.PSResourceGet' = '1.2.0'
}
$modulePath = Join-Path $PSScriptRoot '.tools' 'Modules'
$env:PSModulePath = $modulePath + [IO.Path]::PathSeparator + $env:PSModulePath
$missingModules = @(
    foreach ($module in $requiredModules.GetEnumerator()) {
        if (-not (Get-Module -ListAvailable -FullyQualifiedName @{
            ModuleName = $module.Key
            RequiredVersion = $module.Value
        })) {
            '{0}={1}' -f $module.Key, $module.Value
        }
    }
)
if ($missingModules.Count -gt 0) {
    if (Get-Module -ListAvailable -FullyQualifiedName @{ ModuleName = 'ModuleFast'; RequiredVersion = '0.6.1' }) {
        Import-Module ModuleFast -RequiredVersion 0.6.1 -ErrorAction Stop
    }
    else {
        $bootstrapPath = Join-Path $PSScriptRoot '.tools' 'ModuleFast.ps1'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $bootstrapPath) -Force
        Invoke-WebRequest -Uri 'https://github.com/JustinGrote/ModuleFast/releases/download/v0.6.1/ModuleFast.ps1' -OutFile $bootstrapPath
        & $bootstrapPath -Release 'v0.6.1'
    }
    $installParameters = @{
        Specification = $missingModules
        Destination = $modulePath
        Source = 'https://pwsh.gallery/index.json'
        NoPSModulePathUpdate = $true
        NoProfileUpdate = $true
        ThrottleLimit = 2
        ErrorAction = 'Stop'
    }
    Install-ModuleFast @installParameters
}
$moduleImports = @(
    foreach ($module in $requiredModules.GetEnumerator()) {
        $installed = Get-Module -ListAvailable -FullyQualifiedName @{
            ModuleName = $module.Key
            RequiredVersion = $module.Value
        } | Select-Object -First 1
        if (-not $installed) {
            throw "Build dependency $($module.Key) $($module.Value) is not available after bootstrap."
        }
        $installed.Path
    }
)

$buildParameters = @{
    Configuration = $Configuration
    SkipHelp      = $SkipHelp.IsPresent
    SkipTests     = $SkipTests.IsPresent
    File          = Join-Path $PSScriptRoot 'fullscreen.build.ps1'
    Task          = 'All'
    Result        = 'buildResult'
    Safe          = $true
}

if ($Task) {
    $buildParameters.Task = $Task
}
elseif ($BuildAndTestOnly.IsPresent) {
    $buildParameters.Task = 'BuildAndTest'
}

# Keep the module assembly out of the calling session so it can be rebuilt.
$worker = {
    param($parameters, $moduleImports)
    $ErrorActionPreference = 'Stop'
    foreach ($module in $moduleImports) {
        Import-Module -Name $module -ErrorAction Stop
    }
    Invoke-Build @parameters
    if ($buildResult.Error) { exit 1 }
}
$powerShellPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
& $powerShellPath -NoProfile -Command $worker -args $buildParameters, $moduleImports
exit $LASTEXITCODE
