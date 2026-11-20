param([Parameter(Mandatory)][string]$WorkPath)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '../../..')
New-Item -ItemType Directory -Path $WorkPath -Force | Out-Null
Import-Module ./output/fullscreen.psd1
$sessionPath = Join-Path $WorkPath 'ping-session.json'
function Invoke-Tui {
    param([Parameter(ValueFromPipeline)]$Definition, [switch]$Minimal, $State, $TickInterval, $OnTick, $OnEvent)
    process {
        foreach ($key in $Definition.State.Keys) {
            $State[$key] = $Definition.State[$key]
        }
        if ($State.trace.Replay) {
            & $OnTick $null $State
            if ($State.trace.Pending -or $State.timeline.Count -ne 10 -or $State.hopCount -ne 2) {
                throw 'Replay must restore sweeps without probes'
            }
            if ($State.timeline[-1].Sweep -ne 13) {
                throw 'Replay lost sweep identity'
            }
            & $OnEvent @{
                Id = 'older'
                Kind = 'Click'
            } $State
            if ($State.timeline[-1].Sweep -ge 13) {
                throw 'Replay history did not move'
            }
            'Replay restored the saved route and navigated historical samples.'
            return
        }
        for ($sweep = 1; $sweep -le 12; $sweep++) {
            $address = if ($sweep -ge 8) {
                '192.0.2.9'
            }
            else {
                '192.0.2.1'
            }
            $replies = [Fullscreen.Examples.HopReply[]]@(
                [Fullscreen.Examples.HopReply]::new(1, $address, 'TtlExpired', 15 + $sweep, $false)
                [Fullscreen.Examples.HopReply]::new(2, '192.0.2.2', 'Success', 20 + $sweep, $true)
            )
            $result = [Fullscreen.Examples.RouteSweep]::new('192.0.2.2', $replies, '')
            $State.trace.Pending = [Threading.Tasks.Task]::FromResult($result)
            & $OnTick $null $State
        }
        if ($State.timeline.Count -ne 10 -or $State.hops[1].N -ne 12 -or $State.hopCount -ne 2) {
            throw 'Bounded history and cumulative counts differ'
        }
        if ($State.routeChanges -notmatch 'hop 1') {
            throw 'Route transition missing'
        }
        if ($State.detail -notmatch 'p95.*jitter') {
            throw 'Selected-hop statistics missing'
        }
        & $OnEvent @{
            Id = 'route'
            Kind = 'Selection'
            Value = $State.hops[0]
        } $State
        if ($State.highlight -ne 'Hop 1' -or [double]::IsFinite($State.timeline[0].'Hop 1')) {
            throw 'Responder change must leave earlier samples as gaps'
        }
        & $OnEvent @{
            Id = 'pause'
            Kind = 'Click'
        } $State
        $before = $State.timeline
        $State.trace.Pending = [Threading.Tasks.Task]::FromResult($result)
        & $OnTick $null $State
        if (-not [object]::ReferenceEquals($before, $State.timeline) -or $State.trace.Sweep -ne 13) {
            throw 'Pause must freeze view while collecting'
        }
        & $OnEvent @{
            Id = 'older'
            Kind = 'Click'
        } $State
        if ($State.timeline[-1].Sweep -ge 12) {
            throw 'Older must move the focus window'
        }
        & $OnEvent @{
            Id = 'newer'
            Kind = 'Click'
        } $State
        & $OnEvent @{
            Id = 'save'
            Kind = 'Click'
        } $State
        if ($State.playback -notlike 'Saved*') {
            throw $State.playback
        }
        $a = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
        $document = $a.GetType('fullscreen.Custom.TuiDocument').GetMethod('FromObject').Invoke($null, @($Definition))
        $app = $a.GetType('fullscreen.Custom.CustomTuiApp').GetConstructors()[0].Invoke([object[]]@($document.PSObject.BaseObject, $State, $null, $null, 100, $false, $null, [Threading.CancellationToken]::None))
        try {
            if (!$app.Controls.ContainsKey('lossHeatmap') -or !$app.Controls.ContainsKey('distribution')) {
                throw 'Chart tabs missing'
            }
        }
        finally {
            $app.Dispose()
        }
        'Live view, pause, route changes, loss grid, distribution, save and cumulative counts passed.'
    }
}
./examples/PingPlotter.ps1 -HistorySize 10 -SessionPath $sessionPath
./examples/PingPlotter.ps1 -ReplayPath $sessionPath

# Corrupt each trust-boundary field in an otherwise valid saved session.
foreach ($field in 'Hop', 'Avg', 'Sweep') {
    $saved = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json -Depth 32
    switch ($field) {
        'Hop' {
            $saved.Sweeps[0].Rows[0].Hop = 64
        }
        'Avg' {
            $saved.Sweeps[0].Rows[0].Avg = 'NaN'
        }
        'Sweep' {
            $saved.Sweeps[0].Sweep = 1.5
        }
    }
    $invalid = Join-Path $WorkPath "invalid-$field.json"
    $saved | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $invalid
    $rejected = $false
    try {
        ./examples/PingPlotter.ps1 -ReplayPath $invalid
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Invalid replay $field was accepted."
    }
}
