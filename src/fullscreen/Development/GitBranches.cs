namespace fullscreen.Development;
/// <summary>Describes a Git branch and its tracking relationship.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Reference">The Git reference name.</param>
/// <param name="Head">The current Git head revision.</param>
/// <param name="Upstream">The upstream Git reference.</param>
/// <param name="Tracking">The tracking branch description.</param>
/// <param name="Worktree">The worktree status or path.</param>
/// <param name="Remote">The remote repository or tracking reference.</param>
public sealed record GitBranch(
    string Name,
    string Reference,
    string Head,
    string Upstream,
    string Tracking,
    string Worktree,
    bool Remote
);
public sealed partial class GitBackend {
    private async Task<DeveloperPage> LoadBranches(CancellationToken token) {
        string output = await Git(
            token,
            "for-each-ref",
            "--sort=refname",
            "--format=%(refname)%00%(refname:short)%00%(objectname)%00%(upstream:short)%00%(upstream:track)%00%(HEAD)%00%(worktreepath)%00%(symref)%00",
            "refs/heads/",
            "refs/remotes/"
        ).ConfigureAwait(false);
        var fields = output.Split('\0');
        var items = new List<DeveloperItem>();
        for (int i = 0; i + 7 < fields.Length; i += 8) {
            string reference = fields[i].Trim('\r', '\n');
            if (reference.Length == 0 || fields[i + 7].Length != 0) {
                continue;
            }

            bool remote = reference.StartsWith("refs/remotes/", StringComparison.Ordinal);
            var branch = new GitBranch(
                fields[i + 1],
                reference,
                fields[i + 2],
                fields[i + 3],
                fields[i + 4],
                fields[i + 6],
                remote
            );
            items.Add(new(
                remote ? "remote:" + branch.Name : branch.Name,
                [
                    fields[i + 5],
                    branch.Name,
                    remote ? "Remote" : "Local",
                    branch.Upstream,
                    branch.Tracking,
                    branch.Worktree,
                    branch.Head[..Math.Min(12, branch.Head.Length)]
                ],
                branch
            ));
        }

        return new(
            [
                "Current",
                "Branch",
                "Kind",
                "Upstream",
                "Ahead / behind",
                "Worktree",
                "Commit"
            ],
            [.. items],
            "Local + remote branches · A switch / track / rename / delete / merge / rebase / worktree",
            [
                9,
                28,
                12,
                25,
                24,
                40,
                0
            ]
        );
    }

    private async Task<string> BranchDetail(GitBranch branch, CancellationToken token) {
        await VerifyRef(branch.Reference, branch.Head, token).ConfigureAwait(false);
        return $"{branch.Name}\n{branch.Reference}\nHEAD {branch.Head}\nUpstream: {branch.Upstream} {branch.Tracking}\nChecked out at: {branch.Worktree}\n\n" + await Git(token, "log", "-20", "--oneline", "--decorate", branch.Head, "--").ConfigureAwait(false);
    }

