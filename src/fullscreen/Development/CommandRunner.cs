using System.Diagnostics;
using System.Text;

namespace fullscreen.Development;
/// <summary>Describes a child-process invocation, including its arguments and working directory.</summary>
/// <param name="FileName">The executable or source filename.</param>
/// <param name="Directory">The parent or working directory.</param>
/// <param name="Arguments">The individual child-process arguments, without shell concatenation.</param>
/// <param name="Input">The optional standard-input text. Defaults to null.</param>
/// <param name="TimeoutSeconds">The timeout in seconds. Defaults to 60.</param>
public sealed record CommandRequest(
    string FileName,
    string Directory,
    IReadOnlyList<string> Arguments,
    string? Input = null,
    int TimeoutSeconds = 60
);
/// <summary>Stores the exit code and captured output of a child process.</summary>
/// <param name="ExitCode">The child process exit code.</param>
/// <param name="Output">The captured standard-output text.</param>
/// <param name="Error">The captured standard-error text or error description.</param>
/// <param name="Truncated">Whether the source exceeded the retained output limit. Defaults to false.</param>
public sealed record CommandResult(int ExitCode, string Output, string Error, bool Truncated = false) {
    /// <summary>Returns standard output or throws when the command failed.</summary>
    /// <returns>The successful command&apos;s standard output.</returns>
    public string RequireSuccess() {
        if (ExitCode != 0) {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(Error) ? $"Command exited with code {ExitCode}." : Error.Trim());
        }

        if (Truncated) {
            throw new InvalidOperationException("Command output exceeded 4 MiB. Narrow the selection or use a smaller page.");
        }

        return Output;
    }
}

/// <summary>Defines cancellable child-process execution for developer backends.</summary>
public interface ICommandRunner {
    /// <summary>Executes a child-process request and captures its output.</summary>
    /// <param name="request">The operation request to execute.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the process exit code and captured streams.</returns>
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken token);
}

/// <summary>Argument-list invocation, bounded output, no shell, prompt, pager or terminal inheritance.</summary>
public sealed class CommandRunner : ICommandRunner {
    /// <summary>Executes a cancellable child process and captures both output streams.</summary>
    /// <param name="request">The operation request to execute.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the process exit code and captured streams.</returns>
    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(request.FileName) {
            WorkingDirectory = request.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (string argument in request.Arguments) {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GH_PAGER"] = "cat";
        start.Environment["NO_COLOR"] = "1";
        start.Environment["CLICOLOR"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "never";
        using var process = new Process {
            StartInfo = start
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        try {
            if (!process.Start()) {
                throw new InvalidOperationException($"Could not start {request.FileName}.");
            }
        }
        catch (System.ComponentModel.Win32Exception error) {
            throw new InvalidOperationException($"Install {request.FileName} and make it available on PATH. {error.Message}", error);
        }

        var output = new Capture(process.StandardOutput, timeout.Token);
        var errors = new Capture(process.StandardError, timeout.Token);
        try {
            if (request.Input is not null) {
                await process.StandardInput.WriteAsync(request.Input.AsMemory(), timeout.Token).ConfigureAwait(false);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            // A detached grandchild (git gc --auto, credential daemons) can inherit the pipes and hold them open; don't wait for it.
            var drained = Task.WhenAll(output.Reading, errors.Reading);
            if (await Task.WhenAny(drained, Task.Delay(2000, timeout.Token)).ConfigureAwait(false) == drained) {
                await drained.ConfigureAwait(false);
            }
            else {
                timeout.Token.ThrowIfCancellationRequested();
                Observe(drained);
            }

            return new(process.ExitCode, output.Text, errors.Text, output.Truncated || errors.Truncated);
        }
        catch (OperationCanceledException) {
            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) {
            }

            await DrainAfterStop(process, output.Reading, errors.Reading).ConfigureAwait(false);
            if (!token.IsCancellationRequested) {
                throw new TimeoutException($"{request.FileName} timed out after {request.TimeoutSeconds}s. Check authentication/connectivity; the operation may have reached the server. Refresh before retrying.");
            }

            throw;
        }
        catch {
            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) {
            }

            timeout.Cancel();
            await DrainAfterStop(process, output.Reading, errors.Reading).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task DrainAfterStop(Process process, Task output, Task errors) {
        try {
            await Task.WhenAll(process.WaitForExitAsync(), output, errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException) {
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            Observe(Task.WhenAll(output, errors));
        }
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            done => _ = done.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    /// <summary>Reads a stream up to 4 MiB; the text read so far stays available if the stream never closes.</summary>
    private sealed class Capture {
        private const int Limit = 4 * 1024 * 1024;
        private readonly StringBuilder _text = new();
        private bool _truncated;
        internal Task Reading { get; }

        internal string Text {
            get {
                lock (_text) {
                    return _text.ToString();
                }
            }
        }

        internal bool Truncated {
            get {
                lock (_text) {
                    return _truncated;
                }
            }
        }

        internal Capture(StreamReader reader, CancellationToken token) =>
            Reading = ReadAsync(reader, token);
        private async Task ReadAsync(StreamReader reader, CancellationToken token) {
            char[] buffer = new char[8192];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0) {
                lock (_text) {
                    int keep = Math.Min(count, Limit - _text.Length);
                    if (keep > 0) {
                        _text.Append(buffer, 0, keep);
                    }

                    _truncated |= keep < count;
                }
            }
        }
    }
}
