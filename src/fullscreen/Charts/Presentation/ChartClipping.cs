using XenoAtom.Ansi;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Charts;

internal static class ChartClipping {
    internal static Rectangle Intersect(Rectangle a, Rectangle b) {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    /// <summary>Erases an old graphic's cells except those under popup windows, which are already painted.</summary>
    internal static void Erase(
        AnsiWriter writer,
        Rectangle area,
        IReadOnlyList<Rectangle> windows,
        Rectangle viewport
    ) {
        var visible = Intersect(area, new(0, 0, viewport.Width, viewport.Height));
        for (int y = visible.Y; y < visible.Bottom; y++) {
            for (int x = visible.X; x < visible.Right;) {
                if (Inside(windows, x, y)) {
                    x++;
                    continue;
                }

                int start = x++;
                while (x < visible.Right && !Inside(windows, x, y)) {
                    x++;
                }

                writer.CursorPosition(y + viewport.Y + 1, start + viewport.X + 1);
                // ECH erases cells without advancing/wrapping the cursor.
                writer.Write($"\x1b[{x - start}X");
            }
        }
    }

    private static bool Inside(IReadOnlyList<Rectangle> windows, int x, int y) {
        foreach (var window in windows) {
            if (x >= window.X && x < window.Right && y >= window.Y && y < window.Bottom) {
                return true;
            }
        }

        return false;
    }
}
