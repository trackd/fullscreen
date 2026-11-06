using System.Collections.Concurrent;

namespace fullscreen.Charts;
/// <summary>
/// Fixed DRCS alphabets. Every glyph is a pure function of a small shape key (edge
/// heights, bar height, marker position), so a font depends only on which graph kinds
/// it serves. Frames pick characters from data; they never generate glyphs.
/// </summary>
internal sealed class DrcsFont {
    private const int W = DrcsEncoder.Width, H = DrcsEncoder.Height;
    private const char Blank = ' ';
    private static readonly ConcurrentDictionary<int, DrcsFont> Fonts = new();
    // BoxPlot uses ordinary box-drawing text, so it needs no glyphs.
    internal static int Mask(GraphKind kind) =>
        kind >= GraphKind.BoxPlot ? 0 : 1 << (int)kind;
    internal static readonly int AllKinds = Enum.GetValues<GraphKind>().Aggregate(0, (mask, kind) => mask | Mask(kind));
    private readonly Dictionary<DrcsEncoder.Tile, char> _slots = [];
    private readonly List<DrcsEncoder.Tile> _glyphs = [];
    private readonly char[]? _fill, _line, _bar, _dot, _stemTop;
    private readonly char _stem = Blank;
    private string? _download;
    /// <summary>Graph kinds this font can draw.</summary>
    internal int Kinds { get; }
    /// <summary>Vertical steps per cell; 16 is one step per glyph pixel.</summary>
    internal int Levels { get; }
    internal int Count => _glyphs.Count;
    internal ReadOnlySpan<DrcsEncoder.Tile> Glyphs => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_glyphs);
    internal string Download => _download ??= DrcsEncoder.Download(Glyphs);

    /// <summary>The finest font for these kinds that fits in one 96-glyph set. Cached; fonts are immutable.</summary>
    internal static DrcsFont For(int kinds) =>
        Fonts.GetOrAdd(kinds, static kinds => {
            foreach (int levels in (ReadOnlySpan<int>)[16, 12, 8, 6, 5, 4, 3, 2]) {
                var font = new DrcsFont(kinds, levels);
                if (font.Count <= DrcsEncoder.Capacity) {
                    return font;
                }
            }

            throw new InvalidOperationException("DRCS glyph families exceed one font.");
        });
    private DrcsFont(int kinds, int levels) {
        Levels = levels;
        int n = levels, side = n + 1;
        Add(default);
        bool Has(GraphKind kind) =>
            (kinds & Mask(kind)) != 0;
        // Glyph families are shared, so record every kind they can draw, not only the ones requested.
        if (Has(GraphKind.Area)) {
            kinds |= Mask(GraphKind.Histogram);
        }

        if (Has(GraphKind.Line) || Has(GraphKind.Step)) {
            kinds |= Mask(GraphKind.Line) | Mask(GraphKind.Step);
        }

        if (Has(GraphKind.Stem)) {
            kinds |= Mask(GraphKind.Scatter);
        }

        Kinds = kinds;
        double P(int level) =>
            level * (double)H / n;
        if (Has(GraphKind.Area) || Has(GraphKind.Histogram)) {
            _fill = new char[side * side];
            Array.Fill(_fill, Blank);
            // Histogram bins are flat, so they only need the a == b subset.
            for (int a = 0; a <= n; a++) {
                for (int b = 0; b <= n; b++) {
                    if (Has(GraphKind.Area) || a == b) {
                        double left = P(a), right = P(b);
                        _fill[a * side + b] = Add(Draw((x, h) => h < left + (right - left) * (x + .5) / W));
                    }
                }
            }
        }

        if (Has(GraphKind.Line) || Has(GraphKind.Step)) {
            _line = new char[side * side];
            for (int a = 0; a <= n; a++) {
                for (int b = 0; b <= n; b++) {
                    double left = P(a), right = P(b);
                    _line[a * side + b] = Add(Draw((x, h) => {
                        double start = left + (right - left) * x / W, end = left + (right - left) * (x + 1) / W;
                        return h >= Math.Min(start, end) - 1 && h <= Math.Max(start, end) + 1;
                    }));
                }
            }
        }

        if (Has(GraphKind.Bars)) {
            _bar = new char[side];
            for (int level = 0; level <= n; level++) {
                double top = P(level);
                _bar[level] = Add(Draw((x, h) => x is >= 1 and <= 8 && h < top));
            }
        }

        if (Has(GraphKind.Scatter) || Has(GraphKind.Stem)) {
            // Markers spill into neighboring rows, so centers range one row below and above.
            _dot = new char[3 * n + 1];
            for (int k = -n; k <= 2 * n; k++) {
                double center = P(k);
                _dot[k + n] = Add(Draw((x, h) => Marker(x, h, center)));
            }
        }

        if (Has(GraphKind.Stem)) {
            _stemTop = new char[side];
            for (int k = 0; k <= n; k++) {
                double center = P(k);
                _stemTop[k] = Add(Draw((x, h) => Marker(x, h, center) || x is 4 or 5 && h < center));
            }

            _stem = Add(Draw((x, _) => x is 4 or 5));
        }
    }

    private static bool Marker(
        int x,
        double h,
        double center
    ) =>
        (x + .5 - W / 2d) * (x + .5 - W / 2d) + (h - center) * (h - center) <= 6.25;
    // h is the pixel center's height above the cell bottom.
    private static DrcsEncoder.Tile Draw(Func<int, double, bool> on) {
        ulong low = 0, middle = 0;
        uint high = 0;
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                if (!on(x, H - y - .5)) {
                    continue;
                }

                int bit = y * W + x;
                if (bit < 64) {
                    low |= 1UL << bit;
                }
                else if (bit < 128) {
                    middle |= 1UL << (bit - 64);
                }
                else {
                    high |= 1U << (bit - 128);
                }
            }
        }

        return new(low, middle, high);
    }

    private char Add(DrcsEncoder.Tile tile) {
        if (_slots.TryGetValue(tile, out char glyph)) {
            return glyph;
        }

        glyph = (char)(0x20 + _glyphs.Count);
        _glyphs.Add(tile);
        _slots.Add(tile, glyph);
        return glyph;
    }

    /// <summary>Fills <paramref name="cells"/> (row-major, columns × rows) with characters for one chart.</summary>
    /// <returns>True when the cells are ordinary text (BoxPlot) instead of soft-font glyphs.</returns>
    internal static bool Layout(
        DrcsFont? font,
        in ChartPixelKey key,
        int columns,
        int rows,
        Span<char> cells,
        ref double[] scratch
    ) {
        cells.Fill(Blank);
        if (columns < 1 || rows < 1) {
            return key.Kind == GraphKind.BoxPlot;
        }

        var data = key.Source.GetData(columns);
        var range = key.Source.Range(data);
        foreach (var series in data) {
            LayoutSeries(font, new(series, range), key.Kind, columns, rows, cells, ref scratch);
        }

        return key.Kind == GraphKind.BoxPlot;
    }

    private static bool LayoutSeries(
        DrcsFont? font,
        NumericChartSamples source,
        GraphKind kind,
        int columns,
        int rows,
        Span<char> cells,
        ref double[] scratch
    ) {
        double minimum = source.Range.MinY, maximum = source.Range.MaxY, top = rows * H;
        if (scratch.Length < Math.Max(columns + 1, source.Count)) {
            scratch = new double[Math.Max(columns + 1, source.Count)];
        }

        if (kind == GraphKind.BoxPlot) {
            DrawBox(source, columns, rows, cells, scratch);
            return true;
        }

        if (font is null || (font.Kinds & Mask(kind)) == 0) {
            throw new ArgumentException("The font does not cover this graph kind.", nameof(font));
        }

        int n = font.Levels, side = n + 1;
        double Height(double value) =>
            ChartMath.Fraction(value, minimum, maximum) * top;
        int Q(double local) =>
            (int)Math.Round(Math.Clamp(local, 0, H) * n / H);
        switch (kind) {
            case GraphKind.Area or GraphKind.Line or GraphKind.Step: {
                    var edges = scratch.AsSpan(0, columns + 1);
                    if (kind == GraphKind.Step) {
                        NumericChartSampling.Sample(source, edges, step: true);
                    }
                    else {
                        NumericChartSampling.Sample(source, edges);
                    }

                    for (int column = 0; column < columns; column++) {
                        double a = edges[column], b = edges[column + 1];
                        if (!double.IsFinite(a)) {
                            a = b;
                        }

                        if (!double.IsFinite(b)) {
                            b = a;
                        }

                        if (!double.IsFinite(a)) {
                            continue;
                        }

                        a = Height(a);
                        b = Height(b);
                        for (int row = 0; row < rows; row++) {
                            double bottom = (rows - 1 - row) * H, left = a - bottom, right = b - bottom;
                            if (kind == GraphKind.Area) {
                                cells[row * columns + column] = font._fill![Q(left) * side + Q(right)];
                            }
                            else if (Math.Max(left, right) >= 0 && Math.Min(left, right) <= H) {
                                cells[row * columns + column] = font._line![Q(left) * side + Q(right)];
                            }
                        }
                    }

                    break;
                }

            case GraphKind.Bars: {
                    var values = scratch.AsSpan(0, columns);
                    NumericChartSampling.Sample(source, values);
                    for (int column = 0; column < columns; column++) {
                        if (double.IsFinite(values[column])) {
                            double height = Height(values[column]);
                            for (int row = 0; row < rows; row++) {
                                cells[row * columns + column] = font._bar![Q(height - (rows - 1 - row) * H)];
                            }
                        }
                    }

                    break;
                }

            case GraphKind.Scatter or GraphKind.Stem: {
                    var values = scratch.AsSpan(0, columns);
                    values.Fill(double.NaN);
                    for (int i = 0; i < source.Count; i++) {
                        double position = source.PositionAt(i);
                        if (position < 0 || position > 1 || source.ValueAt(i) is not double value) {
                            continue;
                        }

                        int column = (int)Math.Round(position * (columns * W - 1)) / W;
                        values[column] = double.IsNaN(values[column]) ? value : Math.Max(values[column], value);
                    }

                    for (int column = 0; column < columns; column++) {
                        if (double.IsFinite(values[column])) {
                            double height = Height(values[column]);
                            for (int row = 0; row < rows; row++) {
                                double local = height - (rows - 1 - row) * H;
                                int k = (int)Math.Round(local * n / H);
                                char glyph = kind == GraphKind.Stem && local >= H ? font._stem : kind == GraphKind.Stem && local >= 0 ? font._stemTop![Math.Clamp(k, 0, n)] : k >= -n && k <= 2 * n ? font._dot![k + n] : Blank;
                                cells[row * columns + column] = glyph;
                            }
                        }
                    }

                    break;
                }

            case GraphKind.Histogram: {
                    Span<int> bins = stackalloc int[32];
                    int binCount = Math.Clamp(columns * W / 16, 1, bins.Length), peak = 0;
                    for (int i = 0; i < source.Count; i++) {
                        if (source.ValueAt(i) is double value && value >= minimum && value <= maximum) {
                            peak = Math.Max(
                                peak,
                                ++bins[Math.Min(binCount - 1, (int)(ChartMath.Fraction(value, minimum, maximum) * binCount))]
                            );
                        }
                    }

                    if (peak == 0) {
                        break;
                    }

                    for (int column = 0; column < columns; column++) {
                        int count = bins[Math.Min(binCount - 1, (column * W + W / 2) * binCount / (columns * W))];
                        if (count == 0) {
                            continue;
                        }

                        double height = (double)count / peak * top;
                        for (int row = 0; row < rows; row++) {
                            int level = Q(height - (rows - 1 - row) * H);
                            cells[row * columns + column] = font._fill![level * side + level];
                        }
                    }

                    break;
                }
        }

        return false;
    }

    private const string BoxChars = " ╵╷│╴┘┐┤╶└┌├─┴┬┼";
    private static void DrawBox(NumericChartSamples source, int columns, int rows, Span<char> cells, double[] scratch) {
        int count = 0;
        for (int i = 0; i < source.Count; i++) {
            if (source.ValueAt(i) is double value) {
                scratch[count++] = value;
            }
        }

        if (count == 0) {
            return;
        }

        var box = ChartStatistics.Summarize(scratch.AsSpan(0, count));
        int X(double value) =>
            (int)Math.Round(ChartMath.Fraction(value, source.Range.MinX, source.Range.MaxX) * (columns - 1));
        int center = rows / 2, upper = rows / 4, lower = Math.Max(upper, rows * 3 / 4);
        int left = X(box.FirstQuartile), right = X(box.ThirdQuartile), low = X(box.LowerWhisker), high = X(box.UpperWhisker);
        var masks = new byte[columns * rows];
        void Horizontal(int row, int from, int to) {
            for (int x = from; x <= to; x++) {
                masks[row * columns + x] |= (byte)(from == to ? 12 : (x > from ? 4 : 0) | (x < to ? 8 : 0));
            }
        }

        void Vertical(int column, int from, int to) {
            for (int y = from; y <= to; y++) {
                masks[y * columns + column] |= (byte)(from == to ? 3 : (y > from ? 1 : 0) | (y < to ? 2 : 0));
            }
        }

        Horizontal(upper, left, right);
        Horizontal(lower, left, right);
        Vertical(left, upper, lower);
        Vertical(right, upper, lower);
        Vertical(X(box.Median), upper, lower);
        if (low < left) {
            Horizontal(center, low, left);
        }

        if (high > right) {
            Horizontal(center, right, high);
        }

        Vertical(low, upper, lower);
        Vertical(high, upper, lower);
        for (int i = 0; i < masks.Length; i++) {
            cells[i] = BoxChars[masks[i]];
        }

        foreach (double value in scratch.AsSpan(0, count)) {
            if (value < box.LowerWhisker || value > box.UpperWhisker) {
                cells[center * columns + X(value)] = '•';
            }
        }
    }
}
