using System.Collections;
using System.Management.Automation;
using fullscreen.Hosting;
using fullscreen.Monitoring;
using System.Management.Automation.Runspaces;
using fullscreen.Charts;

namespace fullscreen.Commands;

internal static class RemoteSessionOptions {
    internal static Hashtable Build(string computerName, object? credential, string? configurationName) {
        var options = new Hashtable {
            ["ComputerName"] = computerName,
            ["ErrorAction"] = "Stop"
        };
        if (credential is not null) {
            options["Credential"] = credential;
        }

        if (!string.IsNullOrEmpty(configurationName)) {
            options["ConfigurationName"] = configurationName;
        }

        return options;
    }
}

/// <summary>Opens the interactive fullscreen system monitor.</summary>
[Cmdlet(VerbsCommon.Show, "SystemMonitor", DefaultParameterSetName = "Local")]
[Alias("ftop")]
public sealed class ShowSystemMonitorCommand : FullscreenCommand {
    /// <summary>Maximum data-presentation refresh rate, 1 to 60 Hz. Default 30. The framework redraws when
    /// state changes.</summary>
    [Parameter]
    [ValidateRange(1, 60)]
    public int RefreshRate { get; set; } = 30;

    /// <summary>Delay in milliseconds between completed samples, 100 to 60000. Default 1000 locally or 2000
    /// remotely. Sampling runs independently of input/rendering.</summary>
    [Parameter]
    [ValidateRange(100, 60000)]
    public int SampleInterval { get; set; } = 1000;

    /// <summary>Initial case-insensitive name, PID or username substring. Press / to edit and Enter to
    /// finish. Escape clears the search and active table&apos;s column filters; Ctrl+U clears only the
    /// search text and keeps editing when the search is open. Header dropdown filters combine with this
    /// text filter.</summary>
    [Parameter]
    [ValidateNotNull]
    public string Filter { get; set; } = "";

    /// <summary>Cpu, Memory, Id, Name, Threads, Gpu, GpuMemory, UserName, ParentId, CpuTime, Status, Disk
    /// or Network. Default Cpu. Use SortBy Memory to sort processes by memory usage. Missing counters stay
    /// last in either direction.</summary>
    [Parameter]
    public ProcessSort SortBy { get; set; } = ProcessSort.Cpu;

    /// <summary>Processes, Services, Network, Gpu, Users or Dashboard. Overview remains an alias for
    /// Processes. Keys 1 through 6 switch to Processes, Services, Network, GPU, Users and Dashboard
    /// respectively. F8 also opens Users. Processes is the default. Press A or right-click a
    /// process/service for confirmed actions. Service headers provide sorting and column filters; F6 cycles
    /// sort columns and R reverses. P pauses; Q or Ctrl+Q exits.</summary>
    [Parameter]
    [MonitorViewTransformation]
    public MonitorView View { get; set; } = MonitorView.Processes;

    /// <summary>Selects Line, Area (default), Bars, Step, Scatter or Stem for all monitor histories. Press
    /// G to cycle these styles while the monitor is open. Applies to local and remote monitoring, dashboard
    /// panels and detail/header graphs. Sixel is selected automatically when supported; other terminals use
    /// native XenoAtom line/sparkline controls. Histogram and BoxPlot summarize distributions and are
    /// available on reusable Chart controls rather than monitor panels with time axes.</summary>
    [Parameter]
    [Alias("GraphKind")]
    [ValidateSet("Line", "Area", "Bars", "Step", "Scatter", "Stem")]
    public GraphKind GraphType { get; set; } = GraphKind.Area;

    /// <summary>Select Auto (Sixel with native fallback), Sixel, Native or Regis (experimental Contour
    /// vectors). Use GraphType independently to choose the plot shape. DRCS chart rendering is
    /// disabled.</summary>
    [Parameter]
    public ChartRenderer ChartRenderer { get; set; }

    /// <summary>Group processes by parent. T toggles the tree; Left collapses, Right expands, Enter toggles
    /// a branch. Filtering retains matching processes and their ancestors.</summary>
    [Parameter]
    public SwitchParameter Tree { get; set; }

    /// <summary>Ordered process columns. Id, ParentId, UserName, Cpu, Memory, MemoryPercent, Gpu,
    /// GpuMemory, Threads, Name, CpuTime, Status, Disk, Network. CpuTime is cumulative CPU time and is off
    /// by default. Unsupported disk/network columns are hidden. C opens the current table column
    /// chooser.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public ProcessColumn[]? Column { get; set; }

