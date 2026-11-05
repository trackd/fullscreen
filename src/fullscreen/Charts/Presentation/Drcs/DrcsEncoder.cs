using System.Text;

namespace fullscreen.Charts;
/// <summary>10×16 monochrome glyphs and their 96-character DECDLD download.</summary>
internal static class DrcsEncoder {
    internal readonly record struct Tile(ulong Low, ulong Middle, uint High) {
        internal bool Pixel(int index) =>
            index < 64 ? (Low & (1UL << index)) != 0 : index < 128 ? (Middle & (1UL << (index - 64))) != 0 : (High & (1U << (index - 128))) != 0;
    }

    internal const int Width = 10, Height = 16, Capacity = 96;
    /// <summary>Replaces the whole font, starting at the space position.</summary>
    internal static string Download(ReadOnlySpan<Tile> bank) {
        if (bank.Length > Capacity) {
            throw new ArgumentOutOfRangeException(nameof(bank));
        }

        var text = new StringBuilder("\x1bP0;0;0;10;0;2;16;1{ @", 24 + bank.Length * 33);
        for (int glyph = 0; glyph < bank.Length; glyph++) {
            if (glyph != 0) {
                text.Append(';');
            }

            for (int band = 0; band < Height; band += 6) {
                if (band != 0) {
                    text.Append('/');
                }

                for (int x = 0; x < Width; x++) {
                    int mask = 0;
                    for (int bit = 0; bit < 6 && band + bit < Height; bit++) {
                        if (bank[glyph].Pixel((band + bit) * Width + x)) {
                            mask |= 1 << bit;
                        }
                    }

                    text.Append((char)(63 + mask));
                }
            }
        }

        return text.Append("\x1b\\").ToString();
    }
}
