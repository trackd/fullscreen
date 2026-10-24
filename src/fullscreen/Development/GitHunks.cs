using System.Globalization;
using System.Text;

namespace fullscreen.Development;
/// <summary>Describes a selectable section of a unified Git patch.</summary>
/// <param name="Header">The displayed column heading.</param>
/// <param name="Patch">The unified diff text, or whether the HTTP request uses JSON Patch.</param>
public sealed record GitHunk(string Header, string Patch);
public sealed partial class GitBackend {
    private sealed record HunkSnapshot(string Path, bool Staged, string Patch, GitHunk[] Hunks);
    private HunkSnapshot? _hunkSnapshot;
    private static readonly string[] ReversePatchArguments = ["--reverse"];
    private static readonly string[] CachedDiffArguments = ["--cached"];
    /// <summary>Parses selectable hunks from a unified patch.</summary>
    /// <param name="patch">The unified diff text to parse.</param>
    /// <returns>The parsed patch hunks.</returns>
    public static GitHunk[] ParseHunks(string patch) {
        // Partial application is limited to ordinary modifications. Whole-file actions handle other changes.
        if (patch.Contains("new file mode ", StringComparison.Ordinal)
            || patch.Contains("deleted file mode ", StringComparison.Ordinal)
            || patch.Contains("old mode ", StringComparison.Ordinal)
            || patch.Contains("GIT binary patch", StringComparison.Ordinal)) {
            return [];
        }

        string[] lines = patch.Split('\n');
        int first = Array.FindIndex(lines, line => line.StartsWith("@@ ", StringComparison.Ordinal));
        if (first < 0 || !lines.Take(first).Any(line => line.StartsWith("--- ", StringComparison.Ordinal))) {
            return [];
        }

        string header = string.Join('\n', lines.AsSpan(0, first).ToArray()) + "\n";
        var hunks = new List<GitHunk>();
        for (int start = first; start < lines.Length;) {
            int end = start + 1;
            while (end < lines.Length && !lines[end].StartsWith("@@ ", StringComparison.Ordinal)) {
                end++;
            }

            string body = string.Join('\n', lines.AsSpan(start, end - start).ToArray()).TrimEnd('\n') + "\n";
            hunks.Add(new(lines[start], header + body));
            start = end;
        }

        return [.. hunks];
    }

    private Task<string> HunkDiff(
        string path,
        bool staged,
        CancellationToken token
    ) =>
        Git(
            token,
            [
                "diff",
                ..staged ? CachedDiffArguments : [],
                "--no-ext-diff",
                "--no-textconv",
                "--no-renames",
                "--unified=3",
                "--",
                path
            ]
        );
    private async Task<string> HunkDetail(GitChange change, bool staged, CancellationToken token) {
        string patch = await HunkDiff(change.Path, staged, token).ConfigureAwait(false);
        var hunks = ParseHunks(patch);
        _hunkSnapshot = new(change.Path, staged, patch, hunks);
        var text = new StringBuilder(staged ? "STAGED" : "WORKTREE");
        text.Append(" · ").Append(hunks.Length).Append(" hunk(s) · A actions to stage/unstage a numbered hunk\n\n");
        if (hunks.Length == 0) {
            text.Append(patch);
        }

        for (int i = 0; i < hunks.Length; i++) {
            text.Append("HUNK ").Append(i + 1).Append(" / ").Append(hunks.Length).Append('\n').Append(hunks[i].Patch).Append('\n');
        }

        if (!staged && change.Index is not (" " or "?")) {
            text.Append("\nSTAGED · whole-file preview\n").Append(await HunkDiff(change.Path, true, token).ConfigureAwait(false));
        }

        return text.ToString();
    }

    private DeveloperOperation HunkOperation(
        HunkSnapshot snapshot,
        string root,
        string branch,
        string head
    ) =>
        new(snapshot.Staged ? "Unstage hunk…" : "Stage hunk…", $"{snapshot.Path}\nChoose one of the {snapshot.Hunks.Length} numbered hunks in the diff preview. Worktree contents are preserved.", async (
            input,
            token
        ) => {
        if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1
            || number > snapshot.Hunks.Length) {
            throw new ArgumentException("Choose a hunk number from the preview.");
        }

        await VerifyContext(root, branch, head, token).ConfigureAwait(false);
        string current = await HunkDiff(snapshot.Path, snapshot.Staged, token).ConfigureAwait(false);
        if (!string.Equals(current, snapshot.Patch, StringComparison.Ordinal)) {
            throw new InvalidOperationException("The diff changed. Refresh and review the hunks again.");
        }

        string patch = snapshot.Hunks[number - 1].Patch;
        // Git output is decoded as UTF-8; re-encoding a lossy decode would corrupt non-UTF-8 bytes in the index.
        if (patch.Contains('\uFFFD')) {
            throw new InvalidOperationException("This hunk is not valid UTF-8. Stage the whole file instead.");
        }

        string[] arguments = [
            "--no-pager",
            "apply",
            "--cached",
            ..snapshot.Staged ? ReversePatchArguments : [],
            "--whitespace=nowarn",
            "-"
        ];
        var result = await runner.RunAsync(new("git", root, arguments, patch), token).ConfigureAwait(false);
        result.RequireSuccess();
        return snapshot.Staged ? "Hunk unstaged." : "Hunk staged.";
    }, "Hunk number", "1");
}
