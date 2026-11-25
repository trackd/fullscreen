using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace fullscreen.Monitoring;

// Shared read-only collector runner. Samplers request metric groups; the UI and actions stay unprivileged.
internal sealed class SudoCollector(Func<ProcessStartInfo, Process?>? start = null) : IDisposable {
    private SudoProcess? _process;
    private bool _attempted;
    internal string Status { get; private set; } = "sudo collector unavailable";
    internal bool AuthenticationRequired { get; private set; }
    private const string Bootstrap = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $collectorImage = [Console]::ReadLine()
        $collectorAssembly = [Reflection.Assembly]::Load([Convert]::FromBase64String($collectorImage))
        $collectorHostType = $collectorAssembly.GetType('fullscreen.Monitoring.SudoCollectorHost', $true)
        $collectorHostType::Run()
        """;

    internal CollectorFrame? Sample(CollectorMetrics metrics) {
        try {
            if (_process is not null && _process.HasExited) {
                Stop();
                _attempted = false; // An idle helper can be restarted when collection resumes.
            }
            if (!_attempted) {
                _attempted = true;
                string? executable;
                using (var current = Process.GetCurrentProcess()) { executable = current.MainModule?.FileName; }
                if (executable is null || Path.GetFileName(executable) != "pwsh") {
                    executable = File.Exists("/usr/bin/pwsh") ? "/usr/bin/pwsh" : "/usr/local/bin/pwsh";
                }
                if (executable.Contains('"')) {
                    return null;
                }
                byte[] image = CollectorSessions.CollectorImage ?? File.ReadAllBytes(typeof(PerformanceCollector).Assembly.Location);
                _process = SudoProcess.Start(executable, "-NoLogo -NoProfile -NonInteractive -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(Bootstrap)), start);
                if (_process is null) {
                    return null;
                }
                _process.WriteLine(Convert.ToBase64String(image));
                if (_process.ReadLine() != "ready") {
                    throw new IOException("sudo collector did not start.");
                }
                Status = "Collector reader: sudo -n";
            }
            if (_process is null) {
                return null;
            }
            _process.WriteLine(((int)metrics).ToString(System.Globalization.CultureInfo.InvariantCulture));
            string? response = _process.ReadLine();
            if (response is null || response.Length > 2 * 1024 * 1024) {
                throw new IOException("Invalid sudo GPU response.");
            }
            var frame = new CollectorFrame();
            FrameCodec.Decode(Convert.FromBase64String(response), frame);
            return frame;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException or AggregateException or FormatException or ArgumentException or NotSupportedException) {
            Status = "sudo collector unavailable (non-interactive access required)";
            AuthenticationRequired = _process?.PasswordRequired ?? false;
            Stop();
            return null;
        }
    }

    private void Stop() {
        if (_process is null) {
            return;
        }
        _process.Dispose();
        _process = null;
    }

    public void Dispose() {
        Stop();
        _attempted = false;
        AuthenticationRequired = false;
    }
}

/// <summary>Protocol entry point for the shared elevated collector; not an interactive command.</summary>
public static class SudoCollectorHost {
    /// <summary>Reads metric requests from standard input until the caller disconnects or becomes idle.</summary>
    public static void Run() {
        if (!Platform.IsLinux()) {
            throw new PlatformNotSupportedException();
        }
        using var collector = new PerformanceCollector(); // Elevated readers never request recursive sudo.
        // An abandoned reader must not retain elevated descriptors indefinitely.
        using var idle = new Timer(_ => Environment.Exit(0), null, 15000, Timeout.Infinite);
        Console.WriteLine("ready");
        while (Console.ReadLine() is { } request) {
            if (!int.TryParse(request, out int metrics)) {
                break;
            }
            idle.Change(15000, Timeout.Infinite);
            var frame = collector.Capture((CollectorMetrics)metrics);
            Console.WriteLine(Convert.ToBase64String(FrameCodec.Encode(frame)));
        }
    }
}
