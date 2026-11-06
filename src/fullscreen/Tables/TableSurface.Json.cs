using System.Text.RegularExpressions;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tables;

public sealed partial class TableSurface {
    [GeneratedRegex("""
        "(?:[^"\\]|\\.)*"|\b(?:true|false|null)\b|-?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|[{}\[\]:,]
        """, RegexOptions.CultureInvariant, 50)]
    private static partial Regex JsonTokens();
    private void RenderJsonTokens(CellBuffer buffer, ReadOnlySpan<char> source, int y) {
        int measured = Math.Min(_documentX, source.Length), x = 2;
        foreach (var token in JsonTokens().EnumerateMatches(source)) {
            int start = Math.Max(token.Index, _documentX), end = token.Index + token.Length;
            if (start >= end) {
                continue;
            }
            x += CellWidth(source[measured..start]);
            measured = start;
            if (x >= Bounds.Width - 1) { break; }

            var value = source.Slice(token.Index, token.Length);
            bool quoted = value.StartsWith("\"", StringComparison.Ordinal);
            bool key = quoted
                && source[(token.Index + token.Length)..].TrimStart().StartsWith(":", StringComparison.Ordinal);
            bool punctuation = value is "{" or "}" or "[" or "]" or ":" or ",";
            var color = key ? Palette.Property : punctuation ? Palette.Punctuation : quoted ? Palette.StringLiteral
                : value is "true" or "false" or "null" ? Palette.Keyword : Palette.Number;
            Write(
                buffer,
                x,
                y,
                source[start..end],
                Body.WithForeground(color),
                Bounds.Width - x - 1
            );
        }
    }
}
