using System.Diagnostics;
using System.Net.NetworkInformation;

namespace fullscreen.Monitoring;
/// <summary>Samples network-interface counters and calculates transfer rates.</summary>
public sealed class NetworkSampler {
    private readonly Dictionary<string, Baseline> _previous = new(StringComparer.Ordinal);
    private readonly List<string> _dead = [];
    private int _generation;
    private NetworkInterface[] _interfaces = [];
    private long _lastDiscovery;
    private readonly record struct Baseline(long Received, long Sent, long Timestamp, int Generation);
    /// <summary>Updates network-interface counters and rates in a frame.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    public void Sample(CollectorFrame frame) {
        frame.NetworkCount = 0;
        frame.ReceiveBytesPerSecond = null;
        frame.SendBytesPerSecond = null;
        _generation++;
        double receive = 0, send = 0;
        bool measured = false;
        try {
            if (_lastDiscovery == 0 || Compat.ElapsedTime(_lastDiscovery).TotalSeconds >= 5) {
                var interfaces = new List<NetworkInterface>();
                foreach (var candidate in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (candidate.OperationalStatus == OperationalStatus.Up
                        && candidate.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && candidate.GetIPProperties().UnicastAddresses.Count > 0) {
                        interfaces.Add(candidate);
                    }
                }

                _interfaces = [.. interfaces];
                _lastDiscovery = Stopwatch.GetTimestamp();
            }

            foreach (var adapter in _interfaces) {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || adapter.OperationalStatus != OperationalStatus.Up) {
                    continue;
                }

                try {
                    var stats = adapter.GetIPStatistics();
                    long timestamp = Stopwatch.GetTimestamp();
                    double? rx = null, tx = null;
                    if (_previous.TryGetValue(adapter.Id, out var old)) {
                        double elapsed = Compat.ElapsedTime(old.Timestamp, timestamp).TotalSeconds;
                        rx = Rate(stats.BytesReceived, old.Received, elapsed);
                        tx = Rate(stats.BytesSent, old.Sent, elapsed);
                    }

                    _previous[adapter.Id] = new(stats.BytesReceived, stats.BytesSent, timestamp, _generation);
                    frame.Networks = CollectorFrame.EnsureCapacity(frame.Networks, frame.NetworkCount + 1);
                    frame.Networks[frame.NetworkCount++] = new(
                        adapter.Id,
                        adapter.Name,
                        rx,
                        tx,
                        stats.BytesReceived,
                        stats.BytesSent,
                        stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors,
                        adapter.Speed
                    );
                    if (rx.HasValue && tx.HasValue) {
                        measured = true;
                        receive += rx.Value;
                        send += tx.Value;
                    }
                }
                catch (Exception error) when (error is NetworkInformationException or PlatformNotSupportedException) {
                }
            }

            frame.NetworkStatus = frame.NetworkCount == 0 ? "No active IP-enabled interfaces" : "IP interfaces · aggregate may include virtual links";
        }
        catch (NetworkInformationException) {
            frame.NetworkStatus = "Interface statistics unavailable";
        }

        if (measured) {
            frame.ReceiveBytesPerSecond = receive;
            frame.SendBytesPerSecond = send;
        }

        _dead.Clear();
        foreach (var pair in _previous) {
            if (pair.Value.Generation != _generation) {
                _dead.Add(pair.Key);
            }
        }

        foreach (string key in _dead) {
            _previous.Remove(key);
        }
    }

    /// <summary>Calculates a transfer rate from monotonic counter samples.</summary>
    /// <param name="current">The current monotonic counter sample.</param>
    /// <param name="previous">The previous monotonic counter sample.</param>
    /// <param name="seconds">The elapsed seconds between counter samples.</param>
    /// <returns>The per-second rate, or null when a valid rate cannot be calculated.</returns>
    public static double? Rate(
        long current,
        long previous,
        double seconds
    ) =>
        seconds > 0 && current >= previous ? (current - previous) / seconds : null;
}
