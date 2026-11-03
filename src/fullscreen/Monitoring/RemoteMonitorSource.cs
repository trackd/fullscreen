using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace fullscreen.Monitoring;
/// <summary>Owns a typed collector handle. The caller retains ownership of the PSSession/runspace.</summary>
public sealed class RemoteMonitorSource : IMonitorSource, IMonitorControlSource, IMonitorProcessDetailSource {
    private readonly Runspace _runspace;
    private readonly string _handle = Guid.NewGuid().ToString("N");
    private static readonly string RegistryName = typeof(CollectorSessions).AssemblyQualifiedName!;
    private static readonly Guid ModuleId = typeof(CollectorSessions).Module.ModuleVersionId;
    // Normal calls resolve the loaded type directly. The MVID fallback also handles
    // a newer build loaded into a long-lived runspace with the same assembly version.
    private const string SessionScript = """
        param([string]$RegistryName, [guid]$ModuleId, [string]$Handle, [string]$Operation, [int]$Metrics, [string]$TargetName, [long]$LogonTime)
        $registry = [Type]::GetType($RegistryName, $false)
        if ($null -eq $registry -or $registry.Module.ModuleVersionId -ne $ModuleId) {
            $registry = $null
            foreach ($assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {
                if ($assembly.ManifestModule.ModuleVersionId -eq $ModuleId) {
                    $registry = $assembly.GetType('fullscreen.Monitoring.CollectorSessions', $true)
                    break
                }
            }
        }
        if ($null -eq $registry) { throw 'The Fullscreen collector library is not deployed.' }
        switch ($Operation) {
            Capture { $registry::CaptureBase64($Handle, $Metrics) }
            Invalidate { $registry::InvalidateServices($Handle) }
            Close { $registry::Close($Handle) }
            Logoff { $registry::LogoffSession($Metrics, $TargetName, $LogonTime) }
            CommandLine { $registry::ReadProcessCommandLine($Handle, $Metrics, $LogonTime) }
        }
        """;
    private readonly int _timeoutSeconds;
    private bool _deployed;
    /// <summary>Gets the human-readable label.</summary>
    public string Label { get; }
    /// <summary>Gets whether collection targets a remote computer.</summary>
    public bool IsRemote => true;

    /// <summary>Initializes a new RemoteMonitorSource instance.</summary>
    /// <param name="session">The existing PowerShell remoting session.</param>
    /// <param name="timeoutSeconds">The timeout in seconds. Defaults to 15.</param>
    public RemoteMonitorSource(
        PSSession session,
        int timeoutSeconds = 15
    ) : this(session.Runspace, session.ComputerName, timeoutSeconds) {
    }

