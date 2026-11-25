using System.Runtime.InteropServices;
using System.Text;

namespace fullscreen.Monitoring;
/// <summary>Optional NVIDIA driver API. Cached entry points keep their library loaded for process lifetime.</summary>
internal sealed partial class NvmlSampler : IDisposable {
    internal static string Availability(int status) => status switch {
        0 => "measured",
        3 => "unsupported",
        4 => "permission denied",
        6 => "not found",
        _ => "temporarily unavailable (NVML " + status + ")"
    };
    private static readonly Lazy<DriverApi?> Driver = new(DriverApi.Load);
    private readonly DriverApi? _api;
    private readonly nint[] _devices = new nint[32];
    private readonly string[] _names = new string[32];
    private readonly string?[] _identities = new string?[32];
    private bool _initialized;
    private readonly int _count;
    internal CollectorCapabilities Capabilities { get; private set; }
    internal unsafe NvmlSampler() {
        _api = Driver.Value;
        if (_api is null) {
            return;
        }
        Capabilities = CollectorCapabilities.DeviceGraphics;
        if (_api.Init() != 0) {
            return;
        }

        _initialized = true;
        if (_api.Count(out uint count) != 0) {
            return;
        }
        if (count == 0) {
            Capabilities = CollectorCapabilities.None;
        }

        byte[] name = new byte[128];
        for (uint index = 0; index < Math.Min(count, 32); index++) {
            if (_api.Device(index, out nint device) != 0) {
                continue;
            }

            Array.Clear(name, 0, name.Length);
            fixed (byte* pointer = name) {
                _api.Name(device, pointer, (uint)name.Length);
            }

            int end = Array.IndexOf(name, (byte)0);
            _names[_count] = Encoding.UTF8.GetString(name, 0, end < 0 ? name.Length : end);
            _identities[_count] = DeviceIdentity(device);
            _devices[_count++] = device;
        }
    }

