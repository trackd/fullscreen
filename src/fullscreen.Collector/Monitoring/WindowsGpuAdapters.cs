using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

// DXGI identities are matched to PDH by LUID, never by display-name or enumeration order.
internal static class WindowsGpuAdapters {
    internal readonly record struct Adapter(string Id, string Name, ulong Dedicated, ulong Shared, string? Pci = null, uint Vendor = 0, bool DisplayOnly = false);
    internal static bool IsHyperVDisplay(string name, string? hardwareId = null) =>
        name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0
        || hardwareId is not null && hardwareId.IndexOf("VEN_1414", StringComparison.OrdinalIgnoreCase) >= 0
            && hardwareId.IndexOf("DEV_5353", StringComparison.OrdinalIgnoreCase) >= 0;
    internal static string Identity(uint high, uint low) => $"luid_0x{high:x8}_0x{low:x8}_phys_0";
    internal static unsafe Dictionary<string, Adapter> Read() {
        var result = new Dictionary<string, Adapter>(StringComparer.OrdinalIgnoreCase);
        nint factory = 0;
        try {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref iid, out factory) != 0) {
                return result;
            }
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; index < 64; index++) {
                if (enumerate(factory, index, out nint adapter) != 0) {
                    break;
                }
                try {
                    if (Method<GetDescription>(adapter, 10)(adapter, out var description) != 0 || (description.Flags & 2) != 0) {
                        continue;
                    }
                    string id = Identity(description.High, description.Low);
                    int length = 0;
                    while (length < 128 && description.DescriptionText[length] != '\0') {
                        length++;
                    }
                    string name = new(description.DescriptionText, 0, length);
                    if (description.Vendor == 0x1414 && description.Device == 0x5353) {
                        name = "Microsoft Hyper-V Video";
                    }
                    result[id] = new(id, name, (ulong)description.DedicatedVideo, (ulong)description.Shared,
                        PciAddress(description.Low, description.High), description.Vendor, DisplayOnly: IsHyperVDisplay(name));
                }
                finally {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        finally {
            if (factory != 0) {
                Marshal.Release(factory);
            }
            AppendHyperVDisplays(result);
        }
        return result;
    }
    private static void AppendHyperVDisplays(Dictionary<string, Adapter> result) {
        if (result.Values.Any(adapter => IsHyperVDisplay(adapter.Name))) {
            return;
        }
        try {
            for (uint index = 0; index < 64; index++) {
                var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevicesW(null, index, ref display, 0)) {
                    break;
                }
                if (!IsHyperVDisplay(display.Description, display.HardwareId)) {
                    continue;
                }
                string id = "display:" + display.Name;
                result[id] = new(id, "Microsoft Hyper-V Video", 0, 0, Vendor: 0x1414, DisplayOnly: true);
                break; // Logical monitors can share the same synthetic adapter.
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice {
        internal int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string HardwareId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Key;
    }
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice display, uint flags);
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private static unsafe string? PciAddress(uint low, uint high) {
        var open = new OpenAdapter {
            Low = low,
            High = high
        };
        try {
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0) {
                return null;
            }
            var address = new Address();
            var query = new Query { Adapter = open.Adapter, Type = 6, Data = (nint)(&address), Size = 12 };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) {
                return null;
            }
            return $"00000000:{address.Bus:x2}:{address.Device:x2}.{address.Function:x}";
        }
        catch (EntryPointNotFoundException) {
            return null;
        }
        finally {
            if (open.Adapter != 0) {
                _ = D3DKMTCloseAdapter(ref open.Adapter);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter { internal uint Low, High, Adapter; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Address { internal uint Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Query { internal uint Adapter, Type; internal nint Data; internal uint Size; }
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref Query query);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(ref uint adapter);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct Description {
        internal fixed char DescriptionText[128];
        internal uint Vendor, Device, Subsystem, Revision;
        internal nuint DedicatedVideo, DedicatedSystem, Shared;
        internal uint Low, High, Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(nint factory, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(nint adapter, out Description description);
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
}
