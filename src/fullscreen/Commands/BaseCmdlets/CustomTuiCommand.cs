using System.Management.Automation;

namespace fullscreen.Commands;
/// <summary>Provides common execution and cancellation handling for custom TUI cmdlets.</summary>
public abstract class CustomTuiCommand : FullscreenCommand {
    private int _records;
    /// <inheritdoc/>
    protected override void ProcessRecord() {
        if (++_records > 1) {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Supply one TUI definition per invocation."),
                "FullscreenMultipleTuiDefinitions",
                ErrorCategory.InvalidArgument,
                null
            ));
        }
    }

    /// <summary>Runs a custom TUI operation with cmdlet cancellation and error reporting.</summary>
    /// <param name="run">The cancellable TUI operation to execute.</param>
    /// <param name="errorId">The stable identifier attached to the reported PowerShell error.</param>
    protected void RunCustom(Action<CancellationToken> run, string errorId) {
        if (PipelineStopToken.IsCancellationRequested) {
            return;
        }

        try {
            run(PipelineStopToken);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, errorId, ErrorCategory.InvalidOperation, null));
        }
    }

    /// <summary>Resolves a PowerShell path to its filesystem path.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    /// <returns>The resolved filesystem path.</returns>
    protected string FilePath(string path) {
        string resolved;
        ProviderInfo provider;
        // Designer save dialogs only handle ArgumentException/IOException; an unknown drive must not end the session.
        try {
            resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path, out provider, out _);
        }
        catch (SessionStateException error) {
            throw new ArgumentException(error.Message, nameof(path), error);
        }

        if (provider.Name != "FileSystem") {
            throw new ArgumentException("Choose a literal FileSystem path.");
        }

        return resolved;
    }
}
