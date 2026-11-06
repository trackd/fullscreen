using fullscreen.Themes;
using XenoAtom.Terminal.UI;

namespace fullscreen.Development;

internal static class DeveloperStatus {
    internal static Color? GitChangeColor(string index, string worktree, ThemePalette palette) {
        string status = index + worktree;
        if (status.Contains('U') || status is "AA" or "DD") { return palette.Error; }
        if (status.Contains('D')) { return palette.Error; }
        if (status.Contains('M') || status.Contains('T')) { return palette.Warning; }
        if (status.Contains('R') || status.Contains('C')) { return palette.Purple; }
        if (status.Contains('A') || status.Contains('?')) { return palette.Success; }
        return null;
    }

    internal static Color? GitCellColor(string column, string value, ThemePalette palette) =>
        column switch {
            "Index" or "Worktree" when value.Length == 1 => GitChangeColor(value, "", palette),
            "Current" when value == "*" => palette.Success,
            "Kind" when value == "Remote" => palette.Purple,
            "Ahead / behind" when value.Contains("gone", StringComparison.Ordinal) => palette.Error,
            "Ahead / behind" when value.Contains("behind", StringComparison.Ordinal) => palette.Warning,
            "Ahead / behind" when value.Contains("ahead", StringComparison.Ordinal) => palette.Success,
            "Commit" => palette.Warning,
            _ => null
        };

    internal static Color? GitLineColor(string line, ThemePalette palette) {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff --git ", StringComparison.Ordinal)) { return palette.Purple; }
        if (line.StartsWith("@@", StringComparison.Ordinal)) { return palette.Accent; }
        if (line.StartsWith('+')) { return palette.Success; }
        if (line.StartsWith('-')) { return palette.Error; }
        if (line.StartsWith("<<<<<<<", StringComparison.Ordinal) || line.StartsWith("=======", StringComparison.Ordinal)
            || line.StartsWith(">>>>>>>", StringComparison.Ordinal)) { return palette.Warning; }
        if (line.StartsWith("commit ", StringComparison.Ordinal)) { return palette.Warning; }
        return null;
    }

    internal static Color? Color(string value, ThemePalette palette) {
        string state = value.Trim();
        if (state.StartsWith("✓ ", StringComparison.Ordinal)) {
            return palette.Success;
        }
        if (state.StartsWith("✗ ", StringComparison.Ordinal)) {
            return palette.Error;
        }
        if (state.StartsWith("● ", StringComparison.Ordinal)) {
            return palette.Warning;
        }
        if (state.StartsWith("○ ", StringComparison.Ordinal)) {
            return Muted(palette);
        }

        return state.ToLowerInvariant() switch {
            "success" or "succeeded" or "active" or "open" or "ready" or "approved" => palette.Success,
            "failure" or "failed" or "error" or "timed_out" or "startup_failure" or "action_required"
                or "closed" or "abandoned" or "changes_requested" or "rejected" => palette.Error,
            "queued" or "pending" or "in_progress" or "inprogress" or "waiting" or "requested"
                or "partiallysucceeded" or "notstarted" or "blocked" or "conflicting" => palette.Warning,
            "merged" => palette.Purple,
            "draft" or "cancelled" or "canceled" or "skipped" or "neutral" or "stale" or "completed" or "commented" or "dismissed"
                or "disabled_manually" or "disabled_inactivity" => Muted(palette),
            _ => null
        };
    }

    internal static Color? LineColor(string line, ThemePalette palette) {
        string text = line.TrimStart();
        if (text.StartsWith("State: ", StringComparison.Ordinal)) {
            return Color(text[7..], palette);
        }
        return text.StartsWith("✓ ", StringComparison.Ordinal) || text.StartsWith("✗ ", StringComparison.Ordinal)
            || text.StartsWith("● ", StringComparison.Ordinal) || text.StartsWith("○ ", StringComparison.Ordinal)
            ? Color(text, palette) : null;
    }

    private static Color Muted(ThemePalette palette) =>
        palette.TableMuted.TryGetForeground(out var color) ? color : palette.Foreground;

    internal static string PullRequestState(string state, bool draft, bool merged, string mergeability = "") =>
        merged ? "Merged"
        : state.Equals("closed", StringComparison.OrdinalIgnoreCase) || state.Equals("abandoned", StringComparison.OrdinalIgnoreCase) ? "Closed"
        : draft ? "Draft"
        : mergeability is "dirty" or "blocked" or "conflicts" ? "Blocked" : "Open";
}