    internal void Sample(CollectorFrame frame, bool devices = true, bool processes = false) {
        if (!_initialized) {
            return;
        }

        _processRows.Clear();
        if (processes) {
            SampleProcesses();
        }
        if (!devices) {
            return;
        }
        if (_count > 0) {
            Capabilities = CollectorCapabilities.None;
        }
        for (int i = 0; i < _count; i++) {
            nint device = _devices[i];
            int utilizationStatus = _api!.Utilization(device, out Utilization value);
            int memoryStatus = _api.Memory(device, out Memory memory);
            int temperatureStatus = _api.Temperature(device, 0, out uint temp);
            // NVML_ERROR_NOT_SUPPORTED is structural; permissions and temporary errors are not.
            if (utilizationStatus != 3) {
                Capabilities |= CollectorCapabilities.GpuUtilization;
            }
            if (memoryStatus != 3) {
                Capabilities |= CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuTotalMemory;
            }
            if (temperatureStatus != 3) {
                Capabilities |= CollectorCapabilities.GpuTemperature;
            }
            double? busy = utilizationStatus == 0 ? value.Gpu : null;
            bool hasMemory = memoryStatus == 0;
            double? temperature = temperatureStatus == 0 ? temp : null;
            double? encode = null, decode = null, clock = null, power = null, memoryClock = null, pcieRx = null, pcieTx = null;
            int? pstate = null;
            int encoderStatus = 3, decoderStatus = 3, clockStatus = 3, powerStatus = 3;
            if (_api.Encoder is { } encoder) {
                int status = encoderStatus = encoder(device, out uint percent, out _);
                if (status != 3) {
                    Capabilities |= CollectorCapabilities.GpuEncode;
                }
                if (status == 0) {
                    encode = percent;
                }
            }
            if (_api.Decoder is { } decoder) {
                int status = decoderStatus = decoder(device, out uint percent, out _);
                if (status != 3) {
                    Capabilities |= CollectorCapabilities.GpuDecode;
                }
                if (status == 0) {
                    decode = percent;
                }
            }
            if (_api.Clock is { } clockInfo) {
                int status = clockStatus = clockInfo(device, 0, out uint mhz);
                if (status != 3) {
                    Capabilities |= CollectorCapabilities.GpuClock;
                }
                if (status == 0) {
                    clock = mhz;
                }
                if (clockInfo(device, 2, out uint memoryMhz) == 0) {
                    memoryClock = memoryMhz;
                }
            }
            if (_api.Pcie is { } pcie) {
                if (pcie(device, 0, out uint tx) == 0) {
                    pcieTx = tx * 1024d;
                }
                if (pcie(device, 1, out uint rx) == 0) {
                    pcieRx = rx * 1024d;
                }
            }
            if (_api.State is { } state && state(device, out uint stateValue) == 0 && stateValue <= 15) {
                pstate = (int)stateValue;
            }
            if (_api.Power is { } powerInfo) {
                int status = powerStatus = powerInfo(device, out uint milliwatts);
                if (status != 3) {
                    Capabilities |= CollectorCapabilities.GpuPower;
                }
                if (status == 0) {
                    power = milliwatts / 1000d;
                }
            }
            if (utilizationStatus != 3) {
                Capabilities |= CollectorCapabilities.GpuMemoryActivity;
            }
            frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
            frame.Gpus[frame.GpuCount++] = new(
                _names[i],
                "NVIDIA NVML",
                busy,
                hasMemory ? memory.Used : null,
                hasMemory ? memory.Total : null,
                temperature
            ) {
                Id = _identities[i],
                Status = "NVML usage: " + Availability(utilizationStatus) + "; memory: " + Availability(memoryStatus)
                    + "; temperature: " + Availability(temperatureStatus)
                    + "; encode: " + Availability(encoderStatus) + "; decode: " + Availability(decoderStatus)
                    + "; clock: " + Availability(clockStatus) + "; power: " + Availability(powerStatus),
                DedicatedMemory = hasMemory ? memory.Used : null,
                MemoryKind = "dedicated framebuffer usage",
                Activity = new(Encode: encode, Decode: decode, Memory: utilizationStatus == 0 ? value.Memory : null),
                ClockMHz = clock,
                MemoryClockMHz = memoryClock,
                PcieReceiveBytes = pcieRx,
                PcieSendBytes = pcieTx,
                PerformanceState = pstate,
                PowerWatts = power,
                PowerScope = power.HasValue ? "GPU" : null
            };
        }
    }

