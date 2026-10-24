using System.Management.Automation;

namespace fullscreen.Commands;
/// <summary>Provides path resolution and error reporting for TUI definition cmdlets.</summary>
public abstract class TuiDefinitionCommand : PSCmdlet {
    /// <summary>Resolves a definition path using PowerShell provider semantics.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    /// <returns>The resolved filesystem path.</returns>
    protected string ResolvePath(string path) {
        string resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path, out var provider, out _);
        if (provider.Name != "FileSystem") {
            throw new ArgumentException("Choose a literal FileSystem path.");
        }

        return resolved;
    }

    /// <summary>Reports a definition error through the cmdlet error stream.</summary>
    /// <param name="error">The captured standard-error text or error description.</param>
    /// <param name="target">The destination data buffer or error target.</param>
    protected void InvalidDefinition(
        Exception error,
        object? target
    ) =>
        ThrowTerminatingError(new ErrorRecord(error, "FullscreenTuiDefinitionFailed", ErrorCategory.InvalidArgument, target));
}
