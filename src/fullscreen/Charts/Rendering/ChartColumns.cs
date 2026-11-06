using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Charts;

// Shared category/slot geometry for cells, raster output and source-aware picking.
internal readonly struct ChartColumns {
    private readonly ChartDataSeries[] _data;
    private readonly GraphKind _kind;
    private readonly int _count;
    private readonly int _columns;

    internal ChartColumns(ChartDataSeries[] data, GraphKind kind) {
        _data = data;
        _kind = kind;
        int count = 1, columns = 0;
        for (int index = 0; index < data.Length; index++) {
            if (!data[index].Visible) {
                continue;
            }

            count = Math.Max(count, data[index].Count);
            if (Kind(data[index], kind, index) == GraphKind.Bars) {
                columns++;
            }
        }

        _count = count;
        _columns = Math.Max(1, columns);
    }

    internal static GraphKind Kind(ChartDataSeries series, GraphKind kind, int index) =>
        kind != GraphKind.ColumnLine ? kind : series.Role == ChartSeriesRole.Column
            || series.Role == ChartSeriesRole.Auto && (series.SourceIndex < 0 ? index : series.SourceIndex) == 0
                ? GraphKind.Bars : GraphKind.Line;

    internal int Center(double x, ChartRange range, int width) =>
        ChartMath.BarCenter(x, range.MinX, range.MaxX, width, _count);

    internal Rectangle Bounds(int index, double x, ChartRange range, int width) {
        int slot = 0;
        for (int position = 0; position < index; position++) {
            if (_data[position].Visible && Kind(_data[position], _kind, position) == GraphKind.Bars) {
                slot++;
            }
        }

        int span = Math.Max(1, width / _count - 1);
        int start = Math.Clamp(Center(x, range, width) - span / 2, 0, Math.Max(0, width - span));
        int from = start + slot * span / _columns;
        int to = start + (slot + 1) * span / _columns;
        return new(from, 0, Math.Max(1, to - from), 1);
    }
}
