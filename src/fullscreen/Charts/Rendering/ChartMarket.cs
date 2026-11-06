namespace fullscreen.Charts;

/// <summary>Variable-width composition and reversal-driven price geometry with original point mappings.</summary>
internal static class ChartMarket {
    internal static bool Supports(GraphKind kind) => kind is GraphKind.Marimekko or GraphKind.Kagi;

    internal static void ValidateOptions(GraphKind kind, double reversal, double? minimum, double? maximum,
        double? minimumX, double? maximumX, ChartScale xScale, ChartScale yScale, int thresholds, int annotations) {
        if (!double.IsFinite(reversal) || reversal <= 0) {
            throw new ArgumentException("ReversalAmount must be finite and greater than zero.");
        }
        if (!Supports(kind)) {
            return;
        }
        if (minimumX is not null || maximumX is not null || xScale == ChartScale.Logarithmic || yScale != ChartScale.Linear
            || thresholds != 0 || annotations != 0) {
            throw new ArgumentException("Marimekko and Kagi use intrinsic category/event spacing, a linear Y axis and no coordinate overlays or X viewport limits.");
        }
        if (kind == GraphKind.Marimekko && (minimum is not null || maximum is not null)) {
            throw new ArgumentException("Marimekko composition uses a fixed 0 to 100 percent Y range.");
        }
    }

    internal static void Validate(ChartDataSeries[] data, GraphKind kind) {
        if (kind == GraphKind.Kagi && data.Length > 1) {
            throw new ArgumentException("Kagi requires one ordered closing-price Y series.");
        }
        ChartDataSeries? reference = null;
        foreach (var series in data) {
            if (reference is null || series.Count > reference.Count) {
                reference = series;
            }
            for (int index = 0; index < series.Count; index++) {
                var point = series.Points[index];
                if (!double.IsFinite(point.X) || index > 0 && point.X <= series.Points[index - 1].X) {
                    throw new ArgumentException("Marimekko and Kagi require finite, strictly increasing category/time coordinates; input order can be used for repeated labels.");
                }
                if (point.Y is { } value && (kind == GraphKind.Kagi ? value <= 0 : value < 0)) {
                    throw new ArgumentException(kind == GraphKind.Kagi
                        ? "Kagi closing prices must be positive; missing prices break the path."
                        : "Marimekko segment values must be nonnegative; missing segments contribute no observed total.");
                }
            }
        }
        if (kind != GraphKind.Marimekko || reference is null) {
            return;
        }
        foreach (var series in data) {
            for (int index = 0; index < series.Count; index++) {
                if (series.Points[index].X != reference.Points[index].X) {
                    throw new ArgumentException("Marimekko series must have aligned category coordinates.");
                }
            }
        }
    }

    internal static void BuildMekko(List<ChartPrimitive> shapes, ChartDataSeries[] data, int width, int height) {
        double scale = 0;
        int count = 0;
        foreach (var series in data) {
            count = Math.Max(count, series.Count);
            foreach (var point in series.Span) {
                scale = Math.Max(scale, point.Y ?? 0);
            }
        }
        if (scale == 0) {
            return;
        }
        double total = 0;
        foreach (var series in data) {
            foreach (var point in series.Span) {
                total += (point.Y ?? 0) / scale;
            }
        }
        double cumulativeWidth = 0;
        for (int index = 0; index < count; index++) {
            double category = 0;
            foreach (var series in data) {
                if (index < series.Count) {
                    category += (series.Points[index].Y ?? 0) / scale;
                }
            }
            if (category == 0) {
                continue;
            }
            int left = Math.Clamp((int)Math.Round(cumulativeWidth / total * width), 0, width);
            cumulativeWidth += category;
            int right = Math.Clamp((int)Math.Round(cumulativeWidth / total * width), 0, width);
            double stack = 0;
            for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++) {
                var series = data[seriesIndex];
                if (index >= series.Count || series.Points[index].Y is not { } value || value == 0) {
                    continue;
                }
                int bottom = height - (int)Math.Round(stack / category * height);
                stack += value / scale;
                int top = height - (int)Math.Round(stack / category * height);
                if (right > left && bottom > top) {
                    shapes.Add(new(ChartPrimitiveKind.Rectangle, left, top, right - 1, bottom - 1, seriesIndex, Point: index));
                }
            }
        }
    }

    internal readonly record struct PriceSegment(int Column, double From, double To, int Point, bool Yang, bool Connector = false);

    internal static int BuildKagi(List<PriceSegment> segments, ChartDataSeries[] data, double reversal) {
        segments.Clear();
        if (data.Length == 0) {
            return 0;
        }
        int column = 0, direction = 0;
        double current = 0, shoulder = double.PositiveInfinity, waist = double.NegativeInfinity;
        bool active = false, yang = false;
        for (int index = 0; index < data[0].Count; index++) {
            if (data[0].Points[index].Y is not { } price) {
                if (active) {
                    column += 2;
                }
                active = false;
                direction = 0;
                shoulder = double.PositiveInfinity;
                waist = double.NegativeInfinity;
                continue;
            }
            if (!active) {
                current = price;
                shoulder = price;
                waist = price;
                active = true;
                yang = false;
                segments.Add(new(column, price, price, index, yang));
                continue;
            }
            if (price == current) {
                continue;
            }
            int move = price > current ? 1 : -1;
            if (direction == 0) {
                direction = move;
                yang = move > 0;
            }
            else if (direction != move) {
                if (Math.Abs(price - current) < reversal) {
                    continue;
                }
                if (direction > 0) {
                    shoulder = current;
                }
                else {
                    waist = current;
                }
                segments.Add(new(column, current, current, index, yang, Connector: true));
                column++;
                direction = move;
            }
            double crossing = yang ? waist : shoulder;
            if (yang && price < waist || !yang && price > shoulder) {
                segments.Add(new(column, current, crossing, index, yang));
                yang = !yang;
                current = crossing;
            }
            segments.Add(new(column, current, price, index, yang));
            current = price;
        }
        return segments.Count == 0 ? 0 : column + 1;
    }

    internal static void MapKagi(List<ChartPrimitive> shapes, List<PriceSegment> segments, int columns, ChartRange range, int width, int height) {
        int X(int column) => (int)Math.Round((column + .5) / Math.Max(1, columns) * (width - 1));
        int Y(double price) => height - 1 - (int)Math.Round(ChartMath.Fraction(price, range.MinY, range.MaxY) * (height - 1));
        foreach (var segment in segments) {
            if (Math.Max(segment.From, segment.To) < range.MinY || Math.Min(segment.From, segment.To) > range.MaxY) {
                continue;
            }
            shapes.Add(new(ChartPrimitiveKind.Line, X(segment.Column), Y(segment.From),
                X(segment.Column + (segment.Connector ? 1 : 0)), Y(segment.To), Shade: segment.Yang ? (byte)3 : (byte)6,
                Point: segment.Point, Stroke: segment.Yang ? 2 : 1));
        }
    }
}
