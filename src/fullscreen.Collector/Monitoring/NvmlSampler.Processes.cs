using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

internal sealed partial class NvmlSampler {
    private static readonly string[] ContainerNames = ["docker", "kubepods", "libpod", "lxc"];
    private static readonly char[] NamespaceSeparators = [' ', '\t'];
    private readonly Dictionary<int, (double? Usage, ulong? Memory)> _processRows = [];
    private readonly ulong[] _lastSeen = new ulong[32];
    private readonly Dictionary<int, ProcessGpuUsage[]> _processDetails = [];
    private readonly Dictionary<int, long> _processBirth = [];
    private readonly Dictionary<int, GpuActivity> _activities = [];
    private readonly Dictionary<int, double> _sm = [];
    private readonly Dictionary<(int Pid, string Adapter), ulong> _usageTimestamps = [];
    internal static bool SampleMatchesBirth(ulong microseconds, long birth) {
        const long epoch = 621355968000000000;
        return birth >= epoch && microseconds <= (ulong)((long.MaxValue - epoch) / 10)
            && epoch + (long)microseconds * 10 >= birth;
    }
    internal ProcessGpuUsage[] ProcessDetails(int pid, long birth) => _processBirth.TryGetValue(pid, out long captured) && captured == birth
        && _processDetails.TryGetValue(pid, out var details) ? details : [];
    internal readonly record struct ProcessSnapshot(ProcessInfo[]? Compute, ProcessInfo[]? Graphics, Dictionary<int, double>? Usage);
    internal bool ProcessTelemetryAvailable { get; private set; }
    internal (double? Usage, ulong? Memory) Process(int pid) => _processRows.TryGetValue(pid, out var value) ? value : default;
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo { internal uint Pid; internal ulong Memory; internal uint GpuInstance, ComputeInstance; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessUsage { internal uint Pid; internal ulong Timestamp; internal uint Sm, Memory, Encoder, Decoder; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int ProcessListFunction(nint device, ref uint count, ProcessInfo* values);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int ProcessUtilizationFunction(nint device, ProcessUsage* values, ref uint count, ulong since);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PciFunction(nint device, out PciInfo pci);
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PciInfo {
        internal fixed byte Legacy[16];
        internal uint Domain, Bus, Device, DeviceId, Subsystem;
        internal fixed byte BusId[32];
    }

    private unsafe string? DeviceIdentity(nint device) {
        byte[] buffer = new byte[96];
        fixed (byte* bytes = buffer) {
            if (Platform.IsWindows() && _api!.Pci is { } query && query(device, out var pci) == 0) {
                int length = 0;
                while (length < 32 && pci.BusId[length] != 0) {
                    length++;
                }
                string address = System.Text.Encoding.ASCII.GetString(pci.BusId, length);
                var matches = WindowsGpuAdapters.Read().Values.Where(adapter => string.Equals(adapter.Pci, address, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1) {
                    return matches[0].Id;
                }
            }
            if (_api!.Uuid is { } uuid && uuid(device, bytes, (uint)buffer.Length) == 0) {
                int end = Array.IndexOf(buffer, (byte)0);
                return System.Text.Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
            }
        }
        return null;
    }

    // Both APIs report the whole process allocation. A process present in both must count once.
    internal static Dictionary<int, ulong?> MergeProcesses(IEnumerable<ProcessInfo> compute, IEnumerable<ProcessInfo> graphics) {
        var result = new Dictionary<int, ulong?>();
        var partitions = new Dictionary<int, HashSet<(uint Gpu, uint Compute)>>();
        foreach (var process in compute.Concat(graphics)) {
            if (process.Pid == 0 || process.Pid > int.MaxValue) {
                continue;
            }
            int pid = (int)process.Pid;
            if (!partitions.TryGetValue(pid, out var instances)) {
                partitions[pid] = instances = [];
            }
            instances.Add((process.GpuInstance, process.ComputeInstance));
            ulong? memory = process.Memory == ulong.MaxValue ? null : process.Memory;
            result[pid] = result.TryGetValue(pid, out var previous)
                ? previous.HasValue && memory.HasValue ? Math.Max(previous.Value, memory.Value) : null : memory;
        }
        foreach (var entry in partitions) {
            if (entry.Value.Count > 1) {
                result[entry.Key] = null;
            }
        }
        return result;
    }

    internal static Dictionary<int, (double? Usage, ulong? Memory)> CombineSnapshots(IEnumerable<ProcessSnapshot> snapshots) {
        var result = new Dictionary<int, (double? Usage, ulong? Memory)>();
        bool complete = true;
        foreach (var snapshot in snapshots) {
            complete &= snapshot.Compute is not null && snapshot.Graphics is not null;
            foreach (var item in MergeProcesses(snapshot.Compute ?? [], snapshot.Graphics ?? [])) {
                bool existing = result.TryGetValue(item.Key, out var previous);
                double? usage = snapshot.Usage is not null && snapshot.Usage.TryGetValue(item.Key, out double value) ? value : null;
                result[item.Key] = (previous.Usage.HasValue && usage.HasValue ? Math.Max(previous.Usage.Value, usage.Value) : usage ?? previous.Usage,
                    existing ? previous.Memory.HasValue && item.Value.HasValue && ulong.MaxValue - previous.Memory.Value >= item.Value.Value
                        ? previous.Memory + item.Value : null : item.Value);
            }
        }
        if (!complete) {
            foreach (int pid in result.Keys.ToArray()) {
                result[pid] = (result[pid].Usage, null);
            }
        }
        return result;
    }

    private unsafe ProcessInfo[]? ReadProcesses(ProcessListFunction? query, nint device) {
        if (query is null) {
            return null;
        }
        uint count = 0;
        int status = query(device, ref count, null);
        for (int attempt = 0; attempt < 3 && (status == 7 || status == 0); attempt++) {
            if (count == 0) {
                return [];
            }
            if (count > 65536) {
                return null;
            }
            var values = new ProcessInfo[count];
            fixed (ProcessInfo* pointer = values) {
                status = query(device, ref count, pointer);
            }
            if (status == 0 && count <= values.Length) {
                return values.Take((int)count).ToArray();
            }
        }
        return null;
    }

    private unsafe Dictionary<int, double>? ReadUsage(nint device, int index) {
        _activities.Clear();
        _sm.Clear();
        if (_api!.ProcessUtilization is not { } query) {
            return null;
        }
        uint count = 0;
        int status = query(device, null, ref count, _lastSeen[index]);
        if (status == 6) {
            return [];
        } // No new samples, not measured idle.
        if (status != 7 && status != 0 || count > 65536) {
            return null;
        }
        if (count == 0) {
            return [];
        }
        var values = new ProcessUsage[count];
        fixed (ProcessUsage* pointer = values) { status = query(device, pointer, ref count, _lastSeen[index]); }
        if (status != 0 || count > values.Length) {
            return null;
        }
        ulong previous = _lastSeen[index];
        var latest = new Dictionary<int, ProcessUsage>();
        foreach (var value in values.Take((int)count)) {
            _lastSeen[index] = Math.Max(_lastSeen[index], value.Timestamp);
            if (previous == 0 || value.Timestamp <= previous || value.Pid > int.MaxValue) {
                continue;
            }
            int pid = (int)value.Pid;
            if (!latest.TryGetValue(pid, out var old) || value.Timestamp > old.Timestamp) {
                latest[pid] = value;
            }
        }
        foreach (var entry in latest) {
            _usageTimestamps[(entry.Key, _identities[index] ?? "nvml-index:" + index)] = entry.Value.Timestamp;
            _activities[entry.Key] = new(Encode: entry.Value.Encoder <= 100 ? entry.Value.Encoder : null,
                Decode: entry.Value.Decoder <= 100 ? entry.Value.Decoder : null, Memory: entry.Value.Memory <= 100 ? entry.Value.Memory : null);
            if (entry.Value.Sm <= 100) {
                _sm[entry.Key] = entry.Value.Sm;
            }
        }
        return latest.Where(item => item.Value.Sm <= 100 && item.Value.Encoder <= 100 && item.Value.Decoder <= 100)
            .ToDictionary(item => item.Key, item => (double)Math.Max(item.Value.Sm, Math.Max(item.Value.Encoder, item.Value.Decoder)));
    }

    internal static bool CanAttributeProcesses(string release, string status, string cgroup, bool containerMarker, string? container) {
        if (containerMarker || !string.IsNullOrEmpty(container) || release.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0) {
            return false;
        }
        if (ContainerNames.Any(value => cgroup.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)) {
            return false;
        }
        string? namespaceIds = status.Split('\n').FirstOrDefault(line => line.StartsWith("NSpid:", StringComparison.Ordinal));
        return namespaceIds?.Split(NamespaceSeparators, StringSplitOptions.RemoveEmptyEntries).Length == 2;
    }

    private void SampleProcesses() {
        ProcessTelemetryAvailable = false;
        _processDetails.Clear();
        _processBirth.Clear();
        _usageTimestamps.Clear();
        // WSL NVML reports host identities; known containers have a different PID namespace.
        // Never attach a host PID to an unrelated process with the same local number.
        if (!Platform.IsLinux() || File.Exists("/.dockerenv") || File.Exists("/run/.containerenv")) {
            return;
        }
        try {
            if (!CanAttributeProcesses(File.ReadAllText("/proc/sys/kernel/osrelease"), File.ReadAllText("/proc/self/status"),
                File.ReadAllText("/proc/self/cgroup"), false, Environment.GetEnvironmentVariable("container"))) {
                return;
            }
        }
        catch (IOException) {
            return;
        }
        var snapshots = new List<ProcessSnapshot>();
        long started = DateTime.UtcNow.Ticks;
        var details = new Dictionary<int, List<ProcessGpuUsage>>();
        for (int index = 0; index < _count; index++) {
            var compute = ReadProcesses(_api!.ComputeProcesses, _devices[index]);
            var graphics = ReadProcesses(_api.GraphicsProcesses, _devices[index]);
            var usage = ReadUsage(_devices[index], index);
            ProcessTelemetryAvailable |= compute is not null || graphics is not null || usage is not null;
            snapshots.Add(new(compute, graphics, usage));
            foreach (var process in MergeProcesses(compute ?? [], graphics ?? [])) {
                if (!details.TryGetValue(process.Key, out var entries)) {
                    details[process.Key] = entries = [];
                }
                bool complete = compute is not null && graphics is not null;
                entries.Add(new(_identities[index] ?? "nvml-index:" + index, "NVIDIA NVML", usage is not null && usage.TryGetValue(process.Key, out double percent) ? percent : null,
                    complete ? process.Value : null, _activities.TryGetValue(process.Key, out var activity) ? activity : default,
                    !complete ? "Process list unavailable or denied; memory coverage incomplete" : !process.Value.HasValue ? "Memory unavailable or ambiguous GPU partitions"
                    : usage is null ? "Process utilization unsupported or unavailable" : "Measured process list; new or absent engine samples are unknown") {
                    DedicatedMemory = complete ? process.Value : null,
                    SmActivity = _sm.TryGetValue(process.Key, out double sm) ? sm : null,
                    MemoryKind = "NVML process framebuffer bytes"
                });
            }
        }
        foreach (var item in CombineSnapshots(snapshots)) {
            _processRows[item.Key] = item.Value;
        }
        bool completeCoverage = snapshots.All(snapshot => snapshot.Compute is not null && snapshot.Graphics is not null);
        foreach (var entry in details) {
            try {
                using var process = System.Diagnostics.Process.GetProcessById(entry.Key);
                long birth = process.StartTime.ToUniversalTime().Ticks;
                if (birth > started) {
                    continue;
                }
                _processBirth[entry.Key] = birth;
                _processDetails[entry.Key] = entry.Value.Select(detail => !completeCoverage ? detail with {
                    Memory = null,
                    DedicatedMemory = null,
                    Status = detail.Status + "; process memory coverage incomplete across adapters"
                } : detail)
                    .Select(detail => _usageTimestamps.TryGetValue((entry.Key, detail.AdapterId), out ulong timestamp)
                    && !SampleMatchesBirth(timestamp, birth) ? detail with {
                        Utilization = null,
                        Activity = default,
                        SmActivity = null,
                        Status = "Engine sample predates process identity; warming up"
                    } : detail).ToArray();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        }
    }
}