    public void Dispose() {
        if (_initialized) {
            _api!.Shutdown();
            _initialized = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization {
        public uint Gpu, Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory {
        public ulong Total, Free, Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitFunction();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountFunction(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceFunction(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int NameFunction(nint device, byte* buffer, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UtilizationFunction(nint device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MemoryFunction(nint device, out Memory value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TemperatureFunction(nint device, uint sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EngineFunction(nint device, out uint percent, out uint samplingPeriod);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PowerFunction(nint device, out uint milliwatts);
    private sealed class DriverApi {
        internal readonly InitFunction Init, Shutdown;
        internal readonly CountFunction Count;
        internal readonly DeviceFunction Device;
        internal readonly NameFunction Name;
        internal readonly UtilizationFunction Utilization;
        internal readonly MemoryFunction Memory;
        internal readonly TemperatureFunction Temperature;
        internal readonly EngineFunction? Encoder, Decoder;
        internal readonly TemperatureFunction? Clock;
        internal readonly TemperatureFunction? Pcie;
        internal readonly PowerFunction? State;
        internal readonly PowerFunction? Power;
        internal readonly ProcessListFunction? ComputeProcesses, GraphicsProcesses;
        internal readonly ProcessUtilizationFunction? ProcessUtilization;
        internal readonly PciFunction? Pci;
        internal readonly NameFunction? Uuid;
        private DriverApi(nint handle, bool libc) {
            T Resolve<T>(string name)
                where T : Delegate {
                nint address = Platform.IsWindows() ? GetProcAddress(handle, name) : libc ? LibcSymbol(handle, name) : DlSymbol(handle, name);
                if (address == 0) {
                    throw new EntryPointNotFoundException(name);
                }

                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            Init = Resolve<InitFunction>("nvmlInit_v2");
            Shutdown = Resolve<InitFunction>("nvmlShutdown");
            Count = Resolve<CountFunction>("nvmlDeviceGetCount_v2");
            Device = Resolve<DeviceFunction>("nvmlDeviceGetHandleByIndex_v2");
            Name = Resolve<NameFunction>("nvmlDeviceGetName");
            Utilization = Resolve<UtilizationFunction>("nvmlDeviceGetUtilizationRates");
            Memory = Resolve<MemoryFunction>("nvmlDeviceGetMemoryInfo");
            Temperature = Resolve<TemperatureFunction>("nvmlDeviceGetTemperature");
            T? Optional<T>(string name) where T : Delegate {
                try {
                    return Resolve<T>(name);
                }
                catch (EntryPointNotFoundException) {
                    return null;
                }
            }
            Encoder = Optional<EngineFunction>("nvmlDeviceGetEncoderUtilization");
            Decoder = Optional<EngineFunction>("nvmlDeviceGetDecoderUtilization");
            Clock = Optional<TemperatureFunction>("nvmlDeviceGetClockInfo");
            Pcie = Optional<TemperatureFunction>("nvmlDeviceGetPcieThroughput");
            State = Optional<PowerFunction>("nvmlDeviceGetPerformanceState");
            Power = Optional<PowerFunction>("nvmlDeviceGetPowerUsage");
            ComputeProcesses = Optional<ProcessListFunction>("nvmlDeviceGetComputeRunningProcesses_v3");
            GraphicsProcesses = Optional<ProcessListFunction>("nvmlDeviceGetGraphicsRunningProcesses_v3");
            ProcessUtilization = Optional<ProcessUtilizationFunction>("nvmlDeviceGetProcessUtilization");
            Pci = Optional<PciFunction>("nvmlDeviceGetPciInfo_v3");
            Uuid = Optional<NameFunction>("nvmlDeviceGetUUID");
        }

        internal static DriverApi? Load() {
            nint handle = 0;
            bool libc = false;
            try {
                if (Platform.IsWindows()) {
                    foreach (string path in new[]
                    {
                        Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            "NVIDIA Corporation",
                            "NVSMI",
                            "nvml.dll"
                        )
                    }

                    ) {
                        handle = LoadLibraryEx(path, 0, 0x1100);
                        if (handle != 0) {
                            break;
                        }
                    }
                }
                else if (Platform.IsLinux()) {
                    try {
                        handle = DlOpen("libnvidia-ml.so.1", 2);
                    }
                    catch (DllNotFoundException) {
                        libc = true;
                        handle = LibcOpen("libnvidia-ml.so.1", 2);
                    }
                }

                return handle == 0 ? null : new DriverApi(handle, libc);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) {
                if (handle != 0) {
                    if (Platform.IsWindows()) {
                        _ = FreeLibrary(handle);
                    }
                    else if (libc) {
                        _ = LibcClose(handle);
                    }
                    else {
                        _ = DlClose(handle);
                    }
                }

                return null;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern nint LoadLibraryEx(string path, nint file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
        private static extern nint GetProcAddress(nint handle, string name);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern int FreeLibrary(nint handle);
        [DllImport("libdl.so.2", EntryPoint = "dlopen", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
        private static extern nint DlOpen(string path, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
        private static extern nint DlSymbol(nint handle, string name);
        [DllImport("libdl.so.2", EntryPoint = "dlclose", ExactSpelling = true)]
        private static extern int DlClose(nint handle);
        [DllImport("libc", EntryPoint = "dlopen", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
        private static extern nint LibcOpen(string path, int flags);
        [DllImport("libc", EntryPoint = "dlsym", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
        private static extern nint LibcSymbol(nint handle, string name);
        [DllImport("libc", EntryPoint = "dlclose", ExactSpelling = true)]
        private static extern int LibcClose(nint handle);
    }
}
