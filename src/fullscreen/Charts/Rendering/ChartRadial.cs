namespace fullscreen.Charts;

internal readonly record struct RadialVertex(double X, double Y, int Series, int Point, int Color, bool Overflow);
internal readonly record struct RadialSegment(RadialVertex From, RadialVertex To, bool Grid = false);
internal readonly record struct RadialHit(int Series, int Point);

/// <summary>Reusable polar geometry in normalized coordinates; buffers belong to each renderer.</summary>
internal sealed class ChartRadial {
    private double[] _ends = [];
    private RadialHit[] _slices = [];
    private double[] _loads = [];
    private RadialVertex[] _vertices = [];
    private RadialSegment[] _segments = [];
    private int _sliceCount, _vertexCount, _segmentCount, _laneCount;
    private GraphKind _kind;
    internal ReadOnlySpan<RadialVertex> Vertices => _vertices.AsSpan(0, _vertexCount);
    internal ReadOnlySpan<RadialSegment> Segments => _segments.AsSpan(0, _segmentCount);
    internal bool Empty { get; private set; }
    internal static bool IsRadial(GraphKind kind) =>
        kind is GraphKind.Pie or GraphKind.Gauge or GraphKind.RadialBars or GraphKind.Radar;
    internal static bool IsRows(GraphKind kind) => IsRadial(kind) && kind != GraphKind.Radar;

    internal static void ValidateOptions(GraphKind kind, double? minimum, double? maximum, ChartScale xScale, ChartScale yScale, int thresholds, int annotations) {
        if (!IsRadial(kind)) {
            return;
        }
        if (xScale == ChartScale.Logarithmic || yScale != ChartScale.Linear) {
            throw new ArgumentException("Radial charts require a linear numeric Y scale and non-logarithmic category X coordinates.");
        }
        if (kind == GraphKind.Pie && (minimum is not null || maximum is not null)) {
            throw new ArgumentException("Pie proportions use the positive total, not Minimum or Maximum.");
        }
        if (thresholds > 0 || annotations > 0) {
            throw new ArgumentException("Cartesian thresholds and annotations are not supported on radial charts.");
        }
        if (kind is GraphKind.Gauge or GraphKind.RadialBars) {
            double low = minimum ?? 0, high = maximum ?? 100;
            if (!double.IsFinite(low) || !double.IsFinite(high) || low < 0 || low >= high) {
                throw new ArgumentException("Gauge and radial bar bounds must be finite and nonnegative with Minimum less than Maximum.");
            }
        }
    }

    internal static void Validate(ChartDataSeries[] data, GraphKind kind) {
        if (IsRows(kind) && data.Length > 1) {
            throw new ArgumentException($"{kind} requires one Y series; input rows supply slices or lanes.");
        }
        int categories = 0;
        foreach (var series in data) {
            categories = Math.Max(categories, series.Count);
            foreach (var point in series.Span) {
                if (IsRows(kind) && point.Y < 0) {
                    throw new ArgumentException($"{kind} cannot represent negative values.");
                }
            }
        }
        if (kind == GraphKind.Radar && categories is > 0 and < 3) {
            throw new ArgumentException("Radar requires at least three category rows.");
        }
        if (kind == GraphKind.Radar) {
            for (int series = 1; series < data.Length; series++) {
                int count = Math.Min(data[0].Count, data[series].Count);
                for (int point = 0; point < count; point++) {
                    if (data[0].Points[point].X != data[series].Points[point].X) {
                        throw new ArgumentException("Radar series must use the same ordered category coordinates.");
                    }
                }
            }
        }
    }

