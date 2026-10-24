using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;
/// <summary>Read-only service inventory using the OS service manager. Refreshed at most every five seconds.</summary>
internal sealed class ServiceSampler {
    private readonly CollectorFrame _cache = new();
    // Accounts change only by reconfiguration; OpenService+QueryServiceConfig per service every 5s is ~300 extra RPCs.
    private readonly Dictionary<string, string?> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private long _last;
    private volatile bool _stale;
    internal void Invalidate() {
        _stale = true;
        _last = 0;
    }

    public void Sample(CollectorFrame frame) {
        if (_last == 0 || Compat.ElapsedTime(_last).TotalSeconds >= 5) {
            _cache.ServiceCount = 0;
            if (_stale) {
                _stale = false;
                _accounts.Clear();
            }

            try {
                if (Platform.IsWindows()) {
                    ReadWindows(_cache);
                }
                else if (Platform.IsLinux()) {
                    ParseSystemd(
                        Run("systemctl", "list-units --type=service --all --plain --no-legend --no-pager --full"),
                        _cache
                    );
                    _cache.ServiceStatus = "systemd · loaded system services · refreshed every 5s";
                }
                else if (Platform.IsMacOS()) {
                    ParseLaunchd(Run("/bin/launchctl", "list"), _cache);
                    _cache.ServiceStatus = "launchd · current bootstrap domain · refreshed every 5s";
                }
                else {
                    _cache.ServiceStatus = "Service manager unavailable on this platform";
                }
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or TimeoutException
                or UnauthorizedAccessException) {
                _cache.ServiceCount = 0;
                _cache.ServiceStatus = "Services unavailable: " + error.Message;
            }

            Array.Sort(
                _cache.Services,
                0,
                _cache.ServiceCount,
                Comparer<ServiceRow>.Create((
                    a,
                    b
                ) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name))
            );
            _last = Stopwatch.GetTimestamp();
        }

