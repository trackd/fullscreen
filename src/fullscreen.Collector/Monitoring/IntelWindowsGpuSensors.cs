using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

// Minimal read-only IGCL binding. ControlLib.dll ships with supported Intel graphics
// drivers; load only from System32, never a bundled wrapper or working-directory DLL.
internal sealed class IntelWindowsGpuSensors : IDisposable {
    private nint _library, _api;
    private Close? _close;
    private State? _state;
    private readonly Dictionary<string, (nint Handle, int Type)[]> _sensors = new(StringComparer.OrdinalIgnoreCase);
    internal unsafe IntelWindowsGpuSensors() {
        if (!Platform.IsWindows() || IntPtr.Size != 8) {
            return;
        }
        try {
            var adapters = WindowsGpuAdapters.Read().Values.Where(adapter => adapter.Vendor == 0x8086 && adapter.Pci is not null).ToArray();
            if (adapters.Length == 0) {
                return;
            }
            _library = LoadLibraryExW(Path.Combine(Environment.SystemDirectory, "ControlLib.dll"), 0, 0x1100);
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
            _close = Resolve<Close>("ctlClose");
            _state = Resolve<State>("ctlTemperatureGetState");
            var args = new InitArgs { Size = (uint)sizeof(InitArgs), AppVersion = 0x10001, Flags = 1 }; // USE_LEVEL_ZERO, no firmware/tuning flags.
            if (Resolve<Init>("ctlInit")(ref args, out _api) != 0 || _api == 0) {
                Dispose();
                return;
            }
            var enumerate = Resolve<Enumerate>("ctlEnumerateDevices");
            var temperatures = Resolve<Enumerate>("ctlEnumTemperatureSensors");
            var pci = Resolve<Pci>("ctlPciGetProperties");
            var properties = Resolve<Properties>("ctlTemperatureGetProperties");
            foreach (nint device in Handles(enumerate, _api)) {
                var location = new PciProperties {
                    Size = (uint)sizeof(PciProperties),
                    Address = new() { Size = (uint)sizeof(PciAddress) },
                    Speed = new() { Size = (uint)sizeof(PciSpeed) }
                };
                if (pci(device, ref location) != 0) {
                    continue;
                }
                string address = $"{location.Address.Domain:x8}:{location.Address.Bus:x2}:{location.Address.Device:x2}.{location.Address.Function:x}";
                var matches = adapters.Where(adapter => string.Equals(adapter.Pci, address, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) {
                    continue;
                }
                var sensors = new List<(nint, int)>();
                foreach (nint sensor in Handles(temperatures, device)) {
                    var info = new TemperatureProperties { Size = (uint)sizeof(TemperatureProperties) };
                    if (properties(sensor, ref info) == 0 && info.Type is 0 or 1) {
                        sensors.Add((sensor, info.Type));
                    }
                }
                _sensors[matches[0].Id] = sensors.ToArray();
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) {
            Dispose();
        }
    }
    private static unsafe nint[] Handles(Enumerate enumerate, nint parent) {
        uint count = 0;
        if (enumerate(parent, ref count, null) != 0 || count == 0 || count > 256) {
            return [];
        }
        var handles = new nint[count];
        uint capacity = count;
        fixed (nint* pointer = handles) {
            if (enumerate(parent, ref count, pointer) != 0 || count > capacity) {
                return [];
            }
        }
        return handles.Take((int)count).Where(handle => handle != 0).ToArray();
    }
    internal CollectorCapabilities Sample(CollectorFrame frame) {
        if (_api == 0 || _state is null) { return CollectorCapabilities.None; }
        var capabilities = CollectorCapabilities.None;
        for (int index = 0; index < frame.GpuCount; index++) {
            var row = frame.Gpus[index];
            if (row.Id is null || !_sensors.TryGetValue(row.Id, out var sensors)) {
                continue;
            }
            var readings = sensors.Select(sensor => (sensor.Type, Value: _state(sensor.Handle, out double value) == 0 ? (double?)value : null)).ToArray();
            if (Temperature(readings) is { } temperature) {
                frame.Gpus[index] = row with { Temperature = temperature, Provider = row.Provider + " + Intel IGCL" };
                capabilities |= CollectorCapabilities.GpuTemperature;
            }
        }
        return capabilities;
    }
    internal static double? Temperature(IEnumerable<(int Type, double? Value)> readings) => readings
        .Where(reading => reading.Type is 0 or 1 && reading.Value is >= -20 and <= 150)
        .OrderByDescending(reading => reading.Type).ThenByDescending(reading => reading.Value)
        .Select(reading => reading.Value).FirstOrDefault();
    public void Dispose() {
        _sensors.Clear();
        if (_api != 0) {
            _ = _close?.Invoke(_api);
            _api = 0;
        }
        if (_library != 0) {
            _ = FreeLibrary(_library);
            _library = 0;
        }
    }
    // Layouts from Intel's public igcl_api.h, API 1.1. C bools are single bytes.
    [StructLayout(LayoutKind.Sequential)]
    internal struct InitArgs { internal uint Size; internal byte Version; internal uint AppVersion, Flags, SupportedVersion; internal Guid ApplicationUid; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PciAddress { internal uint Size; internal byte Version; internal uint Domain, Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PciSpeed { internal uint Size; internal byte Version; internal int Generation, Width; internal long Bandwidth; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PciProperties { internal uint Size; internal byte Version; internal PciAddress Address; internal PciSpeed Speed; internal byte BarSupported, BarEnabled; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct TemperatureProperties { internal uint Size; internal byte Version; internal int Type; internal double Maximum; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Init(ref InitArgs args, out nint api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Close(nint api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int Enumerate(nint parent, ref uint count, nint* handles);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Pci(nint device, ref PciProperties properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Properties(nint sensor, ref TemperatureProperties properties);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int State(nint sensor, out double value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint LoadLibraryExW(string path, nint file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
