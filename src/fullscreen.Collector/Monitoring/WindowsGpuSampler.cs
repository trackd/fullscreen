using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;

internal sealed partial class WindowsGpuSampler : IDisposable {
    private readonly SafeQuery? _query;
    private readonly bool _collectProcesses, _collectDevices;
    private readonly nint _engine, _memory, _adapterMemory, _sharedMemory, _adapterSharedMemory;
    private byte[] _buffer = new byte[256 * 1024];
    private readonly Dictionary<int, double> _processUse = [];
    private readonly Dictionary<int, ulong> _processMemory = [];
    private readonly Dictionary<int, ulong> _processDedicated = [], _processShared = [];
    private readonly Dictionary<ulong, double> _engines = [];
    private readonly Dictionary<string, Device> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Pid, string Adapter), Device> _perProcess = [];
    private readonly Dictionary<string, WindowsGpuAdapters.Adapter> _adapters;
    private sealed class Device {
        internal readonly Dictionary<string, double> Engines = [];
        internal ulong? Dedicated, Shared;
    }
    public double? Utilization { get; private set; }
    public ulong? UsedMemory { get; private set; }
    internal CollectorCapabilities Capabilities { get; private set; } =
        CollectorCapabilities.ProcessGraphics | CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory;
    internal static bool MissingCounter(int status) =>
        status is unchecked((int)0xC0000BB8) or unchecked((int)0xC0000BB9);

    public WindowsGpuSampler(bool processes = true, bool devices = true, bool native = true) {
        _collectProcesses = processes;
        _collectDevices = devices;
        _adapters = native ? WindowsGpuAdapters.Read() : [];
        if (!native) {
            return;
        }
        if (PdhOpenQueryW(null, 0, out var query) != 0) {
            query?.Dispose();
            return;
        }

        _query = query;
        int engineStatus = PdhAddEnglishCounterW(query, "\\GPU Engine(*)\\Utilization Percentage", 0, out _engine);
        if (engineStatus != 0) {
            _engine = 0;
            if (MissingCounter(engineStatus)) {
                Capabilities &= ~(CollectorCapabilities.ProcessGpu | CollectorCapabilities.GpuUtilization);
            }
        }

        if (processes) {
            if (PdhAddEnglishCounterW(query, "\\GPU Process Memory(*)\\Shared Usage", 0, out _sharedMemory) == 0) {
                Capabilities |= CollectorCapabilities.ProcessGpuMemory;
            }
            else {
                _sharedMemory = 0;
            }
            int memoryStatus = PdhAddEnglishCounterW(query, "\\GPU Process Memory(*)\\Dedicated Usage", 0, out _memory);
            if (memoryStatus != 0) {
                _memory = 0;
                if (MissingCounter(memoryStatus) && _sharedMemory == 0) {
                    Capabilities &= ~CollectorCapabilities.ProcessGpuMemory;
                }
            }
        }

        if (devices) {
            int adapterStatus = PdhAddEnglishCounterW(query, "\\GPU Adapter Memory(*)\\Dedicated Usage", 0, out _adapterMemory);
            if (adapterStatus != 0) {
                _adapterMemory = 0;
                if (MissingCounter(adapterStatus)) {
                    Capabilities &= ~CollectorCapabilities.GpuUsedMemory;
                }
            }
            if (PdhAddEnglishCounterW(query, "\\GPU Adapter Memory(*)\\Shared Usage", 0, out _adapterSharedMemory) == 0) {
                Capabilities |= CollectorCapabilities.GpuUsedMemory;
            }
            else {
                _adapterSharedMemory = 0;
            }
        }

        _ = PdhCollectQueryData(query);
    }

    public double? ProcessUtilization(int id) =>
        _processUse.TryGetValue(id, out double value) ? value : null;
    public ulong? ProcessMemory(int id) =>
        _processDedicated.TryGetValue(id, out ulong dedicated) && _processShared.TryGetValue(id, out ulong shared)
            ? dedicated + shared : null;
    internal ulong? ProcessDedicated(int id) => _processDedicated.TryGetValue(id, out ulong value) ? value : null;
    internal ulong? ProcessShared(int id) => _processShared.TryGetValue(id, out ulong value) ? value : null;
    public void Sample(bool processes = true) {
        _processUse.Clear();
        _processMemory.Clear();
        _processDedicated.Clear();
        _processShared.Clear();
        _engines.Clear();
        _devices.Clear();
        _perProcess.Clear();
        Utilization = null;
        UsedMemory = null;
        if (_query is null || PdhCollectQueryData(_query) != 0) {
            return;
        }

        Read(_engine, 0);
        if (processes) {
            Read(_memory, 1);
            Read(_sharedMemory, 4);
        }

        Read(_adapterMemory, 2);
        Read(_adapterSharedMemory, 3);
        foreach (double value in _engines.Values) {
            Utilization = Compat.Clamp(Math.Max(Utilization ?? 0, value), 0, 100);
        }
    }

    // Sum processes on the same engine, then select the busiest engine per adapter.
    internal void AddCounter(string name, double value, int kind) {
        if (!Compat.IsFinite(value) || value < 0) {
            return;
        }
        int start = name.IndexOf("luid_", StringComparison.Ordinal);
        int physical = name.IndexOf("_phys_", StringComparison.Ordinal);
        if (start < 0 || physical < start) {
            return;
        }
        int end = name.IndexOf('_', physical + 6);
        if (end < 0) { end = name.Length; }
        string id = name.Substring(start, end - start).ToLowerInvariant();
        int pidEnd = name.IndexOf('_', 4);
        if (_collectProcesses && name.StartsWith("pid_", StringComparison.Ordinal) && pidEnd > 4
            && int.TryParse(name.Substring(4, pidEnd - 4), out int pid)) {
            var key = (pid, id);
            if (!_perProcess.TryGetValue(key, out var process)) {
                _perProcess[key] = process = new();
            }
            if (kind == 1) {
                process.Dedicated = (process.Dedicated ?? 0) + (ulong)value;
            }
            else if (kind == 4) {
                process.Shared = (process.Shared ?? 0) + (ulong)value;
            }
            else if (kind == 0 && end < name.Length) {
                string instance = name.Substring(end);
                process.Engines.TryGetValue(instance, out double prior);
                process.Engines[instance] = prior + value;
            }
        }
        if (kind is 1 or 4) {
            return;
        }
        if (!_devices.TryGetValue(id, out var device)) {
            _devices[id] = device = new();
        }
        if (kind == 2) {
            device.Dedicated = (device.Dedicated ?? 0) + (ulong)value;
            return;
        }
        if (kind == 3) {
            device.Shared = (device.Shared ?? 0) + (ulong)value;
            return;
        }
        if (kind != 0 || end == name.Length) {
            return;
        }
        string engine = name.Substring(end);
        device.Engines.TryGetValue(engine, out double previous);
        device.Engines[engine] = previous + value;
    }

    internal ProcessGpuUsage[] ProcessDetails(int pid) => _perProcess.Where(item => item.Key.Pid == pid).Select(item => {
        var process = item.Value;
        var engines = new Dictionary<string, double>();
        foreach (var engine in process.Engines) {
            int index = engine.Key.IndexOf("_engtype_", StringComparison.Ordinal);
            if (index < 0) {
                continue;
            }
            string name = engine.Key.Substring(index + 9).ToLowerInvariant();
            string type = name switch {
                "3d" => "render",
                "videoprocessing" => "video-enhance",
                _ => name
            };
            engines[type] = Math.Max(engines.TryGetValue(type, out double previous) ? previous : 0, Compat.Clamp(engine.Value, 0, 100));
        }
        var activity = GpuActivity.FromEngines(engines.Where(engine => engine.Key is not ("videodecode" or "videoencode"))) with {
            Encode = engines.TryGetValue("videoencode", out double encode) ? encode : null,
            Decode = engines.TryGetValue("videodecode", out double decode) ? decode : null
        };
        return new ProcessGpuUsage(item.Key.Adapter, "Windows PDH", process.Engines.Count > 0 ? Compat.Clamp(process.Engines.Values.Max(), 0, 100) : null,
            process.Dedicated.HasValue && process.Shared.HasValue ? process.Dedicated + process.Shared : null, activity,
            process.Engines.Count > 0 ? "Measured engine activity; missing counters are unavailable" : "Engine activity warming up or unavailable") {
            DedicatedMemory = process.Dedicated,
            SharedMemory = process.Shared,
            MemoryKind = "dedicated + shared allocation bytes"
        };
    }).ToArray();

    internal void AppendDevices(CollectorFrame frame) {
        foreach (string id in _adapters.Keys.Concat(_devices.Keys).Distinct(StringComparer.OrdinalIgnoreCase)) {
            _adapters.TryGetValue(id, out var adapter);
            if (adapter.Name is not null && !adapter.DisplayOnly) {
                Capabilities |= CollectorCapabilities.GpuTotalMemory;
            }
            _devices.TryGetValue(id, out var device);
            var engines = new Dictionary<string, double>();
            double? busiest = null;
            if (device is not null) {
                foreach (var engine in device.Engines) {
                    double percent = Compat.Clamp(engine.Value, 0, 100);
                    busiest = Math.Max(busiest ?? 0, percent);
                    int type = engine.Key.IndexOf("_engtype_", StringComparison.Ordinal);
                    if (type < 0) {
                        continue;
                    }
                    string name = engine.Key.Substring(type + 9).ToLowerInvariant();
                    string normalized = name switch {
                        "3d" => "render",
                        "videodecode" => "decode-explicit",
                        "videoencode" => "encode-explicit",
                        "videoprocessing" => "video-enhance",
                        var n when n.StartsWith("compute", StringComparison.Ordinal) => "compute",
                        _ => name
                    };
                    engines[normalized] = Math.Max(engines.TryGetValue(normalized, out double prior) ? prior : 0, percent);
                }
            }
            var activity = GpuActivity.FromEngines(engines.Where(item => item.Key is not ("encode-explicit" or "decode-explicit"))) with {
                Encode = engines.TryGetValue("encode-explicit", out double enc) ? enc : null,
                Decode = engines.TryGetValue("decode-explicit", out double dec) ? dec : null
            };
            Capabilities |= engines.Keys.Where(engine => engine is not ("encode-explicit" or "decode-explicit"))
                .Aggregate(CollectorCapabilities.None, (caps, engine) => caps | GpuActivity.EngineCapability(engine));
            if (activity.Encode.HasValue) { Capabilities |= CollectorCapabilities.GpuEncode; }
            if (activity.Decode.HasValue) { Capabilities |= CollectorCapabilities.GpuDecode; }
            frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
            frame.Gpus[frame.GpuCount++] = new(adapter.Name ?? "Graphics adapter", "Windows PDH", busiest,
                device?.Dedicated.HasValue == true && device.Shared.HasValue ? device.Dedicated + device.Shared : null,
                adapter.Name is not null && !adapter.DisplayOnly ? adapter.Dedicated : null, null) {
                Id = id,
                Activity = activity,
                DedicatedMemory = device?.Dedicated,
                SharedMemory = device?.Shared,
                MemoryKind = adapter.DisplayOnly ? null : "dedicated + shared usage",
                Status = busiest.HasValue ? "Measured WDDM engines; sensors not reported by WDDM"
                    : adapter.DisplayOnly || WindowsGpuAdapters.IsHyperVDisplay(adapter.Name ?? "")
                        ? "Hyper-V synthetic display; guest acceleration counters are not exposed"
                        : "WDDM engine activity warming up or unavailable"
            };
        }
    }

    private unsafe void Read(nint counter, int kind) {
        if (counter == 0) {
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++) {
            uint size = (uint)_buffer.Length;
            fixed (byte* pointer = _buffer) {
                int status = PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, out uint count, pointer);
                if (status == unchecked((int)0x800007D2)) {
                    Array.Resize(ref _buffer, Math.Max(checked((int)size + 4096), _buffer.Length * 2));
                    continue;
                }

                if (status != 0 || count > _buffer.Length / sizeof(Item)) {
                    return;
                }

                for (uint i = 0; i < count; i++) {
                    Item item = ((Item*)pointer)[i];
                    if (item.Value.Status > 1 || !Compat.IsFinite(item.Value.Number) || item.Value.Number < 0) {
                        continue;
                    }

                    nuint offset = (nuint)item.Name - (nuint)pointer;
                    if (offset >= (nuint)_buffer.Length) {
                        continue;
                    }

                    char* name = (char*)item.Name;
                    int capacity = (_buffer.Length - (int)offset) / 2, end = 0;
                    while (end < capacity && name[end] != '\0') {
                        end++;
                    }

                    if (end == capacity) {
                        continue;
                    }

                    AddCounter(new string(name, 0, end), item.Value.Number, kind);
                    if (kind is 2 or 3) {
                        UsedMemory = (UsedMemory ?? 0) + (ulong)item.Value.Number;
                        continue;
                    }

                    if (!TryGetPid(name, end, out int pid)) {
                        continue;
                    }

                    if (kind is 1 or 4) {
                        _processMemory.TryGetValue(pid, out ulong old);
                        _processMemory[pid] = old + (ulong)item.Value.Number;
                        var split = kind == 1 ? _processDedicated : _processShared;
                        split.TryGetValue(pid, out ulong bytes);
                        split[pid] = bytes + (ulong)item.Value.Number;
                    }
                    else {
                        if (_collectProcesses) {
                            _processUse.TryGetValue(pid, out double old);
                            _processUse[pid] = Compat.Clamp(Math.Max(old, item.Value.Number), 0, 100);
                        }

                        if (!_collectDevices) {
                            continue;
                        }

                        int keyStart = -1;
                        for (int n = 0; n <= end - 6; n++) {
                            if (name[n] == '_' && name[n + 1] == 'l' && name[n + 2] == 'u' && name[n + 3] == 'i'
                                && name[n + 4] == 'd'
                                && name[n + 5] == '_') {
                                keyStart = n;
                                break;
                            }
                        }

                        if (keyStart < 0) {
                            continue;
                        }

                        ulong key = 14695981039346656037;
                        for (int n = keyStart; n < end; n++) {
                            key = (key ^ name[n]) * 1099511628211;
                        }

                        _engines.TryGetValue(key, out double usage);
                        _engines[key] = usage + item.Value.Number;
                    }
                }

                return;
            }
        }
    }

    internal static unsafe bool TryGetPid(char* name, int length, out int pid) {
        pid = 0;
        if (length < 6 || name[0] != 'p' || name[1] != 'i' || name[2] != 'd' || name[3] != '_') {
            return false;
        }

        int i = 4;
        for (; i < length && name[i] != '_'; i++) {
            int digit = name[i] - '0';
            if (digit < 0 || digit > 9 || pid > (int.MaxValue - digit) / 10) {
                return false;
            }

            pid = pid * 10 + digit;
        }

        return i > 4 && i < length;
    }

    public void Dispose() =>
        _query?.Dispose();
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Value {
        [FieldOffset(0)]
        public uint Status;
        [FieldOffset(8)]
        public double Number;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Item {
        public nint Name;
        public Value Value;
    }

    private sealed class SafeQuery : SafeHandleZeroOrMinusOneIsInvalid {
        public SafeQuery() : base(true) {
        }

        protected override bool ReleaseHandle() =>
            PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? source, nuint userData, out SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(SafeQuery query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(SafeQuery query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static unsafe extern int PdhGetFormattedCounterArrayW(
        nint counter,
        uint format,
        ref uint size,
        out uint count,
        void* buffer
    );
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(nint query);
}
