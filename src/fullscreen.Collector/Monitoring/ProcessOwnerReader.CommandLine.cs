using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace fullscreen.Monitoring;

public sealed partial class ProcessOwnerReader {
    private const int CommandLineLimit = 32768;
    private const int CommandBytesLimit = 65536;

    /// <summary>Reads a process command line after validating its start identity.</summary>
    /// <param name="id">The selected process identifier.</param>
    /// <param name="birth">The start identity reported by the process sampler.</param>
    /// <param name="token">The cancellation token for the lookup.</param>
    /// <returns>A bounded command line or an explicit empty-command-line message.</returns>
    public static string ReadCommandLine(int id, long birth, CancellationToken token = default) {
        token.ThrowIfCancellationRequested();
        if (id <= 0 || birth <= 0) {
            throw new InvalidOperationException("Process start identity is unavailable; refresh before inspecting its command line.");
        }

        string text;
        bool truncated = false;
        if (Platform.IsWindows()) {
            text = ReadWindowsCommandLine(id, birth, token);
        }
        else if (Platform.IsLinux()) {
            ValidateLinuxIdentity(id, birth, token);
            byte[] data = ReadCommandFile($"/proc/{id}/cmdline", CommandBytesLimit, token, out int length, out truncated);
            text = Encoding.UTF8.GetString(data, 0, length).Replace('\0', ' ').TrimEnd();
            ValidateLinuxIdentity(id, birth, token);
        }
        else if (Platform.IsMacOS()) {
            ValidateMacIdentity(id, birth);
            text = ReadMacCommandLine(id, token);
            ValidateMacIdentity(id, birth);
        }
        else {
            throw new PlatformNotSupportedException("Process command-line inspection is supported on Windows, Linux and macOS.");
        }

        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) {
            return "Unavailable: command line is empty or access is denied.";
        }

        if (text.Length > CommandLineLimit) {
            text = text[..CommandLineLimit];
            truncated = true;
        }
        return truncated ? text + "\n[Truncated at 32768 characters or 65536 source bytes.]" : text;
    }

    private static string ReadWindowsCommandLine(int id, long birth, CancellationToken token) {
        using var process = OpenProcess(0x1000, 0, (uint)id);
        if (process.IsInvalid) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        void ValidateIdentity() {
            if (GetProcessTimes(process, out ulong created, out ulong exited, out _, out _) == 0) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if ((long)created != birth || exited != 0) {
                throw new InvalidOperationException("Process identity changed; refresh before inspecting its command line.");
            }
        }
        ValidateIdentity();
        // ProcessCommandLineInformation returns a caller-owned UNICODE_STRING, avoiding
        // PEB reads and target/host pointer-size differences.
        int status = NtQueryInformationProcess(process, 60, 0, 0, out uint required);
        if (status is not (unchecked((int)0xC0000004) or unchecked((int)0xC0000023)) && status < 0) {
            throw new Win32Exception((int)RtlNtStatusToDosError(status));
        }
        int header = IntPtr.Size == 8 ? 16 : 8;
        if (required < header || required > CommandBytesLimit + header) {
            throw new InvalidDataException("Process command-line buffer size is invalid.");
        }

        nint buffer = Marshal.AllocHGlobal((int)required);
        try {
            token.ThrowIfCancellationRequested();
            status = NtQueryInformationProcess(process, 60, buffer, required, out _);
            if (status < 0) {
                throw new Win32Exception((int)RtlNtStatusToDosError(status));
            }
            ValidateIdentity();
            ushort length = unchecked((ushort)Marshal.ReadInt16(buffer));
            nint pointer = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
            ulong offset = unchecked((ulong)((long)pointer - (long)buffer));
            if (length == 0) {
                return "";
            }
            if ((length & 1) != 0 || offset < (ulong)header || offset > required || (ulong)length > required - offset) {
                throw new InvalidDataException("Process command-line buffer is invalid.");
            }
            return Marshal.PtrToStringUni(pointer, length / 2) ?? "";
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ValidateLinuxIdentity(int id, long birth, CancellationToken token) {
        byte[] data = ReadCommandFile($"/proc/{id}/stat", 8192, token, out int length, out bool truncated);
        if (truncated || !ProcessSampler.TryReadLinuxStat(new ByteWindow(data, 0, length), out var stat) || stat.StartTicks != birth) {
            throw new InvalidOperationException("Process identity changed; refresh before inspecting its command line.");
        }
    }

    private static byte[] ReadCommandFile(string path, int limit, CancellationToken token, out int length, out bool truncated) {
        byte[] data = new byte[limit + 1];
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = 0;
        while (read < data.Length) {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(data, read, data.Length - read);
            if (count == 0) {
                break;
            }
            read += count;
        }
        truncated = read > limit;
        length = Math.Min(read, limit);
        return data;
    }

    private static void ValidateMacIdentity(int id, long birth) {
        ReadMacIdentity(id, birth, out int? parent, out _);
        if (!parent.HasValue) {
            throw new InvalidOperationException("Process identity changed or is unavailable; refresh before inspecting its command line.");
        }
    }

    private static string ReadMacCommandLine(int id, CancellationToken token) {
        int[] mib = [1, 49, id]; // CTL_KERN, KERN_PROCARGS2.
        nuint size = 0;
        if (ReadProcessArguments(mib, 3, 0, ref size, 0, 0) != 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (size < 4 || size > 2 * 1024 * 1024) {
            throw new InvalidDataException("Process argument buffer size is invalid.");
        }
        int capacity = (int)size;
        nint buffer = Marshal.AllocHGlobal(capacity);
        try {
            token.ThrowIfCancellationRequested();
            if (ReadProcessArguments(mib, 3, buffer, ref size, 0, 0) != 0) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (size < 4 || size > (nuint)capacity) {
                throw new InvalidDataException("Process argument buffer size changed.");
            }
            byte[] data = new byte[(int)size];
            Marshal.Copy(buffer, data, 0, data.Length);
            return ParseMacCommandLine(data);
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static string ParseMacCommandLine(byte[] data) {
        if (data.Length < 4) {
            throw new InvalidDataException("Process argument buffer is incomplete.");
        }
        int arguments = BitConverter.ToInt32(data, 0);
        if (arguments < 0 || arguments > CommandBytesLimit) {
            throw new InvalidDataException("Process argument count is invalid.");
        }
        int position = Array.IndexOf(data, (byte)0, 4);
        if (position < 0) {
            throw new InvalidDataException("Process executable path is incomplete.");
        }
        position++;
        // Padding and an empty argv[0] have identical bytes. Never skip into envp.
        if (arguments > 0 && position < data.Length && data[position] == 0) {
            return "Unavailable: ambiguous macOS argument boundaries.";
        }
        var result = new StringBuilder();
        for (int i = 0; i < arguments; i++) {
            int end = position < data.Length ? Array.IndexOf(data, (byte)0, position) : -1;
            if (end < 0) {
                throw new InvalidDataException("Process arguments are incomplete.");
            }
            if (i > 0) {
                result.Append(' ');
            }
            result.Append(Encoding.UTF8.GetString(data, position, end - position));
            position = end + 1;
        }
        // KERN_PROCARGS2 includes environment variables after argc arguments; never display them.
        return result.ToString();
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryInformationProcess(SafeKernelHandle process, int kind, nint buffer, uint size, out uint required);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctl", SetLastError = true, ExactSpelling = true)]
    private static extern int ReadProcessArguments(int[] name, uint count, nint oldValue, ref nuint size, nint newValue, nuint newSize);
}
