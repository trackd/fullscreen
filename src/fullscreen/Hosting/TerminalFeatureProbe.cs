using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using XenoAtom.Terminal;

namespace fullscreen.Hosting;

internal interface ITerminalQueryTransport {
    bool CanQuery { get; }
    bool KeyAvailable { get; }
    long ElapsedMilliseconds { get; }
    IDisposable EnterQueryMode();
    ConsoleKeyInfo Read();
    void Write(string query);
    void Wait();
    void PreserveInput(IReadOnlyList<ConsoleKeyInfo> keys);
}

internal sealed record TerminalProbeResult(
    bool Attempted,
    bool Completed,
    bool Sixel,
    bool Regis,
    TerminalPixelMetrics? Metrics
) {
    internal static TerminalProbeResult Unknown { get; } = new(false, false, false, false, null);
    internal static TerminalProbeResult Incomplete { get; } = new(true, false, false, false, null);

    internal TerminalGraphicsCapabilities Capabilities => new() {
        SupportState = Sixel || Regis ? TerminalGraphicsSupportState.Confirmed : TerminalGraphicsSupportState.Unsupported,
        PreferredProtocol = Sixel ? TerminalGraphicsProtocol.Sixel : TerminalGraphicsProtocol.None,
        SupportedProtocols = Sixel ? [TerminalGraphicsProtocol.Sixel] : [],
        SupportsStaticImages = Sixel || Regis,
        SupportsRealTimeUpdates = Sixel || Regis,
        PixelMetrics = Metrics,
        DetectionSource = Completed ? "VT DA1" : "VT unavailable",
        Diagnostics = [Completed ? "Graphics features confirmed by DA1." : "No complete DA1 reply; using terminal cells."]
    };
}

/// <summary>Owns one fenced startup query before XenoAtom starts reading input.</summary>
internal static class TerminalFeatureProbe {
    internal const string DeviceQuery = "\x1b[c";
    internal const string Query = "\x1b[16t\x1b[14t\x1b[18t\x1b[c";
    internal const int TimeoutMilliseconds = 1500;
    private static readonly ConditionalWeakTable<TerminalInstance, TerminalProbeResult> Results = [];
    private static readonly TerminalProbeCache ConsoleCache = new();
    private static readonly Lock QueryLock = new();

    internal static TerminalProbeResult Get(TerminalInstance terminal, CancellationToken token = default) {
        lock (QueryLock) {
            if (Results.TryGetValue(terminal, out var known)) {
                return known;
            }
            var transport = new ConsoleTerminalQueryTransport(terminal);
            if (!transport.CanQuery) {
                return TerminalProbeResult.Unknown;
            }
            token.ThrowIfCancellationRequested();
            var result = ConsoleCache.Get(terminal.Size, () => Read(transport, terminal.Size, token));
            Record(terminal, result);
            return result;
        }
    }

    internal static void Record(TerminalInstance terminal, TerminalProbeResult result) {
        if (!result.Attempted) {
            return;
        }
        Results.Remove(terminal);
        Results.Add(terminal, result);
    }

