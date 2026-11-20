BeforeAll {
    $scenarioRoot = Join-Path $PSScriptRoot 'fixtures/monitoring'
    $engine = Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')
}

Describe 'Monitoring example workflows' {
    It 'Retains route history and selection, saves and replays offline, and rejects malformed sessions' {
        $workPath = Join-Path $TestDrive 'ping'
        $output = & $engine -NoProfile -NonInteractive -File (Join-Path $scenarioRoot 'Invoke-PingScenario.ps1') -WorkPath $workPath 2>&1
        $code = $LASTEXITCODE
        $code | Should -Be 0 -Because ($output -join "`n")
        $session = Get-Content -LiteralPath (Join-Path $workPath 'ping-session.json') -Raw | ConvertFrom-Json -Depth 32
        $session.Sweeps.Count | Should -Be 10
        $session.Sweeps[-1].Rows[1].N | Should -Be 13
    }

    It 'Uses one PSU poll worker and client import, retains stale results, and cleans up on exit' {
        $workPath = Join-Path $TestDrive 'psu'
        $output = & $engine -NoProfile -NonInteractive -File (Join-Path $scenarioRoot 'Invoke-PsuScenario.ps1') -WorkPath $workPath 2>&1
        $code = $LASTEXITCODE
        $code | Should -Be 0 -Because ($output -join "`n")
        @(Get-Content -LiteralPath (Join-Path $workPath 'import-count.txt')).Count | Should -Be 1
    }
}
