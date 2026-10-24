using System.Runtime.InteropServices;
using System.Globalization;
using System.ComponentModel;

namespace fullscreen.Monitoring;

internal sealed partial class UserSessionSampler {
    // Same WTSLogoffSession API used by TerminalSessions; recheck the session
    // identity because Windows can reuse an ID while confirmation is open.
    internal static void LogoffSession(int id, string userName, long logonTime) {
        if (!Platform.IsWindows()) {
            throw new PlatformNotSupportedException("Session logoff is available on Windows targets only.");
        }

        if (id < 0 || string.IsNullOrWhiteSpace(userName) || logonTime <= 0) {
            throw new ArgumentException("A session ID, account and logon time are required.");
        }

        var frame = new CollectorFrame();
        ReadWindows(frame);
        var session = frame.Users.Take(frame.UserCount).FirstOrDefault(row => row.SessionId == id);
        if (!MatchesSession(session, id, userName, logonTime)) {
            throw new InvalidOperationException("Session identity changed; refresh before trying again.");
        }

        // Do not block the collector waiting for applications to finish logging off.
        if (Logoff(0, (uint)id, 0) == 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static bool MatchesSession(
        UserSessionRow session,
        int id,
        string userName,
        long logonTime
    ) =>
        id >= 0 && logonTime > 0 && session.SessionId == id
        && string.Equals(session.UserName, userName, StringComparison.OrdinalIgnoreCase)
        && session.LogonTime?.ToFileTime() == logonTime;
    private static UserSessionRow EnrichWindows(UserSessionRow session, uint id) {
        var info = Query<SessionDetails>(id, 24); // WTSSessionInfo
        var protocol = Query<ushort>(id, 16); // WTSClientProtocolType
        var logon = FileTime(info?.LogonTime ?? 0);
        var lastInput = FileTime(info?.LastInputTime ?? 0);
        var current = FileTime(info?.CurrentTime ?? 0);
        return session with {
            LogonTime = logon,
            LoginTime = logon?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            LastInputTime = lastInput,
            IdleTime = IdleDuration(lastInput, current),
            ConnectTime = FileTime(info?.ConnectTime ?? 0),
            DisconnectTime = FileTime(info?.DisconnectTime ?? 0),
            ClientName = QueryString(id, 10), // WTSClientName
            Protocol = protocol switch {
                0 => "Console",
                2 => "RDP",
                _ => ""
            }
        };
    }

    internal static DateTimeOffset? FileTime(long value) {
        if (value <= 0) {
            return null;
        }

        try {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(value));
        }
        catch (ArgumentOutOfRangeException) {
            return null;
        }
    }

    internal static TimeSpan? IdleDuration(
        DateTimeOffset? lastInput,
        DateTimeOffset? current
    ) =>
        lastInput.HasValue && current.HasValue && current >= lastInput ? current - lastInput : null;
    private static unsafe T? Query<T>(uint id, int kind)
        where T : unmanaged {
        nint memory = 0;
        try {
            return QuerySession(0, id, kind, out memory, out uint bytes) != 0 && memory != 0 && bytes >= sizeof(T) ? *(T*)memory : null;
        }
        finally {
            if (memory != 0) {
                FreeMemory(memory);
            }
        }
    }

    private static string QueryString(uint id, int kind) {
        nint memory = 0;
        try {
            if (QuerySession(0, id, kind, out memory, out uint bytes) == 0 || memory == 0 || bytes < 2
                || bytes > 65536) {
                return "";
            }

            return (Marshal.PtrToStringUni(memory, (int)bytes / 2) ?? "").TrimEnd('\0');
        }
        finally {
            if (memory != 0) {
                FreeMemory(memory);
            }
        }
    }

    // WTSINFOW, wtsapi32.h. LARGE_INTEGER fields require the default eight-byte alignment.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SessionDetails {
        public int State;
        public uint SessionId, IncomingBytes, OutgoingBytes, IncomingFrames, OutgoingFrames, IncomingCompressedBytes, OutgoingCompressedBytes;
        public fixed char StationName[32];
        public fixed char Domain[17];
        public fixed char UserName[21];
        public long ConnectTime, DisconnectTime, LastInputTime, LogonTime, CurrentTime;
    }

    // netstandard2.0 also runs under .NET Framework, where LibraryImport is unavailable.
    [DllImport("wtsapi32.dll", EntryPoint = "WTSLogoffSession", ExactSpelling = true, SetLastError = true)]
    private static extern int Logoff(nint server, uint id, int wait);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", ExactSpelling = true)]
    private static extern int QuerySession(nint server, uint id, int kind, out nint memory, out uint bytes);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory", ExactSpelling = true)]
    private static extern void FreeMemory(nint memory);
}
