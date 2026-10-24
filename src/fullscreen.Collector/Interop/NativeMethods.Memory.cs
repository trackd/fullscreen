using System.Runtime.InteropServices;

namespace fullscreen.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    internal struct VmStatistics {
        public uint Free, Active, Inactive, Wired, ZeroFill, Reactivations, PageIns, PageOuts, Faults, CopyOnWrite, Lookups, Hits, Purgeable, Purges, Speculative;
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "sysctlbyname", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, ExactSpelling = true)]
    internal static extern int SysCtlByName(
        string name,
        out ulong value,
        ref nuint length,
        IntPtr newValue,
        nuint newLength
    );
    [DllImport("libSystem.B.dylib", EntryPoint = "host_page_size", ExactSpelling = true)]
    internal static extern int HostPageSize(uint host, out nuint size);
    [DllImport("libSystem.B.dylib", EntryPoint = "host_statistics", ExactSpelling = true)]
    internal static extern int HostStatistics(uint host, int flavor, out VmStatistics statistics, ref uint count);
}
