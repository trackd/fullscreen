using System.Globalization;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

// i915's exported perf ABI: discover configurations instead of assuming engine counts or PMU types.
internal sealed partial class LinuxIntelPmuSampler(string root = "/sys/bus/event_source/devices",
    Func<uint, ulong, int, LinuxIntelPmuSampler.Counter>? open = null,
    Func<string, LinuxIntelPmuSampler.XeEngine[]>? xeEngines = null) : IDisposable {
    internal readonly record struct XeEngine(ushort Class, ushort Instance, ushort Gt);
    internal abstract class Counter : IDisposable {
        internal abstract Reading Read();
        public abstract void Dispose();
    }
    internal readonly record struct Reading(ulong Busy, ulong Enabled, ulong Running);
    internal readonly record struct Result(double? Utilization, bool Available, string Status,
        GpuActivity Activity = default, CollectorCapabilities EngineCapabilities = CollectorCapabilities.None);
    private sealed class Engine(string name, Counter counter) {
        internal readonly string Name = name;
        internal readonly Counter Counter = counter;
        internal Reading? Previous;
        internal Counter? Total;
        internal Reading? PreviousTotal;
    }
    private sealed class Device {
        internal readonly List<Engine> Engines = [];
        internal string Failure = "";
    }
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);

    internal Result Sample(string pciAddress, string driver) {
        if (driver is not ("i915" or "xe")) {
            return new(null, false, "device counters unsupported");
        }
        string deviceKey = driver + ":" + pciAddress;
        if (!_devices.TryGetValue(deviceKey, out var device)) {
            device = new();
            _devices.Add(deviceKey, device);
            try {
                string path = Path.Combine(root, driver + "_" + pciAddress.Replace(':', '_'));
                if (!Directory.Exists(path) && driver == "i915" && pciAddress == "0000:00:02.0") {
                    path = Path.Combine(root, "i915");
                }
                uint type = uint.Parse(File.ReadAllText(Path.Combine(path, "type")).Trim(), CultureInfo.InvariantCulture);
                string mask = driver == "xe" && !File.Exists(Path.Combine(path, "cpumask")) ? "0" : File.ReadAllText(Path.Combine(path, "cpumask")).Trim();
                int cpu = int.Parse(mask.Split(',', '-')[0], CultureInfo.InvariantCulture);
                if (driver == "xe") {
                    InitializeXe(device, path, pciAddress, type, cpu);
                }
                foreach (string file in driver == "i915" ? Directory.EnumerateFiles(Path.Combine(path, "events"), "*-busy") : []) {
                    string value = File.ReadAllText(file).Trim();
                    if (!value.StartsWith("config=0x", StringComparison.Ordinal)
                        || !ulong.TryParse(value[9..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong config)) {
                        throw new FormatException("Unsupported PMU event format.");
                    }
                    device.Engines.Add(new(Path.GetFileName(file)[..^5], (open ?? NativeCounter.Open)(type, config, cpu)));
                }
                if (device.Engines.Count == 0) {
                    device.Failure = "PMU engine counters missing";
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or OverflowException or PlatformNotSupportedException) {
                foreach (var engine in device.Engines) {
                    engine.Counter.Dispose();
                    engine.Total?.Dispose();
                }
                device.Engines.Clear();
                device.Failure = error is UnauthorizedAccessException ? "PMU access denied (root or CAP_PERFMON required)"
                    : error is PlatformNotSupportedException ? "PMU unsupported on this architecture" : "PMU engine counters unavailable";
            }
        }
        if (device.Engines.Count == 0) {
            return new(null, false, device.Failure);
        }
        double? busiest = null;
        bool failed = false;
        var rates = new Dictionary<string, double>(StringComparer.Ordinal);
        var capabilities = CollectorCapabilities.None;
        foreach (var engine in device.Engines) {
            capabilities |= GpuActivity.EngineCapability(engine.Name);
            try {
                var current = engine.Counter.Read();
                var total = engine.Total?.Read();
                if (engine.Previous is { } previous && current.Busy >= previous.Busy
                    && current.Enabled > previous.Enabled && current.Running > previous.Running) {
                    // Scale for multiplexing: busy * enabled/running divided by elapsed enabled time.
                    double? value = engine.Total is null ? 100d * (current.Busy - previous.Busy) / (current.Running - previous.Running)
                        : total is { } ticks && engine.PreviousTotal is { } oldTicks && ticks.Busy > oldTicks.Busy
                            && ticks.Enabled > oldTicks.Enabled && ticks.Running > oldTicks.Running
                            ? 100d * (current.Busy - previous.Busy) * (current.Enabled - previous.Enabled) / (current.Running - previous.Running)
                                / ((double)(ticks.Busy - oldTicks.Busy) * (ticks.Enabled - oldTicks.Enabled) / (ticks.Running - oldTicks.Running)) : null;
                    if (value.HasValue) {
                        value = Compat.Clamp(value.Value, 0, 100);
                        busiest = Math.Max(busiest ?? 0, value.Value);
                        rates[engine.Name] = value.Value;
                    }
                }
                engine.Previous = current;
                engine.PreviousTotal = total;
            }
            catch (IOException) {
                engine.Previous = null;
                engine.PreviousTotal = null;
                failed = true;
            }
        }
        return new(failed ? null : busiest, true, failed ? "PMU read failed" : busiest.HasValue ? "PMU busiest engine" : "PMU warming up or not scheduled",
            failed ? default : GpuActivity.FromEngines(rates), capabilities);
    }

    public void Dispose() {
        foreach (var device in _devices.Values) {
            foreach (var engine in device.Engines) {
                engine.Counter.Dispose();
                engine.Total?.Dispose();
            }
        }
        _devices.Clear();
    }

    private sealed class NativeCounter : Counter {
        private readonly PerfHandle _handle;
        private NativeCounter(int fd) => _handle = new(fd);

        internal static NativeCounter Open(uint type, ulong config, int cpu) {
            if (!Platform.IsLinux()) {
                throw new PlatformNotSupportedException();
            }
            nint number = RuntimeInformation.ProcessArchitecture switch {
                Architecture.X64 => 298,
                Architecture.Arm64 => 241,
                _ => throw new PlatformNotSupportedException()
            };
            var attributes = new Attributes {
                Type = type,
                Size = 64,
                Config = config,
                ReadFormat = 3
            };
            nint fd = Syscall(number, ref attributes, -1, cpu, -1, 8); // PERF_FLAG_FD_CLOEXEC
            if (fd == -1) {
                int error = Marshal.GetLastWin32Error();
                if (error is 1 or 13) {
                    throw new UnauthorizedAccessException();
                }
                throw new IOException($"perf_event_open failed (errno {error}).");
            }
            return new NativeCounter((int)fd);
        }

        internal override Reading Read() {
            nint count;
            NativeReading value;
            do {
                count = ReadNative(_handle, out value, 24);
            }
            while (count == -1 && Marshal.GetLastWin32Error() == 4);
            if (count != 24) {
                throw new IOException("Incomplete perf counter read.");
            }
            return new(value.Busy, value.Enabled, value.Running);
        }
        public override void Dispose() => _handle.Dispose();

        // PERF_ATTR_SIZE_VER0; fixed-width Linux UAPI fields, not C long.
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct Attributes {
            internal uint Type, Size;
            internal ulong Config, SamplePeriod, SampleType, ReadFormat, Flags;
            internal uint Wakeup, BreakpointType;
            internal ulong Config1;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeReading { internal ulong Busy, Enabled, Running; }

        private sealed class PerfHandle : SafeHandle {
            internal PerfHandle(int fd) : base((nint)(-1), true) => SetHandle((nint)fd);
            public override bool IsInvalid => handle == (nint)(-1); // fd 0 is valid.
            protected override bool ReleaseHandle() => CloseNative(handle.ToInt32()) == 0;
        }
        // syscall is variadic: all scalar arguments use native register width on supported 64-bit ABIs.
        [DllImport("libc", EntryPoint = "syscall", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern nint Syscall(nint number, ref Attributes attributes, nint pid, nint cpu, nint group, nuint flags);
        [DllImport("libc", EntryPoint = "read", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern nint ReadNative(PerfHandle fd, out NativeReading value, nuint size);
        [DllImport("libc", EntryPoint = "close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int CloseNative(int fd);
    }
}
