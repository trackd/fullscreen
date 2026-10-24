using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using fullscreen.Interop;

namespace fullscreen.Monitoring;
/// <summary>OS CPU deltas and physical memory. Unknown values remain null.</summary>
public sealed partial class SystemSampler {
    private ulong _lastTotal, _lastIdle;
    private bool _primed;
    private readonly byte[] _fileBuffer = new byte[32768];
    /// <summary>Gets the system CPU utilization percentage, or null before a usable delta exists.</summary>
    public double? CpuPercent { get; private set; }
    /// <summary>Gets the total physical memory in bytes.</summary>
    public ulong? TotalMemory { get; private set; }
    /// <summary>Gets the available physical memory in bytes.</summary>
    public ulong? AvailableMemory { get; private set; }

    /// <summary>Refreshes system CPU and memory counters selected by the caller.</summary>
    public void Sample() =>
        Sample(true, true);
    /// <summary>Refreshes system CPU and memory counters selected by the caller.</summary>
    /// <param name="cpu">Whether CPU counters are collected.</param>
    /// <param name="memory">Whether memory counters are collected.</param>
    public void Sample(bool cpu, bool memory) {
        CpuPercent = null;
        TotalMemory = null;
        AvailableMemory = null;
        if (!cpu) {
            _primed = false;
        }

        if (!cpu && !memory) {
            return;
        }

        try {
            SampleCore(cpu, memory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            // Missing/restricted procfs should not prevent the process table from running.
        }
    }

    private unsafe void SampleCore(bool collectCpu, bool collectMemory) {
        ulong total = 0, idle = 0;
        bool hasCpu = false;
        if (Platform.IsWindows()) {
            ulong kernel = 0, user = 0;
            hasCpu = collectCpu && GetSystemTimes(out idle, out kernel, out user) != 0;
            total = kernel + user; // Windows kernel includes idle.
            NativeMethods.MemoryStatus status = new() {
                Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>()
            };
            if (collectMemory && NativeMethods.GlobalMemoryStatusEx(ref status)) {
                TotalMemory = status.TotalPhysical;
                AvailableMemory = status.AvailablePhysical;
            }
        }
        else if (Platform.IsLinux()) {
            ByteWindow stat = collectCpu ? ReadProc("/proc/stat") : default;
            int end = stat.IndexOf((byte)'\n');
            if (end >= 0) {
                stat = stat[..end];
            }

            int count = 0;
            // guest/guest_nice are already included in user/nice.
            foreach (var part in stat.Split((byte)' ')) {
                if (part.IsEmpty) {
                    continue;
                }

                int i = count++;
                if (i == 0 || i >= 9 || !ByteNumbers.TryParse(part, out ulong value, out _)) {
                    continue;
                }

                total += value;
                if (i is 4 or 5) {
                    idle += value;
                }
            }

            hasCpu = count >= 5;
            ByteWindow memory = collectMemory ? ReadProc("/proc/meminfo") : default;
            TotalMemory = ReadKilobytes(memory, "MemTotal:");
            AvailableMemory = ReadKilobytes(memory, "MemAvailable:");
        }
        else if (Platform.IsMacOS()) {
            // mach_host_self acquires a send right; release it after both calls.
            uint hostPort = mach_host_self();
            uint* info = stackalloc uint[32];
            uint count = 4;
            if (collectCpu && host_statistics(hostPort, 3, info, ref count) == 0 && count >= 4) {
                total = (ulong)info[0] + info[1] + info[2] + info[3];
                idle = info[2];
                hasCpu = true;
            }

            nuint length = sizeof(ulong);
            if (collectMemory
                && NativeMethods.SysCtlByName("hw.memsize", out ulong physical, ref length, IntPtr.Zero, 0) == 0) {
                TotalMemory = physical;
            }

            // Free + inactive is an estimate on macOS, not Linux's MemAvailable.
            count = 15;
            if (collectMemory
                && NativeMethods.HostStatistics(hostPort, 2, out NativeMethods.VmStatistics vm, ref count) == 0
                && count >= 3
                && NativeMethods.HostPageSize(hostPort, out nuint pageSize) == 0) {
                AvailableMemory = ((ulong)vm.Free + vm.Inactive) * pageSize;
            }

            _ = mach_port_deallocate(mach_task_self(), hostPort);
        }

        if (hasCpu) {
            CpuPercent = _primed && total > _lastTotal && idle >= _lastIdle ? Compat.Clamp(100.0 * (1 - (double)(idle - _lastIdle) / (total - _lastTotal)), 0, 100) : null;
            _lastTotal = total;
            _lastIdle = idle;
            _primed = true;
        }
    }

    private ByteWindow ReadProc(string path) {
        using FileStream handle = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = handle.Read(_fileBuffer, 0, _fileBuffer.Length);
        return new ByteWindow(_fileBuffer, 0, read);
    }

    internal static ulong? ReadKilobytes(ByteWindow text, string key) {
        foreach (var line in text.Split((byte)'\n')) {
            if (!line.StartsWith(key)) {
                continue;
            }

            var number = line[key.Length..].TrimStart((byte)' ');
            return ByteNumbers.TryParse(number, out ulong value, out _) && value <= ulong.MaxValue / 1024 ? value * 1024 : null;
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("/usr/lib/libSystem.B.dylib", ExactSpelling = true)]
    private static extern uint mach_host_self();
    [DllImport("/usr/lib/libSystem.B.dylib", ExactSpelling = true)]
    private static extern uint mach_task_self();
    [DllImport("/usr/lib/libSystem.B.dylib", ExactSpelling = true)]
    private static extern int mach_port_deallocate(uint task, uint name);
    [DllImport("/usr/lib/libSystem.B.dylib", ExactSpelling = true)]
    private static unsafe extern int host_statistics(uint hostPort, int flavor, uint* info, ref uint count);
    /// <summary>Reads a named memory counter from Linux procfs data.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="key">The stable lookup key.</param>
    /// <returns>The memory quantity in bytes, or null when the key is absent.</returns>
    public static ulong? ReadKilobytes(byte[] text, string key) =>
        ReadKilobytes(new ByteWindow(text), key);
}
