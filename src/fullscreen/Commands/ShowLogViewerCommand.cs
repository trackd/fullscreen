using System.Management.Automation;
using fullscreen.Hosting;
using fullscreen.Logs;

namespace fullscreen.Commands;
/// <summary>Browse and follow text or JSON log files in a fullscreen table.</summary>
[Cmdlet(VerbsCommon.Show, "LogViewer", DefaultParameterSetName = "Path")]
[Alias("flog")]
public sealed class ShowLogViewerCommand : FullscreenCommand {
    /// <summary>One or more filesystem log paths, supporting PowerShell wildcards. Choose at most 64
    /// resolved files.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Path", ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string[]? Path { get; set; }

    /// <summary>Exact filesystem paths without wildcard expansion; PSPath is an alias. With Follow, missing
    /// files are retried.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "LiteralPath", ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    [ValidateNotNullOrEmpty]
    public string[]? LiteralPath { get; set; }

    /// <summary>Keep polling for appended lines and file rotation. Initially scrolls to the newest entry; F
    /// toggles tail scrolling.</summary>
    [Parameter]
    public SwitchParameter Follow { get; set; }

    /// <summary>Maximum retained entries across all files, default 10000. Oldest arrival entries are
    /// evicted as new entries arrive.</summary>
    [Parameter]
    [ValidateRange(1, 1000000)]
    public int MaximumCount { get; set; } = 10000;

    /// <summary>File polling interval in milliseconds, 50 through 60000; default 250.</summary>
    [Parameter]
    [ValidateRange(50, 60000)]
    public int PollInterval { get; set; } = 250;

    /// <summary>Initial minimum severity. Trace, Debug, Information, Warning, Error, Critical or Unknown.
    /// Unknown entries remain visible at every threshold.</summary>
    [Parameter]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>Initial case-insensitive filter across table cells. Plain text matches substrings;
    /// regex:^error.*timeout uses a regular expression and wildcard:*timeout* uses * (any characters) and ?
    /// (one character). The interactive / filter supports the same prefixes. Invalid regex patterns display
    /// an error and can be edited; regular expression matching has a 50 ms timeout. Slash edits it inside
    /// the UI.</summary>
    [Parameter]
    [ValidateNotNull]
    public string Filter { get; set; } = "";

    private readonly List<(string Path, bool Literal)> _requested = [];
    /// <inheritdoc/>
    protected override void ProcessRecord() {
        foreach (string path in LiteralPath ?? []) {
            _requested.Add((path, true));
        }

        foreach (string path in Path ?? []) {
            _requested.Add((path, false));
        }
    }

    /// <inheritdoc/>
    protected override void EndProcessing() {
        if (PipelineStopToken.IsCancellationRequested) {
            return;
        }

        try {
            var paths = new List<string>();
            foreach (var (requested, literal) in _requested) {
                if (literal) {
                    string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(requested, out var provider, out _);
                    if (provider.Name != "FileSystem") {
                        throw new ArgumentException("Choose FileSystem paths.");
                    }

                    paths.Add(path);
                }
                else {
                    var resolved = SessionState.Path.GetResolvedProviderPathFromPSPath(requested, out var provider);
                    if (provider.Name != "FileSystem") {
                        throw new ArgumentException("Choose FileSystem paths.");
                    }

                    paths.AddRange(resolved);
                }
            }

            string[] files = [.. paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];
            if (files.Length is 0 or > 64) {
                throw new ArgumentException("Choose between 1 and 64 log files.");
            }

            foreach (string file in files) {
                if (Directory.Exists(file)) {
                    throw new ArgumentException($"Expected a file: {file}");
                }
            }

            using var app = new LogViewerApp(files, Follow.IsPresent, MaximumCount, PollInterval, MinimumLevel, Filter ?? "");
            FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenLogViewerFailed", ErrorCategory.ReadError, LiteralPath ?? Path));
        }
    }
}