    /// <summary>Remove columns from the requested or default process selection. At least one column must
    /// remain. Tree mode requires Name.</summary>
    [Parameter]
    [ValidateNotNull]
    public ProcessColumn[]? ExcludeColumn { get; set; }

    /// <summary>Network interface columns: Interface, Receive, Send, Link, TotalReceived, TotalSent,
    /// Errors. C changes this table independently.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public NetworkColumn[]? NetworkColumn { get; set; }

    /// <summary>Hide network interface columns from the requested or default selection.</summary>
    [Parameter]
    [ValidateNotNull]
    public NetworkColumn[]? ExcludeNetworkColumn { get; set; }

    /// <summary>GPU device columns: Device, Provider, Utilization, UsedMemory, TotalMemory, Temperature. C
    /// changes this table independently.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public GpuColumn[]? GpuColumn { get; set; }

    /// <summary>Hide GPU device columns from the requested or default selection.</summary>
    [Parameter]
    [ValidateNotNull]
    public GpuColumn[]? ExcludeGpuColumn { get; set; }

    /// <summary>A dedicated open PSSession created through WinRM or SSH. The caller owns the session;
    /// closing the monitor removes its collector but keeps the session open. Cannot combine with
    /// ComputerName.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Session", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ValidateNotNull]
    public PSSession? Session { get; set; }

    /// <summary>A WinRM target. Creates a temporary session and removes it on exit. The target must already
    /// have a suitable endpoint configured. Cannot combine with Session.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "Computer", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    public string? ComputerName { get; set; }

    /// <summary>Optional credential for ComputerName. Otherwise uses the caller identity. For SSH
    /// authentication, create a PSSession yourself and use Session.</summary>
    [Parameter(ParameterSetName = "Computer")]
    [Credential]
    [ValidateNotNull]
    public PSCredential? Credential { get; set; }

    /// <summary>Existing WinRM endpoint used with ComputerName. Default PowerShell.7. Windows PowerShell
    /// endpoints can use Microsoft.PowerShell. The endpoint must permit assembly loading.</summary>
    [Parameter(ParameterSetName = "Computer")]
    [ArgumentCompletions("PowerShell.7", "Microsoft.PowerShell")]
    [ValidateNotNullOrEmpty]
    public string? ConfigurationName { get; set; }

    /// <summary>Remote request timeout, 1 to 120 seconds. Default 15. Collection errors preserve the last
    /// snapshot and display stale/error status while retrying with backoff.</summary>
    [Parameter]
    [ValidateRange(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <inheritdoc/>
    protected override void EndProcessing() {
        PSSession? ownedSession = null;
        try {
            var columns = new ProcessColumns();
            columns.Configure(Column, ExcludeColumn, Tree.IsPresent);
            if (ComputerName is not null) {
                var result = InvokeCommand.InvokeScript(false, ScriptBlock.Create("""
                    param($SessionOptions)
                    Microsoft.PowerShell.Core\New-PSSession @SessionOptions
                    """), null, RemoteSessionOptions.Build(ComputerName, Credential, ConfigurationName));
                ownedSession = (PSSession)result[0].BaseObject;
            }

            var session = Session ?? ownedSession;
            IMonitorSource source = session is null ? new LocalMonitorSource() : new RemoteMonitorSource(session, TimeoutSeconds);
            int interval = session is not null && !MyInvocation.BoundParameters.ContainsKey(nameof(SampleInterval)) ? 2000 : SampleInterval;
            using var app = new MonitorApp(
                RefreshRate,
                interval,
                Filter,
                SortBy,
                View,
                source,
                columns,
                Tree.IsPresent,
                NetworkColumn,
                ExcludeNetworkColumn,
                GpuColumn,
                ExcludeGpuColumn,
                Minimal.IsPresent
            );
            app.GraphType = GraphType;
            FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent, ChartRenderer);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenMonitorFailed", ErrorCategory.ResourceUnavailable, null));
        }
        finally {
            if (ownedSession is not null) {
                // A stopped pipeline cannot run Remove-PSSession, so close the remote shell directly first.
                ownedSession.Runspace.Dispose();
                try {
                    InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create(@"param($OwnedSession) Microsoft.PowerShell.Core\Remove-PSSession -Session $OwnedSession -ErrorAction SilentlyContinue"),
                        null,
                        ownedSession
                    );
                }
                catch (PipelineStoppedException) {
                }
            }
        }
    }
}
