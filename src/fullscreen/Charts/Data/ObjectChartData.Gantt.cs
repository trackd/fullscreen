using System.Globalization;
using System.Management.Automation;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartData {
    internal ChartDataSeries[] ProjectTasks(string startProperty, string endProperty, string nameProperty, string? progressProperty, int[] indices, IReadOnlyList<object>? sources = null) {
        string start = Resolve(startProperty), end = Resolve(endProperty), name = Resolve(nameProperty);
        string? progress = progressProperty is null ? null : Resolve(progressProperty);
        var tasks = new ChartTask[indices.Length];
        for (int position = 0; position < indices.Length; position++) {
            int index = indices[position];
            string label = Convert.ToString(Value(index, name), CultureInfo.InvariantCulture) ?? "";
            double? completed = null;
            if (progress is not null && Value(index, progress) is { } value) {
                completed = Number(value) ?? throw new ArgumentException($"Gantt task '{label}' has invalid progress.");
            }
            tasks[position] = new(label, TaskTime(Value(index, start), label, startProperty), TaskTime(Value(index, end), label, endProperty), completed, sources is null ? Objects[index] : sources[index]);
        }
        var schedule = new ChartSchedule(tasks);
        for (int position = 0; position < indices.Length; position++) {
            schedule.Data[0].Points[position] = schedule.Data[0].Points[position] with { Index = indices[position] };
        }
        return schedule.Data;
    }

    private static DateTimeOffset TaskTime(object? value, string label, string field) {
        if (value is PSObject wrapped) {
            value = wrapped.BaseObject;
        }
        if (value is DateTimeOffset offset) {
            return offset;
        }
        if (value is DateTime time && time.Kind == DateTimeKind.Utc) {
            return new(time);
        }
        if (value is string text && text.Length >= 10 && text.Contains('T')
            && (text.EndsWith('Z') || text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-')
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Gantt task '{label}' field '{field}' requires DateTimeOffset, UTC DateTime or ISO 8601 text with Z/explicit offset; numeric durations and timezone-free dates are not timestamps.");
    }
}