    private void AddBranchOperations(
        List<DeveloperOperation> actions,
        GitBranch selected,
        string root,
        string branch,
        string head
    ) {
        actions.Add(new("Compare with current HEAD", $"Compare {selected.Name} with the active worktree.", async (
            _,
            token
        ) => {
            await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
            return "COMMITS · < current, > selected\n" + await Git(token, "log", "--left-right", "--oneline", "HEAD..." + selected.Head, "--").ConfigureAwait(false) +
                "\nDIFF SUMMARY\n" + await Git(token, "diff", "--no-ext-diff", "--no-textconv", "--stat", "HEAD", selected.Head, "--").ConfigureAwait(false);
        }, ReadOnly: true));
        if (selected.Remote) {
            actions.Add(new("Create tracking branch…", "Create and switch to a local branch tracking " + selected.Name, async (
                name,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                await Git(token, "check-ref-format", "--branch", name).ConfigureAwait(false);
                return await Git(token, "switch", "--track", "-c", name, selected.Reference).ConfigureAwait(false);
            }, "New local branch name", selected.Name.Contains('/') ? selected.Name[(selected.Name.IndexOf('/') + 1)..] : selected.Name));
        }
        else {
            actions.Add(new("Switch branch", "Switch to local branch " + selected.Name +
                ". Git protects changes and branches checked out in other worktrees.", async (
                    _,
                    token
                ) => {
                    await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                    await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                    return await Git(token, "switch", "--no-guess", selected.Name).ConfigureAwait(false);
                }));
            actions.Add(new("Rename branch…", "Rename " + selected.Name +
                "; do not overwrite an existing branch.", async (
                    name,
                    token
                ) => {
                    await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                    await Git(token, "check-ref-format", "--branch", name).ConfigureAwait(false);
                    return await Git(token, "branch", "--move", "--", selected.Name, name).ConfigureAwait(false);
                }, "New branch name", selected.Name));
            actions.Add(new("Delete merged branch", "Delete local branch " + selected.Name +
                " at " + selected.Head +
                ". Git refuses unmerged branches and branches checked out in any worktree.", async (
                    _,
                    token
                ) => {
                    await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                    return await Git(token, "branch", "--delete", "--", selected.Name).ConfigureAwait(false);
                }));
            actions.Add(new("Set upstream…", "Set the upstream for " + selected.Name +
                " to an existing remote-tracking branch.", async (
                    upstream,
                    token
                ) => {
                    await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                    string reference = (await Git(token, "rev-parse", "--symbolic-full-name", "--verify", "--end-of-options", upstream).ConfigureAwait(false)).Trim();
                    if (!reference.StartsWith("refs/remotes/", StringComparison.Ordinal) || reference.Contains('\n')) {
                        throw new ArgumentException("Choose an existing remote-tracking branch, such as origin/main.");
                    }

                    return await Git(token, "branch", "--set-upstream-to=" + reference, "--", selected.Name).ConfigureAwait(false);
                }, "Remote-tracking branch", selected.Upstream));
            if (selected.Upstream.Length > 0) {
                actions.Add(new("Unset upstream", "Remove upstream tracking from " + selected.Name, async (
                    _,
                    token
                ) => {
                    await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                    return await Git(token, "branch", "--unset-upstream", "--", selected.Name).ConfigureAwait(false);
                }));
            }

            if (selected.Worktree.Length == 0) {
                actions.Add(new("Add worktree for branch…", "Check out existing branch " + selected.Name +
                    " in a new directory. The destination must not exist.", async (
                        path,
                        token
                    ) => {
                        await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                        return await Git(token, "worktree", "add", "--", WorktreeDestination(path), selected.Name).ConfigureAwait(false);
                    }, "New worktree path"));
            }
        }

        if (head.Length > 0 && selected.Head != head) {
            actions.Add(new("Merge into current branch", $"Merge {selected.Name} ({selected.Head}) into {branch}. Requires a clean worktree. May create a merge commit; conflicts remain for resolution.", async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                await VerifyClean(token).ConfigureAwait(false);
                return await RunSequence(token, "merge", "--no-edit", selected.Head).ConfigureAwait(false);
            }));
            actions.Add(new("Rebase current branch onto selected", $"Rebase {branch} onto {selected.Name} ({selected.Head}). Rewrites commits on {branch}; requires a clean worktree. No automatic stash or force push.", async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                await VerifyClean(token).ConfigureAwait(false);
                return await RunSequence(token, "rebase", "--no-autostash", "--no-update-refs", "--no-autosquash", selected.Head).ConfigureAwait(false);
            }));
        }
    }

    private void AddHistoryOperations(
        List<DeveloperOperation> actions,
        string commit,
        string root,
        string branch,
        string head
    ) {
        foreach (string command in new[]
        {
            "cherry-pick",
            "revert"
        }

        ) {
            string label = command == "cherry-pick" ? "Cherry-pick commit" : "Revert commit";
            actions.Add(new(label, $"{label} {commit} on {branch}. Requires a clean worktree. Git creates a new commit; conflicts remain for resolution.", async (
                _,
                token
            ) => {
                await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                await VerifyClean(token).ConfigureAwait(false);
                return await RunSequence(token, command, "--no-edit", commit).ConfigureAwait(false);
            }));
        }

        AddTagCreation(actions, commit);
    }

    private string OperationState() {
        if (_gitDirectory is null) {
            return "";
        }

        if (Directory.Exists(Path.Combine(_gitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(_gitDirectory, "rebase-apply"))) {
            return "rebase";
        }

        if (File.Exists(Path.Combine(_gitDirectory, "MERGE_HEAD"))) {
            return "merge";
        }

        if (File.Exists(Path.Combine(_gitDirectory, "CHERRY_PICK_HEAD"))) {
            return "cherry-pick";
        }

        if (File.Exists(Path.Combine(_gitDirectory, "REVERT_HEAD"))) {
            return "revert";
        }

        return "";
    }

    private async Task<string> RunSequence(CancellationToken token, params string[] arguments) {
        try {
            return await Git(token, arguments).ConfigureAwait(false);
        }
        finally {
            _inProgress = OperationState();
            if (_inProgress.Length > 0 && !token.IsCancellationRequested) {
                _head = await CurrentHead(token).ConfigureAwait(false);
            }
        }
    }

    private void AddProgressOperations(List<DeveloperOperation> actions, string root, string head) {
        string operation = _inProgress;
        if (operation.Length == 0) {
            return;
        }

        foreach (bool abort in new[]
        {
            false,
            true
        }

        ) {
            string action = abort ? "Abort " : "Continue ";
            actions.Add(new(action + operation, action + operation +
                " in " + root + (abort ? ". Discards in-progress resolution changes and returns to the pre-operation state." : ". Stage resolved files first. Uses the existing commit message without opening an editor."), async (
                    _,
                    token
                ) => {
                    if (!SamePath(root, _root!) || OperationState() != operation
                    || await CurrentHead(token).ConfigureAwait(false) != head) {
                        throw new InvalidOperationException("The Git operation changed. Refresh before acting.");
                    }

                    if (abort) {
                        return await Git(token, operation, "--abort").ConfigureAwait(false);
                    }

                    return operation == "merge" ? await Git(token, "commit", "--no-edit").ConfigureAwait(false) : await Git(token, "-c", "core.editor=:", operation, "--continue").ConfigureAwait(false);
                }));
        }
    }
}
