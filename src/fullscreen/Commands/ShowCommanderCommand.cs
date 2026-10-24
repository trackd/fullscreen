using System.Management.Automation;
using fullscreen.Files;
using fullscreen.Hosting;
using fullscreen.Themes;

namespace fullscreen.Commands;
/// <summary>Opens a fullscreen Midnight Commander-inspired dual-pane file manager.</summary>
[Cmdlet(VerbsCommon.Show, "Commander")]
[Alias("fmc")]
[OutputType(typeof(string))]
public sealed class ShowCommanderCommand : FullscreenCommand {
    /// <summary>Initial literal FileSystem directory for the left pane. Defaults to the current location.
    /// Path is an alias. Native DirectoryInfo and PathInfo values are accepted.</summary>
    [Parameter(Position = 0)]
    [Alias("Path")]
    [ValidateNotNullOrEmpty]
    public string? LeftPath { get; set; }

    /// <summary>Initial literal FileSystem directory for the right pane. Defaults to the left directory.
    /// Native DirectoryInfo and PathInfo values are accepted.</summary>
    [Parameter(Position = 1)]
    [ValidateNotNullOrEmpty]
    public string? RightPath { get; set; }

    /// <summary>Initially show dotfiles and hidden entries. The period key toggles this in the
    /// TUI.</summary>
    [Parameter]
    public SwitchParameter ShowHidden { get; set; }

    /// <summary>After closing, emit marked literal paths or the current selected path as strings.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>After closing, change the caller location to the currently displayed directory. Omitted by
    /// default.</summary>
    [Parameter]
    public SwitchParameter SetLocation { get; set; }

    /// <inheritdoc/>
    protected override void EndProcessing() {
        try {
            string left = ResolveDirectory(LeftPath ?? SessionState.Path.CurrentFileSystemLocation.Path);
            string right = ResolveDirectory(RightPath ?? left);
            using var app = new CommanderApp(left, right, ShowHidden.IsPresent);
            var theme = MyInvocation.BoundParameters.ContainsKey(nameof(Theme)) ? ResolveTheme() : CommanderSurface.ClassicTheme;
            FullscreenHost.Run(app, PipelineStopToken, theme, Minimal.IsPresent);
            if (PipelineStopToken.IsCancellationRequested) {
                return;
            }

            if (SetLocation.IsPresent && app.CurrentPath.Length != 0) {
                SessionState.Path.SetLocation(app.CurrentPath);
            }

            if (PassThru.IsPresent) {
                foreach (string path in app.ChosenPaths) {
                    WriteObject(path);
                }
            }
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenCommanderFailed", ErrorCategory.ResourceUnavailable, LeftPath));
        }
    }

    private string ResolveDirectory(string path) {
        string resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path, out var provider, out _);
        if (provider.Name != "FileSystem" || !Directory.Exists(resolved)) {
            throw new ArgumentException("Choose an existing FileSystem directory.");
        }

        return resolved;
    }
}
