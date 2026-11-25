using System.Diagnostics;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace fullscreen.Monitoring;

// footprint is part of macOS. Expensive queries run in one bounded background worker,
// never in the frame refresh path, and cached results are tied to process start identity.
internal sealed class AppleGpuMemorySampler(Func<int, string?>? read = null, Func<int, long?>? identity = null) : IDisposable {
    internal readonly record struct Reading(ulong Bytes, ulong? Swapped);
    private readonly object _gate = new();
    private readonly Dictionary<(int Pid, long Birth), (Reading? Value, long At)> _cache = [];
    private readonly Func<int, string?> _read = read ?? ReadFootprint;
    private readonly Func<int, long?> _identity = identity ?? Identity;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private bool _disposed;
    internal static bool Fresh(long now, long sampled, long frequency) =>
        frequency > 0 && now >= sampled && (now - sampled) / (double)frequency <= 10;
    internal (Reading? Value, string Status) Get(int pid, long birth) {
        lock (_gate) {
            if (!_cache.TryGetValue((pid, birth), out var value)) {
                return (null, "Graphics footprint pending or unavailable");
            }
            if (!value.Value.HasValue) {
                return (null, "Graphics footprint unavailable or denied");
            }
            if (!Fresh(Stopwatch.GetTimestamp(), value.At, Stopwatch.Frequency)) {
                return (null, "Graphics footprint stale; refresh pending");
            }
            return (value.Value, value.Value.HasValue ? "Measured graphics dirty footprint; not dedicated VRAM" : "Graphics footprint unavailable or denied");
        }
    }
    internal void Schedule(ProcessRow[] rows, int count, HashSet<int> clients) {
        lock (_gate) {
            if (_disposed || _worker is { IsCompleted: false }) {
                return;
            }
            var alive = new HashSet<(int, long)>(rows.Take(count).Where(row => clients.Contains(row.Id)).Select(row => (row.Id, row.StartTimeTicks)));
            foreach (var key in _cache.Keys.Where(key => !alive.Contains(key)).ToArray()) {
                _cache.Remove(key);
            }
            long now = Stopwatch.GetTimestamp();
            var targets = alive.Where(key => key.Item2 != 0 && (!_cache.TryGetValue(key, out var value)
                || (now - value.At) / (double)Stopwatch.Frequency >= (value.Value.HasValue ? 3 : 30)))
                .OrderBy(key => _cache.TryGetValue(key, out var value) ? value.At : 0).Take(4).ToArray();
            if (targets.Length == 0) {
                return;
            }
            _worker = Task.Run(() => {
                foreach (var target in targets) {
                    if (_stop.IsCancellationRequested) {
                        break;
                    }
                    Reading? result = Measure(target.Item1, target.Item2);
                    lock (_gate) {
                        if (!_disposed) {
                            _cache[target] = (result, Stopwatch.GetTimestamp());
                        }
                    }
                }
            });
        }
    }
    internal Reading? Measure(int pid, long birth) {
        try {
            if (birth == 0 || _identity(pid) != birth) {
                return null;
            }
            string? json = _read(pid);
            return _identity(pid) == birth ? Parse(json, pid) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    internal static Reading? Parse(string? json, int pid) {
        if (json is null || string.IsNullOrWhiteSpace(json) || json.Length > 8 * 1024 * 1024) {
            return null;
        }
        try {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var serializer = new DataContractJsonSerializer(typeof(Report), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            if (serializer.ReadObject(stream) is not Report report) {
                return null;
            }
            var process = report.Processes?.FirstOrDefault(process => process.Pid == pid);
            if (process?.Categories is null) {
                return null;
            }
            ulong bytes = 0;
            ulong? swapped = 0;
            foreach (var entry in process.Categories.Where(entry => entry.Key.EndsWith("(graphics)", StringComparison.Ordinal))) {
                if (entry.Value?.Dirty is not { } dirty || ulong.MaxValue - bytes < dirty) {
                    return null;
                }
                bytes += dirty;
                swapped = swapped.HasValue && entry.Value.Swapped.HasValue && ulong.MaxValue - swapped.Value >= entry.Value.Swapped.Value
                    ? swapped + entry.Value.Swapped : null;
            }
            return new(bytes, swapped);
        }
        catch (Exception error) when (error is SerializationException or System.Xml.XmlException or ArgumentException or FormatException or OverflowException) {
            return null;
        }
    }
    private static long? Identity(int pid) {
        try {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
            return null;
        }
    }
    private static string? ReadFootprint(int pid) {
        string directory = Path.Combine(Path.GetTempPath(), "fullscreen-footprint-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "report.json");
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/footprint", "-f bytes -j \"" + file + "\" -p " + pid) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
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
            if (!output.Wait(250) || !errors.Wait(250) || process.ExitCode != 0 || !File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024) {
                return null;
            }
            return File.ReadAllText(file);
        }
        finally {
            try {
                if (Directory.Exists(directory)) {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    public void Dispose() {
        lock (_gate) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _stop.Cancel();
            _cache.Clear();
            if (_worker is null) {
                _stop.Dispose();
            }
            else {
                _ = _worker.ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, TaskScheduler.Default);
            }
        }
        // Each subprocess has a 2s deadline; cancellation prevents subsequent queries.
    }
    [DataContract]
    private sealed class Report {
        [DataMember(Name = "processes")]
        public ProcessReport[]? Processes { get; set; }
    }
    [DataContract]
    private sealed class ProcessReport {
        [DataMember(Name = "pid")]
        public int Pid { get; set; }
        [DataMember(Name = "categories")]
        public Dictionary<string, Category>? Categories { get; set; }
    }
    [DataContract]
    private sealed class Category {
        [DataMember(Name = "dirty")]
        public ulong? Dirty { get; set; }
        [DataMember(Name = "swapped")]
        public ulong? Swapped { get; set; }
    }
}
