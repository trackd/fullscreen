BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'MonitorActions' -Tag 'Integration' {
    It 'Protects process identity and stops only the owned child through the action worker' {
        $assembly = (Get-Command fullscreen\Show-SystemMonitor).ImplementingType.Assembly
        $type = $assembly.GetType('fullscreen.Monitoring.MonitorActions', $true)
        $source = $type.GetField('Script', [Reflection.BindingFlags]'Static,NonPublic').GetRawConstantValue()
        $tokens = $null; $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
        [bool]($parseErrors.Count) | Should -BeFalse -Because 'Monitor action script is invalid.'
        $action = [scriptblock]::Create($source)
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.ArgumentList.Add('-NoProfile'); $start.ArgumentList.Add('-Command'); $start.ArgumentList.Add('Start-Sleep -Seconds 60')
        $child = [Diagnostics.Process]::Start($start)
        try {
            ($child.Id) | Should -Not -Be ($PID) -Because 'Refusing to operate on the test host.'
            $collectorAssembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'fullscreen.Collector' } | Select-Object -First 1
            if ($null -eq $collectorAssembly) {
                $collectorAssembly = $assembly.GetReferencedAssemblies() | Where-Object Name -eq 'fullscreen.Collector' | ForEach-Object { [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($assembly).LoadFromAssemblyName($_) }
            }
            $sampler = [Activator]::CreateInstance($collectorAssembly.GetType('fullscreen.Monitoring.ProcessSampler', $true))
            try {
                $null = $sampler.Sample($false, $false)
                $sample = $sampler.Rows | Where-Object Id -eq $child.Id | Select-Object -First 1
                ($null) | Should -Not -Be ($sample) -Because 'Owned process was not collected.'
                $birth = $sample.StartTimeTicks
            }
            finally { if ($sampler -is [IDisposable]) { $sampler.Dispose() } }
            { & $action StopProcess 'owned fixture' $child.Id ($birth + 1) } | Should -Throw '*identity changed*'
            $child.HasExited | Should -BeFalse
            $actionType = $assembly.GetType('fullscreen.Monitoring.MonitorAction', $true)
            $request = [Activator]::CreateInstance($assembly.GetType('fullscreen.Monitoring.MonitorActionRequest', $true), @([Enum]::Parse($actionType, 'StopProcess'), 'owned fixture', [int]$child.Id, [long]$birth))
            $null = $type.GetMethod('ExecuteLocal', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @($request, [Threading.CancellationToken]::None))
            [bool]($child.WaitForExit(5000)) | Should -BeTrue -Because 'Stop process did not terminate the owned test child.'
            $command = [Activator]::CreateInstance((Get-Command fullscreen\Show-SystemMonitor).ImplementingType)
            ($command.View.ToString()) | Should -Be ('Processes') -Because 'ftop must start on Processes by default.'
        }
        finally { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() }
    }
}
