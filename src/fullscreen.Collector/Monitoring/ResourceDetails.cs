namespace fullscreen.Monitoring;

/// <summary>CPU accounting for a logical processor; temperatures are separate physical sensors.</summary>
public sealed record CpuCore(int Id, double? Usage, double? User = null, double? System = null, double? Wait = null, double? Steal = null, double? Megahertz = null);
/// <summary>A temperature sensor with its hardware scope, in Celsius.</summary>
public sealed record TemperatureReading(string Name, double Celsius);
/// <summary>Host CPU identity and current accounting.</summary>
public sealed record CpuDetails(string Name, CpuCore[] Cores, TemperatureReading[] Temperatures,
    double? UptimeSeconds = null, double? Load1 = null, double? Load5 = null, double? Load15 = null, int? PhysicalCores = null);
/// <summary>OS-defined memory quantities, in bytes. Categories may overlap and must not be added together.</summary>
public sealed record MemoryDetails(ulong? Free = null, ulong? Cached = null, ulong? SwapTotal = null, ulong? SwapUsed = null,
    ulong? Committed = null, ulong? CommitLimit = null, ulong? Compressed = null, ulong? Wired = null, string Scope = "",
    double? PressureSome10 = null, double? PressureFull10 = null);
/// <summary>A volume capacity or a device activity row; unlike volumes, devices have no filesystem capacity.</summary>
public sealed record DiskRow(string Id, string Name, string Kind, string FileSystem = "", ulong? Total = null, ulong? Free = null,
    double? ReadBytes = null, double? WriteBytes = null, double? ReadIops = null, double? WriteIops = null,
    double? Busy = null, double? ReadLatency = null, double? WriteLatency = null, double? Queue = null, double? Temperature = null,
    string Status = "") {
    /// <summary>Filesystems mapped to this device. Shared or bind-mounted filesystems appear once.</summary>
    public DiskVolume[] Volumes { get; init; } = [];
    /// <summary>Whether this row can contribute to the device overview without counting a stacked layer twice.</summary>
    public bool Overview { get; init; } = true;
}
/// <summary>A mounted filesystem's user-visible capacity, distinct from raw disk size.</summary>
public sealed record DiskVolume(string Id, string Mount, string FileSystem, ulong Total, ulong Available);
