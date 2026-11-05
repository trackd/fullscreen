using XenoAtom.Terminal;

namespace fullscreen.Charts;

internal static class ChartText {
    internal static int Width(ReadOnlySpan<char> text) => TerminalTextUtility.GetWidth(text);

    internal static int PrefixLength(ReadOnlySpan<char> text, int cells) {
        TerminalTextUtility.TryGetIndexAtCell(text, Math.Max(0, cells), out int length);
        return length;
    }

    internal static string Fit(string text, int cells, bool ellipsis = false) {
        if (Width(text) <= cells) {
            return text;
        }
        if (cells <= 0) {
            return "";
        }
        return ellipsis ? text[..PrefixLength(text, cells - 1)] + "…" : text[..PrefixLength(text, cells)];
    }
}
