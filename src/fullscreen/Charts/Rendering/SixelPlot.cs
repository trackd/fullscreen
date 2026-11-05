using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>Shape used by the pixel chart backend.</summary>
public enum GraphKind {
    /// <summary>Connects adjacent numeric points with line segments, leaving gaps for missing values.</summary>
    Line,
    /// <summary>Fills the area between a series and its baseline.</summary>
    Area,
    /// <summary>Draws vertical bars for individual values.</summary>
    Bars,
    /// <summary>Connects values with horizontal and vertical step segments.</summary>
    Step,
    /// <summary>Draws independent markers at explicit X and Y coordinates.</summary>
    Scatter,
    /// <summary>Draws a stem from the baseline to each data point.</summary>
    Stem,
    /// <summary>Groups values into bins and displays their frequencies.</summary>
    Histogram,
    /// <summary>Summarizes a distribution using quartiles and whiskers.</summary>
    BoxPlot,
    /// <summary>Draws horizontal bars for individual values.</summary>
    HorizontalBars,
    /// <summary>Stacks series values within each bar category.</summary>
    StackedBars,
    /// <summary>Maps numeric values to colored cells arranged by series and position.</summary>
    Heatmap,
    /// <summary>Draws values with associated lower and upper uncertainty bounds.</summary>
    ErrorBars
}

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
    public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, Width * Height);
    public Color[] Palette { get; } = new Color[7];
    internal int BandCount => (Height + 5) / 6;

    internal ReadOnlySpan<byte> Plane(
        int band,
        int color
    ) =>
        _planes.AsSpan((band * InkColors + color - 1) * Width, Width);
    internal (int First, int Last) Columns(int band, int color) {
        int index = band * InkColors + color - 1;
        return (_firstColumns[index], _lastColumns[index]);
    }

    internal ReadOnlySpan<int> HistogramCounts => _bins.AsSpan(0, BinCount);
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
            if (y == height - 1 || y == height / 2 || y == 0) {
                for (int x = 0; x < width; x++) {
                    SetPixel(x, y, 1);
                }
            }
        }
    }

    // Linear-interpolated quantiles (R type 7); Tukey whiskers stop at observed
    // values within 1.5 IQR. The span is plot-owned scratch storage, sorted in place.
    internal static BoxSummary Summarize(Span<double> values) {
        if (values.IsEmpty) {
            throw new ArgumentException("Expected observations.", nameof(values));
        }

        values.Sort();
        double q1 = Quantile(values, .25), median = Quantile(values, .5), q3 = Quantile(values, .75), iqr = q3 - q1;
        int lower = 0, upper = values.Length - 1;
        while (lower < upper && values[lower] < q1 - 1.5 * iqr) {
            lower++;
        }

        while (upper > lower && values[upper] > q3 + 1.5 * iqr) {
            upper--;
        }

        return new(q1, median, q3, values[lower], values[upper]);
    }

    private static double Quantile(ReadOnlySpan<double> sorted, double fraction) {
        double position = (sorted.Length - 1) * fraction;
        int index = (int)position;
        double weight = position - index;
        return sorted[index] * (1 - weight) + sorted[Math.Min(index + 1, sorted.Length - 1)] * weight;
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

internal readonly record struct BoxSummary(
    double FirstQuartile,
    double Median,
    double ThirdQuartile,
    double LowerWhisker,
    double UpperWhisker
);