    internal static TerminalProbeResult Read(
        ITerminalQueryTransport transport,
        TerminalSize size,
        CancellationToken token = default
    ) {
        token.ThrowIfCancellationRequested();
        if (!transport.CanQuery || transport.KeyAvailable) {
            return TerminalProbeResult.Unknown;
        }
        bool attempted = false;
        try {
            using var mode = transport.EnterQueryMode();
            // Input may have arrived while acquiring raw mode. It still belongs to the user.
            if (transport.KeyAvailable) {
                return TerminalProbeResult.Unknown;
            }
            token.ThrowIfCancellationRequested();
            long started = transport.ElapsedMilliseconds;
            attempted = true;
            transport.Write(DeviceQuery);
            bool negotiated = false;
            var sequence = new StringBuilder();
            var keys = new List<ConsoleKeyInfo>();
            int cellWidth = 0, cellHeight = 0, pixelWidth = 0, pixelHeight = 0;
            int columns = size.Columns, rows = size.Rows, received = 0;
            while (transport.ElapsedMilliseconds - started < TimeoutMilliseconds) {
                token.ThrowIfCancellationRequested();
                if (!transport.KeyAvailable) {
                    transport.Wait();
                    continue;
                }
                var key = transport.Read();
                keys.Add(key);
                char value = key.KeyChar;
                if (sequence.Length == 0 && value is not '\x1b' and not '\u009b') {
                    transport.PreserveInput(keys);
                    return TerminalProbeResult.Incomplete;
                }
                sequence.Append(value);
                if (++received > 4096) {
                    return TerminalProbeResult.Incomplete;
                }
                if (!Complete(sequence)) {
                    continue;
                }
                string response = sequence.ToString();
                sequence.Clear();
                if (TryDeviceAttributes(response, out bool sixel, out bool regis)) {
                    if (!negotiated && (sixel || regis)) {
                        // ConPTY can answer DA1 locally while forwarding size queries.
                        // Request geometry only after graphics are advertised.
                        negotiated = true;
                        cellWidth = cellHeight = pixelWidth = pixelHeight = 0;
                        columns = size.Columns;
                        rows = size.Rows;
                        keys.Clear();
                        transport.Write(Query);
                        continue;
                    }
                    TerminalPixelMetrics? metrics = null;
                    if (cellWidth <= 0 && columns > 0 && rows > 0 && pixelWidth >= columns && pixelHeight >= rows) {
                        cellWidth = pixelWidth / columns;
                        cellHeight = pixelHeight / rows;
                    }
                    if (cellWidth > 0 && cellHeight > 0) {
                        columns = Math.Clamp(columns, 1, 2000);
                        rows = Math.Clamp(rows, 1, 1000);
                        metrics = new(columns * cellWidth, rows * cellHeight, cellWidth, cellHeight, columns, rows);
                    }
                    return new(true, true, sixel, regis, metrics);
                }
                if (TrySize(response, out int report, out int width, out int height)) {
                    switch (report) {
                        case 6:
                            cellWidth = width;
                            cellHeight = height;
                            break;
                        case 4:
                            pixelWidth = width;
                            pixelHeight = height;
                            break;
                        case 8:
                            columns = width;
                            rows = height;
                            break;
                    }
                    keys.Clear();
                    continue;
                }
                // Unknown/malformed control replies are not text or keyboard shortcuts.
                return TerminalProbeResult.Incomplete;
            }
            if (sequence.ToString() == "\x1b") {
                transport.PreserveInput(keys);
            }
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        return attempted ? TerminalProbeResult.Incomplete : TerminalProbeResult.Unknown;
    }

    private static bool Complete(StringBuilder sequence) {
        if (sequence[0] == '\u009b') {
            return sequence.Length > 1 && sequence[^1] is >= '@' and <= '~';
        }
        return sequence.Length >= 3 && sequence[1] == '[' && sequence[^1] is >= '@' and <= '~';
    }

    private static ReadOnlySpan<char> CsiBody(string response) {
        if (response.StartsWith("\x1b[", StringComparison.Ordinal)) {
            return response.AsSpan(2);
        }
        return response.StartsWith('\u009b') ? response.AsSpan(1) : [];
    }

    private static bool TryDeviceAttributes(string response, out bool sixel, out bool regis) {
        sixel = regis = false;
        var body = CsiBody(response);
        if (body.Length < 3 || body[0] != '?' || body[^1] != 'c') {
            return false;
        }
        string[] values = body[1..^1].ToString().Split(';');
        for (int index = 0; index < values.Length; index++) {
            if (!int.TryParse(values[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)) {
                sixel = regis = false;
                return false;
            }
            // The first number is a device class, not a feature flag.
            if (index > 0) {
                sixel |= value == 4;
                regis |= value == 3;
            }
        }
        return true;
    }

    private static bool TrySize(string response, out int report, out int width, out int height) {
        report = width = height = 0;
        var body = CsiBody(response);
        if (body.Length < 2 || body[^1] != 't') {
            return false;
        }
        string[] values = body[..^1].ToString().Split(';');
        return values.Length == 3
            && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out report)
            && report is 4 or 6 or 8
            && int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && width is > 0 and <= 1_000_000 && height is > 0 and <= 1_000_000
            && (report != 6 || width <= 4096 && height <= 4096);
    }
}

/// <summary>Reuses completed evidence, and never starts another query after an unfenced attempt.</summary>
internal sealed class TerminalProbeCache {
    private (TerminalSize Size, TerminalProbeResult Result)? _last;
    internal TerminalProbeResult Get(TerminalSize size, Func<TerminalProbeResult> query) {
        if (_last is { } cached && (!cached.Result.Completed || cached.Size == size)) {
            return cached.Result;
        }
        TerminalProbeResult result;
        try {
            result = query();
        }
        catch (OperationCanceledException) {
            _last = (size, TerminalProbeResult.Incomplete);
            throw;
        }
        if (result.Attempted) {
            _last = (size, result);
        }
        return result;
    }
}
