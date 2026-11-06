using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Charts;

public partial class Chart {
    private LegendKey? _legendKey;
    private LegendLayout _legend = new(0, []);

    private LegendLayout GetLegend() {
        var data = GetData(Math.Max(1, Bounds.Width));
        var key = new LegendKey(data, DataVersion, Bounds.Width, Bounds.Height, TitleRows, ShowAxes, GraphKind, Minimum, Maximum, FlameSearch);
        if (_legendKey == key) {
            return _legend;
        }
        if (ChartTopology.Supports(GraphKind)) {
            string label = $"{GraphKind}: {data[0].Count} nodes, {data[1].Count} edges | click / arrows select, Enter inspect";
            label = ChartText.Fit(label, Math.Max(0, Bounds.Width));
            int length = ChartText.Width(label);
            _legend = new(length == 0 ? 0 : 1, length == 0 ? [] : [new(-1, new(0, 0, length, 1), label)]);
            _legendKey = key;
            return _legend;
        }
        if (GraphKind == GraphKind.Gantt) {
            string label = ChartText.Fit("UTC tasks | bar: interval, green: completed, * milestone | Up/Down lanes", Math.Max(0, Bounds.Width));
            int length = ChartText.Width(label);
            _legend = new(length == 0 ? 0 : 1, length == 0 ? [] : [new(-1, new(0, 0, length, 1), label)]);
            _legendKey = key;
            return _legend;
        }
        if (GraphKind == GraphKind.Flamegraph) {
            string label = "Samples, alphabetical | click zoom, u parent, 0 roots, r reverse, / search";
            if (FlameSearch.Length > 0) {
                label = $"Search '{FlameSearch}': {FlameMatchPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% of profile (nested matches counted once)";
            }
            label = ChartText.Fit(label, Math.Max(0, Bounds.Width));
            int length = ChartText.Width(label);
            _legend = new(length == 0 ? 0 : 1, length == 0 ? [] : [new(-1, new(0, 0, length, 1), label)]);
            _legendKey = key;
            return _legend;
        }

        var entries = new List<LegendEntry>();
        int width = Bounds.Width, maxRows = Math.Max(0, (Bounds.Height - TitleRows - (ShowAxes ? 1 : 0)) / 2);
        int x = 0, row = 0;
        bool rows = ChartRadial.IsRows(GraphKind) && data.Length == 1 || UsesCategoryColors(data, GraphKind);
        int count = rows ? data[0].Count : data.Length;
        double largest = 0, total = 0;
        if (rows && GraphKind == GraphKind.Pie) {
            foreach (var point in data[0].Span) {
                largest = Math.Max(largest, point.Y ?? 0);
            }
            if (largest > 0) {
                foreach (var point in data[0].Span) {
                    total += (point.Y ?? 0) / largest;
                }
            }
        }
        for (int index = 0; width > 0 && maxRows > 0 && index < count; index++) {
            var series = data[rows ? 0 : index];
            string role = GraphKind == GraphKind.ColumnLine ? ChartColumns.Kind(series, GraphKind, index) == GraphKind.Bars ? "[columns] " : "[line] " : "";
            string label = (series.Visible ? "● " : "○ ") + role + (series.Property.Length == 0 ? "Values" : series.Property);
            if (rows) {
                var point = series.Points[index];
                string value = point.Y is { } number ? number.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) : "missing";
                string proportion = GraphKind == GraphKind.Pie && total > 0 && point.Y is { } portion
                    ? $" ({(portion / largest / total * 100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%)" : "";
                bool overflow = ChartRadial.IsRows(GraphKind) && GraphKind != GraphKind.Pie && (point.Y < (Minimum ?? 0) || point.Y > (Maximum ?? 100));
                label = $"{(series.Visible ? "●" : "○")} {point.Label}: {value}{proportion}{(overflow ? " !" : "")}";
            }
            label = ChartText.Fit(label, width, ellipsis: true);
            int labelWidth = ChartText.Width(label);

            if (x > 0 && x + labelWidth > width) {
                x = 0;
                row++;
            }

            if (row >= maxRows) {
                int firstEntry = 0;
                while (entries[firstEntry].Bounds.Y < maxRows - 1) {
                    firstEntry++;
                }

                int firstHidden = rows ? entries[firstEntry].Point : entries[firstEntry].Index;
                string overflow = $"+{count - firstHidden} {(rows ? "rows" : "series")}";
                overflow = ChartText.Fit(overflow, width);

                entries.RemoveRange(firstEntry, entries.Count - firstEntry);
                entries.Add(new(-1, new(0, maxRows - 1, width, 1), overflow));
                break;
            }

            entries.Add(new(rows ? 0 : index, new(x, row, labelWidth, 1), label, rows ? index : -1));
            x += labelWidth + 2;
        }

        _legend = new(entries.Count == 0 ? 0 : entries[^1].Bounds.Y + 1, [.. entries]);
        _legendKey = key;
        return _legend;
    }

    private readonly record struct LegendKey(ChartDataSeries[] Data, long Version, int Width, int Height, int Title, bool Axes, GraphKind Kind, double? Minimum = null, double? Maximum = null, string Search = "");
    private readonly record struct LegendEntry(int Index, Rectangle Bounds, string Label, int Point = -1);
    private sealed record LegendLayout(int Rows, LegendEntry[] Entries);
}
