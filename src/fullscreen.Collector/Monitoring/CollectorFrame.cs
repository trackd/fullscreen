namespace fullscreen.Monitoring;
/// <summary>Stores network-interface identity, throughput, and cumulative counters.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="ReceiveBytesPerSecond">The network receive rate in bytes per second, or null when unavailable.</param>
/// <param name="SendBytesPerSecond">The network send rate in bytes per second, or null when unavailable.</param>
/// <param name="ReceivedBytes">The cumulative received byte count.</param>
/// <param name="SentBytes">The cumulative sent byte count.</param>
/// <param name="Errors">The collection errors associated with the row.</param>
/// <param name="Speed">The interface link speed in bits per second.</param>
public readonly record struct NetworkRow(
    string Id,
    string Name,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond,
    long ReceivedBytes,
    long SentBytes,
    long Errors,
    long Speed
);
/// <summary>Stores graphics-device utilization, memory usage, and temperature.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Provider">The platform provider that supplied the device metrics.</param>
/// <param name="Utilization">The graphics-device utilization percentage, or null when unavailable.</param>
/// <param name="UsedMemory">The used graphics-device memory in bytes, or null when unavailable.</param>
/// <param name="TotalMemory">The total physical memory in bytes.</param>
/// <param name="Temperature">The device temperature in degrees Celsius, or null when unavailable.</param>
public readonly record struct GpuRow(
    string Name,
    string Provider,
    double? Utilization,
    ulong? UsedMemory,
    ulong? TotalMemory,
    double? Temperature
);
/// <summary>Stores a service&apos;s display metadata and optional process ownership.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">The Windows display name or platform service description.</param>
/// <param name="State">The state exposed to the current view or callback.</param>
/// <param name="Detail">Additional platform state details, empty for Windows services.</param>
/// <param name="ProcessId">The associated process identifier, or null when unavailable.</param>
/// <param name="UserName">The account name associated with the session or process. Defaults to null.</param>
public readonly record struct ServiceRow(
    string Name,
    string Description,
    string State,
    string Detail,
    int? ProcessId,
    string? UserName = null
);
/// <summary>Stores a named login session and its client, idle, and logon metadata.</summary>
/// <param name="UserName">The account name associated with the session or process.</param>
/// <param name="Session">The existing PowerShell remoting session.</param>
/// <param name="State">The state exposed to the current view or callback.</param>
/// <param name="Host">The GitHub host name.</param>
/// <param name="LoginTime">The formatted login timestamp.</param>
/// <param name="SessionId">The Windows terminal-services session identifier. Defaults to null.</param>
/// <param name="LogonTime">The expected Windows logon FILETIME used to reject a reused session identifier. Defaults to
/// null.</param>
/// <param name="IdleTime">The time elapsed since the session&apos;s last input, or null when unavailable. Defaults to
/// null.</param>
/// <param name="LastInputTime">The session&apos;s last-input timestamp, or null when unavailable. Defaults to
/// null.</param>
/// <param name="ConnectTime">The session connection timestamp, or null when unavailable. Defaults to null.</param>
/// <param name="DisconnectTime">The session disconnection timestamp, or null when unavailable. Defaults to
/// null.</param>
/// <param name="ClientName">The terminal-services client computer name. Defaults to &quot;&quot;.</param>
/// <param name="Protocol">The terminal-services connection protocol. Defaults to &quot;&quot;.</param>
public readonly record struct UserSessionRow(
    string UserName,
    string Session,
    string State,
    string Host,
    string LoginTime,
    int? SessionId = null,
    DateTimeOffset? LogonTime = null,
    TimeSpan? IdleTime = null,
    DateTimeOffset? LastInputTime = null,
    DateTimeOffset? ConnectTime = null,
    DateTimeOffset? DisconnectTime = null,
    string ClientName = "",
    string Protocol = ""
) {
    /// <summary>Gets whether this WTS row has a named user outside the services session.</summary>
    public bool IsNamedWindowsSession => SessionId > 0 && !string.IsNullOrWhiteSpace(UserName);
}
/// <summary>Reusable snapshot. Counts delimit valid array entries; arrays never escape the source's gate.</summary>
public sealed class CollectorFrame {
    /// <summary>Gets or sets the machine that produced the snapshot.</summary>
    public string MachineName { get; set; } = "";
    /// <summary>Gets or sets the operating-system label reported by the collector.</summary>
    public string Platform { get; set; } = "";
    /// <summary>Gets or sets the collector host's supported optional columns; null means not yet reported.</summary>
    public CollectorCapabilities? Capabilities { get; set; }
    /// <summary>Gets or sets the timestamp of metric capture.</summary>
    public DateTimeOffset CapturedAt { get; set; }
    /// <summary>Gets or sets the CPU utilization percentage.</summary>
    public double? CpuPercent { get; set; }
    /// <summary>Gets or sets the total physical memory in bytes.</summary>
    public ulong? TotalMemory { get; set; }
    /// <summary>Gets or sets the available physical memory in bytes.</summary>
    public ulong? AvailableMemory { get; set; }
    /// <summary>Gets or sets the elapsed metric-collection time in milliseconds.</summary>
    public double SampleMilliseconds { get; set; }
    /// <summary>Gets or sets the managed allocation count in bytes for the capture.</summary>
    public long SampleAllocation { get; set; }
    /// <summary>Gets or sets whether per-process GPU counters are collected.</summary>
    public ProcessRow[] Processes { get; internal set; } = new ProcessRow[1024];
    /// <summary>Gets or sets the reusable network row buffer; only the counted prefix is active.</summary>
    public NetworkRow[] Networks { get; internal set; } = new NetworkRow[16];
    /// <summary>Gets or sets the reusable graphics-device row buffer; only the counted prefix is
    /// active.</summary>
    public GpuRow[] Gpus { get; internal set; } = new GpuRow[8];
    /// <summary>Gets or sets the reusable service buffer; only ServiceCount entries are active.</summary>
    public ServiceRow[] Services { get; internal set; } = new ServiceRow[128];
    /// <summary>Gets or sets the reusable login-session buffer; only UserCount entries are
    /// active.</summary>
    public UserSessionRow[] Users { get; internal set; } = new UserSessionRow[16];
    /// <summary>Gets or sets the number of valid login-session rows.</summary>
    public int UserCount { get; set; }
    /// <summary>Gets or sets the login-session collection status message.</summary>
    public string UserStatus { get; set; } = "Not requested by the current view";
    /// <summary>Gets or sets the number of valid service rows.</summary>
    public int ServiceCount { get; set; }
    /// <summary>Gets or sets the service collection status message.</summary>
    public string ServiceStatus { get; set; } = "Waiting for service manager";
    /// <summary>Gets or sets the number of valid process rows in the process buffer.</summary>
    public int ProcessCount { get; set; }
    /// <summary>Gets or sets the number of valid network-interface rows.</summary>
    public int NetworkCount { get; set; }
    /// <summary>Gets or sets the number of valid graphics-device rows.</summary>
    public int GpuCount { get; set; }
    /// <summary>Gets or sets the graphics collection status message.</summary>
    public string GpuStatus { get; set; } = "Waiting for GPU provider";
    /// <summary>Gets or sets the network collection status message.</summary>
    public string NetworkStatus { get; set; } = "Waiting for interfaces";
    /// <summary>Gets or sets the network receive rate in bytes per second, or null when
    /// unavailable.</summary>
    public double? ReceiveBytesPerSecond { get; set; }
    /// <summary>Gets or sets the network send rate in bytes per second, or null when unavailable.</summary>
    public double? SendBytesPerSecond { get; set; }

