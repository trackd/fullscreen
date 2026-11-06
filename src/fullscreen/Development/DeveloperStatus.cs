using fullscreen.Themes;
using XenoAtom.Terminal.UI;

namespace fullscreen.Development;

internal static class DeveloperStatus {
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
