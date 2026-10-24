using System;

namespace fullscreen.Monitoring;
/// <summary>Selects which performance counters a collection cycle refreshes.</summary>
[Flags]
public enum CollectorMetrics {
    /// <summary>Collects no optional metric groups.</summary>
    None = 0,
    /// <summary>Collects processes metrics.</summary>
    Processes = 1,
    /// <summary>Collects cpu metrics.</summary>
    Cpu = 2,
    /// <summary>Collects memory metrics.</summary>
    Memory = 4,
    /// <summary>Collects network metrics.</summary>
    Network = 8,
    /// <summary>Collects gpu metrics.</summary>
    Gpu = 16,
    /// <summary>Collects process gpu metrics.</summary>
    ProcessGpu = 32,
    /// <summary>Collects process disk metrics.</summary>
    ProcessDisk = 64,
    /// <summary>Collects process network metrics.</summary>
    ProcessNetwork = 128,
    /// <summary>Collects owners metrics.</summary>
    Owners = 256,
    /// <summary>Collects services metrics.</summary>
    Services = 512,
    /// <summary>Collects users metrics.</summary>
    Users = 1024,
    /// <summary>Collects all supported metric groups.</summary>
    All = 2047
}
