using System.Diagnostics;
using System.Management.Automation;

namespace fullscreen.Monitoring;
/// <summary>Identifies a process, service, or user-session operation.</summary>
public enum MonitorAction {
    /// <summary>Requests the stop process operation against the validated target identity.</summary>
    StopProcess,
    /// <summary>Requests the start service operation against the validated target identity.</summary>
    StartService,
    /// <summary>Requests the stop service operation against the validated target identity.</summary>
    StopService,
    /// <summary>Requests the restart service operation against the validated target identity.</summary>
    RestartService,
    /// <summary>Requests the force stop service operation against the validated target identity.</summary>
    ForceStopService,
    /// <summary>Requests the logoff session operation against the validated target identity.</summary>
    LogoffSession
}

/// <summary>Captures the target identity needed to validate and execute a monitor action.</summary>
/// <param name="Action">The operation identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="ProcessId">The associated process identifier, or null when unavailable. Defaults to 0.</param>
/// <param name="StartTimeTicks">The process start identity used to distinguish reused identifiers. Defaults to
/// 0.</param>
public sealed record MonitorActionRequest(
    MonitorAction Action,
    string Name,
    int ProcessId = 0,
    long StartTimeTicks = 0
) {
    /// <summary>Gets or sets the Windows terminal-services session identifier.</summary>
    public int SessionId { get; init; } = -1;
    /// <summary>Gets or sets the logon FILETIME used to revalidate a session before a destructive
    /// action.</summary>
    public long SessionLogonTime { get; init; }
}

/// <summary>Defines execution of monitor actions against a local or remote source.</summary>
public interface IMonitorControlSource {
    /// <summary>Executes a validated process, service, or session action.</summary>
    /// <param name="request">The operation request to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    void Execute(MonitorActionRequest request, CancellationToken cancellationToken);
}

internal static class MonitorActions {
    // Executed in the same target context as the selected inventory, never in a shell string.
    internal const string Script = """
        param([string]$Action, [string]$TargetName, [int]$TargetProcessId, [long]$Birth)
        $ErrorActionPreference = 'Stop'
        $windows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
        if ($Action -eq 'StopProcess') {
            $process = [Diagnostics.Process]::GetProcessById($TargetProcessId)
            try {
                $actualBirth = if ($windows) { $process.StartTime.ToFileTimeUtc() }
                elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) {
                    # Match the collector's boot-relative /proc starttime, not a wall-clock conversion.
                    $stat = [IO.File]::ReadAllText("/proc/$TargetProcessId/stat")
                    $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
                    [long]::Parse($fields[19], [Globalization.CultureInfo]::InvariantCulture)
                }
                else { $process.StartTime.ToUniversalTime().Ticks }
                if ($Birth -le 0 -or $actualBirth -ne $Birth) { throw 'Process identity changed; refresh before trying again.' }
                $process.Kill()
            }
            finally { $process.Dispose() }
            return
        }
        if ($Action -notin 'StartService', 'StopService', 'RestartService', 'ForceStopService' -or [string]::IsNullOrWhiteSpace($TargetName)) { throw 'Invalid service action.' }
        if ($windows) {
            $service = Microsoft.PowerShell.Management\Get-Service -Name ([WildcardPattern]::Escape($TargetName)) -ErrorAction Stop
            try {
                $waitForStatus = {
                    param($Expected)
                    $deadline = [DateTime]::UtcNow.AddSeconds(20)
                    while ($true) {
                        $service.Refresh()
                        if ($service.Status.ToString() -eq $Expected) { return }
                        if ([DateTime]::UtcNow -ge $deadline) { throw 'Service transition timed out; refresh before retrying.' }
                        Start-Sleep -Milliseconds 100
                    }
                }
                if ($Action -in 'StopService', 'RestartService', 'ForceStopService' -and $service.Status.ToString() -ne 'Stopped') {
                    if ($Action -eq 'ForceStopService') { Microsoft.PowerShell.Management\Stop-Service -InputObject $service -Force -ErrorAction Stop }
                    else { $service.Stop() }
                    & $waitForStatus 'Stopped'
                }
                if ($Action -in 'StartService', 'RestartService' -and $service.Status.ToString() -ne 'Running') {
                    $service.Start()
                    & $waitForStatus 'Running'
                }
            }
            finally { $service.Dispose() }
        }
        else {
            $mac = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)
            if ($Action -eq 'ForceStopService' -and $mac) { throw 'Force stop is not supported for launchctl services; use Stop service.' }
            $verb = switch ($Action) { StartService { 'start' } StopService { 'stop' } RestartService { 'restart' } ForceStopService { 'stop' } }
            $operations = if ($Action -eq 'ForceStopService') { @('kill', 'stop') } elseif ($mac -and $verb -eq 'restart') { @('stop', 'start') } else { @($verb) }
            foreach ($operation in $operations) {
                $start = [Diagnostics.ProcessStartInfo]::new()
                $start.FileName = if ($mac) { '/bin/launchctl' } else { 'systemctl' }
                $start.UseShellExecute = $false; $start.CreateNoWindow = $true
                $start.RedirectStandardError = $true; $start.RedirectStandardOutput = $true
                if (-not $mac) { $start.ArgumentList.Add('--no-ask-password') }
                $start.ArgumentList.Add($operation)
                # systemctl kill defaults to all service processes, including on older releases.
                if ($operation -eq 'kill') { $start.ArgumentList.Add('--signal=SIGKILL') }
                if (-not $mac) { $start.ArgumentList.Add('--') }
                $start.ArgumentList.Add($TargetName)
                $command = [Diagnostics.Process]::Start($start)
                try {
                    $output = $command.StandardOutput.ReadToEndAsync()
                    $errors = $command.StandardError.ReadToEndAsync()
                    $deadline = [DateTime]::UtcNow.AddSeconds(20)
                    while (-not $command.WaitForExit(100)) {
                        if ([DateTime]::UtcNow -ge $deadline) { $command.Kill(); throw 'Service action timed out; refresh before retrying.' }
                    }
                    $null = $output.GetAwaiter().GetResult()
                    $errorText = $errors.GetAwaiter().GetResult()
                    if ($command.ExitCode -ne 0) { throw $errorText.Trim() }
                }
                finally { if (-not $command.HasExited) { $command.Kill() }; $command.Dispose() }
            }
        }
        """;
    internal static void ExecuteLocal(MonitorActionRequest request, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (request.Action == MonitorAction.LogoffSession) {
            CollectorSessions.LogoffSession(request.SessionId, request.Name, request.SessionLogonTime);
            return;
        }

        using var shell = PowerShell.Create();
        shell.AddScript(Script).AddArgument(request.Action.ToString()).AddArgument(request.Name).AddArgument(request.ProcessId).AddArgument(request.StartTimeTicks);
        var pending = shell.BeginInvoke();
        long started = Stopwatch.GetTimestamp();
        while (!pending.AsyncWaitHandle.WaitOne(50)) {
            if (token.IsCancellationRequested || Stopwatch.GetElapsedTime(started).TotalSeconds >= 45) {
                shell.BeginStop(null, null);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Action timed out; refresh the target state before retrying.");
            }
        }

        token.ThrowIfCancellationRequested();
        shell.EndInvoke(pending);
        if (shell.HadErrors) {
            throw new InvalidOperationException(shell.Streams.Error[0].ToString());
        }
    }
}
