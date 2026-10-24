using System.Globalization;

namespace fullscreen.Monitoring;

public sealed partial class ProcessView {
    private bool MatchesFilters(in ProcessRow row) {
        if (ExcludedNames.Contains(row.Name)) {
            return false;
        }

        if (!Matches(row, Filter.AsSpan())) {
            return false;
        }

        foreach (var pair in ColumnFilters) {
            var number = pair.Key == ProcessColumn.MemoryPercent && TotalMemory is > 0 ? row.WorkingSet * 100.0 / TotalMemory.Value : NumericValue(row, pair.Key);
            if (pair.Key is not (ProcessColumn.Name or ProcessColumn.UserName or ProcessColumn.Status)
                && pair.Value.StartsWith('>')) {
                if (!double.TryParse(pair.Value.AsSpan(1), CultureInfo.InvariantCulture, out double minimum)
                    || !number.HasValue
                    || number <= minimum) {
                    return false;
                }
            }
            else if (!string.Equals(TextValue(row, pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the numeric value used by a process column.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>The numeric column value, or null when unavailable.</returns>
    public static double? NumericValue(in ProcessRow row, ProcessColumn column) =>
        column switch {
            ProcessColumn.Id => row.Id,
            ProcessColumn.ParentId => row.ParentId,
            ProcessColumn.Cpu => row.CpuPercent,
            ProcessColumn.CpuTime => row.CpuTimeTicks / (double)TimeSpan.TicksPerSecond,
            ProcessColumn.Memory => row.WorkingSet,
            ProcessColumn.Gpu => row.GpuPercent,
            ProcessColumn.GpuMemory => row.GpuMemory,
            ProcessColumn.Threads => row.Threads,
            ProcessColumn.Disk => row.DiskBytesPerSecond,
            ProcessColumn.Network => row.NetworkBytesPerSecond,
            _ => null
        };
    /// <summary>Formats a process column&apos;s display value.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>The formatted column value.</returns>
    public static string TextValue(in ProcessRow row, ProcessColumn column) =>
        column switch {
            ProcessColumn.Name => row.Name,
            ProcessColumn.UserName => row.UserName ?? "—",
            ProcessColumn.Status => row.Status ?? "—",
            _ => NumericValue(row, column)?.ToString("G", CultureInfo.InvariantCulture) ?? "—"
        };
}
