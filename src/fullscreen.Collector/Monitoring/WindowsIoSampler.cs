using System.ComponentModel;
using System.Diagnostics;

namespace fullscreen.Monitoring;
/// <summary>Demand-driven ETW sampling. Access denial leaves counters unavailable, never synthetic zeroes.</summary>
internal sealed class WindowsIoSampler : IDisposable {
    private WindowsKernelTrace? _trace;
    private EtwIoCounters? _counters;
    private long _timestamp;
    private bool _attempted, _disposed;
    internal CollectorCapabilities Capabilities { get; private set; } =
        CollectorCapabilities.ProcessDisk | CollectorCapabilities.ProcessNetwork;
    internal static bool UnsupportedProvider(Exception error) =>
        error is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException
        || error is Win32Exception native && native.NativeErrorCode is 1 or 50 or 120 or 127;
    internal void Enrich(
        ProcessRow[] rows,
        WindowsThreadIdentity[] threads,
        bool disk,
        bool network,
        int rowCount = -1,
        int threadCount = -1
    ) {
        if (_disposed || !Platform.IsWindows() || !Environment.Is64BitProcess) {
            return;
        }

        if (!_attempted) {
            _attempted = true;
            _counters = new(disk, network);
            _counters.Sample(rows, threads, 0, 0, DateTime.UtcNow.ToFileTimeUtc(), rowCount, threadCount);
            try {
                _trace = new(_counters, disk, network);
            }
            catch (Exception error) when (error is Win32Exception or PlatformNotSupportedException or DllNotFoundException
                or EntryPointNotFoundException) {
                if (UnsupportedProvider(error)) {
                    Capabilities = CollectorCapabilities.None;
                }
                return;
            }
        }

        if (_trace is null) {
            return;
        }

        if (!_trace.Healthy || _counters!.Faulted || !_trace.TryGetLoss(out ulong lost)) {
            _trace.Dispose();
            _trace = null;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double seconds = _timestamp == 0 ? 0 : Compat.ElapsedTime(_timestamp, now).TotalSeconds;
        _counters!.Sample(rows, threads, seconds, lost, DateTime.UtcNow.ToFileTimeUtc(), rowCount, threadCount);
        _timestamp = now;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _trace?.Dispose();
        _trace = null;
    }
}
