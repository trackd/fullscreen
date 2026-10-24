using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace fullscreen.Files;
/// <summary>Loads optional integration modules (TextMate, Sixel) into the caller's runspace.</summary>
internal static class OptionalModule {
    /// <summary>Returns a pipeline with <paramref name="name"/> loaded, or null when the module is not installed or no runspace exists.</summary>
    internal static PowerShell? Open(string name) {
        if (Runspace.DefaultRunspace is null) {
            return null;
        }

        var pipeline = PowerShell.Create(RunspaceMode.CurrentRunspace);
        try {
            pipeline.AddCommand("Microsoft.PowerShell.Core\\Get-Module").AddParameter("Name", name);
            if (pipeline.Invoke().Count == 0) {
                pipeline.Commands.Clear();
                pipeline.Streams.Error.Clear();
                pipeline.AddCommand("Microsoft.PowerShell.Core\\Import-Module").AddParameter("Name", name).AddParameter("ErrorAction", ActionPreference.Stop);
                try {
                    pipeline.Invoke();
                }
                catch (RuntimeException error) when (error.ErrorRecord.FullyQualifiedErrorId.StartsWith("Modules_ModuleNotFound", StringComparison.Ordinal)) {
                    pipeline.Dispose();
                    return null;
                }
                catch (RuntimeException error) {
                    throw new InvalidOperationException($"Import-Module {name} failed: {error.Message}", error);
                }
            }

            pipeline.Commands.Clear();
            pipeline.Streams.Error.Clear();
            return pipeline;
        }
        catch {
            pipeline.Dispose();
            throw;
        }
    }
}