    internal void Build(ChartDataSeries[] data, ChartRange range, GraphKind kind) {
        Validate(data, kind);
        _kind = kind;
        _sliceCount = _vertexCount = _segmentCount = _laneCount = 0;
        Empty = true;
        if (data.Length == 0) {
            return;
        }
        if (kind == GraphKind.Radar) {
            BuildRadar(data, range);
            return;
        }
        var points = data[0].Span;
        Ensure(ref _ends, points.Length);
        Ensure(ref _slices, points.Length);
        Ensure(ref _loads, points.Length);
        double largest = 0, total = 0;
        foreach (var point in points) {
            largest = Math.Max(largest, point.Y ?? 0);
        }
        if (largest > 0) {
            foreach (var point in points) {
                total += (point.Y ?? 0) / largest;
            }
        }
        double cumulative = 0;
        for (int index = 0; index < points.Length; index++) {
            var point = points[index];
            if (kind == GraphKind.Pie) {
                if (point.Y is not > 0) {
                    continue;
                }
                cumulative += point.Y.Value / largest / total;
                _ends[_sliceCount] = cumulative;
                _slices[_sliceCount++] = new(0, index);
                Empty = false;
            }
            else {
                _loads[index] = point.Y is { } value ? ChartMath.Fraction(value, range.MinY, range.MaxY) : double.NaN;
                if (point.Y is { } number && (number > range.MaxY || number < range.MinY)) {
                    _loads[index] = number > range.MaxY ? 1.01 : -.01;
                }
                Empty &= point.Y is null;
            }
        }
        if (_sliceCount > 0) {
            _ends[_sliceCount - 1] = 1;
        }
        _laneCount = points.Length;
    }

    private static void Ensure<T>(ref T[] buffer, int count) {
        if (buffer.Length < count) {
            buffer = new T[count];
        }
    }

