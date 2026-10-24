using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;
/// <summary>Read-only ownership metadata. Native handles are lexical; denied lookups are cached as unknown.</summary>
public sealed partial class ProcessOwnerReader {
    private readonly Dictionary<int, Owner> _processes = new(1024);
    private readonly Dictionary<string, string> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _users = [];
    private readonly char[] _account = new char[256], _domain = new char[256];
    private readonly byte[] _status = new byte[8192];
    private readonly byte[] _passwordBuffer = new byte[65536];
    private readonly record struct Owner(long Birth, long CheckedAt, string? Name);
    /// <summary>Resolves ownership for a process identity, using the platform cache where
    /// possible.</summary>
    /// <param name="id">The stable resource or process identifier.</param>
    /// <param name="birth">The process start identity used to distinguish reused process
    /// identifiers.</param>
    /// <param name="uid">The effective user identifier. Defaults to null.</param>
    /// <returns>The owner name, or null when ownership cannot be resolved.</returns>
    public string? Read(int id, long birth, uint? uid = null) {
        if (_processes.TryGetValue(id, out var old) && old.Birth == birth
            && Compat.ElapsedTime(old.CheckedAt).TotalSeconds < 5) {
            return old.Name;
        }

        string? name = null;
        if (Platform.IsWindows()) {
            name = ReadWindows(id, birth);
        }
        else if (Platform.IsLinux()) {
            try {
                using var handle = File.Open($"/proc/{id}/status", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int count = handle.Read(_status, 0, _status.Length);
                if (TryReadEffectiveUid(new ByteWindow(_status, 0, count), out uint effective)) {
                    name = ReadUnixUser(effective);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            }
        }
        else if (Platform.IsMacOS() && uid.HasValue) {
            name = ReadUnixUser(uid.Value);
        }

        _processes[id] = new(birth, Stopwatch.GetTimestamp(), name);
        return name;
    }

    /// <summary>Removes a process identifier from the ownership cache.</summary>
    /// <param name="id">The stable resource or process identifier.</param>
    public void Remove(int id) =>
        _processes.Remove(id);
    internal static bool TryReadEffectiveUid(ByteWindow text, out uint uid) {
        uid = 0;
        foreach (var line in text.Split((byte)'\n')) {
            if (!line.StartsWith("Uid:")) {
                continue;
            }

            int field = 0;
            foreach (var token in line[4..].Split((byte)'\t')) {
                var value = token.Trim((byte)' ');
                if (value.IsEmpty) {
                    continue;
                }

                // procfs separates UIDs with tabs. Also accept spaces in fixtures/readers.
                foreach (var number in value.Split((byte)' ')) {
                    if (number.IsEmpty) {
                        continue;
                    }

                    if (++field == 2) {
                        return ByteNumbers.TryParse(number, out uid, out int used) && used == number.Length;
                    }
                }
            }

            return false;
        }

        return false;
    }

    private unsafe string? ReadWindows(int id, long birth) {
        using var process = OpenProcess(0x1000, 0, (uint)id);
        if (process.IsInvalid || GetProcessTimes(process, out ulong created, out _, out _, out _) == 0
            || (long)created != birth) {
            return null;
        }

        if (OpenProcessToken(process, 8, out var token) == 0) {
            token?.Dispose();
            return null;
        }

        using (token) {
            byte* buffer = stackalloc byte[256];
            if (GetTokenInformation(token, 1, buffer, 256, out _) == 0) {
                return null;
            }

            nint sid = *(nint*)buffer;
            if (ConvertSidToStringSid(sid, out nint sidText) == 0) {
                return null;
            }

            string key;
            try {
                key = Marshal.PtrToStringUni(sidText) ?? "";
            }
            finally {
                LocalFree(sidText);
            }

            if (_accounts.TryGetValue(key, out string? cached)) {
                return cached;
            }

            uint accountLength = (uint)_account.Length, domainLength = (uint)_domain.Length;
            string name = key;
            if (LookupAccountSid(null, sid, _account, ref accountLength, _domain, ref domainLength, out _) != 0) {
                name = (domainLength > 0 ? new string(_domain, 0, (int)domainLength) +
                    "\\" : "") + new string(_account, 0, (int)accountLength);
            }

            _accounts[key] = name;
            return name;
        }
    }

    private unsafe string ReadUnixUser(uint uid) {
        if (_users.TryGetValue(uid, out string? name)) {
            return name;
        }

        // passwd layouts differ between Linux and Darwin. The first member is pw_name on both;
        // provide oversized aligned storage rather than reading platform-specific trailing fields.
        byte* password = stackalloc byte[256];
        fixed (byte* buffer = _passwordBuffer) {
            int status = Platform.IsMacOS() ? MacGetPwUid(uid, password, buffer, (nuint)_passwordBuffer.Length, out nint result) : LinuxGetPwUid(uid, password, buffer, (nuint)_passwordBuffer.Length, out result);
            nint pointer = status == 0 && result != 0 ? *(nint*)password : 0;
            if ((nuint)pointer >= (nuint)buffer && (nuint)pointer - (nuint)buffer < (nuint)_passwordBuffer.Length) {
                var bytes = new ByteWindow((void*)pointer, _passwordBuffer.Length - (int)((nuint)pointer - (nuint)buffer));
                int end = bytes.IndexOf((byte)0);
                if (end >= 0) {
                    name = bytes[..end].Utf8();
                }
            }
        }

        name ??= $"uid:{uid}";
        _users[uid] = name;
        return name;
    }

    /// <summary>Reads the parent identifier and effective user identifier for a macOS process.</summary>
    /// <param name="id">The stable resource or process identifier.</param>
    /// <param name="birth">The process start identity used to distinguish reused process
    /// identifiers.</param>
    /// <param name="parent">The parent directory or returned parent process identifier.</param>
    /// <param name="uid">The effective user identifier.</param>
    public static void ReadMacIdentity(
        int id,
        long birth,
        out int? parent,
        out uint? uid
    ) =>
        ReadMacIdentity(id, birth, out parent, out uid, out _);
    internal static unsafe void ReadMacIdentity(
        int id,
        long birth,
        out int? parent,
        out uint? uid,
        out string? status
    ) {
        parent = null;
        uid = null;
        status = null;
        if (!Platform.IsMacOS()) {
            return;
        }

        // proc_bsdinfo: fixed 136-byte ABI, ppid at 16, effective uid at 20, start timeval at 120.
        byte* pointer = stackalloc byte[136];
        if (proc_pidinfo(id, 3, 0, pointer, 136) != 136) {
            return;
        }

        var data = new ByteWindow(pointer, 136);
        long seconds = (long)ByteNumbers.UInt64(data[120..]);
        long micros = (long)ByteNumbers.UInt64(data[128..]);
        if (!MatchesMacStartTime(birth, seconds, micros)) {
            return;
        }

        uint nativeParent = ByteNumbers.UInt32(data[16..]);
        parent = nativeParent <= int.MaxValue ? (int)nativeParent : null;
        uid = ByteNumbers.UInt32(data[20..]);
        status = ByteNumbers.UInt32(data[4..]) switch {
            1 => "Starting",
            2 => "Running",
            3 => "Sleeping",
            4 => "Stopped",
            5 => "Zombie",
            _ => null
        };
    }

    internal static bool MatchesMacStartTime(long birth, long seconds, long micros) {
        long exact = DateTimeOffset.FromUnixTimeSeconds(seconds).Ticks + micros * 10;
        if (birth == exact) {
            return true;
        }

        // Process.StartTime on macOS converts the entire epoch timestamp through
        // double seconds. Reproduce that conversion rather than allowing a time
        // tolerance that could accept a different process after PID reuse.
        long managed = DateTimeOffset.FromUnixTimeSeconds(0).Ticks + TimeSpan.FromSeconds(seconds + micros / 1000000.0).Ticks;
        return birth == managed;
    }

    // rusage_info_v2 is a stable 160-byte Darwin ABI: disk read/write totals at 144/152.
    // The caller validates proc_bsdinfo's creation time after this read to reject PID reuse.
    internal static unsafe long? ReadMacDisk(int id) {
        byte* memory = stackalloc byte[160];
        if (proc_pid_rusage(id, 2, memory) != 0) {
            return null;
        }

        var bytes = new ByteWindow(memory, 160);
        ulong read = ByteNumbers.UInt64(bytes[144..]), write = ByteNumbers.UInt64(bytes[152..]);
        return read <= long.MaxValue && write <= (ulong)long.MaxValue - read ? (long)(read + write) : null;
    }

    [DllImport("/usr/lib/libproc.dylib", ExactSpelling = true)]
    private static unsafe extern int proc_pid_rusage(int pid, int flavor, void* data);
    [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ConvertSidToStringSid(nint sid, out nint text);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint LocalFree(nint memory);
    [DllImport("advapi32.dll", EntryPoint = "LookupAccountSidW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int LookupAccountSid(
        string? system,
        nint sid,
        [Out] char[] account,
        ref uint accountLength,
        [Out] char[] domain,
        ref uint domainLength,
        out uint use
    );
    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid {
        public SafeKernelHandle() : base(true) {
        }

        protected override bool ReleaseHandle() =>
            CloseHandle(handle) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern SafeKernelHandle OpenProcess(uint access, int inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int GetProcessTimes(
        SafeKernelHandle process,
        out ulong created,
        out ulong exited,
        out ulong kernel,
        out ulong user
    );
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int OpenProcessToken(SafeKernelHandle process, uint access, out SafeKernelHandle token);
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    private static unsafe extern int GetTokenInformation(
        SafeKernelHandle token,
        int kind,
        void* buffer,
        uint length,
        out uint required
    );
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int CloseHandle(nint handle);
    [DllImport("libc", EntryPoint = "getpwuid_r", ExactSpelling = true)]
    private static unsafe extern int LinuxGetPwUid(
        uint uid,
        void* password,
        byte* buffer,
        nuint length,
        out nint result
    );
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getpwuid_r", ExactSpelling = true)]
    private static unsafe extern int MacGetPwUid(uint uid, void* password, byte* buffer, nuint length, out nint result);
    [DllImport("/usr/lib/libproc.dylib", ExactSpelling = true)]
    private static unsafe extern int proc_pidinfo(int id, int flavor, ulong argument, void* buffer, int size);
    /// <summary>Parses the effective user identifier from Linux procfs status bytes.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="uid">The effective user identifier.</param>
    /// <returns>True when an effective user identifier was parsed.</returns>
    public static bool TryReadEffectiveUid(
        byte[] text,
        out uint uid
    ) =>
        TryReadEffectiveUid(new ByteWindow(text), out uid);
}
