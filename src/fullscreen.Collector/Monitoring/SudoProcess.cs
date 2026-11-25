using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace fullscreen.Monitoring;

// Shared sudo transport/authentication. Workload protocols belong to their own readers.
internal sealed class SudoProcess : IDisposable {
    private readonly Process _process;
    private readonly Task<string> _errors;
    private const int TimeoutMilliseconds = 3000;
    private SudoProcess(Process process) {
        _process = process;
        _errors = process.StandardError.ReadToEndAsync();
    }
    internal bool HasExited => _process.HasExited;
    internal bool PasswordRequired {
        get {
            try {
                return _errors.Wait(500) && _errors.GetAwaiter().GetResult().Contains("a password is required");
            }
            catch (AggregateException) {
                return false;
            }
        }
    }

    internal static SudoProcess? Start(string executable, string arguments, Func<ProcessStartInfo, Process?>? start = null) {
        if (executable.Contains('"')) {
            throw new ArgumentException("Invalid executable.", nameof(executable));
        }
        var process = (start ?? Process.Start)(StartInfo("-n -- \"" + executable + "\" " + arguments));
        return process is null ? null : new(process);
    }
    private static ProcessStartInfo StartInfo(string arguments) {
        var info = new ProcessStartInfo(File.Exists("/usr/bin/sudo") ? "/usr/bin/sudo" : "/bin/sudo", arguments) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Stable diagnostics for deciding whether a callback can help, not for displaying stderr.
        info.EnvironmentVariables["LC_ALL"] = "C";
        return info;
    }
    internal void WriteLine(string value) {
        if (!_process.StandardInput.WriteLineAsync(value).Wait(TimeoutMilliseconds)) {
            throw new IOException("sudo request timed out.");
        }
    }
    internal string? ReadLine() {
        var read = _process.StandardOutput.ReadLineAsync();
        if (!read.Wait(TimeoutMilliseconds)) {
            throw new IOException("sudo response timed out.");
        }
        return read.GetAwaiter().GetResult();
    }

    internal static bool Authenticate(SecureString password, CancellationToken token) {
        if (!Platform.IsLinux()) {
            return false;
        }
        nint plaintext = 0;
        char[]? characters = null;
        try {
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(StartInfo("-S -p \"\" -v"));
            if (process is null) {
                return false;
            }
            using var transport = new SudoProcess(process);
            var output = process.StandardOutput.ReadToEndAsync();
            plaintext = Marshal.SecureStringToGlobalAllocUnicode(password);
            characters = new char[password.Length + 1];
            for (int i = 0; i < password.Length; i++) {
                characters[i] = (char)Marshal.ReadInt16(plaintext, i * 2);
                if (characters[i] is '\0' or '\r' or '\n') {
                    return false;
                }
            }
            characters[password.Length] = '\n';
            if (!process.StandardInput.WriteAsync(characters, 0, characters.Length).Wait(TimeoutMilliseconds, token)) {
                return false;
            }
            process.StandardInput.Close(); // One attempt only; no hidden retry/password prompt.
            for (int elapsed = 0; elapsed < 10000 && !process.WaitForExit(50); elapsed += 50) { token.ThrowIfCancellationRequested(); }
            if (!process.HasExited) {
                return false;
            }
            if (!output.Wait(TimeoutMilliseconds, token)) {
                return false;
            }
            _ = output.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or AggregateException) {
            return false;
        }
        finally {
            if (characters is not null) {
                Array.Clear(characters, 0, characters.Length);
            }
            if (plaintext != 0) {
                Marshal.ZeroFreeGlobalAllocUnicode(plaintext);
            }
        }
    }

    public void Dispose() {
        try {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(500)) {
                _process.Kill();
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception) { }
        finally {
            _process.Dispose();
            _ = _errors.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
