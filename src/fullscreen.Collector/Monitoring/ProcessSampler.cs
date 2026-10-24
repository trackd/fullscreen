using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;
/// <summary>Stores process identity, resource usage, ownership, and optional platform metadata.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="CpuPercent">The CPU utilization percentage.</param>
/// <param name="WorkingSet">The resident process memory in bytes.</param>
/// <param name="Threads">The process thread count.</param>
/// <param name="GpuPercent">The process GPU utilization percentage, or null when unavailable. Defaults to null.</param>
/// <param name="GpuMemory">The process graphics memory usage in bytes, or null when unavailable. Defaults to
/// null.</param>
/// <param name="ParentId">The parent process identifier, or null when unavailable. Defaults to null.</param>
/// <param name="UserName">The account name associated with the session or process. Defaults to null.</param>
/// <param name="StartTimeTicks">The process start identity used to distinguish reused identifiers. Defaults to
/// 0.</param>
/// <param name="CpuTimeTicks">The accumulated process CPU time in TimeSpan ticks. Defaults to 0.</param>
/// <param name="Status">The raw Git porcelain status text. Defaults to null.</param>
/// <param name="DiskBytesPerSecond">The process disk throughput in bytes per second, or null when unavailable. Defaults
/// to null.</param>
/// <param name="NetworkBytesPerSecond">The process network throughput in bytes per second, or null when unavailable.
/// Defaults to null.</param>
public readonly record struct ProcessRow(
    int Id,
    string Name,
    double CpuPercent,
    long WorkingSet,
    int Threads,
    double? GpuPercent = null,
    ulong? GpuMemory = null,
    int? ParentId = null,
    string? UserName = null,
    long StartTimeTicks = 0,
    long CpuTimeTicks = 0,
    string? Status = null,
    double? DiskBytesPerSecond = null,
    double? NetworkBytesPerSecond = null
);
/// <summary>Single-writer sampler. Buffers are reused; consumers copy under the gate.</summary>
public sealed partial class ProcessSampler : IDisposable {
    private readonly Dictionary<int, Entry> _previous = new(1024);
    private readonly List<int> _dead = new(128);
    private readonly ProcessOwnerReader _owners = new();
    private WindowsThreadIdentity[] _threadIdentities = new WindowsThreadIdentity[4096];
    private int _threadCount;
    internal WindowsThreadIdentity[] ThreadIdentities => _threadIdentities;
    internal int ThreadCount => _threadCount;

    private ProcessRow[] _rows = new ProcessRow[1024];
    private long _lastTimestamp;
    private int _generation;
    private int _count;
    private bool _collectOwners = true, _collectDisk = true;
    /// <summary>Gets the synchronization object protecting shared collector state.</summary>
    public object Gate { get; } = new();
    /// <summary>Gets the active entry count.</summary>
    public int Count => _count;
    /// <summary>Gets the elapsed metric-collection time in milliseconds.</summary>
    public double SampleMilliseconds { get; private set; }
    /// <summary>Gets the managed bytes allocated during the latest collection.</summary>
    public long AllocatedBytes { get; private set; }
    /// <summary>Gets the total process working-set memory in bytes.</summary>
    public long TotalWorkingSet { get; private set; }
    /// <summary>Gets the source rows or requested terminal row count.</summary>
    public ArraySegment<ProcessRow> Rows => new(_rows, 0, _count);

