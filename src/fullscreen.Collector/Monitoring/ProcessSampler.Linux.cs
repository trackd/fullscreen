using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace fullscreen.Monitoring;

public sealed partial class ProcessSampler {
    private readonly byte[] _procBuffer = new byte[8192];
    private readonly Dictionary<int, long> _ioDenied = [];
    private long _clockTicks;
    private void SampleLinux(double seconds) {
        if (_clockTicks == 0) {
            _clockTicks = (long)sysconf(2); // Linux _SC_CLK_TCK.
            if (_clockTicks <= 0) {
                throw new IOException("sysconf(_SC_CLK_TCK) failed.");
            }
        }

        foreach (string directory in Directory.EnumerateDirectories("/proc")) {
            if (!int.TryParse(
                Path.GetFileName(directory),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int id
            )) {
                continue;
            }

            try {
                using var handle = File.Open(Path.Combine(directory, "stat"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int length = handle.Read(_procBuffer, 0, _procBuffer.Length);
                if (!TryReadLinuxStat(new ByteWindow(_procBuffer, 0, length), out var stat)) {
                    continue;
                }

                string name = _previous.TryGetValue(id, out var previous) && previous.StartTicks == stat.StartTicks ? previous.Name : Encoding.UTF8.GetString(_procBuffer, stat.NameStart, stat.NameLength);
                long ticks = (long)(stat.CpuTicks * ((double)TimeSpan.TicksPerSecond / _clockTicks));
                long? disk = _collectDisk ? ReadLinuxDisk(directory, id, stat.StartTicks) : null;
                Add(id, name, ticks, stat.StartTicks, checked(stat.ResidentPages * Environment.SystemPageSize), stat.Threads, seconds, stat.ParentId, status: stat.State switch {
                    'R' => "Running",
                    'S' => "Sleeping",
                    'D' => "Disk wait",
                    'T' or 't' => "Stopped",
                    'Z' => "Zombie",
                    'I' => "Idle",
                    _ => null
                }, diskBytes: disk);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                // A process may disappear or deny access between enumeration and read.
            }
        }
    }

    /// <summary>Stores process fields parsed from a Linux procfs stat record.</summary>
    /// <param name="NameStart">The byte offset of the process name in the raw stat record.</param>
    /// <param name="NameLength">The byte length of the process name.</param>
    /// <param name="CpuTicks">The accumulated CPU counter ticks.</param>
    /// <param name="StartTicks">The process start counter used as a stable identity.</param>
    /// <param name="ResidentPages">The resident memory page count.</param>
    /// <param name="Threads">The process thread count.</param>
    /// <param name="ParentId">The parent process identifier, or null when unavailable.</param>
    /// <param name="State">The state exposed to the current view or callback. Defaults to
    /// &apos;?&apos;.</param>
    public readonly record struct LinuxStat(
        int NameStart,
        int NameLength,
        long CpuTicks,
        long StartTicks,
        long ResidentPages,
        int Threads,
        int ParentId,
        char State = '?'
    );
    internal static bool TryReadLinuxStat(ByteWindow data, out LinuxStat stat) {
        stat = default;
        int open = data.IndexOf((byte)'(');
        int close = data.LastIndexOf((byte)')'); // comm can contain spaces and closing parentheses.
        if (open < 0 || close <= open || close + 2 >= data.Length) {
            return false;
        }

        long user = 0, kernel = 0, birth = 0, rss = 0;
        int threads = 0, index = 0, parent = 0;
        foreach (var field in data[(close + 2)..].Split((byte)' ')) {
            if (field.IsEmpty) {
                continue;
            }

            int current = index++;
            if (current is not (1 or 11 or 12 or 17 or 19 or 21)) {
                continue;
            }

            if (!ByteNumbers.TryParse(field, out long value, out _)) {
                return false;
            }

            switch (current) {
                case 1:
                    if (value is < 0 or > int.MaxValue) {
                        return false;
                    }

                    parent = (int)value;
                    break;
                case 11:
                    user = value;
                    break;
                case 12:
                    kernel = value;
                    break;
                case 17:
                    if (value is < 0 or > int.MaxValue) {
                        return false;
                    }

                    threads = (int)value;
                    break;
                case 19:
                    birth = value;
                    break;
                case 21:
                    rss = Math.Max(0, value);
                    break;
            }
        }

        if (index < 22) {
            return false;
        }

        stat = new(open + 1, close - open - 1, user + kernel, birth, rss, threads, parent, (char)data[close + 2]);
        return true;
    }

    private long? ReadLinuxDisk(string directory, int id, long birth) {
        // Other users' io files stay unreadable for the process lifetime; don't throw for them every sample.
        if (_ioDenied.TryGetValue(id, out long denied) && denied == birth) {
            return null;
        }

        try {
            using var handle = File.Open(Path.Combine(directory, "io"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = handle.Read(_procBuffer, 0, _procBuffer.Length);
            return TryReadLinuxDisk(new ByteWindow(_procBuffer, 0, length));
        }
        catch (UnauthorizedAccessException) {
            _ioDenied[id] = birth;
            return null;
        }
        catch (IOException) {
            return null;
        }
    }

    internal static long? TryReadLinuxDisk(ByteWindow data) {
        long? read = null, write = null;
        foreach (var line in data.Split((byte)'\n')) {
            bool reading = line.StartsWith("read_bytes:"), writing = line.StartsWith("write_bytes:");
            if (!reading && !writing) {
                continue;
            }

            int colon = line.IndexOf((byte)':') + 1;
            while (colon < line.Length && line[colon] == ' ') {
                colon++;
            }

            if (!ByteNumbers.TryParse(line[colon..], out long value, out _) || value < 0) {
                return null;
            }

            if (reading) {
                read = value;
            }
            else {
                write = value;
            }
        }

        return read.HasValue && write.HasValue && read <= long.MaxValue - write ? read.Value + write.Value : null;
    }

    [DllImport("libc", SetLastError = true, ExactSpelling = true)]
    private static extern nint sysconf(int name);
    /// <summary>Parses a Linux procfs process stat record.</summary>
    /// <param name="data">The raw process-information bytes.</param>
    /// <param name="stat">The parsed Linux process stat fields.</param>
    /// <returns>True when the required process fields were parsed.</returns>
    public static bool TryReadLinuxStat(
        byte[] data,
        out LinuxStat stat
    ) =>
        TryReadLinuxStat(new ByteWindow(data), out stat);
    /// <summary>Parses process disk byte counters from Linux procfs data.</summary>
    /// <param name="data">The raw process-information bytes.</param>
    /// <returns>The accumulated disk byte count, or null when unavailable.</returns>
    public static long? TryReadLinuxDisk(byte[] data) =>
        TryReadLinuxDisk(new ByteWindow(data));
}
