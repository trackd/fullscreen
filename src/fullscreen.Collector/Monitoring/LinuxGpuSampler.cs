using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

/// <summary>Intel DRM client accounting, independent of vendor-specific sysfs utilization files.</summary>
internal sealed class LinuxGpuSampler(string procRoot = "/proc") {
    internal sealed class Client {
        internal string Driver = "", Device = "", Id = "";
        internal readonly Dictionary<string, ulong> Busy = [], Cycles = [], TotalCycles = [], Capacity = [], Memory = [];
        internal ulong? UsedMemory => Memory.Count == 0 ? null : Memory.Values.Aggregate(0UL, (sum, value) => sum > ulong.MaxValue - value ? ulong.MaxValue : sum + value);
    }

    private Dictionary<string, Client> _previous = new(StringComparer.Ordinal);
    private readonly List<string> _paths = [];
    private readonly char[] _readBuffer = new char[8192];
    private readonly byte[] _linkBuffer = new byte[64];
    private long _last;
    private double _discoveryAge = 5;
    internal CollectorCapabilities Capabilities { get; private set; }

    internal void Sample(CollectorFrame frame, string drmRoot = "/sys/class/drm", double? elapsedSeconds = null) {
        long now = Stopwatch.GetTimestamp();
        double seconds = elapsedSeconds ?? (_last == 0 ? 0 : (double)(now - _last) / Stopwatch.Frequency);
        _last = now;
        Capabilities = GpuSampler.SampleDrm(frame, drmRoot);
        var devices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < frame.GpuCount; index++) {
            var row = frame.Gpus[index];
            if (!row.Provider.StartsWith("DRM/Intel", StringComparison.Ordinal)) {
                continue;
            }
            string card = row.Name.Split(' ')[0];
            string? uevent;
            try {
                uevent = ReadText(Path.Combine(drmRoot, card, "device", "uevent"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                continue;
            }
            string? slot = uevent?.Split('\n').FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal));
            if (slot is not null) {
                devices[slot[14..].Trim()] = index;
            }
        }
        if (devices.Count == 0) {
            _paths.Clear();
            _previous.Clear();
            _discoveryAge = 5;
            return;
        }
        _discoveryAge += Math.Max(0, seconds);
        var clients = new Dictionary<string, Client>(StringComparer.Ordinal);
        bool denied = false;
        if (_discoveryAge >= 5) {
            _paths.Clear();
            foreach (string process in SafeDirectories(procRoot)) {
                if (!int.TryParse(Path.GetFileName(process), out _)) {
                    continue;
                }
                try {
                    foreach (string path in Directory.EnumerateFiles(Path.Combine(process, "fdinfo"))) {
                        ReadClient(path, true);
                    }
                }
                catch (UnauthorizedAccessException) {
                    denied = true;
                }
                catch (IOException) {
                    // Processes and descriptors can disappear during enumeration.
                }
            }
            _discoveryAge = 0;
        }
        else {
            foreach (string path in _paths) {
                ReadClient(path, false);
            }
        }

        foreach (var entry in devices) {
            string device = entry.Key;
            int index = entry.Value;
            var engines = new Dictionary<string, double>(StringComparer.Ordinal);
            ulong? used = null;
            bool counters = false;
            foreach (var item in clients) {
                string key = item.Key;
                var client = item.Value;
                if (client.Device != device) {
                    continue;
                }
                if (client.UsedMemory is { } memory) {
                    used = used.HasValue ? (used.Value > ulong.MaxValue - memory ? ulong.MaxValue : used.Value + memory) : memory;
                }
                counters |= client.Busy.Count > 0 || client.Cycles.Count > 0;
                if (!_previous.TryGetValue(key, out var previous)) {
                    continue;
                }
                foreach (string engine in client.Busy.Keys.Concat(client.Cycles.Keys).Distinct(StringComparer.Ordinal).ToArray()) {
                    ulong capacity = client.Capacity.TryGetValue(engine, out ulong count) ? count : 1;
                    double? percent = null;
                    if (capacity == 0) {
                        continue;
                    }
                    if (client.Busy.TryGetValue(engine, out ulong busy) && previous.Busy.TryGetValue(engine, out ulong old) && seconds > 0) {
                        client.Busy[engine] = Math.Max(busy, old);
                        percent = (double)(client.Busy[engine] - old) / (seconds * 1e9) * 100 / capacity;
                    }
                    else if (client.Cycles.TryGetValue(engine, out ulong cycles) && previous.Cycles.TryGetValue(engine, out ulong oldCycles)
                        && client.TotalCycles.TryGetValue(engine, out ulong total) && previous.TotalCycles.TryGetValue(engine, out ulong oldTotal)) {
                        client.Cycles[engine] = Math.Max(cycles, oldCycles);
                        client.TotalCycles[engine] = Math.Max(total, oldTotal);
                        if (total > oldTotal) {
                            percent = (double)(client.Cycles[engine] - oldCycles) / (total - oldTotal) * 100 / capacity;
                        }
                    }
                    if (percent is { } value) {
                        engines.TryGetValue(engine, out double existing);
                        engines[engine] = existing + value;
                    }
                }
            }
            double? utilization = engines.Count == 0 ? null : Compat.Clamp(engines.Values.Max(), 0, 100);
            frame.Gpus[index] = frame.Gpus[index] with {
                Utilization = frame.Gpus[index].Utilization ?? utilization,
                UsedMemory = frame.Gpus[index].UsedMemory ?? used
            };
            if (counters) {
                Capabilities |= CollectorCapabilities.GpuUtilization;
            }
            if (used.HasValue) {
                Capabilities |= CollectorCapabilities.GpuUsedMemory;
            }
        }
        _previous = clients;
        frame.GpuStatus = denied
            ? "Intel DRM: some processes deny access; metrics cover readable clients only."
            : clients.Count == 0 ? "Intel GPU detected; no readable i915/xe client counters. Check driver support and procfs access."
            : "Intel DRM: readable clients only; client-memory totals may count shared buffers more than once. First sample warms up.";

