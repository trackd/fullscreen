using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using fullscreen.Tables;

namespace fullscreen.Logs;
/// <summary>Orders log severity levels for display and filtering.</summary>
public enum LogLevel {
    /// <summary>Classifies messages at trace severity.</summary>
    Trace,
    /// <summary>Classifies messages at debug severity.</summary>
    Debug,
    /// <summary>Classifies messages at information severity.</summary>
    Information,
    /// <summary>Classifies messages at warning severity.</summary>
    Warning,
    /// <summary>Classifies messages at error severity.</summary>
    Error,
    /// <summary>Classifies messages at critical severity.</summary>
    Critical,
    /// <summary>Classifies messages at unknown severity.</summary>
    Unknown
}

/// <summary>Stores a parsed log message together with its original text.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Timestamp">The sample timestamp.</param>
/// <param name="Level">The lowest included log severity.</param>
/// <param name="Source">The source data, object, or filesystem path.</param>
/// <param name="Message">The parsed log message.</param>
/// <param name="Raw">The unmodified source log line.</param>
public sealed record LogEntry(
    long Id,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string Raw
) {
    /// <summary>Converts this log entry to an interactive table row.</summary>
    /// <returns>The table row retaining this log entry as its source.</returns>
    public TableRow Row() =>
        new(
            Id,
            this,
            [
                Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Level.ToString(),
                TableModel.Text(Path.GetFileName(Source)),
                Message
            ],
            [
                Timestamp,
                Level,
                Source,
                Message
            ],
            TableModel.Text(Source) +
        "\n\n" + Raw
        );
}

