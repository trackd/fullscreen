using System.Management.Automation;
using System.Security;

namespace fullscreen.Monitoring;

/// <summary>Optional authentication capability for a monitor source.</summary>
public interface IMonitorSudoSource {
    /// <summary>Gets the reason privileged reads are requested on this source.</summary>
    string SudoReason { get; }
    /// <summary>Checks whether sudo requested authentication on the target.</summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Whether a password callback is needed.</returns>
    bool NeedsSudoAuthentication(CancellationToken token);
    /// <summary>Authenticates on the target without elevating monitor actions.</summary>
    /// <param name="password">The password, owned by the caller.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Whether sudo accepted authentication.</returns>
    bool AuthenticateSudo(SecureString password, CancellationToken token);
}

/// <summary>Context supplied to the optional sudo password callback.</summary>
/// <param name="Target">The collector host requiring authentication.</param>
/// <param name="Reason">Why elevated reads are requested.</param>
public sealed record SudoPromptRequest(string Target, string Reason);

internal enum SudoPreparation { NotNeeded, Cancelled, Authenticated, Failed }
internal static class MonitorSudo {
    internal static SecureString? PasswordFromPrompt(IList<PSObject> result) {
        if (result.Count == 0 || result.Count == 1 && result[0].BaseObject is null) { return null; }
        if (result.Count == 1 && result[0].BaseObject is SecureString password) { return password; }
        throw new ArgumentException("SudoPrompt must return one SecureString or no output.");
    }
    // Called on the pipeline thread before the TUI owns terminal input, never from a sampling worker.
    internal static SudoPreparation Prepare(IMonitorSource source, Func<SudoPromptRequest, SecureString?> prompt, CancellationToken token) {
        if (source is not IMonitorSudoSource sudo) { return SudoPreparation.NotNeeded; }
        token.ThrowIfCancellationRequested();
        source.Sample(new CollectorFrame(), CollectorMetrics.Gpu, token);
        if (!sudo.NeedsSudoAuthentication(token)) { return SudoPreparation.NotNeeded; }
        token.ThrowIfCancellationRequested();
        var response = prompt(new(source.Label, sudo.SudoReason));
        if (response is null) { return SudoPreparation.Cancelled; }
        using var password = response.Copy(); // Do not dispose caller-owned callback output.
        token.ThrowIfCancellationRequested();
        return sudo.AuthenticateSudo(password, token) ? SudoPreparation.Authenticated : SudoPreparation.Failed;
    }
}