    private readonly record struct Entry(long CpuTicks, long StartTicks, string Name, int Generation, long? DiskBytes);
    /// <summary>Refreshes process counters and optional ownership and disk metrics.</summary>
    public void Sample() =>
        Sample(true, true);
    /// <summary>Refreshes process counters and optional ownership and disk metrics.</summary>
    /// <param name="owners">Whether to resolve and cache process account names.</param>
    /// <param name="disk">Whether to collect per-process disk counters.</param>
    public void Sample(bool owners, bool disk) {
        _collectOwners = owners;
        _collectDisk = disk;
        long start = Stopwatch.GetTimestamp();
        long allocated = Compat.AllocatedBytes();
        double seconds = _lastTimestamp == 0 ? 0 : Compat.ElapsedTime(_lastTimestamp, start).TotalSeconds;
        _generation++;
        lock (Gate) {
            _count = 0;
            _threadCount = 0;
            TotalWorkingSet = 0;
            if (Platform.IsWindows() && Environment.Is64BitProcess) {
                SampleWindows(seconds);
            }
            else if (Platform.IsLinux()) {
                SampleLinux(seconds);
            }
            else {
                SamplePortable(seconds);
            }

            _dead.Clear();
            foreach (var pair in _previous) {
                if (pair.Value.Generation != _generation) {
                    _dead.Add(pair.Key);
                }
            }

            foreach (int id in _dead) {
                _previous.Remove(id);
                _owners.Remove(id);
                _ioDenied.Remove(id);
            }

            _lastTimestamp = start;
            SampleMilliseconds = Compat.ElapsedTime(start).TotalMilliseconds;
            AllocatedBytes = Compat.AllocatedBytes() - allocated;
        }
    }

    private void SamplePortable(double seconds) {
        var processes = Process.GetProcesses();
        try {
            foreach (var process in processes) {
                try {
                    int id = process.Id;
                    if (id == 0) {
                        continue;
                    }

                    long cpu = process.TotalProcessorTime.Ticks;
                    long birth = Platform.IsWindows() ? process.StartTime.ToFileTimeUtc() : process.StartTime.ToUniversalTime().Ticks;
                    string name = _previous.TryGetValue(id, out var previous) && previous.StartTicks == birth ? previous.Name : process.ProcessName;
                    int? parent = null;
                    uint? uid = null;
                    string? status = null;
                    long? diskBytes = null;
                    if (Platform.IsMacOS()) {
                        if (_collectDisk) {
                            diskBytes = ProcessOwnerReader.ReadMacDisk(id);
                        }

                        ProcessOwnerReader.ReadMacIdentity(id, birth, out parent, out uid, out status);
                        if (!parent.HasValue) {
                            diskBytes = null;
                        }
                    }

                    Add(
                        id,
                        name,
                        cpu,
                        birth,
                        process.WorkingSet64,
                        process.Threads.Count,
                        seconds,
                        parent,
                        uid,
                        status,
                        diskBytes
                    );
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException
                    or UnauthorizedAccessException) {
                    // Protected/exiting processes are expected during an OS snapshot.
                }
            }
        }
        finally {
            foreach (var process in processes) {
                process.Dispose();
            }
        }
    }

    private void Add(
        int id,
        string name,
        long cpu,
        long birth,
        long memory,
        int threads,
        double seconds,
        int? parent = null,
        uint? uid = null,
        string? status = null,
        long? diskBytes = null
    ) {
        bool known = _previous.TryGetValue(id, out var previous) && previous.StartTicks == birth;
        double percent = known && seconds > 0 ? Compat.Clamp(
            (cpu - previous.CpuTicks) / (seconds * TimeSpan.TicksPerSecond * Environment.ProcessorCount) * 100,
            0,
            100
        ) : 0;
        double? diskRate = known && seconds > 0 && diskBytes.HasValue && previous.DiskBytes.HasValue
            && diskBytes >= previous.DiskBytes ? (diskBytes.Value - previous.DiskBytes.Value) / seconds : null;
        _previous[id] = new(cpu, birth, name, _generation, diskBytes);
        if (_count == _rows.Length) {
            Array.Resize(ref _rows, _rows.Length * 2);
        }

        _rows[_count++] = new(
            id,
            name,
            percent,
            memory,
            threads,
            ParentId: parent,
            UserName: _collectOwners ? _owners.Read(id, birth, uid) : null,
            StartTimeTicks: birth,
            CpuTimeTicks: cpu,
            Status: status,
            DiskBytesPerSecond: diskRate
        );
        TotalWorkingSet += memory;
    }

    /// <inheritdoc/>
    public void Dispose() =>
        _previous.Clear();
}
