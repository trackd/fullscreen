using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

/// <summary>DRM client accounting and independent device telemetry for Linux GPUs.</summary>
internal sealed class LinuxGpuSampler(string procRoot = "/proc", LinuxIntelPmuSampler? pmu = null,
    Func<CollectorFrame?>? sudoSample = null, bool allowSudo = false, SudoCollector? sudo = null) : IDisposable {
    internal sealed class Client {
        internal string Driver = "", Device = "", Id = "";
        internal readonly Dictionary<string, ulong> Busy = [], Cycles = [], TotalCycles = [], Capacity = [], Memory = [];
        internal readonly HashSet<int> Owners = [];
        internal readonly Dictionary<string, double> Rates = [];
        internal readonly Dictionary<string, ulong> Shared = [];
        internal bool InvalidMemory;
        internal bool InvalidSharing;
        internal ulong? PrivateMemory => !InvalidSharing && !InvalidMemory && Memory.Count > 0 && Memory.Keys.All(region => Shared.TryGetValue(region, out ulong shared) && shared == 0) ? UsedMemory : null;
        internal ulong? UsedMemory => InvalidMemory || Memory.Count == 0 ? null : Memory.Values.Aggregate(0UL, (sum, value) => sum > ulong.MaxValue - value ? ulong.MaxValue : sum + value);
    }

    private Dictionary<string, Client> _previous = new(StringComparer.Ordinal);
    private readonly LinuxIntelPmuSampler _pmu = pmu ?? new();
    private SudoCollector? _sudo = sudo;
    private readonly bool _ownsSudo = sudo is null;
    private readonly LinuxGpuSensors _sensors = new();
    private readonly Dictionary<int, (double? Usage, ulong? Memory)> _processes = [];
    private readonly Dictionary<int, ProcessGpuUsage[]> _processDetails = [];
    internal ProcessGpuUsage[] ProcessDetails(int pid) => _processDetails.TryGetValue(pid, out var rows) ? rows : [];
    internal CollectorCapabilities ProcessCapabilities { get; } = CollectorCapabilities.ProcessGraphics;
    internal double? ProcessUtilization(int pid) => _processes.TryGetValue(pid, out var value) ? value.Usage : null;
    internal ulong? ProcessMemory(int pid) => _processes.TryGetValue(pid, out var value) ? value.Memory : null;
    private readonly List<string> _paths = [];
    private readonly char[] _readBuffer = new char[8192];
    private readonly byte[] _linkBuffer = new byte[64];
    private long _last;
    private double _discoveryAge = 5;
    private bool _discoveryDenied;
    internal CollectorCapabilities Capabilities { get; private set; }

    internal void Sample(CollectorFrame frame, string drmRoot = "/sys/class/drm", double? elapsedSeconds = null,
        bool includeDevices = true, bool includeProcesses = true) {
        long now = Stopwatch.GetTimestamp();
        double seconds = elapsedSeconds ?? (_last == 0 ? 0 : (double)(now - _last) / Stopwatch.Frequency);
        _last = now;
        if (!includeDevices) {
            _pmu.Dispose();
            if (_ownsSudo) {
                _sudo?.Dispose();
            }
        }
        Capabilities = GpuSampler.SampleDrm(frame, drmRoot);
        _processes.Clear();
        _processDetails.Clear();
        var devices = new Dictionary<string, int>(StringComparer.Ordinal);
        var drivers = new Dictionary<string, string>(StringComparer.Ordinal);
        var statuses = new List<string>();
        for (int index = 0; index < frame.GpuCount; index++) {
            var row = frame.Gpus[index];
            if (!row.Provider.StartsWith("DRM/", StringComparison.Ordinal)) {
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
                string pci = slot[14..].Trim();
                devices[pci] = index;
                drivers[pci] = uevent!.Split('\n').FirstOrDefault(line => line.StartsWith("DRIVER=", StringComparison.Ordinal))?[7..].Trim() ?? "unknown";
            }
        }
        if (devices.Count == 0) {
            _paths.Clear();
            _previous.Clear();
            _discoveryAge = 5;
            _discoveryDenied = false;
            frame.GpuStatus = frame.GpuCount > 0 ? string.Join(" | ", frame.Gpus.Take(frame.GpuCount)
                .Select(gpu => gpu.Status ?? (gpu.Utilization.HasValue || gpu.UsedMemory.HasValue
                    ? "GPU telemetry available" : "Device detected; activity counters unavailable"))) : "No DRM devices or vendor telemetry";
            return;
        }
        _discoveryAge += Math.Max(0, seconds);
        var clients = new Dictionary<string, Client>(StringComparer.Ordinal);
        bool denied = _discoveryDenied;
        // Process attribution needs all holders of a client, including newly inherited descriptors.
        if (_discoveryAge >= 5 || includeProcesses) {
            denied = false;
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
            _discoveryDenied = denied;
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
            bool memoryComplete = true;
            int clientCount = 0;
            foreach (var item in clients) {
                string key = item.Key;
                var client = item.Value;
                if (client.Device != device) {
                    continue;
                }
                clientCount++;
                memoryComplete &= client.UsedMemory.HasValue;
                if (client.UsedMemory is { } memory) {
                    used = used.HasValue ? (used.Value > ulong.MaxValue - memory ? ulong.MaxValue : used.Value + memory) : memory;
                }
                counters |= client.Busy.Count > 0 || client.Cycles.Count > 0;
                foreach (string engine in client.Busy.Keys.Concat(client.Cycles.Keys)) {
                    Capabilities |= GpuActivity.EngineCapability(engine);
                }
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
                        client.Rates[engine] = value;
                        engines.TryGetValue(engine, out double existing);
                        engines[engine] = existing + value;
                    }
                }
            }
            double? utilization = engines.Count == 0 ? null : Compat.Clamp(engines.Values.Max(), 0, 100);
            var deviceCounters = includeDevices ? _pmu.Sample(device, drivers[device]) : default;
            string provider = frame.Gpus[index].Provider;
            if (deviceCounters.Available) {
                provider += " + PMU";
            }
            else if (counters) {
                provider += " + clients";
            }
            bool sysfsActivity = frame.Gpus[index].Utilization.HasValue;
            frame.Gpus[index] = frame.Gpus[index] with {
                Provider = provider,
                Utilization = frame.Gpus[index].Utilization ?? (deviceCounters.Available ? deviceCounters.Utilization : utilization),
                UsedMemory = frame.Gpus[index].UsedMemory ?? (memoryComplete ? used : null),
                MemoryKind = frame.Gpus[index].MemoryKind ?? (memoryComplete && used.HasValue ? "resident bytes of readable DRM clients" : null),
                Activity = deviceCounters.Available ? deviceCounters.Activity : GpuActivity.FromEngines(engines)
            };
            Capabilities |= deviceCounters.EngineCapabilities;
            if (includeDevices) {
                frame.Gpus[index] = _sensors.Sample(frame.Gpus[index], Path.Combine(drmRoot, frame.Gpus[index].Name.Split(' ')[0], "device"), seconds);
                if (frame.Gpus[index].ClockMHz.HasValue) {
                    Capabilities |= CollectorCapabilities.GpuClock;
                }
                if (frame.Gpus[index].PowerWatts.HasValue) {
                    Capabilities |= CollectorCapabilities.GpuPower;
                }
            }
            if (counters || deviceCounters.Available) {
                Capabilities |= CollectorCapabilities.GpuUtilization;
            }
            if (frame.Gpus[index].UsedMemory.HasValue) {
                Capabilities |= CollectorCapabilities.GpuUsedMemory;
            }
            string activity = frame.Gpus[index].Utilization.HasValue ? (sysfsActivity ? "device activity" : deviceCounters.Available ? deviceCounters.Status : "readable client activity only")
                : deviceCounters.Available ? deviceCounters.Status : counters ? "client activity warming up; " + deviceCounters.Status : drivers[device] == "i915" ? deviceCounters.Status
                : "device activity counters unavailable";
            if (drivers[device] == "hyperv_drm") {
                activity = "Hyper-V synthetic display; guest acceleration counters are not exposed";
            }
            statuses.Add(frame.Gpus[index].Name.Split(' ')[0] + ": " + activity
                + (clientCount == 0 ? "; no readable DRM accounting clients" : "; client memory may count shared buffers more than once")
                + (frame.Gpus[index].PowerScope is { } scope ? "; power: " + scope : ""));
            frame.Gpus[index] = frame.Gpus[index] with { Status = statuses[statuses.Count - 1] + (denied ? "; procfs denied; client coverage incomplete" : "") };
        }
        if (includeProcesses) {
            var processEngines = new Dictionary<int, Dictionary<string, double>>();
            var invalid = new HashSet<int>();
            var incompleteRates = new HashSet<int>();
            foreach (var client in clients.Values) {
                foreach (int pid in client.Owners) {
                    if (!processEngines.TryGetValue(pid, out var rates)) {
                        processEngines[pid] = rates = [];
                    }
                    // A shared DRM file describes the client, not which holder did the work.
                    if (client.Owners.Count != 1 || denied) {
                        invalid.Add(pid);
                        continue;
                    }
                    if (client.Rates.Count == 0) {
                        incompleteRates.Add(pid);
                    }
                    foreach (var rate in client.Rates) {
                        string engine = client.Device + "/" + rate.Key;
                        rates.TryGetValue(engine, out double previous);
                        rates[engine] = previous + rate.Value;
                    }
                    _processes.TryGetValue(pid, out var current);
                    _processes[pid] = (null, current.Memory.HasValue && client.PrivateMemory.HasValue
                        ? Add(current.Memory.Value, client.PrivateMemory.Value) : !_processes.ContainsKey(pid) ? client.PrivateMemory : null);
                }
            }
            foreach (var process in processEngines) {
                _processes.TryGetValue(process.Key, out var value);
                _processes[process.Key] = invalid.Contains(process.Key) ? (null, null)
                    : (incompleteRates.Contains(process.Key) || process.Value.Count == 0 ? null : Compat.Clamp(process.Value.Values.Max(), 0, 100), value.Memory);
                _processDetails[process.Key] = clients.Values.Where(client => client.Owners.Contains(process.Key)).GroupBy(client => client.Device).Select(group => {
                    bool ambiguous = denied || group.Any(client => client.Owners.Count != 1);
                    bool warming = group.Any(client => client.Rates.Count == 0);
                    var engines = new Dictionary<string, double>();
                    ulong? memory = 0;
                    foreach (var client in group) {
                        memory = memory.HasValue && client.PrivateMemory.HasValue ? Add(memory.Value, client.PrivateMemory.Value) : null;
                        foreach (var rate in client.Rates) {
                            engines.TryGetValue(rate.Key, out double previous);
                            engines[rate.Key] = previous + rate.Value;
                        }
                    }
                    string status = denied ? "Permission denied; client coverage incomplete" : ambiguous ? "Shared DRM client; process attribution unavailable"
                        : warming ? "Engine counters warming up" : "Measured readable-client activity; memory requires private resident buffers";
                    return new ProcessGpuUsage(group.Key, "DRM/" + group.First().Driver,
                        ambiguous || warming || engines.Count == 0 ? null : Compat.Clamp(engines.Values.Max(), 0, 100), ambiguous ? null : memory,
                        ambiguous || warming ? default : GpuActivity.FromEngines(engines), status) { MemoryKind = "private resident DRM client bytes" };
                }).ToArray();
            }
        }
        _previous = clients;
        frame.GpuStatus = string.Join(" | ", statuses) + (denied ? " | procfs access denied; client coverage incomplete" : "");
        bool permissionNeeded = frame.GpuStatus.Contains("PMU access denied") || denied && devices.Any(entry =>
            drivers[entry.Key] is "i915" or "xe" or "amdgpu"
            && !(frame.Gpus[entry.Value].Provider.Contains("sysfs")
                && frame.Gpus[entry.Value].Utilization.HasValue && frame.Gpus[entry.Value].UsedMemory.HasValue));
        if (allowSudo && includeDevices && permissionNeeded && (sudoSample is not null
            || Platform.IsLinux() && procRoot == "/proc" && GetEffectiveUserId() != 0)
            && drivers.Values.Any(driver => driver is "i915" or "xe" or "amdgpu")) {
            var privileged = sudoSample is not null ? sudoSample() : (_sudo ??= new()).Sample(CollectorMetrics.Gpu);
            if (privileged is not null) {
                for (int index = 0; index < frame.GpuCount; index++) {
                    for (int elevated = 0; elevated < privileged.GpuCount; elevated++) {
                        var candidate = privileged.Gpus[elevated];
                        bool sameDevice = candidate.Id is not null && frame.Gpus[index].Id is not null
                            ? string.Equals(candidate.Id, frame.Gpus[index].Id, StringComparison.OrdinalIgnoreCase)
                            : candidate.Name == frame.Gpus[index].Name;
                        if (sameDevice && candidate.Provider.StartsWith("DRM/", StringComparison.Ordinal)) {
                            frame.Gpus[index] = candidate with { Provider = candidate.Provider + " (sudo)" };
                            break;
                        }
                    }
                }
                Capabilities |= (privileged.Capabilities ?? CollectorCapabilities.None) & CollectorCapabilities.DeviceGraphics;
                frame.GpuStatus = privileged.GpuStatus + " | GPU reader: sudo -n";
            }
            else { frame.GpuStatus += " | " + (_sudo?.Status ?? "sudo collector unavailable"); }
        }

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
                if (client is null) {
                    return;
                }
                if (client.Device.Length == 0 && Platform.IsLinux()) {
                    // Map the actual render/card node; numbering does not imply a pairing.
                    nint length = ReadLink(path.Replace("/fdinfo/", "/fd/"), _linkBuffer, (nuint)_linkBuffer.Length);
                    if (length > 0 && length < _linkBuffer.Length) {
                        string node = Path.GetFileName(System.Text.Encoding.UTF8.GetString(_linkBuffer, 0, (int)length));
                        string? identity = ReadText(Path.Combine(drmRoot, node, "device", "uevent"));
                        client.Device = identity?.Split('\n').FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal))?[14..].Trim() ?? "";
                    }
                }
                if (!devices.ContainsKey(client.Device) || drivers[client.Device] != client.Driver) {
                    return;
                }
                if (discover) {
                    _paths.Add(path);
                }
                string key = client.Device + "/" + client.Id;
                if (!clients.TryGetValue(key, out var existing)) {
                    clients.Add(key, client);
                    existing = client;
                }
                if (int.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))), out int pid)) {
                    existing.Owners.Add(pid);
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
    [DllImport("libc", EntryPoint = "geteuid", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetEffectiveUserId();

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
            if (length == _readBuffer.Length) {
                return null;
            } // Never publish a truncated memory/accounting total.
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
                        if (key.StartsWith("drm-resident-", StringComparison.Ordinal) || key.StartsWith("drm-memory-", StringComparison.Ordinal)) {
                            client.InvalidMemory = true;
                        }
                        if (key.StartsWith("drm-shared-", StringComparison.Ordinal)) {
                            client.InvalidSharing = true;
                        }
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
                    else if (key.StartsWith("drm-resident-", StringComparison.Ordinal) || key.StartsWith("drm-memory-", StringComparison.Ordinal) || key.StartsWith("drm-shared-", StringComparison.Ordinal)) {
                        bool isResident = key.StartsWith("drm-resident-", StringComparison.Ordinal);
                        bool isShared = key.StartsWith("drm-shared-", StringComparison.Ordinal);
                        string region = key[(isResident ? 13 : 11)..];
                        ulong multiplier = parts.Length == 1 ? 1UL : parts[1] switch {
                            "KiB" => 1024,
                            "MiB" => 1024 * 1024,
                            "B" => 1,
                            _ => 0
                        };
                        if (parts.Length > 2 || multiplier == 0 || number > ulong.MaxValue / multiplier) {
                            if (isShared) {
                                client.InvalidSharing = true;
                            }
                            else {
                                client.InvalidMemory = true;
                            }
                        }
                        else if (isShared) {
                            client.Shared[region] = number * multiplier;
                        }
                        else if (isResident || !resident.Contains(region)) {
                            client.Memory[region] = number * multiplier;
                            if (isResident) {
                                resident.Add(region);
                            }
                        }
                    }
                    break;
            }
        }
        return client.Driver is "i915" or "xe" or "amdgpu"
            && ulong.TryParse(client.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? client : null;
    }

    private static ulong Add(ulong left, ulong right) => left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
    public void Dispose() {
        _pmu.Dispose();
        if (_ownsSudo) { _sudo?.Dispose(); }
    }
}
