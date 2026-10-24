namespace fullscreen.Development;
/// <summary>Describes a saved Git stash.</summary>
/// <param name="Reference">The Git reference name.</param>
/// <param name="Head">The current Git head revision.</param>
public sealed record GitStash(string Reference, string Head);
/// <summary>Describes a Git tag and its target.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Head">The current Git head revision.</param>
public sealed record GitTag(string Name, string Head);
public sealed partial class GitBackend {
    private async Task<DeveloperPage> LoadStashes(CancellationToken token) {
        string output = await Git(token, "stash", "list", "--format=%gd%x00%H%x00%gs%x00%ci%x00").ConfigureAwait(false);
        var fields = output.Split('\0');
        var items = new List<DeveloperItem>();
        for (int i = 0; i + 3 < fields.Length; i += 4) {
            var stash = new GitStash(fields[i].Trim('\r', '\n'), fields[i + 1]);
            items.Add(new(
                stash.Head,
                [
                    stash.Reference,
                    fields[i + 2],
                    fields[i + 3],
                    stash.Head[..Math.Min(12, stash.Head.Length)]
                ],
                stash
            ));
        }

        return new(
            [
                "Stash",
                "Message",
                "Date",
                "Commit"
            ],
            [.. items],
            "Stashes · A save / apply / pop / drop · selected stash IDs are rechecked",
            [
                16,
                48,
                28,
                0
            ]
        );
    }

    private void AddStashOperations(
        List<DeveloperOperation> actions,
        GitStash? selected,
        string root,
        string branch,
        string head
    ) {
        if (selected is null) {
            return;
        }

        foreach (string command in new[]
        {
            "apply",
            "pop",
            "drop"
        }

        ) {
            string label = command switch {
                "apply" => "Apply stash",
                "pop" => "Pop stash",
                _ => "Drop stash"
            };
            string description = $"{label} {selected.Reference} ({selected.Head}) in {root}. " + (command == "drop" ? "Deletes this saved stash entry." : command == "pop" ? "Apply to a clean worktree and remove the stash only when Git succeeds." : "Apply to a clean worktree and retain the saved stash.");
            actions.Add(new(label, description, async (_, token) => {
                if (!SamePath(root, _root!)) {
                    throw new InvalidOperationException("The active worktree changed. Refresh before acting.");
                }

                await VerifyRef(selected.Reference, selected.Head, token).ConfigureAwait(false);
                if (command != "drop") {
                    await VerifyContext(root, branch, head, token).ConfigureAwait(false);
                    await VerifyClean(token).ConfigureAwait(false);
                }

                return await Git(token, "stash", command, command == "apply" ? selected.Head : selected.Reference).ConfigureAwait(false);
            }));
        }
    }

    private async Task<DeveloperPage> LoadTags(CancellationToken token) {
        string output = await Git(
            token,
            "for-each-ref",
            "--sort=-creatordate",
            "--format=%(refname:short)%00%(objectname)%00%(objecttype)%00%(creatordate:iso-strict)%00%(subject)%00",
            "refs/tags/"
        ).ConfigureAwait(false);
        var fields = output.Split('\0');
        var items = new List<DeveloperItem>();
        for (int i = 0; i + 4 < fields.Length; i += 5) {
            var tag = new GitTag(fields[i].Trim('\r', '\n'), fields[i + 1]);
            items.Add(new(
                tag.Name,
                [
                    tag.Name,
                    fields[i + 2] == "tag" ? "Annotated" : "Lightweight",
                    fields[i + 3],
                    fields[i + 4],
                    tag.Head[..Math.Min(12, tag.Head.Length)]
                ],
                tag
            ));
        }

        return new(
            [
                "Tag",
                "Kind",
                "Date",
                "Message",
                "Object"
            ],
            [.. items],
            "Tags · A create / delete local / push selected tag",
            [
                25,
                16,
                28,
                42,
                0
            ]
        );
    }

    private void AddTagCreation(List<DeveloperOperation> actions, string commit) {
        actions.Add(new("Create annotated tag…", "Create an annotated tag at " + commit +
            ". The tag name is also its annotation message; existing tags are not overwritten.", async (
                name,
                token
            ) => {
                await Git(token, "check-ref-format", "refs/tags/" + name).ConfigureAwait(false);
                if (commit.Length == 0) {
                    throw new InvalidOperationException("Create a commit before adding a tag.");
                }

                return await Git(token, "tag", "--annotate", "--message", name, "--", name, commit).ConfigureAwait(false);
            }, "Tag name"));
    }

    private void AddTagOperations(List<DeveloperOperation> actions, GitTag? selected, string head) {
        AddTagCreation(actions, head);
        if (selected is null) {
            return;
        }

        actions.Add(new("Delete local tag", "Delete local tag " + selected.Name +
            " at " + selected.Head +
            ". Remote tags are unchanged.", async (
                _,
                token
            ) => {
                await VerifyRef("refs/tags/" + selected.Name, selected.Head, token).ConfigureAwait(false);
                return await Git(token, "tag", "--delete", "--", selected.Name).ConfigureAwait(false);
            }));
        actions.Add(new("Push selected tag…", "Push only tag " + selected.Name +
            " at " + selected.Head +
            " to the entered remote. No force update.", async (
                remote,
                token
            ) => {
                if (string.IsNullOrWhiteSpace(remote)) {
                    throw new ArgumentException("Enter a configured remote name.");
                }

                await VerifyRef("refs/tags/" + selected.Name, selected.Head, token).ConfigureAwait(false);
                await Git(token, "remote", "get-url", "--", remote).ConfigureAwait(false);
                string reference = "refs/tags/" + selected.Name;
                return await Git(
                    token,
                    "push",
                    "--no-force",
                    "--no-follow-tags",
                    "--",
                    remote,
                    reference +
                    ":" + reference
                ).ConfigureAwait(false);
            }, "Configured remote", "origin"));
    }
}
