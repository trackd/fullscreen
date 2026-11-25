using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

/// <summary>Optional columns implemented by the collector host and its metric providers.</summary>
[Flags]
public enum CollectorCapabilities {
    /// <summary>No optional metrics are supported.</summary>
    None = 0,
    /// <summary>Process parent identifiers.</summary>
    ProcessParent = 1,
    /// <summary>Process account names.</summary>
    ProcessOwner = 2,
    /// <summary>Process state.</summary>
    ProcessStatus = 4,
    /// <summary>Per-process disk transfer rates.</summary>
    ProcessDisk = 8,
    /// <summary>Per-process network transfer rates.</summary>
    ProcessNetwork = 16,
    /// <summary>Per-process GPU utilization.</summary>
    ProcessGpu = 32,
    /// <summary>Per-process dedicated GPU memory.</summary>
    ProcessGpuMemory = 64,
    /// <summary>GPU device utilization.</summary>
    GpuUtilization = 128,
    /// <summary>GPU device used memory.</summary>
    GpuUsedMemory = 256,
    /// <summary>GPU device total memory.</summary>
    GpuTotalMemory = 512,
    /// <summary>GPU device temperature.</summary>
    GpuTemperature = 1024,
    /// <summary>Render engine activity.</summary>
    GpuRender = 2048,
    /// <summary>Compute engine activity.</summary>
    GpuCompute = 4096,
    /// <summary>Copy engine activity.</summary>
    GpuCopy = 8192,
    /// <summary>Unified video engine activity.</summary>
    GpuVideo = 16384,
    /// <summary>Video processing activity.</summary>
    GpuVideoEnhance = 32768,
    /// <summary>Explicit encoder activity.</summary>
    GpuEncode = 65536,
    /// <summary>Explicit decoder activity.</summary>
    GpuDecode = 131072,
    /// <summary>Memory read/write activity.</summary>
    GpuMemoryActivity = 262144,
    /// <summary>Graphics clock.</summary>
    GpuClock = 524288,
    /// <summary>Device or SoC power.</summary>
    GpuPower = 1048576,
    /// <summary>All optional process GPU metrics.</summary>
    ProcessGraphics = ProcessGpu | ProcessGpuMemory,
    /// <summary>All optional GPU device metrics.</summary>
    DeviceGraphics = GpuUtilization | GpuUsedMemory | GpuTotalMemory | GpuTemperature | GpuRender | GpuCompute | GpuCopy
        | GpuVideo | GpuVideoEnhance | GpuEncode | GpuDecode | GpuMemoryActivity | GpuClock | GpuPower
}

internal static class CollectorCapabilityDetection {
    internal static CollectorCapabilities Detect(bool windows, bool linux, bool mac, bool is64Bit, Version version) {
        const CollectorCapabilities identity = CollectorCapabilities.ProcessParent | CollectorCapabilities.ProcessOwner | CollectorCapabilities.ProcessStatus;
        if (linux) {
            return identity | CollectorCapabilities.ProcessDisk | CollectorCapabilities.DeviceGraphics | CollectorCapabilities.ProcessGraphics;
        }
        if (mac) {
            return identity | CollectorCapabilities.ProcessDisk | CollectorCapabilities.ProcessGraphics
                | CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuRender;
        }
        if (!windows) {
            return CollectorCapabilities.None;
        }
        var supported = CollectorCapabilities.ProcessOwner;
        if (is64Bit) {
            supported |= CollectorCapabilities.ProcessParent | CollectorCapabilities.ProcessStatus;
            // This collector uses private system-loggers, available starting with Windows 8.
            if (version >= new Version(6, 2)) {
                supported |= CollectorCapabilities.ProcessDisk | CollectorCapabilities.ProcessNetwork;
            }
        }
        // WDDM GPU performance counters were introduced in Windows 10 version 1709.
        if (version >= new Version(10, 0, 16299)) {
            supported |= CollectorCapabilities.ProcessGraphics | CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory;
        }
        // NVML device telemetry can be present independently of WDDM.
        return supported | CollectorCapabilities.DeviceGraphics;
    }

    private static readonly Lazy<CollectorCapabilities> Host = new(() =>
        Detect(Platform.IsWindows(), Platform.IsLinux(), Platform.IsMacOS(), Environment.Is64BitProcess, HostVersion()));
    internal static CollectorCapabilities Current() => Host.Value;

    private static Version HostVersion() {
        if (Platform.IsWindows()) {
            // Environment.OSVersion can report the compatibility version in Windows PowerShell 5.1.
            var version = new NativeVersion { Size = (uint)Marshal.SizeOf<NativeVersion>(), ServicePack = "" };
            int status = RtlGetVersion(ref version);
            if (status == 0) {
                return new Version((int)version.Major, (int)version.Minor, (int)version.Build);
            }
            throw new InvalidOperationException($"Could not determine Windows collector capabilities (RtlGetVersion 0x{status:X8}).");
        }
        return Environment.OSVersion.Version;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeVersion {
        public uint Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;
    }
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlGetVersion(ref NativeVersion version);
}
