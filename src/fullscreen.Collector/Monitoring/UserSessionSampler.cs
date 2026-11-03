using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;
/// <summary>Structured local login inventory, created only when Users metrics are requested.</summary>
internal sealed partial class UserSessionSampler {
    private readonly CollectorFrame _cache = new();
    private long _last;
    internal void Invalidate() =>
        _last = 0;
    public void Sample(CollectorFrame frame) {
        if (_last == 0 || Compat.ElapsedTime(_last).TotalSeconds >= 5) {
            _cache.UserCount = 0;
            try {
                if (Platform.IsWindows()) {
                    ReadWindows(_cache);
                }
                else {
                    ParseWho(ServiceSampler.Run("/usr/bin/who", "-u"), _cache, detailed: true);
                }

                _cache.UserStatus = _cache.UserCount == 0 ? "No login sessions reported" : "Login sessions · refreshed every 5s while Users is visible";
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or TimeoutException
                or UnauthorizedAccessException) {
                _cache.UserCount = 0;
                _cache.UserStatus = "Login sessions unavailable: " + error.Message;
            }

            _last = Stopwatch.GetTimestamp();
        }

        frame.Users = CollectorFrame.EnsureCapacity(frame.Users, _cache.UserCount);
        Array.Copy(_cache.Users, frame.Users, _cache.UserCount);
        frame.UserCount = _cache.UserCount;
        frame.UserStatus = _cache.UserStatus;
    }

    private static void Add(CollectorFrame frame, UserSessionRow row) {
        frame.Users = CollectorFrame.EnsureCapacity(frame.Users, frame.UserCount + 1);
        frame.Users[frame.UserCount++] = row;
    }

    internal static void ParseWho(string output, CollectorFrame frame, bool detailed = false) {
        foreach (string line in output.Split('\n')) {
            string[] fields = line.Trim().Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3) {
                continue;
            }

            string detail = fields[2], host = "";
            int start = detail.LastIndexOf(" (", StringComparison.Ordinal);
            if (start >= 0 && detail.EndsWith(")", StringComparison.Ordinal)) {
                host = detail.Substring(start + 2, detail.Length - start - 3);
                detail = detail[..start];
            }

            TimeSpan? idle = null;
            DateTimeOffset? logon = null;
            if (detailed) {
                string[] parts = detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                int dateParts = parts.Length > 0 && parts[0].Length == 10 && parts[0][4] == '-' ? 2 : 3;
                if (parts.Length < dateParts + 2) {
                    continue;
                }

                detail = string.Join(" ", parts.Take(dateParts));
                string idleText = parts[dateParts];
                if (idleText == ".") {
                    idle = TimeSpan.Zero;
                }
                else if (TimeSpan.TryParseExact(idleText, @"h\:mm", CultureInfo.InvariantCulture, out var duration)) {
                    idle = duration;
                }

                if (dateParts == 2 && DateTime.TryParseExact(
                    detail,
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var local
                ) && !TimeZoneInfo.Local.IsInvalidTime(local) && !TimeZoneInfo.Local.IsAmbiguousTime(local)) {
                    logon = new DateTimeOffset(local);
                }
            }

            Add(frame, new(fields[0], fields[1], "Logged in", host, detail, LogonTime: logon, IdleTime: idle));
        }
    }

    private static unsafe void ReadWindows(CollectorFrame frame) {
        uint level = 1;
        if (Enumerate(0, ref level, 0, out var memory, out uint count) == 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try {
            for (uint index = 0; index < count; index++) {
                var row = ((SessionInfo*)memory)[index];
                string? user = Marshal.PtrToStringUni(row.UserName);
                if (string.IsNullOrWhiteSpace(user)) {
                    continue;
                }

                string? domain = Marshal.PtrToStringUni(row.DomainName);
                var session = new UserSessionRow(
                    string.IsNullOrEmpty(domain) ? user : domain +
                    "\\" + user,
                    Marshal.PtrToStringUni(row.SessionName) ?? "",
                    StateName(row.State),
                    Marshal.PtrToStringUni(row.HostName) ?? "",
                    "",
                    (int)row.SessionId
                );
                if (session.IsNamedWindowsSession) {
                    Add(frame, EnrichWindows(session, row.SessionId));
                }
            }
        }
        finally {
            if (memory != 0) {
                _ = Free(2, memory, count);
            }
        }
    }

    private static string StateName(int state) =>
        state switch {
            0 => "Active",
            1 => "Connected",
            2 => "Connecting",
            3 => "Shadow",
            4 => "Disconnected",
            5 => "Idle",
            6 => "Listen",
            7 => "Reset",
            8 => "Down",
            9 => "Initializing",
            _ => "Unknown"
        };
    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo {
        public uint EnvironmentId;
        public int State;
        public uint SessionId;
        public nint SessionName, HostName, UserName, DomainName, FarmName;
    }

    // The collector also runs on .NET Framework; LibraryImport is unavailable there.
    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsExW", ExactSpelling = true, SetLastError = true)]
    private static extern int Enumerate(nint server, ref uint level, uint filter, out nint sessions, out uint count);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSFreeMemoryExW", ExactSpelling = true)]
    private static extern int Free(int type, nint memory, uint count);
}
