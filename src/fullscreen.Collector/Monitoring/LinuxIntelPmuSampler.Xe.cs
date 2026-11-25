using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;

internal sealed partial class LinuxIntelPmuSampler {
    private void InitializeXe(Device device, string path, string pci, uint type, int cpu) {
        ulong Event(string name) {
            string value = File.ReadAllText(Path.Combine(path, "events", name)).Trim();
            if (!value.StartsWith("event=0x", StringComparison.Ordinal) || !ulong.TryParse(value.Substring(8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id)) {
                throw new FormatException("Unsupported Xe PMU event.");
            }
            return XeField(path, "event", id);
        }
        ulong active = Event("engine-active-ticks"), total = Event("engine-total-ticks");
        foreach (var engine in (xeEngines ?? QueryXeEngines)(pci)) {
            string? name = engine.Class switch {
                0 => "render",
                1 => "copy",
                2 => "video",
                3 => "video-enhance",
                4 => "compute",
                _ => null
            };
            if (name is null) {
                continue;
            }
            ulong config = XeField(path, "gt", engine.Gt) | XeField(path, "engine_class", engine.Class) | XeField(path, "engine_instance", engine.Instance);
            var counter = new Engine(name + engine.Instance + "@gt" + engine.Gt, (open ?? NativeCounter.Open)(type, active | config, cpu));
            device.Engines.Add(counter); // Own the first descriptor before opening its pair.
            counter.Total = (open ?? NativeCounter.Open)(type, total | config, cpu);
        }
    }
    private static ulong XeField(string path, string field, ulong value) {
        string format = File.ReadAllText(Path.Combine(path, "format", field)).Trim();
        if (!format.StartsWith("config:", StringComparison.Ordinal)) {
            throw new FormatException("Unsupported Xe PMU format.");
        }
        var bits = format.Substring(7).Split('-');
        if (!int.TryParse(bits[0], out int first) || !int.TryParse(bits[bits.Length - 1], out int last)
            || first < 0 || last < first || last > 63 || last - first >= 63 || value >= (1UL << (last - first + 1))) {
            throw new FormatException("Xe PMU field out of range.");
        }
        return value << first;
    }
    private static unsafe XeEngine[] QueryXeEngines(string pci) {
        if (!Platform.IsLinux()) {
            throw new PlatformNotSupportedException();
        }
        string directory = Path.Combine("/sys/bus/pci/devices", pci, "drm");
        string? render = Directory.EnumerateDirectories(directory, "renderD*").FirstOrDefault();
        if (render is null) {
            throw new IOException("Xe render node not exposed.");
        }
        using var file = new FileStream(Path.Combine("/dev/dri", Path.GetFileName(render)), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var query = new XeQuery(); // DRM_XE_DEVICE_QUERY_ENGINES = 0, no extensions.
        const uint request = 0xC0286440; // DRM_IOWR(0x40, drm_xe_device_query), fixed 40-byte UAPI.
        if (XeIoctl(file.SafeFileHandle, request, ref query) != 0 || query.Size < 8 || query.Size > 65536) {
            throw new IOException("Xe engine query unavailable.");
        }
        var buffer = new byte[query.Size];
        fixed (byte* data = buffer) {
            query.Data = (ulong)data;
            if (XeIoctl(file.SafeFileHandle, request, ref query) != 0) {
                throw new IOException("Xe engine query failed.");
            }
        }
        return ParseXeEngines(buffer);
    }
    internal static XeEngine[] ParseXeEngines(byte[] buffer) {
        if (buffer.Length < 8) {
            throw new IOException("Incomplete Xe topology.");
        }
        uint count = BitConverter.ToUInt32(buffer, 0);
        if (count > 1024 || buffer.Length < 8 + (long)count * 32) {
            throw new IOException("Invalid Xe topology size.");
        }
        var result = new XeEngine[count];
        for (int index = 0; index < result.Length; index++) {
            int start = 8 + index * 32;
            result[index] = new(BitConverter.ToUInt16(buffer, start), BitConverter.ToUInt16(buffer, start + 2), BitConverter.ToUInt16(buffer, start + 4));
        }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct XeQuery { internal ulong Extensions; internal uint Query, Size; internal ulong Data, Reserved0, Reserved1; }
    [DllImport("libc", EntryPoint = "ioctl", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern int XeIoctl(SafeFileHandle file, nuint request, ref XeQuery query);
}