    /// <summary>Initializes a new RemoteMonitorSource instance.</summary>
    /// <param name="runspace">The PowerShell runspace used for commands.</param>
    /// <param name="label">The human-readable label.</param>
    /// <param name="timeoutSeconds">The timeout in seconds. Defaults to 15.</param>
    public RemoteMonitorSource(Runspace runspace, string label, int timeoutSeconds = 15) {
        ArgumentNullException.ThrowIfNull(runspace);
        if (timeoutSeconds < 1 || timeoutSeconds > 120) {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        _runspace = runspace;
        Label = label;
        _timeoutSeconds = timeoutSeconds;
    }

    /// <inheritdoc/>
    public void Sample(CollectorFrame destination, CancellationToken cancellationToken) {
        Sample(destination, CollectorMetrics.All, cancellationToken);
    }

    private void EnsureDeployed(CancellationToken cancellationToken) {
        if (!_deployed) {
            byte[] assembly = File.ReadAllBytes(typeof(PerformanceCollector).Assembly.Location);
            Invoke("""
                param([byte[]]$CollectorBytes, [string]$Handle, [guid]$ModuleId)
                if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { throw 'The endpoint does not permit loading the Fullscreen collector.' }
                $collectorAssembly = $null
                foreach ($assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {
                    if ($assembly.ManifestModule.ModuleVersionId -eq $ModuleId) {
                        $collectorAssembly = $assembly
                        break
                    }
                }
                if ($null -eq $collectorAssembly) { $collectorAssembly = [System.Reflection.Assembly]::Load($CollectorBytes) }
                $registry = $collectorAssembly.GetType('fullscreen.Monitoring.CollectorSessions', $true)
                $registry::Open($Handle)
                """, cancellationToken, assembly, _handle, ModuleId);
            _deployed = true;
        }
    }

    /// <inheritdoc/>
    public void Sample(CollectorFrame destination, CollectorMetrics metrics, CancellationToken cancellationToken) {
        EnsureDeployed(cancellationToken);
        var output = Invoke(SessionScript, cancellationToken, RegistryName, ModuleId, _handle, "Capture", (int)metrics);
        if (output.Count != 1 || output[0].BaseObject is not string payload) {
            throw new InvalidDataException("Remote collector returned an invalid snapshot envelope.");
        }

        if (payload.Length > 90 * 1024 * 1024) {
            throw new InvalidDataException("Remote snapshot is too large.");
        }

        FrameCodec.Decode(Convert.FromBase64String(payload), destination);
    }

    /// <inheritdoc cref="fullscreen.Monitoring.IMonitorControlSource.Execute"/>
    public void Execute(MonitorActionRequest request, CancellationToken cancellationToken) {
        if (request.Action == MonitorAction.LogoffSession) {
            Invoke(
                SessionScript,
                cancellationToken,
                RegistryName,
                ModuleId,
                _handle,
                "Logoff",
                request.SessionId,
                request.Name,
                request.SessionLogonTime
            );
        }
        else {
            Invoke(
                MonitorActions.Script,
                cancellationToken,
                request.Action.ToString(),
                request.Name,
                request.ProcessId,
                request.StartTimeTicks
            );
        }

        if (_deployed) {
            Invoke(SessionScript, cancellationToken, RegistryName, ModuleId, _handle, "Invalidate", 0);
        }
    }

    /// <inheritdoc/>
    public string ReadCommandLine(ProcessRow process, CancellationToken token) {
        EnsureDeployed(token);
        var result = Invoke(SessionScript, token, RegistryName, ModuleId, _handle, "CommandLine", process.Id, "", process.StartTimeTicks);
        if (result.Count != 1 || result[0].BaseObject is not string text) {
            throw new InvalidDataException("Remote collector returned an invalid command-line response.");
        }
        return text;
    }

    private PSDataCollection<PSObject> Invoke(string script, CancellationToken token, params object[] arguments) {
        token.ThrowIfCancellationRequested();
        if (_runspace.RunspaceStateInfo.State != RunspaceState.Opened) {
            throw new InvalidOperationException("Remote session is not open.");
        }

        if (_runspace.RunspaceAvailability != RunspaceAvailability.Available) {
            throw new InvalidOperationException("Remote session is busy; use a dedicated PSSession for monitoring.");
        }

        using var shell = PowerShell.Create();
        shell.Runspace = _runspace;
        shell.AddScript(script, useLocalScope: true);
        foreach (object argument in arguments) {
            shell.AddArgument(argument);
        }

        var pending = shell.BeginInvoke();
        long start = Stopwatch.GetTimestamp();
        while (!pending.AsyncWaitHandle.WaitOne(50)) {
            if (token.IsCancellationRequested || Stopwatch.GetElapsedTime(start).TotalSeconds >= _timeoutSeconds) {
                shell.BeginStop(null, null);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException($"Remote collection exceeded {_timeoutSeconds} seconds.");
            }
        }

        token.ThrowIfCancellationRequested();
        var result = shell.EndInvoke(pending);
        if (shell.HadErrors) {
            throw new InvalidOperationException(shell.Streams.Error[0].ToString());
        }

        return result;
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Also attempt cleanup after an interrupted deployment whose outcome may be unknown.
        try {
            Invoke(SessionScript, CancellationToken.None, RegistryName, ModuleId, _handle, "Close", 0);
        }
        catch (Exception error) when (error is InvalidOperationException or RuntimeException or TimeoutException) {
        }
    }
}
