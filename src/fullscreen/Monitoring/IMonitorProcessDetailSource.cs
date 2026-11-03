namespace fullscreen.Monitoring;

/// <summary>Reads command lines for a validated local or remote process.</summary>
public interface IMonitorProcessDetailSource {
    /// <summary>Reads a process command line without executing it.</summary>
    /// <param name="process">The process and start identity selected in the monitor.</param>
    /// <param name="token">The cancellation token for the lookup.</param>
    /// <returns>The bounded command line or an explicit unavailable message.</returns>
    string ReadCommandLine(ProcessRow process, CancellationToken token);
}
