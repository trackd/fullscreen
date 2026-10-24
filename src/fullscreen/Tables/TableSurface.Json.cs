using System.Text.RegularExpressions;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tables;

public sealed partial class TableSurface {
    private static readonly Regex JsonTokens = new("""
        "(?:[^"\\]|\\.)*"|\b(?:true|false|null)\b|-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|[{}\[\]]
        """, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private void RenderJsonTokens(CellBuffer buffer, ReadOnlySpan<char> source, int y) {
        string line = source.ToString();
        foreach (Match token in JsonTokens.Matches(line)) {
            int start = Math.Max(token.Index, _documentX), end = Math.Min(token.Index + token.Length, _documentX + Bounds.Width - 3);
            if (start >= end) {
                continue;
            }

            bool quoted = token.Value.StartsWith('"');
            bool key = quoted
                && line.AsSpan(token.Index + token.Length).TrimStart().StartsWith(":", StringComparison.Ordinal);
            bool bracket = token.Value is "{" or "}" or "[" or "]";
            var color = key || bracket ? Palette.Accent : quoted ? Palette.Success : token.Value is "true" or "false" or "null" ? Palette.Warning : Palette.Purple;
            Write(
                buffer,
                2 + start - _documentX,
                y,
                line.AsSpan(start, end - start),
                Body.WithForeground(color),
                end - start
            );
        }
    }
}
