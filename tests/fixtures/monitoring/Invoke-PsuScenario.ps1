param([Parameter(Mandatory)][string]$WorkPath)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '../../..')
New-Item -ItemType Directory -Path $WorkPath -Force | Out-Null
Import-Module ./output/fullscreen.psd1
$moduleRoot = Join-Path $WorkPath 'Modules'
$fixture = Join-Path $moduleRoot 'Devolutions.PowerShellUniversal/Devolutions.PowerShellUniversal.psm1'
New-Item -ItemType Directory -Path (Split-Path -Parent $fixture) -Force | Out-Null
$env:PSModulePath = $moduleRoot + [IO.Path]::PathSeparator + $env:PSModulePath
[IO.File]::WriteAllText($fixture, @'
$script:polls = 0
Add-Content -LiteralPath $env:FULLSCREEN_PSU_IMPORT_COUNT -Value imported
function Get-PSUJob {
    [CmdletBinding()]
    param($ComputerName, $AppToken, $First, $OrderBy, $OrderDirection)
    $script:polls++
    if ($script:polls -gt 1) {
        throw 'Simulated job source failure'
    }
    [pscustomobject]@{
        Id = 42
        Script = [pscustomobject]@{
            Name = 'Example.ps1'
        }
        Status = 'Completed'
        StartTime = [datetime]::Now
        EndTime = [datetime]::Now
    }
}
function Get-PSUEndpoint {
    [CmdletBinding()]
    param($ComputerName, $AppToken)
    if ($script:polls -gt 1) {
        throw 'Simulated endpoint source failure'
    }
    [pscustomobject]@{
        Url = '/api/demo'
        Methods = @('GET')
        Disabled = $false
        Authentication = $true
    }
}
'@)
$env:FULLSCREEN_PSU_IMPORT_COUNT = Join-Path $WorkPath 'import-count.txt'
[IO.File]::WriteAllText($env:FULLSCREEN_PSU_IMPORT_COUNT, '')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$server = "http://127.0.0.1:$($listener.LocalEndpoint.Port)"
$responder = Start-ThreadJob -ArgumentList $listener -ScriptBlock {
    param($listener)
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream)
            while ($reader.ReadLine()) {
            }
            $wire = [Text.Encoding]::UTF8.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: 2`r`nConnection: close`r`n`r`n{}")
            $stream.Write($wire)
        }
        finally {
            $client.Dispose()
        }
    }
}
function Invoke-Tui {
    param([Parameter(ValueFromPipeline)]$Definition, $State, $TickInterval, $OnTick)
    process {
        foreach ($key in $Definition.State.Keys) {
            $State[$key] = $Definition.State[$key]
        }
        $jobId = $State.monitor.Pending.Id
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 12 -and $State.jobsInfo -notlike '*stale since*') {
            & $OnTick $null $State
            Start-Sleep -Milliseconds 60
        }
        if ($State.jobs.Count -ne 1 -or $State.jobs[0].Id -ne 42 -or $State.jobsInfo -notlike '*stale since*' -or $State.endpointsInfo -notlike '*stale since*') {
            throw "Last successful snapshot was lost: $($State.jobsInfo) / $($State.endpointsInfo)"
        }
        if ($State.monitor.Pending.Id -ne $jobId -or $State.probes[0].Calls -lt 2) {
            throw 'Poll worker was recreated or stopped sampling'
        }
        if (@(Get-Content $env:FULLSCREEN_PSU_IMPORT_COUNT).Count -ne 1) {
            throw 'Client module was imported more than once'
        }
    }
}
try {
    $before = @(Get-Job).Count
    $fixtureToken = [securestring]::new()
    $fixtureToken.AppendChar('x')
    ./examples/PowerShellUniversalMonitor.ps1 -Server $server -Token $fixtureToken -RefreshSeconds 5
    if (@(Get-Job).Count -ne $before) {
        throw 'Worker job leaked after close'
    }
    'PSU persistent worker, single module import, stale-data retention, probes and cleanup passed.'
}
finally {
    $listener.Stop()
    Remove-Job -Job $responder -Force
    Remove-Item Env:FULLSCREEN_PSU_IMPORT_COUNT
}
