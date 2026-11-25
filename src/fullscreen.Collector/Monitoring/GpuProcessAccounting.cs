namespace fullscreen.Monitoring;

internal static class GpuProcessAccounting {
    internal static string? EngineSummary(ProcessGpuUsage[]? details) {
        if (details is not { Length: > 0 }) { return null; }
        var engines = new HashSet<string>(StringComparer.Ordinal);
        bool measured = false;
        foreach (var detail in details) {
            void Add(string name, double? value) {
                if (value.HasValue) {
                    measured = true;
                    if (value > 0) {
                        engines.Add(name);
                    }
                }
            }
            Add("render", detail.Activity.Render);
            Add("compute", detail.Activity.Compute);
            Add("copy", detail.Activity.Copy);
            Add("video", detail.Activity.Video);
            Add("video processing", detail.Activity.VideoEnhance);
            Add("encode", detail.Activity.Encode);
            Add("decode", detail.Activity.Decode);
            Add("SM", detail.SmActivity);
        }
        return engines.Count > 0 ? string.Join(", ", engines.OrderBy(name => name, StringComparer.Ordinal))
            : measured ? "reported engines idle" : details.Any(detail => detail.Utilization.HasValue) ? "GPU time" : null;
    }
    internal static ProcessRow Apply(ProcessRow row, IEnumerable<ProcessGpuUsage> source) {
        var details = source.ToArray();
        if (details.Length == 0) { return row with { GpuDetails = null, GpuPercent = null, GpuMemory = null, GpuDedicatedMemory = null, GpuSharedMemory = null }; }
        // A duplicate provider identity must not turn into double-counted memory.
        bool distinct = details.Select(item => item.AdapterId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == details.Length;
        ulong? Sum(Func<ProcessGpuUsage, ulong?> value) {
            ulong sum = 0;
            foreach (var item in details) {
                if (value(item) is not { } bytes || ulong.MaxValue - sum < bytes) { return null; }
                sum += bytes;
            }
            return distinct ? sum : null;
        }
        return row with {
            GpuDetails = details,
            GpuPercent = distinct && details.All(item => item.Utilization.HasValue) ? details.Max(item => item.Utilization) : null,
            GpuMemory = Sum(item => item.Memory),
            GpuDedicatedMemory = Sum(item => item.DedicatedMemory),
            GpuSharedMemory = Sum(item => item.SharedMemory)
        };
    }
}
