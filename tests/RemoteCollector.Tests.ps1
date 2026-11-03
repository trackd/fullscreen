BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Remote collector' -Tag 'Integration' {
    It 'Deploys, samples, cancels and cleans up without closing the caller runspace (<Edition>)' -ForEach @(
        @{ Edition = 'PowerShell 7'; WindowsPowerShell = [switch]$false }
        @{ Edition = 'Windows PowerShell 5.1'; WindowsPowerShell = [switch]$true }
    ) {
        if ($WindowsPowerShell.IsPresent -and -not $IsWindows) { Set-ItResult -Skipped -Because 'Windows PowerShell compatibility requires Windows'; return }
        $command = Get-Command -Name fullscreen\Show-SystemMonitor
        $sourceType = $command.ImplementingType.Assembly.GetType('fullscreen.Monitoring.RemoteMonitorSource', $true)
        $sampleMethod = $sourceType.GetMethods() | Where-Object { $_.Name -eq 'Sample' -and $_.GetParameters().Count -eq 2 } | Select-Object -First 1
        $frameType = $sampleMethod.GetParameters()[0].ParameterType
        $child = if ($WindowsPowerShell.IsPresent) {
            [System.Management.Automation.Runspaces.PowerShellProcessInstance]::new([version]'5.1', $null, $null, $false)
        }
        else { [System.Management.Automation.Runspaces.PowerShellProcessInstance]::new() }
        [bool]($child.Process.StartInfo.ArgumentList.Contains('-NoProfile')) | Should -BeTrue -Because 'The test child must run with -NoProfile.'
        $remoteRunspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateOutOfProcessRunspace($null, $child)
        $source = $null
        try {
            $remoteRunspace.Open()
            $source = [Activator]::CreateInstance($sourceType, @($remoteRunspace, 'PSRP stdio test', 15))
            $frame = [Activator]::CreateInstance($frameType)
            $source.Sample($frame, [System.Threading.CancellationToken]::None)
            $source.Sample($frame, [System.Threading.CancellationToken]::None)
            $cancellation = [System.Threading.CancellationTokenSource]::new()
            try {
                $cancellation.Cancel()
                $wasCancelled = $false
                try { $source.Sample($frame, $cancellation.Token) }
                catch {
                    $wasCancelled = $_.Exception.InnerException -is [System.OperationCanceledException]
                    if (-not $wasCancelled) { throw }
                }
                [bool]($wasCancelled) | Should -BeTrue -Because 'Collection ignored cancellation.'
            }
            finally { $cancellation.Dispose() }
            ($frame.ProcessCount) | Should -BeGreaterThan (0) -Because 'Remote snapshot did not contain the expected machine/process data.'
            ($frame.MachineName) | Should -Be ([Environment]::MachineName) -Because 'Remote snapshot did not contain the expected machine/process data.'
            $collectorProcess = $null
            for ($index = 0; $index -lt $frame.ProcessCount; $index++) {
                if ($frame.Processes[$index].Id -eq $child.Process.Id) { $collectorProcess = $frame.Processes[$index]; break }
            }
            ($null) | Should -Not -Be ($collectorProcess) -Because 'Remote collector process missing from its snapshot.'
            ($collectorProcess.CpuTimeTicks) | Should -BeGreaterThan (0) -Because 'Remote cumulative CPU time was not preserved.'
            ($collectorProcess.CpuPercent) | Should -BeGreaterOrEqual (0) -Because 'Remote CPU must use 0-100 percent.'
            ($collectorProcess.CpuPercent) | Should -BeLessOrEqual (100) -Because 'Remote CPU must use 0-100 percent.'
            $frame.Capabilities | Should -Not -BeNullOrEmpty -Because 'Capabilities must be supplied by the collector host over PSRP.'
            $commandLine = $source.ReadCommandLine($collectorProcess, [Threading.CancellationToken]::None)
            $commandLine | Should -Match '(?i)(pwsh|powershell)' -Because 'The deployed collector must inspect command lines without the UI assembly.'
            [bool](($IsWindows -or $IsLinux) -and [string]::IsNullOrEmpty($collectorProcess.UserName)) | Should -BeFalse -Because 'Remote process username was lost during collection/serialization.'
            [bool](($IsWindows -or $IsLinux) -and $null -eq $collectorProcess.ParentId) | Should -BeFalse -Because 'Remote parent process ID was lost during collection/serialization.'
            $metricsType = ($sourceType.GetMethods() | Where-Object { $_.Name -eq 'Sample' -and $_.GetParameters().Count -eq 3 } | Select-Object -First 1).GetParameters()[1].ParameterType
            foreach ($metricMask in @(8, 4, 0)) {
                $source.Sample($frame, [Enum]::ToObject($metricsType, $metricMask), [System.Threading.CancellationToken]::None)
                ($frame.ProcessCount) | Should -Be (0) -Because 'Remote collector gathered hidden process/GPU/CPU data.'
                ($frame.GpuCount) | Should -Be (0) -Because 'Remote collector gathered hidden process/GPU/CPU data.'
                ($null) | Should -Be ($frame.CpuPercent) -Because 'Remote collector gathered hidden process/GPU/CPU data.'
                [bool]($metricMask -eq 4 -and $null -eq $frame.TotalMemory) | Should -BeFalse -Because 'Remote memory-only request lost its memory counters.'
                [bool]($metricMask -ne 4 -and $null -ne $frame.TotalMemory) | Should -BeFalse -Because 'Remote collector gathered hidden memory data.'
                [bool]($metricMask -ne 8 -and $frame.NetworkCount -ne 0) | Should -BeFalse -Because 'Remote collector gathered hidden network data.'
            }
            $source.Sample($frame, [Enum]::ToObject($metricsType, 1024), [System.Threading.CancellationToken]::None)
            $frame.UserStatus | Should -Not -Be 'Not requested by the current view'
            $frame.ProcessCount | Should -Be 0 -Because 'Session inventory alone must not enumerate processes.'
            $handle = $sourceType.GetField('_handle', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($source)
            $source.Dispose()
            $source = $null
            ($remoteRunspace.RunspaceStateInfo.State) | Should -Be ('Opened') -Because 'The caller-owned runspace was closed.'
            $probe = [System.Management.Automation.PowerShell]::Create()
            try {
                $probe.Runspace = $remoteRunspace
                $null = $probe.AddScript('[fullscreen.Monitoring.ProcessOwnerReader].Assembly.GetName().Name')
                ([string]$probe.Invoke()[0]) | Should -Be 'fullscreen.Collector'
                $probe.Commands.Clear()
                $null = $probe.AddScript('@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "fullscreen" -or $_.GetName().Name -like "XenoAtom*" }).Count')
                ([int]$probe.Invoke()[0].BaseObject) | Should -Be 0 -Because 'Remote details must not load the UI assembly or dependencies.'
                $probe.Commands.Clear()
                $null = $probe.AddScript('param($Handle) [fullscreen.Monitoring.CollectorSessions]::IsOpen($Handle)').AddArgument($handle)
                $result = $probe.Invoke()
                ([bool]$result[0].BaseObject) | Should -BeFalse -Because 'Remote collector handle was not closed.'
                $probe.Commands.Clear()
                $null = $probe.AddScript('@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "fullscreen.Collector" }).Count')
                $beforeCount = [int]$probe.Invoke()[0].BaseObject
                for ($cycle = 0; $cycle -lt 3; $cycle++) {
                    $reopened = [Activator]::CreateInstance($sourceType, @($remoteRunspace, 'PSRP reopen test', 15))
                    try { $reopened.Sample($frame, [Enum]::ToObject($metricsType, 0), [System.Threading.CancellationToken]::None) }
                    finally { $reopened.Dispose() }
                }
                $probe.Commands.Clear()
                $null = $probe.AddScript('@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq "fullscreen.Collector" }).Count')
                ([int]$probe.Invoke()[0].BaseObject) | Should -Be ($beforeCount) -Because 'Repeated monitoring leaked collector assembly loads.'
            }
            finally {
                $probe.Dispose()
            }

        }
        finally {
            if ($null -ne $source) { $source.Dispose() }
            $remoteRunspace.Dispose()
            $child.Dispose()
        }
    }
}
