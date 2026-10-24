using System.Diagnostics;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

internal static class Platform {
    public static bool IsWindows() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}

internal static class Compat {
    private static readonly Func<long>? AllocationCounter = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes)?.CreateDelegate(typeof(Func<long>)) as Func<long>;
    internal static readonly int ProcessId = ReadProcessId();
    private static int ReadProcessId() {
        using var process = Process.GetCurrentProcess();
        return process.Id;
    }

    internal static long AllocatedBytes() =>
        AllocationCounter?.Invoke() ?? 0;
    internal static TimeSpan ElapsedTime(long start) =>
        ElapsedTime(start, Stopwatch.GetTimestamp());
    internal static TimeSpan ElapsedTime(
        long start,
        long end
    ) =>
        TimeSpan.FromTicks((long)((end - start) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
    internal static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
    internal static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
