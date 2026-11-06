using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>Reusable indexed chart raster, with palette-based antialiasing.</summary>
internal sealed partial class SixelPlot {
    private byte[] _pixels = [];
    private byte[] _planes = [];
    private int[] _firstColumns = [], _lastColumns = [];
    internal const int InkColors = 6;
    private double[] _observations = [];
    private readonly int[] _bins = new int[32];
    private Color _ink, _background;
    private bool _hasPalette;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public ReadOnlySpan<byte> Pixels =>
        _pixels.AsSpan(0, Width * Height);
    public Color[] Palette { get; } = new Color[7];
    internal int BandCount => (Height + 5) / 6;

    internal ReadOnlySpan<byte> Plane(int band,int color) =>
        _planes.AsSpan((band * InkColors + color - 1) * Width, Width);
    internal (int First, int Last) Columns(int band, int color) {
        int index = band * InkColors + color - 1;
        return (_firstColumns[index], _lastColumns[index]);
    }

    internal ReadOnlySpan<int> HistogramCounts =>
        _bins.AsSpan(0, BinCount);
    private int BinCount { get; set; }

    private void Initialize(int width, int height, Color ink, Color background, GraphKind kind) {
        if (width < 1 || height < 1 || (long)width * height > 16_777_216) {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Width = width;
        Height = height;
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (_pixels.Length < width * height) {
            _pixels = new byte[width * height];
        }
        else {
            _pixels.AsSpan(0, width * height).Clear();
        }

        int planeCount = BandCount * InkColors, planeSize = checked(planeCount * width);
        if (_planes.Length < planeSize) {
            _planes = new byte[planeSize];
        }
        else {
            _planes.AsSpan(0, planeSize).Clear();
        }

        if (_firstColumns.Length < planeCount) {
            _firstColumns = new int[planeCount];
            _lastColumns = new int[planeCount];
        }

        _firstColumns.AsSpan(0, planeCount).Fill(width);
        _lastColumns.AsSpan(0, planeCount).Clear();
        if (!_hasPalette || _ink != ink || _background != background) {
            Palette[0] = background;
            Palette[1] = Color.Mix(background, ink, .10f);
            Palette[2] = Color.Mix(background, ink, .24f);
            Palette[3] = Color.Mix(background, ink, .4f);
            Palette[4] = Color.Mix(background, ink, .6f);
            Palette[5] = Color.Mix(background, ink, .8f);
            Palette[6] = ink;
            _ink = ink;
            _background = background;
            _hasPalette = true;
        }

        BinCount = 0;
        for (int y = 0; y < height; y++) {
            if (!ChartTopology.Supports(kind) && !ChartRadial.IsRadial(kind) &&
            kind is not (GraphKind.Heatmap or GraphKind.Flamegraph or GraphKind.Gantt) &&
            (y == height - 1 || y == height / 2 || y == 0)) {
                for (int x = 0; x < width; x++) {
                    SetPixel(x, y, 1);
                }
            }
        }
    }

    private void Stroke(double x0, double y0, double x1, double y1) {
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2));
        for (int i = 0; i <= steps; i++) {
            double t = (double)i / steps;
            Dot(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
        }
    }

    private void Dot(double x, double y) {
        int column = (int)Math.Round(x), row = (int)Math.Floor(y);
        double fraction = y - row;
        Paint(column, row, (byte)(3 + Math.Round((1 - fraction) * 3)));
        if (fraction > 0) {
            Paint(column, row + 1, (byte)(3 + Math.Round(fraction * 3)));
        }
    }

    private void Paint(int x, int y, byte color) {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height) {
            SetPixel(x, y, Math.Max(_pixels[y * Width + x], color));
        }
    }

    private void FillRectangle(int left, int top, int right, int bottom, byte color) {
        if (right < left || bottom < top) {
            return;
        }

        for (int y = top; y <= bottom; y++) {
            _pixels.AsSpan(y * Width + left, right - left + 1).Fill(color);
        }

        // Replace all earlier shades only in the intersected rows of each band.
        for (int band = top / 6; band <= bottom / 6; band++) {
            int firstRow = Math.Max(top - band * 6, 0), lastRow = Math.Min(bottom - band * 6, 5);
            byte mask = (byte)(((1 << (lastRow - firstRow + 1)) - 1) << firstRow);
            for (int shade = 1; shade <= InkColors; shade++) {
                int plane = band * InkColors + shade - 1;
                var columns = _planes.AsSpan(plane * Width + left, right - left + 1);
                if (shade == color) {
                    for (int x = 0; x < columns.Length; x++) {
                        columns[x] |= mask;
                    }

                    _firstColumns[plane] = Math.Min(_firstColumns[plane], left);
                    _lastColumns[plane] = Math.Max(_lastColumns[plane], right + 1);
                }
                else {
                    for (int x = 0; x < columns.Length; x++) {
                        columns[x] &= (byte)~mask;
                    }
                }
            }
        }
    }

    private void FillColumn(int x, int top, int bottom, byte color) {
        if ((uint)x >= (uint)Width) {
            return;
        }

        top = Math.Max(top, 0);
        bottom = Math.Min(bottom, Height - 1);
        if (top > bottom) {
            return;
        }

        for (int band = top / 6; band <= bottom / 6; band++) {
            int firstRow = Math.Max(top - band * 6, 0), lastRow = Math.Min(bottom - band * 6, 5);
            byte mask = (byte)(((1 << (lastRow - firstRow + 1)) - 1) << firstRow);
            // Paint keeps the stronger shade, including existing antialiased strokes.
            for (int shade = color; shade <= InkColors && mask != 0; shade++) {
                mask &= (byte)~_planes[(band * InkColors + shade - 1) * Width + x];
            }

            if (mask == 0) {
                continue;
            }

            for (int shade = 1; shade < color; shade++) {
                _planes[(band * InkColors + shade - 1) * Width + x] &= (byte)~mask;
            }

            int plane = band * InkColors + color - 1;
            _planes[plane * Width + x] |= mask;
            _firstColumns[plane] = Math.Min(_firstColumns[plane], x);
            _lastColumns[plane] = Math.Max(_lastColumns[plane], x + 1);
            for (uint rows = mask; rows != 0; rows &= rows - 1) {
                int y = band * 6 + System.Numerics.BitOperations.TrailingZeroCount(rows);
                _pixels[y * Width + x] = color;
            }
        }
    }

    private void SetPixel(int x, int y, byte color) {
        int pixel = y * Width + x;
        byte previous = _pixels[pixel];
        if (previous == color) {
            return;
        }

        _pixels[pixel] = color;
        int band = y / 6 * InkColors, mask = 1 << (y % 6);
        // A stronger stroke replaces the earlier color at this pixel. Retaining
        // both masks would make the final shade depend on palette emission order.
        if (previous != 0) {
            _planes[(band + previous - 1) * Width + x] &= (byte)~mask;
        }

        int plane = band + color - 1;
        _planes[plane * Width + x] |= (byte)mask;
        _firstColumns[plane] = Math.Min(_firstColumns[plane], x);
        _lastColumns[plane] = Math.Max(_lastColumns[plane], x + 1);
    }
}
