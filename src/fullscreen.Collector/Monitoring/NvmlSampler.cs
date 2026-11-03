using System.Runtime.InteropServices;
using System.Text;

namespace fullscreen.Monitoring;
/// <summary>Optional NVIDIA driver API. Cached entry points keep their library loaded for process lifetime.</summary>
internal sealed class NvmlSampler : IDisposable {
    private static readonly Lazy<DriverApi?> Driver = new(DriverApi.Load);
    private readonly DriverApi? _api;
    private readonly nint[] _devices = new nint[32];
    private readonly string[] _names = new string[32];
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
            _devices[_count++] = device;
        }
    }

    internal void Sample(CollectorFrame frame) {
        if (!_initialized) {
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
            if (utilizationStatus != 3) { Capabilities |= CollectorCapabilities.GpuUtilization; }
            if (memoryStatus != 3) { Capabilities |= CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuTotalMemory; }
            if (temperatureStatus != 3) { Capabilities |= CollectorCapabilities.GpuTemperature; }
            double? busy = utilizationStatus == 0 ? value.Gpu : null;
            bool hasMemory = memoryStatus == 0;
            double? temperature = temperatureStatus == 0 ? temp : null;
            frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
            frame.Gpus[frame.GpuCount++] = new(
                _names[i],
                "NVIDIA NVML",
                busy,
                hasMemory ? memory.Used : null,
                hasMemory ? memory.Total : null,
                temperature
            );
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
    private sealed class DriverApi {
        internal readonly InitFunction Init, Shutdown;
        internal readonly CountFunction Count;
        internal readonly DeviceFunction Device;
        internal readonly NameFunction Name;
        internal readonly UtilizationFunction Utilization;
        internal readonly MemoryFunction Memory;
        internal readonly TemperatureFunction Temperature;
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
