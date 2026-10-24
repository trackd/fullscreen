using System.Management.Automation;
using fullscreen.Development;

namespace fullscreen.Commands;
/// <summary>Interact with a Git repository in a fullscreen TUI.</summary>
[Cmdlet(VerbsCommon.Show, "GitManager")]
[Alias("fgit")]
public sealed class ShowGitManagerCommand : ShowDeveloperCommand {
    /// <summary>Initial lazy tab: Changes, Staged, History, Branches, Remotes, Worktrees, Stashes or Tags.
    /// Defaults to Changes.</summary>
    [Parameter]
    public GitView View { get; set; }

    /// <inheritdoc/>
    protected override void EndProcessing() =>
        Run(
            directory => new GitBackend(directory, new CommandRunner(), PageSize),
            (int)View,
            "FullscreenGitManagerFailed"
        );
}