        frame.Services = CollectorFrame.EnsureCapacity(frame.Services, _cache.ServiceCount);
        Array.Copy(_cache.Services, frame.Services, _cache.ServiceCount);
        frame.ServiceCount = _cache.ServiceCount;
        frame.ServiceStatus = _cache.ServiceStatus;
    }

    private static void Add(CollectorFrame frame, ServiceRow row) {
        frame.Services = CollectorFrame.EnsureCapacity(frame.Services, frame.ServiceCount + 1);
        frame.Services[frame.ServiceCount++] = row;
    }

    internal static void ParseSystemd(string output, CollectorFrame frame) {
        foreach (string line in output.Split('\n')) {
            var fields = line.Trim().Split((char[]?)null, 5, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !fields[0].EndsWith(".service", StringComparison.Ordinal)) {
                continue;
            }

            Add(
                frame,
                new(
                    fields[0],
                    fields.Length == 5 ? fields[4] : "",
                    fields[2],
                    fields[1] +
                "/" + fields[3],
                    null
                )
            );
        }
    }

    internal static void ParseLaunchd(string output, CollectorFrame frame) {
        foreach (string line in output.Split('\n')) {
            var fields = line.Trim().Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int exit)) {
                continue;
            }

            int? processId = int.TryParse(fields[0], out int value) && value > 0 ? value : null;
            Add(
                frame,
                new(
                    fields[2],
                    "",
                    processId.HasValue ? "running" : "not running",
                    "last exit " + exit.ToString(CultureInfo.InvariantCulture),
                    processId
                )
            );
        }
    }

    internal static string Run(string executable, string arguments) {
        using var process = new Process {
            StartInfo = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.EnvironmentVariables["LC_ALL"] = "C";
        process.StartInfo.EnvironmentVariables["SYSTEMD_COLORS"] = "0";
        process.StartInfo.EnvironmentVariables["SYSTEMD_PAGER"] = "";
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(3000)) {
            try {
                process.Kill();
            }
            catch (InvalidOperationException) {
            }

            process.WaitForExit();
            throw new TimeoutException(executable + " exceeded 3 seconds");
        }

        string text = output.GetAwaiter().GetResult(), error = errors.GetAwaiter().GetResult();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(executable + ": " + error.Trim());
        }

        return text;
    }

    private void ReadWindows(CollectorFrame frame) {
        using var manager = OpenSCManagerW(null, null, 5); // CONNECT | ENUMERATE_SERVICE
        if (manager.IsInvalid) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        const int capacity = 256 * 1024;
        IntPtr buffer = Marshal.AllocHGlobal(capacity);
        try {
            uint resume = 0;
            do {
                int success = EnumServicesStatusExW(manager, 0, 0x30, 3, buffer, capacity, out _, out uint count, ref resume, null);
                int error = success == 0 ? Marshal.GetLastWin32Error() : 0;
                if (error != 0 && error != 234) {
                    throw new Win32Exception(error);
                }

                int size = Marshal.SizeOf<NativeService>();
                for (int i = 0; i < count; i++) {
                    var service = Marshal.PtrToStructure<NativeService>(IntPtr.Add(buffer, i * size));
                    string state = service.State switch {
                        1 => "stopped",
                        2 => "starting",
                        3 => "stopping",
                        4 => "running",
                        5 => "continuing",
                        6 => "pausing",
                        7 => "paused",
                        _ => "unknown"
                    };
                    string name = Marshal.PtrToStringUni(service.Name) ?? "";
                    string detail = service.ExitCode == 0 ? "—" : "error " + service.ExitCode.ToString(CultureInfo.InvariantCulture) + (service.ServiceExitCode == 0 ? "" : " / service " + service.ServiceExitCode.ToString(CultureInfo.InvariantCulture));
                    Add(
                        frame,
                        new(
                            name,
                            Marshal.PtrToStringUni(service.DisplayName) ?? "",
                            state,
                            detail,
                            service.ProcessId == 0 ? null : (int)service.ProcessId,
                            Account(manager, name)
                        )
                    );
                }

                if (success != 0) {
                    break;
                }

                if (count == 0) {
                    throw new InvalidOperationException("Service enumeration made no progress");
                }
            }
            while (resume != 0);
            frame.ServiceStatus = "Windows Service Control Manager · refreshed every 5s";
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfig {
        public uint Type, Start, Error;
        public IntPtr Binary, Group;
        public uint Tag;
        public IntPtr Dependencies, Account, Display;
    }

    private string? Account(ServiceManagerHandle manager, string name) {
        if (!_accounts.TryGetValue(name, out string? account)) {
            _accounts[name] = account = ReadAccount(manager, name);
        }

        return account;
    }

    private static string? ReadAccount(ServiceManagerHandle manager, string name) {
        using var service = OpenServiceW(manager, name, 1); // SERVICE_QUERY_CONFIG
        if (service.IsInvalid) {
            return null;
        }

        _ = QueryServiceConfigW(service, IntPtr.Zero, 0, out uint needed);
        if (needed == 0 || needed > 8192) {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try {
            return QueryServiceConfigW(service, buffer, needed, out _) != 0 ? Marshal.PtrToStringUni(Marshal.PtrToStructure<NativeConfig>(buffer).Account) : null;
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ServiceManagerHandle OpenServiceW(ServiceManagerHandle manager, string name, uint access);
    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int QueryServiceConfigW(
        ServiceManagerHandle service,
        IntPtr buffer,
        uint size,
        out uint needed
    );
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeService {
        public IntPtr Name, DisplayName;
        public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }

    private sealed class ServiceManagerHandle : SafeHandleZeroOrMinusOneIsInvalid {
        public ServiceManagerHandle() : base(true) {
        }

        protected override bool ReleaseHandle() =>
            CloseServiceHandle(handle) != 0;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ServiceManagerHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int EnumServicesStatusExW(
        ServiceManagerHandle manager,
        int level,
        uint type,
        uint state,
        IntPtr buffer,
        int size,
        out uint needed,
        out uint count,
        ref uint resume,
        string? group
    );
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int CloseServiceHandle(IntPtr handle);
}
