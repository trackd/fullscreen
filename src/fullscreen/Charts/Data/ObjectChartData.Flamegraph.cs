using System.Globalization;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartData {
    internal ChartDataSeries[] ProjectFrames(string idProperty, string parentProperty, string? nameProperty, string weightProperty, int[] indices, IReadOnlyList<object>? sources = null) {
        string id = Resolve(idProperty), parent = Resolve(parentProperty);
        string? name = nameProperty is null ? null : Resolve(nameProperty);
        var frames = new ChartFrame[indices.Length];
        for (int position = 0; position < indices.Length; position++) {
            int index = indices[position];
            object? idValue = Value(index, id), parentValue = Value(index, parent);
            string frameId = Convert.ToString(idValue, CultureInfo.InvariantCulture) ?? "";
            string? parentId = parentValue is null ? null : Convert.ToString(parentValue, CultureInfo.InvariantCulture);
            string label = name is null ? frameId : Convert.ToString(Value(index, name), CultureInfo.InvariantCulture) ?? "";
            double weight = Number(Value(index, weightProperty)) ?? throw new ArgumentException($"Flamegraph frame '{frameId}' has a missing or nonfinite self weight.");
            frames[position] = new(frameId, parentId, label, weight, sources is null ? Objects[index] : sources[index]);
        }
        var tree = new ChartFlameTree(frames);
        var data = tree.Data;
        for (int index = 0; index < data[0].Count; index++) {
            var point = data[0].Points[index];
            data[0].Points[index] = point with {
                Index = indices[point.Index],
                SourceIndices = [.. point.SourceIndices!.Select(source => indices[source])]
            };
        }
        return data;
    }
}
