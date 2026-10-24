namespace fullscreen.Development;
/// <summary>Describes a Git worktree and its checked-out revision.</summary>
/// <param name="Path">The filesystem or resource path.</param>
/// <param name="Head">The current Git head revision.</param>
/// <param name="Branch">The branch name.</param>
/// <param name="Detached">Whether the worktree uses a detached HEAD.</param>
/// <param name="Bare">Whether the repository is bare.</param>
/// <param name="Locked">Whether the worktree is locked.</param>
/// <param name="Prunable">Whether Git reports the worktree as prunable.</param>
/// <param name="Main">Whether this worktree is the repository&apos;s main worktree.</param>
public sealed record GitWorktree(
    string Path,
    string Head,
    string Branch,
    bool Detached,
    bool Bare,
    string? Locked,
    string? Prunable,
    bool Main
);
public sealed partial class GitBackend {
    /// <summary>Parses Git&apos;s machine-readable worktree listing.</summary>
    /// <param name="output">The output of git worktree list --porcelain.</param>
    /// <returns>The parsed worktree records.</returns>
    public static GitWorktree[] ParseWorktrees(string output) {
        var result = new List<GitWorktree>();
        string? path = null;
        string head = "", branch = "";
        bool detached = false, bare = false;
        string? locked = null, prunable = null;
        void Add() {
            if (path is not null) {
                result.Add(new(path, head, branch, detached, bare, locked, prunable, result.Count == 0));
            }

            path = null;
            head = "";
            branch = "";
            detached = false;
            bare = false;
            locked = null;
            prunable = null;
        }

        foreach (string field in output.Split('\0')) {
            if (field.Length == 0) {
                Add();
                continue;
            }

            int space = field.IndexOf(' ');
            string key = space < 0 ? field : field[..space], value = space < 0 ? "" : field[(space + 1)..];
            switch (key) {
                case "worktree":
                    path = value;
                    break;
                case "HEAD":
                    head = value;
                    break;
                case "branch":
                    branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value[11..] : value;
                    break;
                case "detached":
                    detached = true;
                    break;
                case "bare":
                    bare = true;
                    break;
                case "locked":
                    locked = value;
                    break;
                case "prunable":
                    prunable = value;
                    break;
            }
        }

        Add();
        return [.. result];
    }

    private async Task<GitWorktree[]> Worktrees(CancellationToken token) =>
        ParseWorktrees(await Git(token, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false));
    private async Task<DeveloperPage> LoadWorktrees(CancellationToken token) {
        var worktrees = await Worktrees(token).ConfigureAwait(false);
        return new(
            [
                "Current",
                "Path",
                "Branch",
                "State",
                "Commit",
                "Lock reason"
            ],
            [..worktrees.Select(worktree => new DeveloperItem(
                worktree.Path,
                [
                    SamePath(worktree.Path, _root!) ? "*" : "",
                    worktree.Path,
                    worktree.Branch,
                    worktree.Bare ? "Bare" : worktree.Prunable is not null ? "Prunable" : worktree.Locked is not null ? "Locked" : worktree.Detached ? "Detached" : worktree.Main ? "Main" : "Linked",
                    worktree.Head[..Math.Min(12, worktree.Head.Length)],
                    worktree.Locked ?? ""
                ],
                worktree
            ))],
            "Worktrees · A open / add / move / remove / lock · paths are literal",
            [
                9,
                40,
                25,
                14,
                14,
                0
            ]
        );
    }

    private async Task<string> WorktreeDetail(GitWorktree worktree, CancellationToken token) {
        string info = $"{worktree.Path}\nBranch: {worktree.Branch}\nHEAD: {worktree.Head}\nMain: {worktree.Main}\nDetached: {worktree.Detached}\nLocked: {worktree.Locked ?? "no"}\nPrunable: {worktree.Prunable ?? "no"}\n\n";
        if (!Directory.Exists(worktree.Path) || worktree.Bare) {
            return info;
        }

        return info + await Git(token, "-C", worktree.Path, "status", "--short", "--branch").ConfigureAwait(false);
    }

    private string WorktreeDestination(string input) {
        if (string.IsNullOrWhiteSpace(input)) {
            throw new ArgumentException("A destination path is required.");
        }

        string path = Path.GetFullPath(input, _root!);
        if (Directory.Exists(path) || File.Exists(path)) {
            throw new IOException("The destination already exists. Choose a new directory.");
        }

        string current = Path.GetPathRoot(path)!;
        foreach (string segment in path[current.Length..].Split(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ],
            StringSplitOptions.RemoveEmptyEntries
        )) {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (Directory.Exists(current) && (info.Attributes & FileAttributes.ReparsePoint) != 0) {
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unable to resolve destination directory link.");
            }
        }

