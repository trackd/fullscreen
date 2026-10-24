using System.Diagnostics;

namespace fullscreen.Monitoring;
/// <summary>Runtime-only collector; safe to load into a remote PowerShell process without the UI or module.</summary>
public sealed class PerformanceCollector : IDisposable {
    private ProcessSampler? _processes;
    private readonly SystemSampler _system = new();
    private NetworkSampler? _network;
    private GpuSampler? _gpu;
    private WindowsIoSampler? _io;
    private CollectorMetrics _ioMetrics;
    private ServiceSampler? _services;
    private UserSessionSampler? _users;
    /// <summary>Gets the reusable metric snapshot to read or fill.</summary>
    public CollectorFrame Frame { get; } = new() {
        MachineName = Environment.MachineName,
        Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription
    };

    /// <summary>Refreshes requested metrics in the collector&apos;s reusable frame.</summary>
    /// <returns>The reusable frame; copy it before retaining it across later captures.</returns>
    public CollectorFrame Capture() =>
        Capture(CollectorMetrics.All);
    /// <summary>Refreshes requested metrics in the collector&apos;s reusable frame.</summary>
    /// <param name="metrics">The metric groups requested for this capture.</param>
    /// <returns>The reusable frame; copy it before retaining it across later captures.</returns>
    public CollectorFrame Capture(CollectorMetrics metrics) {
        long start = Stopwatch.GetTimestamp();
        long allocated = Compat.AllocatedBytes();
        Frame.ProcessCount = Frame.NetworkCount = Frame.GpuCount = 0;
        Frame.ServiceCount = 0;
        Frame.UserCount = 0;
        Frame.UserStatus = "Not requested by the current view";
        if ((metrics & CollectorMetrics.Users) != 0) {
            (_users ??= new()).Sample(Frame);
        }
        else {
            _users = null;
        }

        Frame.ServiceStatus = "Not requested by the current view";
        if ((metrics & CollectorMetrics.Services) != 0) {
            (_services ??= new()).Sample(Frame);
        }

        Frame.ReceiveBytesPerSecond = Frame.SendBytesPerSecond = null;
        Frame.GpuStatus = Frame.NetworkStatus = "Not requested by the current view";
        _system.Sample((metrics & CollectorMetrics.Cpu) != 0, (metrics & CollectorMetrics.Memory) != 0);
        if ((metrics & CollectorMetrics.Network) != 0) {
            (_network ??= new()).Sample(Frame);
        }
        else {
            _network = null;
        }

        if ((metrics & (CollectorMetrics.Gpu | CollectorMetrics.ProcessGpu)) != 0) {
            (_gpu ??= new()).Sample(
                Frame,
                (metrics & CollectorMetrics.Gpu) != 0,
                (metrics & CollectorMetrics.ProcessGpu) != 0
            );
        }
        else {
            _gpu?.Dispose();
            _gpu = null;
        }

        if ((metrics & CollectorMetrics.Processes) != 0) {
            (_processes ??= new()).Sample((metrics & CollectorMetrics.Owners) != 0, (metrics & CollectorMetrics.ProcessDisk) != 0);
            Frame.Processes = CollectorFrame.EnsureCapacity(Frame.Processes, _processes.Count);
            Frame.ProcessCount = _processes.Count;
            Array.Copy(_processes.Rows.Array!, Frame.Processes, _processes.Count);
            if ((metrics & CollectorMetrics.ProcessGpu) != 0) {
                _gpu!.Enrich(Frame.Processes, Frame.ProcessCount);
            }

            if ((metrics & (CollectorMetrics.ProcessDisk | CollectorMetrics.ProcessNetwork)) != 0) {
                var ioMetrics = metrics & (CollectorMetrics.ProcessDisk | CollectorMetrics.ProcessNetwork);
                if (_ioMetrics != ioMetrics) {
                    _io?.Dispose();
                    _io = null;
                    _ioMetrics = ioMetrics;
                }

                (_io ??= new()).Enrich(
                    Frame.Processes,
                    _processes.ThreadIdentities,
                    (metrics & CollectorMetrics.ProcessDisk) != 0,
                    (metrics & CollectorMetrics.ProcessNetwork) != 0,
                    Frame.ProcessCount,
                    _processes.ThreadCount
                );
            }
            else {
                _io?.Dispose();
                _io = null;
            }
        }
        else {
            _processes?.Dispose();
            _processes = null;
            _io?.Dispose();
            _io = null;
        }

        Frame.CpuPercent = _system.CpuPercent;
        Frame.TotalMemory = _system.TotalMemory;
        Frame.AvailableMemory = _system.AvailableMemory;
        Frame.CapturedAt = DateTimeOffset.UtcNow;
        Frame.SampleMilliseconds = Compat.ElapsedTime(start).TotalMilliseconds;
        Frame.SampleAllocation = Compat.AllocatedBytes() - allocated;
        return Frame;
    }

    /// <summary>Captures metrics and serializes the active frame for transport.</summary>
    /// <returns>The Base64-encoded frame.</returns>
    public string CaptureBase64() =>
        Convert.ToBase64String(FrameCodec.Encode(Capture()));
    /// <summary>Requests refresh of cached service metadata on the next applicable capture.</summary>
    public void InvalidateServices() =>
        _services?.Invalidate();
    /// <summary>Requests refresh of cached login-session metadata on the next applicable capture.</summary>
    public void InvalidateUsers() =>
        _users?.Invalidate();
    /// <summary>Captures metrics and serializes the active frame for transport.</summary>
    /// <param name="metrics">The metric groups requested for this capture.</param>
    /// <returns>The Base64-encoded frame.</returns>
    public string CaptureBase64(int metrics) =>
        Convert.ToBase64String(FrameCodec.Encode(Capture((CollectorMetrics)metrics)));
    /// <inheritdoc/>
    public void Dispose() {
        try {
            _io?.Dispose();
        }
        finally {
            try {
                _gpu?.Dispose();
            }
            finally {
                _processes?.Dispose();
            }
        }
    }
}
