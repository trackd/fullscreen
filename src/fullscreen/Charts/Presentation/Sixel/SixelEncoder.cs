using System.Text;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>DEC sixel encoding for an indexed raster. Index zero is transparent.</summary>
internal sealed class SixelEncoder {
    private readonly StringBuilder _output = new(4096);
    private byte[] _planes = [];
    private readonly Color[] _chartPalette = new Color[SixelPlot.InkColors + 1];
    private string? _chartPaletteHeader;
    internal string EncodePlot(SixelPlot plot) {
        var output = _output;
        output.Clear();
        // These six theme-derived shades are known before any pixels are drawn.
        // Cache their protocol definitions until the theme/ink changes.
        if (_chartPaletteHeader is null || !plot.Palette.AsSpan().SequenceEqual(_chartPalette)) {
            for (int color = 1; color <= SixelPlot.InkColors; color++) {
                var rgb = plot.Palette[color].ToRgb();
                output.Append('#').Append(color).Append(";2;").Append((rgb.R * 100 + 127) / 255).Append(';').Append((rgb.G * 100 + 127) / 255).Append(';').Append((rgb.B * 100 + 127) / 255);
            }

            _chartPaletteHeader = output.ToString();
            plot.Palette.CopyTo(_chartPalette, 0);
            output.Clear();
        }

        output.Append("\x1bP0;1q\"1;1;").Append(plot.Width).Append(';').Append(plot.Height).Append(_chartPaletteHeader);
        int emittedBand = 0, selectedColor = SixelPlot.InkColors;
        bool painted = false;
        for (int band = 0; band < plot.BandCount; band++) {
            bool first = true;
            for (int color = 1; color <= SixelPlot.InkColors; color++) {
                var (left, right) = plot.Columns(band, color);
                if (left >= right) {
                    continue;
                }

                var plane = plot.Plane(band, color);
                // Overpainting can remove the boundary pixels of an earlier shade.
                while (left < right && plane[left] == 0) {
                    left++;
                }

                while (right > left && plane[right - 1] == 0) {
                    right--;
                }

                if (left == right) {
                    continue;
                }

                if (first) {
                    output.Append('-', band - emittedBand);
                    emittedBand = band;
                }
                else {
                    output.Append('$');
                }

                first = false;
                painted = true;
                if (selectedColor != color) {
                    output.Append('#').Append(color);
                    selectedColor = color;
                }

                Repeat(output, 0, left);
                int previous = -1, count = 0;
                foreach (byte mask in plane[left..right]) {
                    if (mask == previous) {
                        count++;
                    }
                    else {
                        Repeat(output, previous, count);
                        previous = mask;
                        count = 1;
                    }
                }

                Repeat(output, previous, count);
            }
        }

        if (!painted) {
            output.Append('?');
        }

        return output.Append("\x1b\\").ToString();
    }

    public static string Encode(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        ReadOnlySpan<Color> palette
    ) =>
        new SixelEncoder().EncodeFrame(pixels, width, height, palette);
    public string EncodeFrame(ReadOnlySpan<byte> pixels, int width, int height, ReadOnlySpan<Color> palette) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height) || palette.Length is < 1 or > 256) {
            throw new ArgumentException("Invalid indexed raster.");
        }

        var output = _output;
        output.Clear();
        int planeSize = checked(width * (palette.Length - 1));
        if (_planes.Length < planeSize) {
            _planes = new byte[planeSize];
        }

        // Declare the complete canvas on every frame, but only transmit graph ink.
        // Palette index zero is absent; the presenter owns erasing previous ink.
        output.Append("\x1bP0;1q\"1;1;").Append(width).Append(';').Append(height);
        Span<byte> defined = stackalloc byte[256];
        defined.Clear();
        Span<int> lastColumn = stackalloc int[256];
        Span<byte> bandColors = stackalloc byte[256];
        int emittedBand = 0;
        int selectedColor = -1;
        bool painted = false;
        for (int top = 0; top < height; top += 6) {
            _planes.AsSpan(0, planeSize).Clear();
            lastColumn.Clear();
            int bandColorCount = 0;
            // Visit each source pixel once, rather than once for every palette
            // color. The reusable planes hold the completed six-bit column masks.
            for (int y = top; y < Math.Min(top + 6, height); y++) {
                for (int x = 0; x < width; x++) {
                    int color = pixels[y * width + x];
                    if (color >= palette.Length) {
                        throw new ArgumentException("Pixel outside palette.");
                    }

                    if (color == 0) {
                        continue;
                    }

                    if (lastColumn[color] == 0) {
                        bandColors[bandColorCount++] = (byte)color;
                    }

                    // Define registers at their first source pixel; no palette discovery pass.
                    if (defined[color] == 0) {
                        var rgb = palette[color].ToRgb();
                        output.Append('#').Append(color).Append(";2;").Append((rgb.R * 100 + 127) / 255).Append(';').Append((rgb.G * 100 + 127) / 255).Append(';').Append((rgb.B * 100 + 127) / 255);
                        defined[color] = 1;
                        selectedColor = color;
                    }

                    _planes[(color - 1) * width + x] |= (byte)(1 << (y - top));
                    lastColumn[color] = Math.Max(lastColumn[color], x + 1);
                }
            }

            bool first = true;
            for (int colorIndex = 0; colorIndex < bandColorCount; colorIndex++) {
                int color = bandColors[colorIndex];
                if (first) {
                    int band = top / 6;
                    output.Append('-', band - emittedBand);
                    emittedBand = band;
                }
                else {
                    output.Append('$');
                }

                first = false;
                painted = true;
                if (selectedColor != color) {
                    output.Append('#').Append(color);
                    selectedColor = color;
                }

                int previous = -1, count = 0;
                var plane = _planes.AsSpan((color - 1) * width, lastColumn[color]);
                for (int x = 0; x < plane.Length; x++) {
                    int mask = plane[x];
                    if (mask == previous) {
                        count++;
                    }
                    else {
                        Repeat(output, previous, count);
                        previous = mask;
                        count = 1;
                    }
                }

                Repeat(output, previous, count);
            }
        }

        // An empty plot still submits a valid canvas; the presenter erases old ink.
        if (!painted) {
            output.Append('?');
        }

        return output.Append("\x1b\\").ToString();
    }

    private static void Repeat(StringBuilder output, int mask, int count) {
        if (count == 0) {
            return;
        }

        char value = (char)(63 + mask);
        int digits = count < 10 ? 1 : count < 100 ? 2 : count < 1000 ? 3 : count < 10000 ? 4 : count < 100000 ? 5 : count < 1000000 ? 6 : count < 10000000 ? 7 : count < 100000000 ? 8 : count < 1000000000 ? 9 : 10;
        if (count > digits + 2) {
            output.Append('!').Append(count).Append(value);
        }
        else {
            output.Append(value, count);
        }
    }
}
