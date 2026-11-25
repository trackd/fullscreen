using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace fullscreen.Monitoring;

// IORegistry properties are available without sudo. No powermetrics or private IOReport ABI.
internal sealed class AppleGpuSampler(Func<string, string?>? read = null) : IDisposable {
    private readonly Func<string, string?> _read = read ?? ReadRegistry;
    private Dictionary<(int Pid, ulong Client), ulong> _previous = [];
    private readonly Dictionary<int, double> _usage = [];
    private readonly HashSet<int> _clients = [];
    private readonly AppleGpuMemorySampler _memory = new();
    internal void Enrich(ProcessRow[] rows, int count) {
        _memory.Schedule(rows, count, _clients);
        for (int index = 0; index < count; index++) {
            var row = rows[index];
            if (!_clients.Contains(row.Id)) {
                rows[index] = GpuProcessAccounting.Apply(row, []);
                continue;
            }
            var memory = _memory.Get(row.Id, row.StartTimeTicks);
            rows[index] = GpuProcessAccounting.Apply(row, [new("apple-integrated-gpu", "Apple IORegistry + footprint",
                ProcessUtilization(row.Id), memory.Value?.Bytes, default, memory.Status) { MemoryKind = "graphics dirty footprint in unified memory"
                    + (memory.Value?.Swapped is { } swapped ? "; swapped " + swapped + " bytes" : "") }]);
        }
    }
    public void Dispose() => _memory.Dispose();
    private long _last;
    internal CollectorCapabilities Capabilities { get; private set; }
    internal bool HasProcessCounters { get; private set; }
    internal double? ProcessUtilization(int pid) => _usage.TryGetValue(pid, out double usage) ? usage : null;

    internal void Sample(CollectorFrame frame, bool devices, bool processes, double? elapsed = null) {
        long now = Stopwatch.GetTimestamp();
        double seconds = elapsed ?? (_last == 0 ? 0 : (double)(now - _last) / Stopwatch.Frequency);
        _last = now;
        Capabilities = CollectorCapabilities.None;
        _usage.Clear();
        _clients.Clear();
        HasProcessCounters = false;
        if (devices) {
            foreach (var entry in Dictionaries(_read("IOAccelerator"))) {
                if (Property(entry, "PerformanceStatistics") is not { Name.LocalName: "dict" } stats) {
                    continue;
                }
                double? utilization = Percent(stats, "Device Utilization %");
                ulong? allocated = Unsigned(stats, "Alloc system memory");
                double? renderer = Percent(stats, "Renderer Utilization %");
                if (utilization.HasValue) {
                    Capabilities |= CollectorCapabilities.GpuUtilization;
                }
                if (allocated.HasValue) {
                    Capabilities |= CollectorCapabilities.GpuUsedMemory;
                }
                if (renderer.HasValue) {
                    Capabilities |= CollectorCapabilities.GpuRender;
                }
                string name = Property(entry, "model") is { } model ? Text(model) : "Apple GPU";
                frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                frame.Gpus[frame.GpuCount++] = new(name, "Apple IORegistry", utilization, allocated, null, null) {
                    Id = Unsigned(entry, "IORegistryEntryID") is { } id ? "ioreg:" + id.ToString(CultureInfo.InvariantCulture) : null,
                    SharedMemory = allocated,
                    MemoryKind = "unified GPU allocations (not resident VRAM)",
                    Status = "Unprivileged IORegistry; allocation bytes are not resident VRAM; absent sensors unsupported",
                    Activity = new(Render: renderer)
                };
            }
            frame.GpuStatus = frame.GpuCount > 0
                ? "Apple IORegistry · unified allocation bytes; GPU capacity, power and temperature unavailable"
                : "Apple GPU telemetry unavailable · IORegistry statistics not exposed";
        }
        if (!processes) {
            _previous.Clear();
            return;
        }
        var current = new Dictionary<(int Pid, ulong Client), ulong>();
        foreach (var entry in Dictionaries(_read("AGXDeviceUserClient"))) {
            string creator = Property(entry, "IOUserClientCreator")?.Value ?? "";
            string[] fields = creator.Split(' ');
            if (fields.Length < 2 || fields[0] != "pid" || !int.TryParse(fields[1].TrimEnd(','), out int pid)
                || Unsigned(entry, "IORegistryEntryID") is not { } client
                || Property(entry, "AppUsage") is not { } appUsage) {
                continue;
            }
            ulong total = 0;
            bool reported = false;
            foreach (var usage in appUsage.Elements("dict")) {
                if (Unsigned(usage, "accumulatedGPUTime") is not { } time || ulong.MaxValue - total < time) {
                    continue;
                }
                total += time;
                reported = true;
            }
            if (!reported) {
                continue;
            }
            HasProcessCounters = true;
            _clients.Add(pid);
            var key = (pid, client);
            current[key] = total;
            if (!_previous.TryGetValue(key, out ulong old) || total < old || seconds <= 0) {
                continue;
            }
            _usage.TryGetValue(pid, out double existing);
            _usage[pid] = Compat.Clamp(existing + (total - old) / (seconds * 1e9) * 100, 0, 100);
        }
        _previous = current;
    }

    private static string Text(XElement element) {
        if (element.Name.LocalName != "data") {
            return element.Value;
        }
        try {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(element.Value)).TrimEnd('\0');
        }
        catch (FormatException) {
            return "Apple GPU";
        }
    }
    private static XElement? Property(XElement dictionary, string key) =>
        dictionary.Elements("key").FirstOrDefault(element => element.Value == key)?.ElementsAfterSelf().FirstOrDefault();
    private static ulong? Unsigned(XElement dictionary, string key) =>
        ulong.TryParse(Property(dictionary, key)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value) ? value : null;
    private static double? Percent(XElement dictionary, string key) =>
        double.TryParse(Property(dictionary, key)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && Compat.IsFinite(value) && value >= 0 && value <= 100 ? value : null;
    private static XElement[] Dictionaries(string? xml) {
        if (string.IsNullOrWhiteSpace(xml)) {
            return [];
        }
        try {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            return XDocument.Load(reader).Descendants("dict").ToArray();
        }
        catch (XmlException) {
            return [];
        }
    }
    private static string? ReadRegistry(string service) {
        try {
            using var process = System.Diagnostics.Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-a -r -d 1 -w 0 -c " + service) {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null) {
                return null;
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2000)) {
                process.Kill();
                return null;
            }
            if (!output.Wait(500) || !errors.Wait(500) || process.ExitCode != 0) {
                return null;
            }
            return output.GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or AggregateException) {
            return null;
        }
    }
}
