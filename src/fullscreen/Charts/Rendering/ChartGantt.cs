namespace fullscreen.Charts;

internal static class ChartGantt {
    internal static void Build(List<ChartPrimitive> shapes, ChartDataSeries[] data, ChartRange range, int width, int height, int cellHeight, int offset) {
        if (data.Length != 1 || data[0].Schedule is not { } schedule) {
            throw new ArgumentException("Gantt requires task intervals, not numeric series.");
        }
        if (!data[0].Visible) {
            return;
        }
        cellHeight = Math.Clamp(cellHeight, 1, height);
        int laneHeight = 2 * cellHeight;
        int capacity = Math.Max(1, height / laneHeight);
        int first = Math.Min(offset, Math.Max(0, schedule.Tasks.Length - 1));
        for (int index = first; index < schedule.Tasks.Length && index - first < capacity; index++) {
            var point = data[0].Points[index];
            double end = point.Upper!.Value;
            if (end < range.MinX || point.X > range.MaxX) {
                continue;
            }
            int top = (index - first) * laneHeight;
            if (top >= height) {
                break;
            }
            int barTop = Math.Min(height - 1, top + cellHeight);
            int bottom = Math.Min(height - 1, top + laneHeight - 1);
            int left = Math.Clamp((int)Math.Round(ChartMath.Fraction(point.X, range.MinX, range.MaxX) * width), 0, width - 1);
            int right = Math.Clamp((int)Math.Round(ChartMath.Fraction(end, range.MinX, range.MaxX) * width) - 1, left, width - 1);
            shapes.Add(new(ChartPrimitiveKind.Rectangle, left, barTop, right, bottom, Shade: 2, Point: index));
            if (schedule.Tasks[index].Progress is { } progress && progress > 0) {
                double completeTime = point.X + (end - point.X) * progress;
                int completeEnd = Math.Min(right, (int)Math.Round(ChartMath.Fraction(completeTime, range.MinX, range.MaxX) * width) - 1);
                if (completeEnd >= left) {
                    shapes.Add(new(ChartPrimitiveKind.Rectangle, left, barTop, completeEnd, bottom, Shade: 3, Point: index));
                }
            }
            if (barTop > top) {
                shapes.Add(new(ChartPrimitiveKind.Text, 0, top, width - 1, top, Text: schedule.Labels[index], Point: index));
            }
            if (schedule.Tasks[index].Start == schedule.Tasks[index].End) {
                shapes.Add(new(ChartPrimitiveKind.Text, left, barTop, left, barTop, Text: "*", Point: index));
            }
        }
    }
}
