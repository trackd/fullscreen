using System.Management.Automation;
using fullscreen.Files;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Opens a fullscreen file manager with rich previews.</summary>
[Cmdlet(VerbsCommon.Show, "FileManager")]
[Alias("ffile")]
[OutputType(typeof(string))]
public sealed class ShowFileManagerCommand : FullscreenCommand {
    /// <summary>Initial literal file or directory path; LiteralPath is an alias. Defaults to the current
    /// filesystem location. A file path selects that file in its parent. Only FileSystem paths are
    /// accepted.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Initially show dotfiles and hidden entries. The period key toggles this in the
    /// TUI.</summary>
    [Parameter]
    public SwitchParameter ShowHidden { get; set; }

    /// <summary>Name, Size, Modified or Type. Folders stay first. Lowercase s cycles sorting inside the
    /// TUI.</summary>
    [Parameter]
    public FileSort SortBy { get; set; }

    /// <summary>Initially reverse sorting. Lowercase r toggles the direction in the TUI.</summary>
    [Parameter]
    public SwitchParameter Descending { get; set; }

    /// <summary>Disable the preview pane and content reads.</summary>
    [Parameter]
    public SwitchParameter NoPreview { get; set; }

    /// <summary>After closing, emit marked literal paths or the current selected path as strings.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>After closing, change the caller location to the currently displayed directory. Omitted by
    /// default.</summary>
    [Parameter]
    public SwitchParameter SetLocation { get; set; }

    private int _records;
    /// <inheritdoc/>
    protected override void ProcessRecord() {
        if (++_records > 1) {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Pipe one path to Show-FileManager."),
                "FullscreenFileManagerMultiplePaths",
                ErrorCategory.InvalidArgument,
                Path
            ));
        }
    }

    /// <inheritdoc/>
    protected override void EndProcessing() {
        try {
            string path = SessionState.Path.CurrentFileSystemLocation.Path;
            if (Path is not null) {
                path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path, out var provider, out _);
                if (provider.Name != "FileSystem") {
                    throw new ArgumentException("Choose a FileSystem provider path.", nameof(Path));
                }
            }

            if (!Directory.Exists(path) && !File.Exists(path)) {
                throw new DirectoryNotFoundException($"File or directory not found: {path}");
            }

            using var app = new FileManagerApp(
                path,
                ShowHidden.IsPresent,
                SortBy,
                Descending.IsPresent,
                !NoPreview.IsPresent,
                Minimal.IsPresent
            );
            FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent);
            if (PipelineStopToken.IsCancellationRequested) {
                return;
            }

            if (SetLocation.IsPresent && app.CurrentPath.Length > 0) {
                SessionState.Path.SetLocation(app.CurrentPath);
            }

            if (PassThru.IsPresent) {
                foreach (string selected in app.ChosenPaths) {
                    WriteObject(selected);
                }
            }
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenFileManagerFailed", ErrorCategory.ResourceUnavailable, Path));
        }
    }
}
