using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Custom;
/// <summary>Text-cell latency range with absolute millisecond bands, min/max whiskers and an average marker.</summary>
public sealed class LatencyRangeCell(double? minimum, double? average, double? maximum, double scale) : Visual {
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) =>
        SizeHints.Flex(new(8, 1), new(32, 1), new(int.MaxValue, 1), 1, 0, 1, 0);
    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        int width = Bounds.Width;
        double limit = double.IsFinite(scale) && scale > 0 ? scale : 200;
        int Column(double value) => (int)Math.Round(Math.Clamp(value / limit, 0, 1) * Math.Max(0, width - 1));
        bool valid = minimum is { } low && average is { } mid && maximum is { } high
            && double.IsFinite(low) && double.IsFinite(mid) && double.IsFinite(high);
        int first = valid ? Column(minimum!.Value) : -1, last = valid ? Column(maximum!.Value) : -1;
        int center = valid ? Column(average!.Value) : -1;
        for (int x = 0; x < width; x++) {
            double milliseconds = x * limit / Math.Max(1, width - 1);
            Color background = milliseconds < 50 ? Color.Rgb(29, 74, 48)
                : milliseconds < 150 ? Color.Rgb(91, 70, 27) : Color.Rgb(94, 38, 44);
            char glyph = x == center ? '●' : x == first || x == last ? '│' : x > first && x < last ? '━' : ' ';
            if (!valid && x == width / 2) {
                glyph = '×';
            }
            var style = new Style().WithForeground(x == center ? Color.Rgb(255, 255, 255) : Color.Rgb(184, 195, 202)).WithBackground(background);
            buffer.SetCell(Bounds.X + x, Bounds.Y, new Rune(glyph), style);
        }
    }
}
