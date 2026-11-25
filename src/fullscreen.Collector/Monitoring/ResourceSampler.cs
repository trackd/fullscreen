using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace fullscreen.Monitoring;

internal sealed class ResourceSampler(string procRoot = "/proc", string sysRoot = "/sys") : IDisposable {
    private readonly Dictionary<int, ulong[]> _cpu = [];
    private string? _name;
    private int? _physicalCores;
    private NativeCounterSet? _windows;
    private static readonly string[] CpuCounters = ["% Processor Time", "% User Time", "% Privileged Time", "Processor Frequency"];
    internal void Sample(CollectorFrame frame, bool cpu, bool memory) {
        frame.CpuDetails = null;
        frame.MemoryDetails = null;
        if (!cpu) { _cpu.Clear(); }
        try {
            if (Platform.IsLinux()) {
                Linux(frame, cpu, memory);
            }
            else if (Platform.IsWindows()) {
                Windows(frame, cpu, memory);
            }
            else if (Platform.IsMacOS()) {
                Mac(frame, cpu, memory);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) { }
    }
    internal void Linux(CollectorFrame frame, bool cpu, bool memory) {
        if (cpu) {
            string info = Read(Path.Combine(procRoot, "cpuinfo"));
            _name ??= info.Split('\n').FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal) || line.StartsWith("Hardware", StringComparison.Ordinal))?.Split(':').Last().Trim() ?? "CPU";
            var cores = new List<CpuCore>();
            var frequencies = new Dictionary<int, double>();
            int? processorId = null;
            foreach (string entry in info.Split('\n')) {
                int colon = entry.IndexOf(':');
                if (colon < 0) {
                    continue;
                }
                string key = entry.Substring(0, colon).Trim(), value = entry.Substring(colon + 1).Trim();
                if (key == "processor") {
                    processorId = int.TryParse(value, out int parsed) ? parsed : null;
                }
                else if (key == "cpu MHz" && processorId.HasValue && Number(value) is { } mhz) {
                    frequencies[processorId.Value] = mhz;
                }
            }
            var active = new HashSet<int>();
            foreach (string line in Read(Path.Combine(procRoot, "stat")).Split('\n')) {
                string[] fields = Fields(line);
                if (fields.Length < 5 || !fields[0].StartsWith("cpu", StringComparison.Ordinal) || !int.TryParse(fields[0].Substring(3), out int id)) {
                    continue;
                }
                ulong[] ticks = fields.Skip(1).Take(8).Select(ParseUnsigned).ToArray();
                double? mhz = Number(Read(Path.Combine(sysRoot, "devices/system/cpu/cpu" + id, "cpufreq/scaling_cur_freq"))) / 1000;
                mhz ??= frequencies.TryGetValue(id, out double fallback) ? fallback : null;
                cores.Add(CpuDelta(id, ticks, mhz));
                active.Add(id);
            }
            foreach (int id in _cpu.Keys.Where(id => !active.Contains(id)).ToArray()) {
                _cpu.Remove(id);
            }
            double?[] load = Fields(Read(Path.Combine(procRoot, "loadavg"))).Take(3).Select(Number).ToArray();
            double? uptime = Number(Fields(Read(Path.Combine(procRoot, "uptime"))).FirstOrDefault() ?? "");
            var topology = new HashSet<string>();
            foreach (var core in cores) {
                string root = Path.Combine(sysRoot, "devices/system/cpu/cpu" + core.Id, "topology");
                string physical = Read(Path.Combine(root, "physical_package_id")), physicalCore = Read(Path.Combine(root, "core_id"));
                if (physical.Length > 0 && physicalCore.Length > 0) {
                    topology.Add(physical + ":" + physicalCore);
                }
            }
            frame.CpuDetails = new(_name, cores.ToArray(), Temperatures(Path.Combine(sysRoot, "class/hwmon"), Path.Combine(sysRoot, "class/thermal")), uptime,
                load.ElementAtOrDefault(0), load.ElementAtOrDefault(1), load.ElementAtOrDefault(2), topology.Count > 0 ? topology.Count : null);
        }
        if (memory) {
            string pressure = Read(Path.Combine(procRoot, "pressure/memory"));
            frame.MemoryDetails = LinuxMemory(Read(Path.Combine(procRoot, "meminfo"))) with {
                PressureSome10 = Pressure(pressure, "some"),
                PressureFull10 = Pressure(pressure, "full")
            };
        }
    }
    internal static double? Pressure(string text, string category) {
        string? line = text.Split('\n').FirstOrDefault(value => value.StartsWith(category + " ", StringComparison.Ordinal));
        string? value = line is not null ? Fields(line).FirstOrDefault(field => field.StartsWith("avg10=", StringComparison.Ordinal)) : null;
        return value is not null && Number(value.Substring(6)) is >= 0 and <= 100 ? Number(value.Substring(6)) : null;
    }
    internal CpuCore CpuDelta(int id, ulong[] ticks, double? mhz = null) {
        _cpu.TryGetValue(id, out ulong[]? previous);
        _cpu[id] = ticks;
        if (previous is null || previous.Length != ticks.Length || ticks.Where((value, index) => value < previous[index]).Any()) {
            return new(id, null, Megahertz: mhz);
        }
        double[] delta = ticks.Select((value, index) => (double)(value - previous[index])).ToArray();
        double total = delta.Sum();
        double? Percent(params int[] indices) => total > 0 ? indices.Where(index => index < delta.Length).Sum(index => delta[index]) / total * 100 : null;
        return new(id, total > 0 ? 100 - Percent(3, 4) : null, Percent(0, 1), Percent(2, 5, 6), ticks.Length > 4 ? Percent(4) : null,
            ticks.Length > 7 ? Percent(7) : null, mhz);
    }
    internal static MemoryDetails LinuxMemory(string text) {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ulong? Get(string key) => SystemSampler.ReadKilobytes(bytes, key + ":");
        ulong? swap = Get("SwapTotal"), freeSwap = Get("SwapFree");
        ulong? cache = Get("Cached"), reclaim = Get("SReclaimable"), shared = Get("Shmem");
        return new(Get("MemFree"), cache.HasValue ? cache + (reclaim ?? 0) - Math.Min(cache.Value + (reclaim ?? 0), shared ?? 0) : null,
            swap, swap.HasValue && freeSwap <= swap ? swap - freeSwap : null, Get("Committed_AS"), Get("CommitLimit"),
            Scope: "Linux; cache includes reclaimable slab and excludes shmem; commit is reserved virtual memory");
    }
    internal static TemperatureReading[] Temperatures(string root, string? thermalRoot = null) {
        var result = new List<TemperatureReading>();
        try {
            foreach (string monitor in Directory.EnumerateDirectories(root, "hwmon*")) {
                string name = Read(Path.Combine(monitor, "name"));
                // CPU sensors only: do not attribute GPU, motherboard or drive sensors to the CPU.
                if (name is not ("coretemp" or "k10temp" or "zenpower" or "cpu_thermal" or "k8temp")) {
                    continue;
                }
                foreach (string input in Directory.EnumerateFiles(monitor, "temp*_input")) {
                    double? value = Number(Read(input)) / 1000;
                    if (value is not (>= -20 and <= 150)) {
                        continue;
                    }
                    string label = Read(input.Replace("_input", "_label"));
                    result.Add(new(name + " · " + (label.Length > 0 ? label : Path.GetFileName(input)), value.Value));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        // Thermal zones often back the same hwmon readings. Use them only as a fallback,
        // and only with an explicit CPU identity; ACPI zones are not necessarily CPU sensors.
        if (result.Count == 0 && thermalRoot is not null) {
            try {
                foreach (string zone in Directory.EnumerateDirectories(thermalRoot, "thermal_zone*")) {
                    string type = Read(Path.Combine(zone, "type"));
                    if (type is not ("x86_pkg_temp" or "cpu-thermal" or "cpu_thermal")) {
                        continue;
                    }
                    double? value = Number(Read(Path.Combine(zone, "temp"))) / 1000;
                    if (value is >= -20 and <= 150) {
                        result.Add(new(type + " · " + Path.GetFileName(zone), value.Value));
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return result.ToArray();
    }
    private void Windows(CollectorFrame frame, bool cpu, bool memory) {
        if (cpu) {
            _windows ??= new(CpuCounters.Select(counter => "\\Processor Information(*)\\" + counter));
            var values = _windows.Sample();
            Dictionary<string, double> ReadCounter(int index) => values.TryGetValue("\\Processor Information(*)\\" + CpuCounters[index], out var data) ? data : [];
            var usage = ReadCounter(0);
            double? Get(int index, string id) => ReadCounter(index).TryGetValue(id, out double value) ? value : null;
            _name ??= WindowsCpuName();
            var cores = values.Values.SelectMany(data => data.Keys).Distinct().Where(id => !id.Contains("_Total")).OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => {
                    string[] identity = id.Split(',');
                    int logical = identity.Length == 2 && int.TryParse(identity[0], out int group) && int.TryParse(identity[1], out int processor) ? group * 64 + processor : -1;
                    return new CpuCore(logical, Get(0, id) is { } busy ? Compat.Clamp(busy, 0, 100) : null, Get(1, id), Get(2, id), Megahertz: Get(3, id));
                }).Where(core => core.Id >= 0).OrderBy(core => core.Id).ToArray();
            _physicalCores ??= WindowsPhysicalCores();
            frame.CpuDetails = new(_name, cores, [], GetTickCount64() / 1000d, PhysicalCores: _physicalCores);
        }
        if (memory) {
            var info = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
            if (GetPerformanceInfo(ref info, info.Size)) {
                ulong page = (ulong)info.PageSize;
                var paging = WindowsPaging(page);
                frame.MemoryDetails = new(Cached: (ulong)info.SystemCache * page, SwapTotal: paging.Total, SwapUsed: paging.Used, Committed: (ulong)info.CommitTotal * page,
                    CommitLimit: (ulong)info.CommitLimit * page, Scope: "Windows; available RAM includes standby pages; commit limit is RAM plus paging capacity");
            }
        }
    }
    private static unsafe int? WindowsPhysicalCores() {
        uint size = 0;
        _ = GetLogicalProcessorInformationEx(0, null, ref size);
        if (size == 0 || size > 16 * 1024 * 1024) {
            return null;
        }
        var bytes = new byte[size];
        fixed (byte* pointer = bytes) {
            if (!GetLogicalProcessorInformationEx(0, pointer, ref size)) {
                return null;
            }
        }
        int offset = 0, cores = 0;
        while (offset + 8 <= size) {
            uint length = BitConverter.ToUInt32(bytes, offset + 4);
            if (length < 8 || length > size - offset) {
                return null;
            }
            if (BitConverter.ToUInt32(bytes, offset) == 0) {
                cores++;
            }
            offset += (int)length;
        }
        return cores > 0 ? cores : null;
    }
    private static (ulong? Total, ulong? Used) WindowsPaging(ulong page) {
        for (int size = 65536; size <= 4 * 1024 * 1024; size *= 2) {
            var bytes = new byte[size];
            int status = NtQuerySystemInformation(18, bytes, (uint)size, out uint returned);
            if (status == unchecked((int)0xc0000004)) {
                continue;
            }
            if (status < 0 || returned > size) {
                return (null, null);
            }
            ulong total = 0, used = 0;
            int offset = 0;
            if (returned == 0) {
                return (0, 0);
            }
            while (offset + 16 <= returned) {
                uint next = BitConverter.ToUInt32(bytes, offset);
                total += BitConverter.ToUInt32(bytes, offset + 4) * page;
                used += BitConverter.ToUInt32(bytes, offset + 8) * page;
                if (next == 0) {
                    return (total, used);
                }
                if (next < 16 || next > returned - offset) {
                    return (null, null);
                }
                offset += (int)next;
            }
            return (null, null);
        }
        return (null, null);
    }
    private static string WindowsCpuName() {
        var text = new char[512];
        uint bytes = 1024;
        return RegGetValueW(unchecked((nint)(int)0x80000002), "HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString",
            2, 0, text, ref bytes) == 0 ? new string(text).TrimEnd('\0').Trim() : "CPU";
    }
    private unsafe void Mac(CollectorFrame frame, bool cpu, bool memory) {
        if (cpu) {
            byte[] name = new byte[256];
            nuint length = (nuint)name.Length;
            if (_name is null) { _name = sysctlbyname("machdep.cpu.brand_string", name, ref length, 0, 0) == 0 ? Encoding.UTF8.GetString(name, 0, (int)length).TrimEnd('\0') : "Apple CPU"; }
            uint host = mach_host_self();
            var cores = new List<CpuCore>();
            nint data = 0;
            uint size = 0;
            try {
                if (host_processor_info(host, 2, out uint count, out data, out size) == 0 && count <= 65536 && size >= count * 4) {
                    uint* ticks = (uint*)data;
                    for (int i = 0; i < count; i++) {
                        cores.Add(CpuDelta(i, [ticks[i * 4], ticks[i * 4 + 3], ticks[i * 4 + 1], ticks[i * 4 + 2]]));
                    }
                }
            }
            finally {
                if (data != 0) {
                    _ = vm_deallocate(mach_task_self(), data, (nuint)size * 4);
                }
                _ = mach_port_deallocate(mach_task_self(), host);
            }
            double[] load = new double[3];
            int loads = getloadavg(load, 3);
            byte[] boot = MacValue("kern.boottime", 16), physical = MacValue("hw.physicalcpu", 4);
            double? uptime = boot.Length >= 8 ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - BitConverter.ToInt64(boot, 0)) : null;
            frame.CpuDetails = new(_name, cores.ToArray(), [], uptime, loads > 0 ? load[0] : null, loads > 1 ? load[1] : null,
                loads > 2 ? load[2] : null, physical.Length == 4 ? BitConverter.ToInt32(physical, 0) : null);
        }
        if (memory) {
            uint host = mach_host_self();
            try {
                uint* values = stackalloc uint[64];
                uint count = 64;
                if (host_statistics64(host, 4, values, ref count) == 0 && count >= 36 && host_page_size(host, out uint page) == 0) {
                    byte[] swap = MacValue("vm.swapusage", 32);
                    frame.MemoryDetails = new(Free: (ulong)values[0] * page, Cached: (ulong)values[34] * page,
                        SwapTotal: swap.Length >= 24 ? BitConverter.ToUInt64(swap, 0) : null, SwapUsed: swap.Length >= 24 ? BitConverter.ToUInt64(swap, 16) : null,
                        Compressed: (ulong)values[32] * page, Wired: (ulong)values[3] * page,
                        Scope: "macOS; cache is file-backed pages; compressed is physical compressor storage; available RAM is estimated from free + inactive pages");
                }
            }
            finally {
                _ = mach_port_deallocate(mach_task_self(), host);
            }
        }
    }
    private static byte[] MacValue(string name, int size) {
        byte[] data = new byte[size];
        nuint length = (nuint)size;
        return sysctlbyname(name, data, ref length, 0, 0) == 0 ? data.Take((int)length).ToArray() : [];
    }
    internal static string Read(string path) {
        try {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return "";
        }
    }
    internal static string[] Fields(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    internal static double? Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && Compat.IsFinite(value) ? value : null;
    private static ulong ParseUnsigned(string text) => ulong.TryParse(text, out ulong value) ? value : 0;
    public void Dispose() => _windows?.Dispose();
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo {
        internal uint Size;
        internal nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        internal uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInfo info, uint size);
    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int kind, [Out] byte[] buffer, uint size, out uint returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegGetValueW(nint key, string subkey, string value, uint flags, nint type, [Out] char[] data, ref uint size);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_host_self();
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_task_self();
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int mach_port_deallocate(uint task, uint name);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int vm_deallocate(uint task, nint address, nuint size);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_processor_info(uint host, int flavor, out uint count, out nint info, out uint size);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int getloadavg([Out] double[] values, int count);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern unsafe int host_statistics64(uint host, int flavor, uint* info, ref uint count);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_page_size(uint host, out uint size);
    [DllImport("/usr/lib/libSystem.B.dylib", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int sysctlbyname(string name, [Out] byte[] data, ref nuint size, nint newData, nuint newSize);
}
