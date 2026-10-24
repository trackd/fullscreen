using System.Globalization;

namespace fullscreen.Development;
/// <summary>Describes a working-tree or index change reported by Git.</summary>
/// <param name="Path">The filesystem or resource path.</param>
/// <param name="Index">The zero-based entry index.</param>
/// <param name="Worktree">The worktree status or path.</param>
/// <param name="OldPath">The original path before a rename. Defaults to null.</param>
public sealed record GitChange(string Path, string Index, string Worktree, string? OldPath = null);
/// <summary>Loads local Git repository state and exposes repository operations.</summary>
/// <param name="directory">The parent or working directory.</param>
/// <param name="runner">The child-process runner used by the backend.</param>
/// <param name="pageSize">The number of entries that fit in the visible page. Defaults to 100.</param>
public sealed partial class GitBackend(
    string directory,
    ICommandRunner runner,
    int pageSize = 100
) : IDeveloperBackend {
    private string? _root;
    private string _branch = "";
    private string? _index;
    private string _head = "";
    private string? _gitDirectory;
    private string? _commonDirectory;
    private string _inProgress = "";
    /// <summary>Gets the displayed heading.</summary>
    public string Title => "⑂ GIT · " + (_root ?? directory);
    /// <summary>Gets the available tab headings.</summary>
    public string[] Tabs { get; } = [
        "Changes",
        "Staged",
        "History",
        "Branches",
        "Remotes",
        "Worktrees",
        "Stashes",
        "Tags"
    ];

    private async Task<CommandResult> GitResult(CancellationToken token, params string[] arguments) {
        var args = new List<string>
        {
            "--no-pager"
        };
        // Limit literal pathspec mode to direct pathspec consumers. Inheriting it into
        // stash's internal clean command prevents removal of saved untracked files.
        if (arguments.Length > 0
            && arguments[0] is "add" or "restore" or "rm" or "diff" or "show" or "log" or "status") {
            args.Add("--literal-pathspecs");
        }

        args.AddRange(["-c", "color.ui=false", "-c", "core.quotepath=false"]);
        args.AddRange(arguments);
        return await runner.RunAsync(new("git", _root ?? directory, args), token).ConfigureAwait(false);
    }

    private async Task<string> Git(
        CancellationToken token,
        params string[] arguments
    ) =>
        (await GitResult(token, arguments).ConfigureAwait(false)).RequireSuccess();
    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.LoadAsync"/>
    public async Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) {
        _root ??= (await Git(token, "rev-parse", "--show-toplevel").ConfigureAwait(false)).TrimEnd('\r', '\n');
        _gitDirectory ??= (await Git(token, "rev-parse", "--absolute-git-dir").ConfigureAwait(false)).TrimEnd('\r', '\n');
        _commonDirectory ??= (await Git(token, "rev-parse", "--path-format=absolute", "--git-common-dir").ConfigureAwait(false)).TrimEnd('\r', '\n');
        _branch = (await Git(token, "branch", "--show-current").ConfigureAwait(false)).Trim();
        _head = await CurrentHead(token).ConfigureAwait(false);
        _inProgress = OperationState();
        if (view is 0 or 1) {
            string status = await Git(token, "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all").ConfigureAwait(false);
            string branch = status.Split('\0')[0];
            _index = await Git(token, "diff", "--cached", "--raw", "-z", "--no-abbrev").ConfigureAwait(false);
            var changes = ParseStatus(status).Where(change => view == 0 || change.Index is not (" " or "?")).ToArray();
            return new(
                [
                    "Index",
                    "Worktree",
                    "Path"
                ],
                [..changes.Select(change => new DeveloperItem(
                    change.Path,
                    [
                        change.Index,
                        change.Worktree,
                        change.OldPath is null ? change.Path : change.OldPath +
                " → " + change.Path
                    ],
                    change
                ))],
                branch +
                " · " + changes.Length +
                " changes · F5 refresh · A actions",
                [
                    12,
                    12,
                    0
                ]
            );
        }

        if (view == 2) {
            var head = await GitResult(token, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false);
            if (head.ExitCode == 1) {
                return new(["Commit", "Author", "Date", "Subject"], [], "No commits yet");
            }

            head.RequireSuccess();
            // NUL fields and record separators avoid localized or padded CLI output.
            string history = await Git(
                token,
                "log",
                "--date=iso-strict",
                $"--max-count={pageSize}",
                $"--skip={page * pageSize}",
                "--format=%H%x00%h%x00%an%x00%aI%x00%s%x00"
            ).ConfigureAwait(false);
            var fields = history.Split('\0');
            var items = new List<DeveloperItem>();
            for (int i = 0; i + 4 < fields.Length; i += 5) {
                string hash = fields[i].Trim('\r', '\n');
                if (hash.Length == 0) {
                    break;
                }

                items.Add(new(hash, [fields[i + 1], fields[i + 2], fields[i + 3], fields[i + 4]], hash));
            }

            return new(
                [
                    "Commit",
                    "Author",
                    "Date",
                    "Subject"
                ],
                [.. items],
                $"History page {page + 1} · [ previous · ] next · Enter diff",
                [
                    13,
                    22,
                    28,
                    0
                ]
            );
        }

        if (view == 3) {
            return await LoadBranches(token).ConfigureAwait(false);
        }

        if (view == 5) {
            return await LoadWorktrees(token).ConfigureAwait(false);
        }

        if (view == 6) {
            return await LoadStashes(token).ConfigureAwait(false);
        }

        if (view == 7) {
            return await LoadTags(token).ConfigureAwait(false);
        }

        ArgumentOutOfRangeException.ThrowIfNotEqual(view, 4);
        string remotes = await Git(token, "remote", "-v").ConfigureAwait(false);
        return new(
            ["Remote"],
            [..remotes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select((
                line,
                index
            ) => new DeveloperItem(index.ToString(CultureInfo.InvariantCulture), [line.TrimEnd('\r')]))],
            "Configured remotes · A actions"
        );
    }

    /// <summary>Parses Git porcelain status output into change records.</summary>
    /// <param name="status">The output of git status in porcelain format.</param>
    /// <returns>The parsed working-tree and index changes.</returns>
    public static GitChange[] ParseStatus(string status) {
        var fields = status.Split('\0');
        var changes = new List<GitChange>();
        for (int i = 0; i < fields.Length; i++) {
            string value = fields[i];
            if (value.StartsWith("##", StringComparison.Ordinal) || value.Length < 4) {
                continue;
            }

            string? old = null;
            if ((value[0] is 'R' or 'C' || value[1] is 'R' or 'C') && i + 1 < fields.Length) {
                old = fields[++i];
            }

            changes.Add(new(value[3..], value[..1], value.Substring(1, 1), old));
        }

        return [.. changes];
    }

    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.DetailAsync"/>
    public async Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) {
        if (item.State is GitChange change) {
            if (change.Index == "?") {
                string path = Path.GetFullPath(Path.Combine(_root!, change.Path));
                if (new FileInfo(path).LinkTarget is not null) {
                    return "Untracked symbolic link: " + change.Path;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                byte[] data = new byte[(int)Math.Min(65536, stream.Length)];
                int count = await stream.ReadAsync(data, token).ConfigureAwait(false);
                return "UNTRACKED · " + change.Path +
                    "\n\n" + (data.AsSpan(0, count).Contains((byte)0) ? "Binary file" : System.Text.Encoding.UTF8.GetString(data, 0, count));
            }

            if (change.OldPath is null && (view == 1 ? change.Index == "M" : change.Worktree == "M")) {
                return await HunkDetail(change, view == 1, token).ConfigureAwait(false);
            }

            string[] paths = change.OldPath is null ? [change.Path] : [change.OldPath, change.Path];
            string unstaged = view == 1 ? "" : await Git(token, [
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                "--",
                ..paths
            ]).ConfigureAwait(false);
            string staged = await Git(token, [
                "diff",
                "--cached",
                "--no-ext-diff",
                "--no-textconv",
                "--",
                ..paths
            ]).ConfigureAwait(false);
            return "WORKTREE\n" + unstaged + "\nSTAGED\n" + staged;
        }

        if (view == 2) {
            return await Git(token, "show", "--no-ext-diff", "--no-textconv", "--stat", "--patch", item.Key, "--").ConfigureAwait(false);
        }

        if (item.State is GitBranch branch) {
            return await BranchDetail(branch, token).ConfigureAwait(false);
        }

        if (item.State is GitWorktree worktree) {
            return await WorktreeDetail(worktree, token).ConfigureAwait(false);
        }

        if (item.State is GitStash stash) {
            return await Git(
                token,
                "stash",
                "show",
                "--include-untracked",
                "--patch",
                "--no-ext-diff",
                "--no-textconv",
                stash.Head
            ).ConfigureAwait(false);
        }

        if (item.State is GitTag tag) {
            return await Git(token, "show", "--no-ext-diff", "--no-textconv", "--stat", tag.Head, "--").ConfigureAwait(false);
        }

        return string.Join('\n', item.Cells);
    }

    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.Operations"/>
    public DeveloperOperation[] Operations(int view, DeveloperItem? item) {
        var actions = new List<DeveloperOperation>();
        string branch = _branch;
        string? index = _index;
        string head = _head;
        string root = _root ?? directory;
        if (item?.State is GitChange change) {
            if (_hunkSnapshot is { Hunks.Length: > 0 } snapshot && snapshot.Path == change.Path
                && snapshot.Staged == (view == 1)) {
                actions.Add(HunkOperation(snapshot, root, branch, head));
            }

            string[] paths = change.OldPath is null ? [change.Path] : [change.OldPath, change.Path];
            string[] stagePaths = change.OldPath is not null && change.Index is not ("R" or "C") ? paths : [change.Path];
            actions.Add(new("Stage file", "Stage all current changes for:\n" + string.Join('\n', stagePaths), async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                return await Git(token, ["add", "--", .. stagePaths]).ConfigureAwait(false);
            }));
            if (change.Index is not (" " or "?")) {
                actions.Add(new("Unstage file", "Unstage the selected paths; preserve worktree contents:\n" + string.Join('\n', paths), async (
                    _,
                    token
                ) => {
                    await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                    return head.Length > 0 ? await Git(token, [
                        "restore",
                        "--staged",
                        "--",
                        ..paths
                    ]).ConfigureAwait(false) : await Git(token, [
                        "rm",
                        "--cached",
                        "--",
                        ..paths
                    ]).ConfigureAwait(false);
                }));
            }
        }

        actions.Add(new("Commit staged changes…", "Create a commit from the current index on branch " + branch +
            ". Git hooks/signing configuration apply. Review the Staged tab before confirming.", async (
                message,
                token
            ) => {
                if (string.IsNullOrWhiteSpace(message)) {
                    throw new ArgumentException("A commit message is required.");
                }

                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                if (index is null
                    || index != await Git(token, "diff", "--cached", "--raw", "-z", "--no-abbrev").ConfigureAwait(false)) {
                    throw new InvalidOperationException("The index changed or has not been loaded. Refresh and review staged changes before committing.");
                }

                return await Git(token, "commit", "-m", message).ConfigureAwait(false);
            }, "Commit message"));
        if (item?.State is GitBranch selectedBranch) {
            AddBranchOperations(actions, selectedBranch, root, branch, head);
        }

        if (view == 5) {
            AddWorktreeOperations(actions, item?.State as GitWorktree, root, branch, head);
        }

        if (view == 6) {
            AddStashOperations(actions, item?.State as GitStash, root, branch, head);
        }

        if (view == 7) {
            AddTagOperations(actions, item?.State as GitTag, head);
        }

        if (view == 2 && item is not null) {
            AddHistoryOperations(actions, item.Key, root, branch, head);
        }

        AddProgressOperations(actions, root, head);
        if (view is 0 or 1) {
            actions.Add(new("Stage all changes", "Stage all current tracked changes and untracked files in " + root, async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                return await Git(token, "add", "--all").ConfigureAwait(false);
            }));
            actions.Add(new("Unstage all changes", "Unstage the entire index while preserving worktree files in " + root, async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                return head.Length > 0 ? await Git(token, "restore", "--staged", "--", ".").ConfigureAwait(false) : await Git(token, "rm", "--cached", "-r", "--", ".").ConfigureAwait(false);
            }));
        }

        actions.Add(new("Create branch…", "Create and switch to a new branch at the current HEAD.", async (
            name,
            token
        ) => {
            await VerifyContext(root, branch, head, token).ConfigureAwait(false);
            await Git(token, "check-ref-format", "--branch", name).ConfigureAwait(false);
            return await Git(token, "switch", "-c", name).ConfigureAwait(false);
        }, "New branch name"));
        actions.Add(new("Create branch without switching…", "Create a branch at " + head +
            " while staying in " + root, async (
                name,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                await Git(token, "check-ref-format", "--branch", name).ConfigureAwait(false);
                return await Git(token, "branch", "--", name, head).ConfigureAwait(false);
            }, "New branch name"));
        actions.Add(new("Stash changes…", "Save tracked and untracked changes from " + root +
            " to a new stash. Ignored files are excluded.", async (
                message,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                return await Git(token, "stash", "push", "--include-untracked", "--message", message).ConfigureAwait(false);
            }, "Stash message"));
        actions.Add(new(
            "Fetch",
            "Fetch configured remotes and update remote-tracking refs.",
            async (
                _,
                token
            ) => await Git(token, "fetch", "--all").ConfigureAwait(false)
        ));
        actions.Add(new("Pull (fast-forward only)", "Fetch and fast-forward branch " + branch +
            " from its configured upstream. No merge commit, rebase or autostash.", async (
                _,
                token
            ) => {
                var (Remote, Ref) = await Upstream(branch, token).ConfigureAwait(false);
                return await Git(token, "pull", "--ff-only", "--no-rebase", "--no-autostash", "--", Remote, Ref).ConfigureAwait(false);
            }));
        actions.Add(new("Push", "Push only branch " + branch +
            " to its configured upstream branch. No force push.", async (
                _,
                token
            ) => {
                var (Remote, Ref) = await Upstream(branch, token).ConfigureAwait(false);
                return await Git(token, "push", "--no-force", "--no-follow-tags", "--", Remote, "HEAD:" + Ref).ConfigureAwait(false);
            }));
        return [.. actions];
    }

    private async Task<string> CurrentHead(CancellationToken token) {
        var result = await GitResult(token, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false);
        return result.ExitCode == 1 ? "" : result.RequireSuccess().Trim();
    }

    private async Task VerifyContext(string root, string branch, string head, CancellationToken token) {
        if (!SamePath(root, _root ?? directory)) {
            throw new InvalidOperationException("The active worktree changed. Refresh before acting.");
        }

        await VerifyBranch(branch, token).ConfigureAwait(false);
        if (await CurrentHead(token).ConfigureAwait(false) != head) {
            throw new InvalidOperationException("HEAD changed. Refresh before acting.");
        }
    }

    private async Task VerifyClean(CancellationToken token) {
        if ((await Git(token, "status", "--porcelain=v1", "-z", "--untracked-files=all").ConfigureAwait(false)).Length != 0) {
            throw new InvalidOperationException("Commit or stash existing changes before this operation.");
        }

        if (OperationState().Length > 0) {
            throw new InvalidOperationException("Finish or abort the current Git operation first.");
        }
    }

    private async Task VerifyRef(string reference, string expected, CancellationToken token) {
        if ((await Git(token, "rev-parse", "--verify", "--end-of-options", reference).ConfigureAwait(false)).Trim() != expected) {
            throw new InvalidOperationException("The selected reference changed. Refresh before acting.");
        }
    }

    private static bool SamePath(
        string left,
        string right
    ) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
    private async Task VerifyBranch(string branch, CancellationToken token) {
        if (branch.Length == 0
            || (await Git(token, "branch", "--show-current").ConfigureAwait(false)).Trim() != branch) {
            throw new InvalidOperationException("The current branch changed or HEAD is detached. Refresh before acting.");
        }
    }

    private async Task<(string Remote, string Ref)> Upstream(string branch, CancellationToken token) {
        await VerifyBranch(branch, token).ConfigureAwait(false);
        string remote = (await Git(token, "config", "--get", "branch." + branch +
            ".remote").ConfigureAwait(false)).Trim();
        string reference = (await Git(token, "config", "--get", "branch." + branch +
            ".merge").ConfigureAwait(false)).Trim();
        if (!reference.StartsWith("refs/heads/", StringComparison.Ordinal) || remote.Length == 0) {
            throw new InvalidOperationException("Configure an upstream for the branch before pulling or pushing.");
        }

        return (remote, reference);
    }
}
