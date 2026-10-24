using System.Globalization;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartData {
    internal ChartScale ScaleFor(string? property) {
        if (property is null) {
            return ChartScale.Linear;
        }

        object? sample = Objects.Select((
            _,
            index
        ) => Value(index, property)).FirstOrDefault(value => value is not null);
        if (sample is System.Management.Automation.PSObject wrapped) {
            sample = wrapped.BaseObject;
        }

        return sample is DateTime or DateTimeOffset ? ChartScale.Time : sample is null || IsCoordinate(sample) ? ChartScale.Linear : ChartScale.Category;
    }

    internal ChartDataSeries[] AggregateRows(
        IReadOnlyList<int> indices,
        string? groupProperty,
        IReadOnlyList<string> properties,
        ChartAggregate aggregate,
        CancellationToken token
    ) {
        if (aggregate == ChartAggregate.None) {
            throw new ArgumentException("Choose an aggregate for grouped data.");
        }

        var groups = indices.GroupBy(
            index => groupProperty is null ? "All" : Convert.ToString(Value(index, groupProperty), CultureInfo.InvariantCulture) ?? "(missing)",
            StringComparer.Ordinal
        ).Select(group => {
            int[] rows = [.. group];
            return (group.Key, Rows: rows, Source: new ChartGroup(group.Key, rows.Select(index => (object)Objects[index]).ToArray()));
        }).ToArray();
        string[] fields = aggregate == ChartAggregate.Count ? ["Count"] : [.. properties];
        var result = new List<ChartDataSeries>();
        foreach (string field in fields) {
            var points = new ChartPoint[groups.Length];
            for (int position = 0; position < groups.Length; position++) {
                token.ThrowIfCancellationRequested();
                var (Key, Rows, Source) = groups[position];
                int[] rows = Rows;
                double[] values = aggregate == ChartAggregate.Count ? [] : [.. rows.Select(index => Number(Value(index, field))).Where(value => value.HasValue).Select(value => value!.Value)];
                double? value = aggregate == ChartAggregate.Count ? rows.Length : values.Length == 0 ? null : aggregate switch {
                    ChartAggregate.Sum => values.Sum(),
                    ChartAggregate.Average => values.Sum(value => value / values.Length),
                    ChartAggregate.Minimum => values.Min(),
                    ChartAggregate.Maximum => values.Max(),
                    _ => null
                };
                points[position] = new(
                    position,
                    position,
                    value.HasValue && double.IsFinite(value.Value) ? value : null,
                    Key
                ) {
                    Source = Source,
                    SourceIndices = rows
                };
            }

            result.Add(new(aggregate == ChartAggregate.Count ? "Count" : $"{Label(field)} ({aggregate})", points));
        }

        return [.. result];
    }
}
