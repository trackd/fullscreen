using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

// Optional read-only telemetry from the AMD driver's system-installed ADL library.
// No tuning API or polling/logging session is started. Unsupported PMLog remains unknown.
internal sealed class AmdWindowsGpuSampler : IDisposable {
    internal readonly record struct Sensor(int Supported, int Value);
    private nint _library;
    private nint _context;
    private readonly Destroy? _destroy;
    private readonly Metrics? _metrics;
    private readonly Temperature? _temperature;
    private readonly Allocate _allocate = size => size > 0 ? Malloc((nuint)size) : 0;
    private readonly Dictionary<string, int> _indices = new(StringComparer.OrdinalIgnoreCase);
    internal unsafe AmdWindowsGpuSampler() {
        if (!Platform.IsWindows()) {
            return;
        }
        try {
            var adapters = WindowsGpuAdapters.Read().Values.Where(adapter => adapter.Vendor == 0x1002 && adapter.Pci is not null).ToArray();
            if (adapters.Length == 0) {
                return;
            }
            _library = LoadLibraryExW(Path.Combine(Environment.SystemDirectory, IntPtr.Size == 8 ? "atiadlxx.dll" : "atiadlxy.dll"), 0, 0x1100);
            if (_library == 0) {
                return;
            }
            T Resolve<T>(string name) where T : Delegate {
                nint pointer = GetProcAddress(_library, name);
                if (pointer == 0) {
                    throw new EntryPointNotFoundException(name);
                }
                return Marshal.GetDelegateForFunctionPointer<T>(pointer);
            }
            var create = Resolve<Create>("ADL2_Main_Control_Create");
            _destroy = Resolve<Destroy>("ADL2_Main_Control_Destroy");
            nint metrics = GetProcAddress(_library, "ADL2_New_QueryPMLogData_Get");
            if (metrics != 0) {
                _metrics = Marshal.GetDelegateForFunctionPointer<Metrics>(metrics);
            }
            nint temperature = GetProcAddress(_library, "ADL2_Overdrive6_Temperature_Get");
            if (temperature != 0) {
                _temperature = Marshal.GetDelegateForFunctionPointer<Temperature>(temperature);
            }
            if (create(_allocate, 1, out _context) != 0 || _context == 0) {
                return;
            }
            if (Resolve<Count>("ADL2_Adapter_NumberOfAdapters_Get")(_context, out int count) != 0 || count <= 0 || count > 256) {
                return;
            }
            var info = new AdapterInfo[count];
            for (int index = 0; index < count; index++) { info[index].Size = sizeof(AdapterInfo); }
            fixed (AdapterInfo* pointer = info) {
                if (Resolve<Adapters>("ADL2_Adapter_AdapterInfo_Get")(_context, pointer, sizeof(AdapterInfo) * count) != 0) {
                    return;
                }
            }
            foreach (var adapter in adapters) {
                if (adapters.Count(other => string.Equals(other.Pci, adapter.Pci, StringComparison.OrdinalIgnoreCase)) != 1) {
                    continue;
                }
                var matches = info.Where(entry => entry.Present != 0 && entry.Bus >= 0 && entry.Device >= 0 && entry.Function >= 0
                    && string.Equals($"00000000:{entry.Bus:x2}:{entry.Device:x2}.{entry.Function:x}", adapter.Pci, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 0) {
                    _indices[adapter.Id] = matches.Min(entry => entry.Index);
                }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { Dispose(); }
    }
    internal unsafe CollectorCapabilities Sample(CollectorFrame frame) {
        var capabilities = CollectorCapabilities.None;
        if (_context == 0) {
            return capabilities;
        }
        for (int index = 0; index < frame.GpuCount; index++) {
            var row = frame.Gpus[index];
            if (row.Id is null || !_indices.TryGetValue(row.Id, out int adapter)) {
                continue;
            }
            var output = new Output { Size = sizeof(Output) };
            int status = _metrics?.Invoke(_context, adapter, ref output) ?? -1;
            var sensors = new Sensor[256];
            if (status == 0) {
                for (int sensor = 0; sensor < sensors.Length; sensor++) {
                    sensors[sensor] = new(output.Sensors[sensor * 2], output.Sensors[sensor * 2 + 1]);
                }
            }
            frame.Gpus[index] = Apply(row, sensors, status == 0);
            if (!frame.Gpus[index].Temperature.HasValue && _temperature is not null) {
                int result = _temperature(_context, adapter, out int millidegrees);
                frame.Gpus[index] = ApplyTemperature(frame.Gpus[index], result, millidegrees);
            }
            if (frame.Gpus[index].ClockMHz.HasValue) {
                capabilities |= CollectorCapabilities.GpuClock;
            }
            if (frame.Gpus[index].PowerWatts.HasValue) {
                capabilities |= CollectorCapabilities.GpuPower;
            }
            if (frame.Gpus[index].Temperature.HasValue) {
                capabilities |= CollectorCapabilities.GpuTemperature;
            }
            if (frame.Gpus[index].Activity.Memory.HasValue) {
                capabilities |= CollectorCapabilities.GpuMemoryActivity;
            }
        }
        return capabilities;
    }
    internal static GpuRow ApplyTemperature(GpuRow row, int status, int millidegrees) => status == 0 && millidegrees is >= -20000 and <= 150000
        ? row with { Temperature = millidegrees / 1000d, Provider = row.Provider.Contains("AMD ADL") ? row.Provider : row.Provider + " + AMD ADL" }
        : row;
    internal static GpuRow Apply(GpuRow row, IReadOnlyList<Sensor> sensors, bool success) {
        if (!success) {
            return row with { Status = row.Status + "; AMD sensor query unavailable or unsupported" };
        }
        double? Value(int index) => index < sensors.Count && sensors[index].Supported != 0 && sensors[index].Value >= 0 ? sensors[index].Value : null;
        double? Percent(int index) => Value(index) is { } value && value <= 100 ? value : null;
        double? board = Value(73), asic = Value(23);
        return row with {
            Provider = row.Provider + " + AMD ADL",
            Utilization = Percent(19) ?? row.Utilization,
            ClockMHz = Value(1),
            Temperature = Value(8) is <= 150 ? Value(8) : row.Temperature,
            PowerWatts = board ?? asic,
            PowerScope = board.HasValue ? "board" : asic.HasValue ? "ASIC" : null,
            Activity = row.Activity with { Memory = Percent(20) },
            Status = row.Status + "; optional AMD sensors: unsupported readings stay unknown"
        };
    }
    public void Dispose() {
        if (_context != 0) {
            _ = _destroy?.Invoke(_context);
            _context = 0;
        }
        if (_library != 0) {
            _ = FreeLibrary(_library);
            _library = 0;
        }
        _indices.Clear();
        GC.KeepAlive(_allocate);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AdapterInfo {
        internal int Size, Index;
        internal fixed byte Udid[256];
        internal int Bus, Device, Function, Vendor;
        internal fixed byte Name[256];
        internal fixed byte Display[256];
        internal int Present, Exists;
        internal fixed byte Driver[256];
        internal fixed byte DriverExtended[256];
        internal fixed byte Pnp[256];
        internal int OsIndex;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Output {
        internal int Size;
        internal fixed int Sensors[512];
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Allocate(int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Create(Allocate allocate, int present, out nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Destroy(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Count(nint context, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int Adapters(nint context, AdapterInfo* adapters, int bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Metrics(nint context, int adapter, ref Output output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Temperature(nint context, int adapter, out int millidegrees);
    [DllImport("msvcrt.dll", EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Malloc(nuint bytes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint LoadLibraryExW(string path, nint file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