        void ReadClient(string path, bool discover) {
            try {
                if (discover && Platform.IsLinux()) {
                    // Avoid opening every regular file, pipe and socket's fdinfo.
                    // Keep all /dev paths, including custom DRM device mounts.
                    nint length = ReadLink(path.Replace("/fdinfo/", "/fd/"), _linkBuffer, (nuint)_linkBuffer.Length);
                    if (length >= 0 && (length < 5 || _linkBuffer[0] != '/' || _linkBuffer[1] != 'd'
                        || _linkBuffer[2] != 'e' || _linkBuffer[3] != 'v' || _linkBuffer[4] != '/')) {
                        return;
                    }
                }
                string? text = ReadText(path);
                var client = text is null ? null : ParseClient(text);
                if (client is null || !devices.ContainsKey(client.Device)) {
                    return;
                }
                if (discover) {
                    _paths.Add(path);
                }
                string key = client.Device + "/" + client.Id;
                if (!clients.ContainsKey(key)) {
                    clients.Add(key, client);
                }
            }
            catch (UnauthorizedAccessException) {
                denied = true;
            }
            catch (IOException) {
                // Descriptor closed or process exited.
            }
        }
    }

    [DllImport("libc", EntryPoint = "readlink", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl,
        BestFitMapping = false, ThrowOnUnmappableChar = true, SetLastError = true)]
    private static extern nint ReadLink(string path, [Out] byte[] target, nuint size);

    private static string[] SafeDirectories(string path) {
        try {
            return Directory.GetDirectories(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return [];
        }
    }

    // Bound reads: non-DRM fdinfo (for example inotify) can be very large.
    private string? ReadText(string path) {
        try {
            using var reader = new StreamReader(path);
            int length = reader.ReadBlock(_readBuffer, 0, _readBuffer.Length);
            return new string(_readBuffer, 0, length);
        }
        catch (FileNotFoundException) {
            return null;
        }
        catch (DirectoryNotFoundException) {
            return null;
        }
    }

    internal static Client? ParseClient(string text) {
        if (text.IndexOf("drm-driver:", StringComparison.Ordinal) < 0) {
            return null;
        }
        var client = new Client();
        var resident = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n')) {
            int separator = line.IndexOf(':');
            if (separator < 0) {
                continue;
            }
            string key = line[..separator].Trim(), value = line[(separator + 1)..].Trim();
            switch (key) {
                case "drm-driver":
                    client.Driver = value;
                    break;
                case "drm-pdev":
                    client.Device = value;
                    break;
                case "drm-client-id":
                    client.Id = value;
                    break;
                default:
                    string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0 || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong number)) {
                        continue;
                    }
                    if (key.StartsWith("drm-engine-capacity-", StringComparison.Ordinal)) {
                        client.Capacity[key[20..]] = number;
                    }
                    else if (key.StartsWith("drm-engine-", StringComparison.Ordinal) && parts.Length == 2 && parts[1] == "ns") {
                        client.Busy[key[11..]] = number;
                    }
                    else if (key.StartsWith("drm-total-cycles-", StringComparison.Ordinal)) {
                        client.TotalCycles[key[17..]] = number;
                    }
                    else if (key.StartsWith("drm-cycles-", StringComparison.Ordinal)) {
                        client.Cycles[key[11..]] = number;
                    }
                    else if (key.StartsWith("drm-resident-", StringComparison.Ordinal) || key.StartsWith("drm-memory-", StringComparison.Ordinal) || key.StartsWith("drm-total-", StringComparison.Ordinal)) {
                        bool isResident = key.StartsWith("drm-resident-", StringComparison.Ordinal);
                        string region = key[(isResident ? 13 : key.StartsWith("drm-memory-", StringComparison.Ordinal) ? 11 : 10)..];
                        ulong multiplier = parts.Length == 1 ? 1UL : parts[1] switch { "KiB" => 1024, "MiB" => 1024 * 1024, "B" => 1, _ => 0 };
                        if (multiplier > 0 && number <= ulong.MaxValue / multiplier && (isResident || !resident.Contains(region))) {
                            client.Memory[region] = number * multiplier;
                            if (isResident) {
                                resident.Add(region);
                            }
                        }
                    }
                    break;
            }
        }
        return client.Driver is "i915" or "xe" && client.Device.Length > 0
            && ulong.TryParse(client.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? client : null;
    }
}