/// <summary>Contains a batch of parsed log entries and optional reader status.</summary>
/// <param name="Entries">The active directory entries.</param>
/// <param name="Status">The raw Git porcelain status text. Defaults to null.</param>
public sealed record LogBatch(LogEntry[] Entries, string? Status = null);
/// <summary>Extracts timestamps and severity information from incoming log lines.</summary>
public static class LogParser {
    /// <summary>Parses a log line while retaining its raw text.</summary>
    /// <param name="id">The unique table-row identifier assigned to the parsed entry.</param>
    /// <param name="line">The original log line.</param>
    /// <param name="source">The log source label.</param>
    /// <param name="received">The timestamp used when the log line has no timestamp.</param>
    /// <returns>The parsed log entry.</returns>
    public static LogEntry Parse(long id, string line, string source, DateTimeOffset received) {
        line = line.TrimStart('\uFEFF');
        DateTimeOffset timestamp = received;
        LogLevel level = LogLevel.Unknown;
        string message = line;
        if (line.AsSpan().TrimStart().StartsWith("{")) {
            try {
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
                foreach (var property in document.RootElement.EnumerateObject()) {
                    string name = property.Name;
                    if (name.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || name is "@t" or "time" or "ts") {
                        if (property.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(
                            property.Value.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsed
                        )) {
                            timestamp = parsed;
                        }
                    }
                    else if (name.Equals("level", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("severity", StringComparison.OrdinalIgnoreCase)
                        || name == "@l") {
                        level = ParseLevel(property.Value.ToString());
                    }
                    else if (name.Equals("message", StringComparison.OrdinalIgnoreCase) || name is "@m" or "msg" or "@mt") {
                        message = property.Value.ToString();
                    }
                }
            }
            catch (JsonException) {
            }
        }
        else {
            // ISO timestamps, optionally enclosed in brackets, are common across text loggers.
            var span = line.AsSpan().TrimStart();
            if (!span.IsEmpty && span[0] == '[') {
                span = span[1..];
            }

            int end = span.IndexOfAny(' ', ']');
            if (span.Length >= 19 && span[10] == ' ' && char.IsDigit(span[0])) {
                int afterTime = span[11..].IndexOfAny(' ', ']');
                end = afterTime >= 0 ? afterTime + 11 : span.Length;
            }

            if (end > 0 && DateTimeOffset.TryParse(
                span[..end],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed
            )) {
                timestamp = parsed;
            }

            int limit = Math.Min(160, line.Length);
            int start = 0;
            while (start < limit) {
                while (start < limit && !char.IsLetter(line[start])) {
                    start++;
                }

                int finish = start;
                while (finish < limit && char.IsLetter(line[finish])) {
                    finish++;
                }

                var candidate = ParseLevel(line.AsSpan(start, finish - start));
                if (candidate != LogLevel.Unknown) {
                    level = candidate;
                    break;
                }

                start = finish + 1;
            }
        }

        return new(
            id,
            timestamp,
            level,
            source,
            TableModel.Text(message),
            TableModel.Text(line, LogReader.MaximumLineLength + 64)
        );
    }

    /// <summary>Finds a recognized severity level in log text.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <returns>The detected severity level.</returns>
    public static LogLevel ParseLevel(ReadOnlySpan<char> text) {
        if (text.Equals("trace", StringComparison.OrdinalIgnoreCase)
            || text.Equals("verbose", StringComparison.OrdinalIgnoreCase)
            || text.Equals("vrb", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Trace;
        }

        if (text.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || text.Equals("dbg", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Debug;
        }

        if (text.Equals("information", StringComparison.OrdinalIgnoreCase)
            || text.Equals("info", StringComparison.OrdinalIgnoreCase)
            || text.Equals("inf", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Information;
        }

        if (text.Equals("warning", StringComparison.OrdinalIgnoreCase)
            || text.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || text.Equals("wrn", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Warning;
        }

        if (text.Equals("error", StringComparison.OrdinalIgnoreCase)
            || text.Equals("err", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Error;
        }

        if (text.Equals("critical", StringComparison.OrdinalIgnoreCase)
            || text.Equals("fatal", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ftl", StringComparison.OrdinalIgnoreCase)) {
            return LogLevel.Critical;
        }

        return LogLevel.Unknown;
    }
}

/// <summary>One bounded producer; decoding survives partial UTF-8 writes and CRLF boundaries.</summary>
public sealed class LogReader : IDisposable {
    /// <summary>Stores the maximum initial log bytes read from a file.</summary>
    public const int InitialByteLimit = 8 * 1024 * 1024;
    /// <summary>Stores the maximum retained length of one log line.</summary>
    public const int MaximumLineLength = 16384;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<LogBatch> _batches = Channel.CreateBounded<LogBatch>(new BoundedChannelOptions(4) {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Task _worker;
    private long _nextId;
    /// <summary>Gets the channel containing incoming parsed log batches.</summary>
    public ChannelReader<LogBatch> Batches => _batches.Reader;

    /// <summary>Initializes a new LogReader instance.</summary>
    /// <param name="paths">The filesystem paths to read or operate on.</param>
    /// <param name="follow">Whether to continue reading appended log data.</param>
    /// <param name="maximumCount">The maximum number of input rows retained by the selected count
    /// filter.</param>
    /// <param name="pollMilliseconds">The delay in milliseconds between log polling attempts.</param>
    public LogReader(string[] paths, bool follow, int maximumCount, int pollMilliseconds) {
        _worker = Task.Run(() => Run(paths, follow, maximumCount, pollMilliseconds));
    }

    private sealed class Source(string path) {
        public readonly string Path = path;
        public long Position;
        public DateTime Creation;
        public bool Initialized;
        public readonly Decoder Decoder = new UTF8Encoding(false, false).GetDecoder();
        public readonly StringBuilder Pending = new();
        public bool DropFirst;
        public bool Truncated;
        public string? LastError;
        public byte[] Prefix = [];
        public readonly byte[] PositionWitness = new byte[256];
        public readonly byte[] CurrentWitness = new byte[256];
        public int PositionWitnessLength;
    }

    private async Task Run(string[] paths, bool follow, int maximumCount, int pollMilliseconds) {
        var sources = paths.Select(path => new Source(path)).ToArray();
        byte[] bytes = new byte[65536];
        char[] chars = new char[65536];
        try {
            do {
                foreach (var source in sources) {
                    var initial = new Queue<LogEntry>();
                    var batch = new List<LogEntry>(256);
                    bool first = !source.Initialized;
                    void Emit() {
                        if (source.DropFirst) {
                            source.DropFirst = false;
                            source.Pending.Clear();
                            source.Truncated = false;
                            return;
                        }

                        string line = source.Pending.ToString().TrimEnd('\r');
                        if (source.Truncated) {
                            line += " … [line truncated]";
                        }

                        source.Pending.Clear();
                        source.Truncated = false;
                        if (line.Length == 0) {
                            return;
                        }

                        var entry = LogParser.Parse(Interlocked.Increment(ref _nextId), line, source.Path, DateTimeOffset.UtcNow);
                        if (first) {
                            initial.Enqueue(entry);
                            if (initial.Count > maximumCount) {
                                initial.Dequeue();
                            }
                        }
                        else {
                            batch.Add(entry);
                        }
                    }

                    try {
                        using var stream = new FileStream(
                            source.Path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            4096,
                            FileOptions.SequentialScan
                        );
                        // On Linux filesystems without birth time, .NET can return mutable
                        // ctime/mtime here. An ordinary append must not restart the decoder.
                        // Prefix and length changes still detect rewrites and truncation.
                        DateTime creation = OperatingSystem.IsLinux() ? default : File.GetCreationTimeUtc(stream.SafeFileHandle);
                        byte[] prefix = new byte[(int)Math.Min(256, stream.Length)];
                        stream.ReadExactly(prefix);
                        bool changedPrefix = source.Prefix.Length > 0
                            && (prefix.Length < source.Prefix.Length
                            || !prefix.AsSpan(0, source.Prefix.Length).SequenceEqual(source.Prefix));
                        bool truncated = stream.Length < source.Position;
                        int witnessLength = (int)Math.Min(source.PositionWitness.Length, source.Position);
                        bool changedPositionWitness = false;
                        // shortcut: the Linux witness can miss replacements sharing both samples; use file IDs if that becomes a real case.
                        if (source.Initialized && !truncated && witnessLength > 0 && source.PositionWitnessLength > 0) {
                            stream.Position = source.Position - witnessLength;
                            stream.ReadExactly(source.CurrentWitness.AsSpan(0, witnessLength));
                            changedPositionWitness = source.PositionWitnessLength != witnessLength
                                || !source.CurrentWitness.AsSpan(0, witnessLength).SequenceEqual(source.PositionWitness.AsSpan(0, source.PositionWitnessLength));
                        }

                        bool rotated = source.Initialized
                            && (truncated || creation != source.Creation || changedPrefix || changedPositionWitness);
                        if (!source.Initialized || rotated) {
                            source.Position = !source.Initialized ? Math.Max(0, stream.Length - InitialByteLimit) : 0;
                            source.DropFirst = source.Position > 0;
                            source.Pending.Clear();
                            source.Decoder.Reset();
                            source.Truncated = false;
                            source.PositionWitnessLength = 0;
                        }

                        source.Initialized = true;
                        source.Creation = creation;
                        source.Prefix = prefix;
                        stream.Position = source.Position;
                        long remaining = first ? InitialByteLimit : 256 * 1024;
                        int read;
                        while (remaining > 0 && (read = stream.Read(bytes, 0, (int)Math.Min(bytes.Length, remaining))) > 0) {
                            _stop.Token.ThrowIfCancellationRequested();
                            source.Position += read;
                            remaining -= read;
                            int count = source.Decoder.GetChars(bytes, 0, read, chars, 0, false);
                            for (int i = 0; i < count; i++) {
                                if (chars[i] == '\n') {
                                    Emit();
                                }
                                else if (source.Pending.Length < MaximumLineLength) {
                                    source.Pending.Append(chars[i]);
                                }
                                else {
                                    source.Truncated = true;
                                }
                            }

                            if (batch.Count > 0) {
                                await Publish([.. batch]);
                                batch.Clear();
                            }
                        }

                        source.PositionWitnessLength = (int)Math.Min(source.PositionWitness.Length, source.Position);
                        if (source.PositionWitnessLength > 0) {
                            stream.Position = source.Position - source.PositionWitnessLength;
                            stream.ReadExactly(source.PositionWitness.AsSpan(0, source.PositionWitnessLength));
                        }

                        if (!follow && source.Pending.Length > 0) {
                            Emit();
                        }

                        if (first) {
                            await Publish([.. initial], $"Loaded {source.Path} · initial read capped at 8 MiB");
                        }

                        if (batch.Count > 0) {
                            await Publish([.. batch]);
                        }

                        if (source.LastError is not null) {
                            source.LastError = null;
                            await Publish([], $"Reading {source.Path}");
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                        // Position may have advanced past the stored witness; re-sample it on the next good read.
                        source.PositionWitnessLength = 0;
                        // A failed first read discards its tail; read it again next time.
                        if (first) {
                            source.Initialized = false;
                        }

                        string message = source.Path + ": " + error.Message;
                        if (message != source.LastError) {
                            source.LastError = message;
                            await Publish([], message);
                        }
                    }
                }

                if (follow) {
                    await Task.Delay(pollMilliseconds, _stop.Token);
                }
            }
            while (follow);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
        }
        catch (Exception error) {
            _batches.Writer.TryWrite(new([], error.Message));
        }
        finally {
            _batches.Writer.TryComplete();
        }
    }

    private ValueTask Publish(
        LogEntry[] entries,
        string? status = null
    ) =>
        _batches.Writer.WriteAsync(new(entries, status), _stop.Token);
    /// <inheritdoc/>
    public void Dispose() {
        _stop.Cancel();
        _worker.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