    /// <summary>Copies the active data in another frame into this frame&apos;s reusable buffers.</summary>
    /// <param name="other">The source frame to copy.</param>
    public void CopyFrom(CollectorFrame other) {
        MachineName = other.MachineName;
        Platform = other.Platform;
        Capabilities = other.Capabilities;
        CapturedAt = other.CapturedAt;
        CpuPercent = other.CpuPercent;
        TotalMemory = other.TotalMemory;
        AvailableMemory = other.AvailableMemory;
        SampleMilliseconds = other.SampleMilliseconds;
        SampleAllocation = other.SampleAllocation;
        Processes = EnsureCapacity(Processes, other.ProcessCount);
        Networks = EnsureCapacity(Networks, other.NetworkCount);
        Gpus = EnsureCapacity(Gpus, other.GpuCount);
        Services = EnsureCapacity(Services, other.ServiceCount);
        Users = EnsureCapacity(Users, other.UserCount);
        UserCount = other.UserCount;
        UserStatus = other.UserStatus;
        Array.Copy(other.Users, Users, UserCount);
        ServiceCount = other.ServiceCount;
        ServiceStatus = other.ServiceStatus;
        Array.Copy(other.Services, Services, ServiceCount);
        ProcessCount = other.ProcessCount;
        NetworkCount = other.NetworkCount;
        GpuCount = other.GpuCount;
        Array.Copy(other.Processes, Processes, ProcessCount);
        Array.Copy(other.Networks, Networks, NetworkCount);
        Array.Copy(other.Gpus, Gpus, GpuCount);
        GpuStatus = other.GpuStatus;
        NetworkStatus = other.NetworkStatus;
        ReceiveBytesPerSecond = other.ReceiveBytesPerSecond;
        SendBytesPerSecond = other.SendBytesPerSecond;
    }

    /// <summary>Grows an array only when its current capacity is insufficient.</summary>
    /// <param name="array">The existing array to reuse when its capacity is sufficient.</param>
    /// <param name="count">The minimum required array capacity.</param>
    /// <typeparam name="T">The element type stored in the buffer.</typeparam>
    /// <returns>The existing array or a larger replacement.</returns>
    public static T[] EnsureCapacity<T>(T[] array, int count) {
        if (array.Length < count) {
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
        }

        return array;
    }
}
