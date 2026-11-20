using System.Text;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>DEC sixel encoding for an indexed raster. Index zero is transparent.</summary>
internal sealed class SixelEncoder {
    private readonly StringBuilder _output = new(4096);
    private byte[] _planes = [];
    private readonly Color[] _chartPalette = new Color[SixelPlot.MaximumInkColors + 1];
    private int _chartColorCount;
    private readonly StringBuilder _chartPaletteHeader = new(256);
    private readonly int[] _paletteStarts = new int[SixelPlot.MaximumInkColors + 1];
    private readonly int[] _paletteLengths = new int[SixelPlot.MaximumInkColors + 1];
    internal string EncodePlot(SixelPlot plot) {
        return BuildPlot(plot).ToString();
    }

    internal void EncodePlot(SixelPlot plot, SixelPayload destination) {
        destination.CopyFrom(BuildPlot(plot));
    }

    private StringBuilder BuildPlot(SixelPlot plot) {
        var output = _output;
        output.Clear();
        // Cache protocol definitions until the theme/ink or active palette size changes.
        if (_chartPaletteHeader.Length == 0 || _chartColorCount != plot.ColorCount || !plot.Palette.AsSpan().SequenceEqual(_chartPalette)) {
            _chartPaletteHeader.Clear();
            for (int color = 1; color <= plot.ColorCount; color++) {
                _paletteStarts[color] = _chartPaletteHeader.Length;
                _chartPaletteHeader.AddSixelColor(color, plot.Palette[color]);
                _paletteLengths[color] = _chartPaletteHeader.Length - _paletteStarts[color];
            }

            plot.Palette.AsSpan().CopyTo(_chartPalette);
            _chartColorCount = plot.ColorCount;
        }

        output.AddSixelCanvas(plot.Width, plot.Height);
        Span<byte> defined = stackalloc byte[SixelPlot.MaximumInkColors + 1];
        defined.Clear();
        int emittedBand = 0, selectedColor = -1;
        bool painted = false;
        for (int band = 0; band < plot.BandCount; band++) {
            bool first = true;
            int column = 0;
            for (int color = 1; color <= plot.ColorCount; color++) {
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
                else if (left < column) {
                    output.Append('$');
                    column = 0;
                }

                first = false;
                painted = true;
                if (defined[color] == 0) {
                    output.Append(_chartPaletteHeader, _paletteStarts[color], _paletteLengths[color]);
                    defined[color] = 1;
                    selectedColor = color;
                }
                else if (selectedColor != color) {
                    output.SelectSixelColor(color);
                    selectedColor = color;
                }

                // Disjoint planes can continue from the preceding plane's cursor.
                output.AddSixelRepeat(0, left - column);
                int previous = -1, count = 0;
                foreach (byte mask in plane[left..right]) {
                    if (mask == previous) {
                        count++;
                    }
                    else {
                        output.AddSixelRepeat(previous, count);
                        previous = mask;
                        count = 1;
                    }
                }

                output.AddSixelRepeat(previous, count);
                column = right;
            }
        }

        if (!painted) {
            output.Append('?');
        }

        output.EndSixel();
        return output;
    }

    public static string Encode(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        ReadOnlySpan<Color> palette
    ) =>
        new SixelEncoder().EncodeFrame(pixels, width, height, palette);
    public string EncodeFrame(ReadOnlySpan<byte> pixels, int width, int height, ReadOnlySpan<Color> palette) {
        return BuildFrame(pixels, width, height, palette).ToString();
    }

    internal void EncodeFrame(ReadOnlySpan<byte> pixels, int width, int height, ReadOnlySpan<Color> palette, SixelPayload destination) {
        destination.CopyFrom(BuildFrame(pixels, width, height, palette));
    }

    private StringBuilder BuildFrame(ReadOnlySpan<byte> pixels, int width, int height, ReadOnlySpan<Color> palette) {
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
        output.AddSixelCanvas(width, height);

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
                        output.AddSixelColor(color, palette[color]);
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
                    output.SelectSixelColor(color);
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
                        output.AddSixelRepeat(previous, count);
                        previous = mask;
                        count = 1;
                    }
                }

                output.AddSixelRepeat(previous, count);
            }
        }

        // An empty plot still submits a valid canvas; the presenter erases old ink.
        if (!painted) {
            output.Append('?');
        }

        output.EndSixel();
        return output;
    }
}
