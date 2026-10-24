namespace fullscreen.Monitoring;
/// <summary>Defines a source that fills reusable monitor snapshots.</summary>
public interface IMonitorSource : IDisposable {
    /// <summary>Gets the human-readable label.</summary>
    string Label { get; }

    /// <summary>Gets whether collection targets a remote computer.</summary>
    bool IsRemote { get; }

    /// <summary>Fills a reusable destination frame with current monitor metrics.</summary>
    /// <param name="destination">The reusable collector frame to populate.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    void Sample(CollectorFrame destination, CancellationToken cancellationToken);
    /// <summary>Fills a reusable destination frame with current monitor metrics.</summary>
    /// <param name="destination">The reusable collector frame to populate.</param>
    /// <param name="metrics">The metric groups required for this collection cycle.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    void Sample(
        CollectorFrame destination,
        CollectorMetrics metrics,
        CancellationToken cancellationToken
    ) =>
        Sample(destination, cancellationToken);
}

/// <summary>Collects local machine metrics and executes local monitor actions.</summary>
public sealed class LocalMonitorSource : IMonitorSource, IMonitorControlSource {
    private readonly PerformanceCollector _collector = new();
    /// <summary>Gets the human-readable label.</summary>
    public string Label => Environment.MachineName;
    /// <summary>Gets whether collection targets a remote computer.</summary>
    public bool IsRemote => false;

    /// <inheritdoc/>
    public void Sample(CollectorFrame destination, CancellationToken cancellationToken) {
        Sample(destination, CollectorMetrics.All, cancellationToken);
    }

    /// <inheritdoc/>
    public void Sample(CollectorFrame destination, CollectorMetrics metrics, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        destination.CopyFrom(_collector.Capture(metrics));
    }

    /// <inheritdoc/>
    public void Dispose() =>
        _collector.Dispose();
    /// <inheritdoc cref="fullscreen.Monitoring.IMonitorControlSource.Execute"/>
    public void Execute(MonitorActionRequest request, CancellationToken cancellationToken) {
        try {
            MonitorActions.ExecuteLocal(request, cancellationToken);
        }
        finally {
            _collector.InvalidateServices();
            _collector.InvalidateUsers();
        }
    }
}