        path = current;
        if (_commonDirectory is { } common && (SamePath(path, common) || path.StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(common)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        ))) {
            throw new ArgumentException("Choose a destination outside Git's administration directory.");
        }

        return path;
    }

    private async Task<GitWorktree> VerifyWorktree(GitWorktree expected, CancellationToken token) {
        var current = (await Worktrees(token).ConfigureAwait(false)).FirstOrDefault(worktree => SamePath(worktree.Path, expected.Path));
        if (current is null || current.Head != expected.Head || current.Branch != expected.Branch
            || current.Locked != expected.Locked) {
            throw new InvalidOperationException("The selected worktree changed. Refresh before acting.");
        }

        return current;
    }

    private void AddWorktreeOperations(
        List<DeveloperOperation> actions,
        GitWorktree? selected,
        string root,
        string branch,
        string head
    ) {
        actions.Add(new("Add worktree on new branch…", "Create a new worktree at " + head +
            ". Its folder name becomes a new branch name. The path and branch must not already exist.", async (
                input,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                string path = WorktreeDestination(input), name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                await Git(token, "check-ref-format", "--branch", name).ConfigureAwait(false);
                return await Git(token, "worktree", "add", "-b", name, "--", path, head).ConfigureAwait(false);
            }, "New worktree path (folder name becomes branch name)"));
        actions.Add(new("Add detached worktree…", "Create a detached worktree at " + head +
            ". The destination must not exist.", async (
                input,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                return await Git(token, "worktree", "add", "--detach", "--", WorktreeDestination(input), head).ConfigureAwait(false);
            }, "New worktree path"));
        if (selected is null || selected.Bare) {
            return;
        }

        actions.Add(new("Open worktree", "Inspect " + selected.Path + " in this Git TUI.", async (_, token) => {
            await VerifyWorktree(selected, token).ConfigureAwait(false);
            string common = (await Git(token, "-C", selected.Path, "rev-parse", "--path-format=absolute", "--git-common-dir").ConfigureAwait(false)).TrimEnd('\r', '\n');
            if (_commonDirectory is null || !SamePath(common, _commonDirectory)) {
                throw new InvalidOperationException("The selected path no longer belongs to this repository.");
            }

            string next = (await Git(token, "-C", selected.Path, "rev-parse", "--show-toplevel").ConfigureAwait(false)).TrimEnd('\r', '\n');
            _root = next;
            _gitDirectory = null;
            _index = null;
            return "Opened " + next;
        }, ReadOnly: true, TargetView: 0));
        if (selected.Main) {
            return;
        }

        if (selected.Locked is null) {
            actions.Add(new("Lock worktree…", "Protect " + selected.Path +
                " from removal, movement and pruning.", async (
                    reason,
                    token
                ) => {
                    await VerifyWorktree(selected, token).ConfigureAwait(false);
                    return await Git(token, "worktree", "lock", "--reason", reason, "--", selected.Path).ConfigureAwait(false);
                }, "Lock reason"));
        }
        else {
            actions.Add(new("Unlock worktree", "Remove the lock on " + selected.Path +
                "\nReason: " + selected.Locked, async (
                    _,
                    token
                ) => {
                    await VerifyWorktree(selected, token).ConfigureAwait(false);
                    return await Git(token, "worktree", "unlock", "--", selected.Path).ConfigureAwait(false);
                }));
        }

        if (SamePath(selected.Path, root)) {
            return;
        }

        actions.Add(new("Move worktree…", "Move " + selected.Path +
            " to a new directory, preserving its files. Git refuses locked worktrees.", async (
                input,
                token
            ) => {
                await VerifyWorktree(selected, token).ConfigureAwait(false);
                return await Git(token, "worktree", "move", "--", selected.Path, WorktreeDestination(input)).ConfigureAwait(false);
            }, "New worktree path"));
        actions.Add(new("Remove clean worktree", "Remove worktree directory " + selected.Path +
            " and its registration. Git refuses dirty, untracked, locked or main worktrees. The branch is retained.", async (
                _,
                token
            ) => {
                await VerifyWorktree(selected, token).ConfigureAwait(false);
                if (SamePath(selected.Path, _root!)) {
                    throw new InvalidOperationException("Switch away from this worktree before removing it.");
                }

                return await Git(token, "worktree", "remove", "--", selected.Path).ConfigureAwait(false);
            }));
    }
}
