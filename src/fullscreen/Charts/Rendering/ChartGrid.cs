using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

/// <summary>Ordered matrix cells and cumulative bars, shared by drawing and exact source picking.</summary>
internal static class ChartGrid
{
    internal static bool Supports(GraphKind kind) => kind is GraphKind.Heatmap or GraphKind.Waterfall;
    internal static Color HeatColor(Color background, Color ink, byte shade) =>
        Color.Mix(background, ink, shade switch {
            2 => .24f,
            3 => .4f,
            4 => .6f,
            5 => .8f,
            _ => 1f
        });

    internal static void Validate(ChartDataSeries[] data, GraphKind kind)
    {
        if (kind == GraphKind.Waterfall && data.Length > 1)
        {
            throw new ArgumentException("Waterfall requires one Y series; rows are ordered signed increments.");
        }
        ChartDataSeries? reference = null;
        foreach (var series in data)
        {
            if (reference is null || series.Count > reference.Count)
            {
                reference = series;
            }
            foreach (var point in series.Span)
            {
                if (!double.IsFinite(point.X))
                {
                    throw new ArgumentException("Waterfall and Heatmap category coordinates must be finite.");
                }
            }
            for (int point = 1; point < series.Count; point++)
            {
                if (series.Points[point].X <= series.Points[point - 1].X)
                {
                    throw new ArgumentException("Waterfall and Heatmap category coordinates must be strictly increasing; use input order for repeated labels.");
                }
            }
        }
        if (kind != GraphKind.Heatmap || reference is null)
        {
            return;
        }
        foreach (var series in data)
        {
            for (int point = 0; point < series.Count; point++)
            {
                if (reference.Points[point].X != series.Points[point].X)
                {
                    throw new ArgumentException("Heatmap series must have matching ordered category coordinates.");
                }
            }
        }
    }

    internal static void ValidateOptions(GraphKind kind, ChartScale xScale, ChartScale yScale)
    {
        if (Supports(kind) && (xScale == ChartScale.Logarithmic || yScale == ChartScale.Logarithmic
            || kind == GraphKind.Waterfall && yScale != ChartScale.Linear))
        {
            throw new ArgumentException("Waterfall and Heatmap require linear values and ordered non-logarithmic categories.");
        }
    }

    internal static double Add(double total, double increment)
    {
        double result = total + increment;
        if (!double.IsFinite(result))
        {
            throw new ArgumentException("Waterfall cumulative totals must fit in finite double values.");
        }
        return result;
    }

    internal static void Build(List<ChartPrimitive> shapes, ChartDataSeries[] data, ChartRange range, GraphKind kind, int width, int height)
    {
        Validate(data, kind);
        int count = 0;
        foreach (var series in data)
        {
            count = Math.Max(count, series.Count);
        }
        int first = count, last = -1;
        for (int point = 0; point < count; point++)
        {
            double x = 0;
            foreach (var series in data)
            {
                if (point < series.Count)
                {
                    x = series.Points[point].X;
                    break;
                }
            }
            if (x >= range.MinX && x <= range.MaxX)
            {
                first = Math.Min(first, point);
                last = point;
            }
        }
        if (last < first)
        {
            return;
        }
        int columns = last - first + 1;
        double total = 0;
        int previousRight = -1, previousY = 0;
        for (int seriesIndex = 0; seriesIndex < data.Length; seriesIndex++)
        {
            var series = data[seriesIndex];
            for (int index = 0; index < series.Count; index++)
            {
                var point = series.Points[index];
                if (point.Y is not { } value)
                {
                    previousRight = -1;
                    continue;
                }
                double start = total;
                if (kind == GraphKind.Waterfall)
                {
                    total = Add(total, value);
                }
                if (index < first || index > last)
                {
                    previousRight = -1;
                    continue;
                }
                int left = (int)((long)(index - first) * width / columns);
                int end = (int)((long)(index - first + 1) * width / columns);
                if (end <= left)
                {
                    continue;
                }
                if (kind == GraphKind.Heatmap)
                {
                    int top = (int)((long)seriesIndex * height / data.Length);
                    int bottom = (int)((long)(seriesIndex + 1) * height / data.Length);
                    if (bottom > top)
                    {
                        byte shade = (byte)(2 + Math.Round(ChartMath.Fraction(value, range.MinColor, range.MaxColor) * 4));
                        shapes.Add(new(ChartPrimitiveKind.Rectangle, left, top, end - 1, bottom - 1, seriesIndex, shade, Point: index));
                    }
                }
                else
                {
                    int inset = end - left >= 3 ? Math.Max(1, (end - left) / 8) : 0;
                    int right = end - inset - 1;
                    left += inset;
                    int from = height - 1 - (int)Math.Round(ChartMath.Fraction(start, range.MinY, range.MaxY) * (height - 1));
                    int to = height - 1 - (int)Math.Round(ChartMath.Fraction(total, range.MinY, range.MaxY) * (height - 1));
                    if (previousRight >= 0 && start >= range.MinY && start <= range.MaxY)
                    {
                        shapes.Add(new(ChartPrimitiveKind.Line, previousRight, previousY, left, previousY, Shade: 1));
                    }
                    if (Math.Max(start, total) >= range.MinY && Math.Min(start, total) <= range.MaxY)
                    {
                        shapes.Add(new(ChartPrimitiveKind.Rectangle, left, Math.Min(from, to), right, Math.Max(from, to), seriesIndex,
                            value > 0 ? (byte)3 : value < 0 ? (byte)6 : (byte)2, Point: index));
                    }
                    previousRight = right;
                    previousY = to;
                }
            }
        }
    }
}
