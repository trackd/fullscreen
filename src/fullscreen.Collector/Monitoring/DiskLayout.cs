using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;

internal static class DiskLayout {
    internal readonly record struct Mount(string Key, string Path, string FileSystem, string Device);
    internal static Mount[] LinuxMounts(string text, string sysRoot) {
        var mounts = new List<Mount>();
        var seen = new HashSet<string>();
        foreach (string line in text.Split('\n')) {
            string[] parts = line.Split(' ');
            int separator = Array.IndexOf(parts, "-");
            if (parts.Length < 8 || separator < 6 || separator + 2 >= parts.Length) {
                continue;
            }
            string major = parts[2], fileSystem = parts[separator + 1];
            string source = Unescape(parts[separator + 2]);
            if ((!source.StartsWith("/dev/", StringComparison.Ordinal) && fileSystem is not ("btrfs" or "zfs" or "overlay"))
                || fileSystem is "squashfs" or "iso9660" or "udf") {
                continue;
            }
            string path = Unescape(parts[4]);
            string? device = null;
            try {
                foreach (string directory in Directory.EnumerateDirectories(Path.Combine(sysRoot, "block"))) {
                    if (ResourceSampler.Read(Path.Combine(directory, "dev")) == major
                        || major.StartsWith("0:", StringComparison.Ordinal) && Path.GetFileName(directory) == Path.GetFileName(source)) {
                        device = directory;
                        break;
                    }
                    foreach (string partition in Directory.EnumerateDirectories(directory)) {
                        if (ResourceSampler.Read(Path.Combine(partition, "partition")).Length == 0) {
                            continue;
                        }
                        if (ResourceSampler.Read(Path.Combine(partition, "dev")) == major
                            || major.StartsWith("0:", StringComparison.Ordinal) && Path.GetFileName(partition) == Path.GetFileName(source)) {
                            device = directory;
                            break;
                        }
                    }
                    if (device is not null) {
                        break;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            // One filesystem may have several bind mounts/subvolumes sharing the same capacity.
            if (!seen.Add(major + ":" + fileSystem)) {
                continue;
            }
            mounts.Add(new(major + ":" + fileSystem, path, fileSystem, device is not null ? Path.GetFileName(device) : ""));
        }
        return mounts.ToArray();
    }
    private static string Unescape(string text) =>
        text.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");
    internal static (string? Device, DiskVolume Volume)[] LinuxSwaps(string text, string sysRoot) {
        var volumes = new List<(string? Device, DiskVolume Volume)>();
        foreach (string line in text.Split('\n')) {
            string[] fields = ResourceSampler.Fields(line);
            if (fields.Length < 4 || fields[1] != "partition" || !ulong.TryParse(fields[2], out ulong size) || !ulong.TryParse(fields[3], out ulong used)
                || used > size || size > ulong.MaxValue / 1024) {
                continue;
            }
            string source = Unescape(fields[0]), name = Path.GetFileName(source);
            try {
                string? device = Directory.EnumerateDirectories(Path.Combine(sysRoot, "block")).FirstOrDefault(directory =>
                    Path.GetFileName(directory) == name || ResourceSampler.Read(Path.Combine(directory, "dm/name")) == name
                    || File.Exists(Path.Combine(directory, name, "partition")));
                if (device is not null) {
                    volumes.Add((Path.GetFileName(device), new("swap:" + source, "Swap", "swap", size * 1024, (size - used) * 1024)));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return volumes.ToArray();
    }
    internal static DiskRow[] Combine(IEnumerable<DiskRow> devices, IEnumerable<(string? Device, DiskVolume Volume)> volumes) {
        var rows = devices.ToList();
        foreach (var group in volumes.GroupBy(item => item.Device ?? "volume:" + item.Volume.Id)) {
            DiskVolume[] mapped = group.Select(item => item.Volume).GroupBy(volume => volume.Id).Select(volumesForId => volumesForId.First()).ToArray();
            DiskVolume[] capacity = mapped.Any(volume => volume.FileSystem != "swap") ? mapped.Where(volume => volume.FileSystem != "swap").ToArray() : mapped;
            int index = rows.FindIndex(row => row.Id == group.Key);
            ulong total = capacity.Aggregate(0UL, (sum, volume) => sum > ulong.MaxValue - volume.Total ? ulong.MaxValue : sum + volume.Total);
            ulong available = capacity.Aggregate(0UL, (sum, volume) => sum > ulong.MaxValue - volume.Available ? ulong.MaxValue : sum + volume.Available);
            string fileSystems = string.Join(", ", mapped.Select(volume => volume.FileSystem).Distinct());
            if (index >= 0) {
                rows[index] = rows[index] with {
                    Total = total,
                    Free = available,
                    FileSystem = fileSystems,
                    Volumes = mapped
                };
            }
            else {
                rows.Add(new(group.Key, string.Join(", ", mapped.Select(volume => volume.Mount)), "Filesystem", fileSystems, total, available,
                Status: "Backing device is unavailable or spans several disks; capacity is shown once; I/O attribution unavailable") { Volumes = mapped, Overview = false });
            }
        }
        return rows.OrderBy(row => row.Kind == "Device" ? 0 : 1).ThenBy(row => row.Name, StringComparer.Ordinal).ToArray();
    }
    internal static int? WindowsDisk(string path) {
        if (path.Length < 2 || path[1] != ':') {
            return null;
        }
        using var volume = CreateFileW("\\\\.\\" + path.Substring(0, 2), 0, 3, 0, 3, 0, 0);
        if (volume.IsInvalid) {
            return null;
        }
        for (int size = 32; size <= 65536; size *= 2) {
            byte[] buffer = new byte[size];
            bool success = DeviceIoControl(volume, 0x560000, 0, 0, buffer, (uint)buffer.Length, out uint returned, 0);
            if (!success) {
                if (Marshal.GetLastWin32Error() == 234) {
                    continue;
                }
                return null;
            }
            if (returned < 32) {
                return null;
            }
            uint count = BitConverter.ToUInt32(buffer, 0);
            if (count == 0 || count > (returned - 8) / 24) {
                return null;
            }
            uint disk = BitConverter.ToUInt32(buffer, 8);
            for (int index = 1; index < count; index++) {
                if (BitConverter.ToUInt32(buffer, 8 + index * 24) != disk) {
                    return null;
                }
            }
            return disk <= int.MaxValue ? (int)disk : null;
        }
        return null;
    }
    // Standard storage-class query; no SMART pass-through, driver installation or tuning.
    internal static double? WindowsTemperature(int disk) {
        if (disk < 0) {
            return null;
        }
        using var device = CreateFileW("\\\\.\\PhysicalDrive" + disk, 0, 3, 0, 3, 0, 0);
        if (device.IsInvalid) {
            return null;
        }
        byte[] query = new byte[12];
        BitConverter.GetBytes(52U).CopyTo(query, 0); // StorageDeviceTemperatureProperty
        byte[] buffer = new byte[4096];
        return QueryStorage(device, 0x2D1400, query, (uint)query.Length, buffer, (uint)buffer.Length, out uint returned, 0)
            ? StorageTemperature(buffer, returned) : null;
    }
    internal static double? StorageTemperature(byte[] buffer, uint returned) {
        const int header = 24, entry = 16;
        if (returned > buffer.Length || returned < header + entry) {
            return null;
        }
        uint version = BitConverter.ToUInt32(buffer, 0), size = BitConverter.ToUInt32(buffer, 4);
        int count = BitConverter.ToUInt16(buffer, 12);
        if (version < header + entry || size < header + entry || size > returned || count == 0 || count > (size - header) / entry) {
            return null;
        }
        double? hottest = null;
        for (int index = 0; index < count; index++) {
            int offset = header + index * entry;
            short value = BitConverter.ToInt16(buffer, offset + 2);
            if (value is < -60 or > 200) {
                continue;
            } // Also excludes STORAGE_TEMPERATURE_VALUE_NOT_REPORTED (-32768).
            if (BitConverter.ToUInt16(buffer, offset) == 0) {
                return value;
            } // Composite, when supplied.
            hottest = hottest.HasValue ? Math.Max(hottest.Value, value) : value;
        }
        return hottest;
    }
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryStorage(SafeFileHandle file, uint code, [In] byte[] input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, nint overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, nint input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, nint overlapped);
}
