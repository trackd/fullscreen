using System.Globalization;

namespace fullscreen.Charts;

/// <summary>A schedule interval. End equals Start for a milestone; Progress is an optional fraction from 0 to 1.</summary>
/// <param name="Name">Printable task label, in input row order.</param>
/// <param name="Start">Absolute start instant.</param>
/// <param name="End">Absolute end instant, not earlier than Start.</param>
/// <param name="Progress">Optional completed fraction; null means unknown, not zero.</param>
/// <param name="Source">Original source record to inspect.</param>
public sealed record ChartTask(string Name, DateTimeOffset Start, DateTimeOffset End, double? Progress = null, object? Source = null);

internal sealed class ChartSchedule {
    internal ChartTask[] Tasks { get; }
    internal string[] Labels { get; }
    internal ChartDataSeries[] Data { get; }
    internal ChartRange Range { get; }

    internal ChartSchedule(IReadOnlyList<ChartTask> tasks) {
        if (tasks.Count > 1000000) {
            throw new ArgumentException("Gantt supports at most 1000000 tasks.");
        }
        Tasks = new ChartTask[tasks.Count];
        Labels = new string[tasks.Count];
        var points = new ChartPoint[tasks.Count];
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        for (int index = 0; index < tasks.Count; index++) {
            var task = tasks[index];
            if (task is null || string.IsNullOrWhiteSpace(task.Name) || task.Name.Length > 1024 || task.Name.Any(char.IsControl)
                || task.End < task.Start || task.Progress is { } progress && (!double.IsFinite(progress) || progress < 0 || progress > 1)) {
                throw new ArgumentException("Gantt requires printable task labels, End >= Start and finite progress fractions from 0 to 1.");
            }
            Tasks[index] = task;
            double start = Seconds(task.Start), end = Seconds(task.End);
            minimum = Math.Min(minimum, start);
            maximum = Math.Max(maximum, end);
            points[index] = new(index, start, end - start, task.Name) { Upper = end, Source = task.Source ?? task };
            Labels[index] = task.Name + (task.End == task.Start ? " (milestone)" : "")
                + (task.Progress is { } completed ? " [" + completed.ToString("P0", CultureInfo.InvariantCulture) + "]" : "");
        }
        if (tasks.Count == 0) {
            minimum = 0;
            maximum = 1;
        }
        if (minimum == maximum) {
            // A single-instant schedule still needs a valid time viewport.
            minimum = Math.Max(0, minimum - 1);
            maximum = Math.Min(DateTimeOffset.MaxValue.UtcTicks / (double)TimeSpan.TicksPerSecond, maximum + 1);
        }
        Range = new(minimum, maximum, 0, Math.Max(0, tasks.Count - 1));
        Data = [new("Tasks", points) { Schedule = this }];
    }

    internal static double Seconds(DateTimeOffset time) => time.UtcTicks / (double)TimeSpan.TicksPerSecond;

    internal static void ValidateOptions(double? minimum, double? maximum, ChartScale x, ChartScale y, int thresholds, int annotations, int offset) {
        if (minimum is not null || maximum is not null || x is ChartScale.Logarithmic or ChartScale.Category
            || y == ChartScale.Logarithmic || thresholds != 0 || annotations != 0 || offset < 0) {
            throw new ArgumentException("Gantt uses absolute time intervals and ordered task lanes; Y bounds, logarithmic/category X, overlays and negative TaskOffset are unsupported.");
        }
    }
}