    private void BuildRadar(ChartDataSeries[] data, ChartRange range) {
        int count = 0;
        foreach (var series in data) {
            count = Math.Max(count, series.Count);
        }
        Ensure(ref _vertices, data.Length * count);
        Ensure(ref _segments, count * (data.Length + 5));
        for (int spoke = 0; spoke < count; spoke++) {
            var edge = Vertex(spoke, count, 1, -1, -1, 1, false);
            _segments[_segmentCount++] = new(new(0, 0, -1, -1, 1, false), edge, true);
            for (int ring = 1; ring <= 4; ring++) {
                _segments[_segmentCount++] = new(
                    Vertex(spoke, count, ring / 4d, -1, -1, 1, false),
                    Vertex((spoke + 1) % count, count, ring / 4d, -1, -1, 1, false), true);
            }
        }
        for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
            var series = data[seriesIndex];
            int start = _vertexCount;
            for (int point = 0; point < count; point++) {
                double? value = point < series.Count ? series.Points[point].Y : null;
                _vertices[_vertexCount++] = Vertex(point, count,
                    value is { } number ? ChartMath.Fraction(number, range.MinY, range.MaxY) : double.NaN,
                    seriesIndex, point, 2 + (series.SourceIndex < 0 ? seriesIndex : series.SourceIndex) % Themes.ThemePalette.SeriesColorCount,
                    value < range.MinY || value > range.MaxY);
                Empty &= value is null;
            }
            for (int point = 0; point < count; point++) {
                var from = _vertices[start + point];
                var to = _vertices[start + (point + 1) % count];
                if (double.IsFinite(from.X) && double.IsFinite(to.X)) {
                    _segments[_segmentCount++] = new(from, to);
                }
            }
        }
    }

    private static RadialVertex Vertex(int point, int count, double radius, int series, int index, int color, bool overflow) {
        double angle = Math.Tau * point / count - Math.PI / 2;
        return new(Math.Cos(angle) * radius, Math.Sin(angle) * radius, series, index, color, overflow);
    }

    internal static (double CenterX, double CenterY, double RadiusX, double RadiusY) Layout(
        GraphKind kind, int width, int height, bool cells
    ) {
        double cx = (width - 1) / 2d, cy = kind == GraphKind.Gauge ? height - 1d : (height - 1) / 2d;
        double radius = Math.Max(.5, Math.Min(cx, (kind == GraphKind.Gauge ? height - 1d : cy) * (cells ? 2 : 1)));
        return (cx, cy, radius, radius / (cells ? 2 : 1));
    }

    internal static (double X, double Y) Position(RadialVertex vertex, GraphKind kind, int width, int height, bool cells) {
        var (CenterX, CenterY, RadiusX, RadiusY) = Layout(kind, width, height, cells);
        return (CenterX + vertex.X * RadiusX, CenterY + vertex.Y * RadiusY);
    }

    internal int Sample(double x, double y, out RadialHit hit, out bool overflow) {
        hit = new(-1, -1);
        overflow = false;
        double radius = Math.Sqrt(x * x + y * y);
        if (radius > 1 || _kind == GraphKind.Gauge && y > 0 || Empty) {
            return 0;
        }
        double angle = Math.Atan2(y, x);
        if (_kind == GraphKind.Pie) {
            double fraction = (angle + Math.PI / 2 + Math.Tau) % Math.Tau / Math.Tau;
            int low = 0, high = _sliceCount - 1;
            while (low < high) {
                int middle = (low + high) / 2;
                if (fraction < _ends[middle]) {
                    high = middle;
                }
                else {
                    low = middle + 1;
                }
            }
            hit = _slices[low];
            return 2 + hit.Point % Themes.ThemePalette.SeriesColorCount;
        }
        if (_kind is GraphKind.Gauge or GraphKind.RadialBars) {
            int lane = (int)((1 - radius) * _laneCount / .8);
            if ((uint)lane >= (uint)_laneCount) {
                return 0;
            }
            double outer = 1 - .8 * lane / _laneCount, inner = outer - .64 / _laneCount;
            if (radius < inner || radius > outer) {
                return 0;
            }
            double fraction = _kind == GraphKind.Gauge ? (angle <= 0 ? angle + Math.PI : 0) / Math.PI
                : (angle + Math.PI / 2 + Math.Tau) % Math.Tau / Math.Tau;
            double load = _loads[lane];
            if (double.IsNaN(load)) {
                return 1;
            }
            hit = new(0, lane);
            overflow = (load > 1 || load < 0) && (_kind == GraphKind.Gauge ? load > 1 && fraction > .96 || load < 0 && fraction < .04
                : fraction > .94 || fraction < .02);
            if (overflow) {
                return _kind == GraphKind.Gauge ? Themes.ThemePalette.GaugeColorCount + 1 : 6;
            }
            return fraction <= Math.Clamp(load, 0, 1) ? _kind == GraphKind.Gauge
                ? 2 + Math.Min(Themes.ThemePalette.GaugeColorCount - 1, (int)(fraction * Themes.ThemePalette.GaugeColorCount))
                : 2 + lane % Themes.ThemePalette.SeriesColorCount : 1;
        }
        return 0;
    }

    internal RadialHit Pick(int x, int y, int width, int height, bool cells) {
        var (CenterX, CenterY, RadiusX, RadiusY) = Layout(_kind, width, height, cells);
        if (_kind != GraphKind.Radar) {
            Sample((x - CenterX) / RadiusX, (y - CenterY) / RadiusY, out var hit, out _);
            return hit;
        }
        double best = 4;
        RadialHit selected = new(-1, -1);
        foreach (var vertex in Vertices) {
            if (!double.IsFinite(vertex.X)) {
                continue;
            }
            var (X, Y) = Position(vertex, _kind, width, height, cells);
            double distance = Math.Pow(X - x, 2) + Math.Pow(Y - y, 2);
            if (distance <= best) {
                best = distance;
                selected = new(vertex.Series, vertex.Point);
            }
        }
        foreach (var segment in Segments) {
            if (segment.Grid) {
                continue;
            }
            var (X, Y) = Position(segment.From, _kind, width, height, cells);
            var to = Position(segment.To, _kind, width, height, cells);
            double dx = to.X - X, dy = to.Y - Y, length = dx * dx + dy * dy;
            double t = length == 0 ? 0 : Math.Clamp(((x - X) * dx + (y - Y) * dy) / length, 0, 1);
            double distance = Math.Pow(X + t * dx - x, 2) + Math.Pow(Y + t * dy - y, 2);
            if (distance <= best) {
                best = distance;
                var vertex = t < .5 ? segment.From : segment.To;
                selected = new(vertex.Series, vertex.Point);
            }
        }
        return selected;
    }

    internal RadialVertex Anchor(int point) {
        if (_kind == GraphKind.Pie) {
            double start = 0;
            for (int slice = 0; slice < _sliceCount; slice++) {
                double end = _ends[slice];
                if (_slices[slice].Point == point) {
                    double angle = (start + end) / 2 * Math.Tau - Math.PI / 2;
                    return new(Math.Cos(angle) * .65, Math.Sin(angle) * .65, 0, point, 1, false);
                }
                start = end;
            }
        }
        else if ((uint)point < (uint)_laneCount && double.IsFinite(_loads[point])) {
            double radius = 1 - .8 * point / _laneCount - .32 / _laneCount;
            double fraction = Math.Clamp(_loads[point], 0, 1) / 2;
            double angle = _kind == GraphKind.Gauge ? fraction * Math.PI - Math.PI : fraction * Math.Tau - Math.PI / 2;
            return new(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0, point, 1, false);
        }
        return new(double.NaN, double.NaN, -1, -1, 1, false);
    }
}
