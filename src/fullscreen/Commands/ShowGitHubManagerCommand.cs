using System.Management.Automation;
using fullscreen.Development;

namespace fullscreen.Commands;
/// <summary>Monitor and review GitHub development activity in a fullscreen TUI.</summary>
[Cmdlet(VerbsCommon.Show, "GitHubManager")]
[Alias("fgh")]
public sealed class ShowGitHubManagerCommand : ShowDeveloperCommand {
    /// <summary>GitHub owner/name. Repo is an alias. When omitted, gh infers it from the current Git
    /// checkout.</summary>
    [Parameter]
    [Alias("Repo")]
    [ValidatePattern(@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", ErrorMessage = "Repository must use owner/name syntax.")]
    [ValidateNotNullOrEmpty]
    public string? Repository { get; set; }

    /// <summary>GitHub hostname, default github.com. Supports authenticated GitHub Enterprise hosts through
    /// gh.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string HostName { get; set; } = "github.com";

    /// <summary>Initial lazy tab: Issues, PullRequests, Runs or Workflows. Defaults to
    /// PullRequests.</summary>
    [Parameter]
    public GitHubView View { get; set; } = GitHubView.PullRequests;

    /// <inheritdoc/>
    protected override void EndProcessing() =>
        Run(
            directory => new GitHubBackend(directory, Repository, new CommandRunner(), PageSize, HostName),
            (int)View,
            "FullscreenGitHubManagerFailed"
        );
}
