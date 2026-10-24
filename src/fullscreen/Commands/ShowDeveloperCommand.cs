using System.Management.Automation;
using fullscreen.Development;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Selects the Git repository page shown in the developer TUI.</summary>
public enum GitView {
    /// <summary>Shows the changes view.</summary>
    Changes,
    /// <summary>Shows the staged view.</summary>
    Staged,
    /// <summary>Shows the history view.</summary>
    History,
    /// <summary>Shows the branches view.</summary>
    Branches,
    /// <summary>Shows the remotes view.</summary>
    Remotes,
    /// <summary>Shows the worktrees view.</summary>
    Worktrees,
    /// <summary>Shows the stashes view.</summary>
    Stashes,
    /// <summary>Shows the tags view.</summary>
    Tags
}

/// <summary>Selects the GitHub resource page shown in the developer TUI.</summary>
public enum GitHubView {
    /// <summary>Shows the issues view.</summary>
    Issues,
    /// <summary>Shows the pull requests view.</summary>
    PullRequests,
    /// <summary>Shows the runs view.</summary>
    Runs,
    /// <summary>Shows the workflows view.</summary>
    Workflows
}

/// <summary>Runs developer backends with shared paging, theme, and cancellation handling.</summary>
public abstract class ShowDeveloperCommand : FullscreenCommand {
    /// <summary>Gets or sets the filesystem or resource path.</summary>
    [Parameter(Position = 0)]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Gets or sets whether actions that change external state are disabled.</summary>
    [Parameter]
    public SwitchParameter ReadOnly { get; set; }

    /// <summary>Gets or sets the delay in seconds between object-chart refreshes.</summary>
    [Parameter]
    [ValidateRange(1, 3600)]
    public int RefreshInterval { get; set; } = 30;

    /// <summary>Gets or sets the number of entries that fit in the visible page.</summary>
    [Parameter]
    [ValidateRange(1, 100)]
    public int PageSize { get; set; } = 50;

    /// <summary>Creates and runs a developer backend with cmdlet cancellation and error
    /// reporting.</summary>
    /// <param name="create">The factory used to create a backend.</param>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="errorId">The stable identifier attached to the reported PowerShell error.</param>
    protected void Run(Func<string, IDeveloperBackend> create, int view, string errorId) {
        Run((directory, _) => create(directory), view, errorId);
    }

    /// <summary>Creates and runs a developer backend with cmdlet cancellation and error
    /// reporting.</summary>
    /// <param name="create">The factory used to create a backend.</param>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="errorId">The stable identifier attached to the reported PowerShell error.</param>
    protected void Run(Func<string, CancellationToken, IDeveloperBackend> create, int view, string errorId) {
        if (PipelineStopToken.IsCancellationRequested) {
            return;
        }

        try {
            string directory = SessionState.Path.CurrentFileSystemLocation.Path;
            if (Path is not null) {
                directory = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path, out var provider, out _);
                if (provider.Name != "FileSystem") {
                    throw new ArgumentException("Choose a FileSystem directory.");
                }
            }

            if (!Directory.Exists(directory)) {
                throw new DirectoryNotFoundException(directory);
            }

            var theme = ResolveTheme();
            using var app = new DeveloperApp(
                create(directory, PipelineStopToken),
                view,
                RefreshInterval,
                ReadOnly.IsPresent,
                Minimal.IsPresent
            );
            FullscreenHost.Run(app, PipelineStopToken, theme, Minimal.IsPresent);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, errorId, ErrorCategory.InvalidOperation, Path));
        }
    }
}
