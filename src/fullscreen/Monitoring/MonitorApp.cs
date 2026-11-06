using System.Diagnostics;
using System.Collections.Concurrent;
using fullscreen.Hosting;
using XenoAtom.Terminal.UI;

namespace fullscreen.Monitoring;
/// <summary>Coordinates background collection, monitor views, and interactive actions.</summary>
public sealed class MonitorApp : IFullscreenApp {
    private readonly object _gate = new();
    private readonly IMonitorSource _source;
    private readonly CollectorFrame _published = new();
    private readonly CollectorFrame _snapshot = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _requestedMetrics;
    private CollectorMetrics _publishedMetrics;
    private readonly MonitorSurface _surface;
    private readonly TimeSpan _frameInterval;
    private long _frameTimestamp, _receivedTimestamp, _snapshotReceivedTimestamp;
    private int _version, _displayedVersion = -1, _age = -1;
    private string? _sourceError;
    private double _latency;
    private readonly ConcurrentQueue<MonitorActionRequest[]> _actions = new();
    private int _actionVersion, _shownActionVersion;
    private string _actionMessage = "";
    private bool _batchResults;
    private readonly ConcurrentQueue<(long Id, ProcessRow Process)> _detailRequests = new();
    private readonly ConcurrentQueue<(long Id, string Text)> _detailResults = new();
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => _surface;
    /// <summary>Gets or sets the chart representation to display.</summary>
    public Charts.GraphKind GraphType {
        get => _surface.GraphType;
        set => _surface.GraphType = value;
    }

    /// <summary>Initializes a new MonitorApp instance.</summary>
    /// <param name="refreshRate">The interval in milliseconds between monitor redraws.</param>
    /// <param name="sampleInterval">The interval in milliseconds between metric collections.</param>
    /// <param name="filter">The text or expression used to restrict visible rows.</param>
    /// <param name="sort">The process sort field.</param>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="source">The source data, object, or filesystem path. Defaults to null.</param>
    /// <param name="columns">The table column names or column definitions. Defaults to null.</param>
    /// <param name="tree">Whether tree display requires the process-name column. Defaults to false.</param>
    /// <param name="networkColumns">The network-interface columns included in display. Defaults to
    /// null.</param>
    /// <param name="excludedNetwork">The network-interface columns excluded from display. Defaults to
    /// null.</param>
    /// <param name="gpuColumns">The graphics-device columns included in display. Defaults to null.</param>
    /// <param name="excludedGpu">The graphics-device columns excluded from display. Defaults to
    /// null.</param>
    /// <param name="minimal">Whether reduced-chrome layout is enabled. Defaults to false.</param>
    /// <param name="includeUserProcesses">Whether owned processes are shown in the user-session tree.
    /// Defaults to true.</param>
    /// <param name="usersOnly">Whether only the user-session view is available. Defaults to false.</param>
    public MonitorApp(
        int refreshRate,
        int sampleInterval,
        string filter,
        ProcessSort sort,
        MonitorView view,
        IMonitorSource? source = null,
        ProcessColumns? columns = null,
        bool tree = false,
        NetworkColumn[]? networkColumns = null,
        NetworkColumn[]? excludedNetwork = null,
        GpuColumn[]? gpuColumns = null,
        GpuColumn[]? excludedGpu = null,
        bool minimal = false,
        bool includeUserProcesses = true,
        bool usersOnly = false
    ) {
        _source = source ?? new LocalMonitorSource();
        _frameInterval = TimeSpan.FromSeconds(1.0 / refreshRate);
        _surface = new MonitorSurface {
            UsersOnly = usersOnly,
            IncludeUserProcesses = includeUserProcesses,
            View = view,
            SourceLabel = _source.Label,
            IsRemote = _source.IsRemote,
            Frame = _snapshot,
            ColumnSettings = columns ?? new(),
            Minimal = minimal
        };
        _surface.Model.Tree = tree;
        try {
            _surface.ConfigureDetailColumns(networkColumns, excludedNetwork, gpuColumns, excludedGpu);
        }
        catch {
            _source.Dispose();
            _stop.Dispose();
            throw;
        }

        if (tree) {
            _surface.ColumnSettings.EnsureTreeName();
        }

        _surface.Model.Filter = filter;
        _surface.Model.Sort = sort;
        _surface.ActionsEnabled = _source is IMonitorControlSource;
        void EnqueueActions(MonitorActionRequest[] requests) {
            if (_surface.ActionBusy || !_surface.ActionsEnabled) {
                return;
            }

            _surface.ActionBusy = true;
            _surface.ActionMessage = "Running " + requests[0].Action + " · " + requests.Length + " target(s)";
            _surface.Refresh();
            _actions.Enqueue(requests);
            if (_wake.CurrentCount == 0) {
                _wake.Release();
            }
        }
        _surface.ActionRequested += request => EnqueueActions([request]);
        _surface.SessionLogoffRequested += requests => EnqueueActions([.. requests]);
        _surface.ProcessDetailRequested += (id, process) => {
            _detailRequests.Enqueue((id, process));
            if (_wake.CurrentCount == 0) {
                _wake.Release();
            }
        };
        _requestedMetrics = (int)_surface.RequiredMetrics;
        _worker = Task.Run(async () => {
            var incoming = new CollectorFrame();
            int failures = 0;
            CollectorMetrics? previous = null;
            try {
                while (!_stop.IsCancellationRequested) {
                    var metrics = (CollectorMetrics)Volatile.Read(ref _requestedMetrics);
                    if (_actions.TryDequeue(out var actions)) {
                        var results = new List<string>();
                        foreach (var action in actions) {
                            try {
                                _stop.Token.ThrowIfCancellationRequested();
                                ((IMonitorControlSource)_source).Execute(action, _stop.Token);
                                results.Add(action.Action + " requested · " + action.Name +
                                    (action.Action == MonitorAction.LogoffSession ? " #" + action.SessionId : ""));
                            }
                            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
                                throw;
                            }
                            catch (Exception error) {
                                results.Add("Action failed · " + error.Message + " · " + action.Name +
                                    (action.Action == MonitorAction.LogoffSession ? " #" + action.SessionId : ""));
                            }
                        }

                        lock (_gate) {
                            _actionMessage = string.Join(" | ", results);
                            _batchResults = actions.Length > 1;
                            _actionVersion++;
                        }

                        previous = null;
                    }

                    if (_detailRequests.TryDequeue(out var detail)) {
                        // Only the newest open process dialog needs a lookup.
                        while (_detailRequests.TryDequeue(out var newer)) {
                            detail = newer;
                        }
                        string text;
                        try {
                            text = _source is IMonitorProcessDetailSource details
                                ? details.ReadCommandLine(detail.Process, _stop.Token)
                                : "Unavailable: this source does not support command-line inspection.";
                        }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
                            throw;
                        }
                        catch (Exception error) {
                            text = "Unavailable: " + error.Message;
                        }
                        _detailResults.Enqueue((detail.Id, text));
                    }

                    if (metrics == CollectorMetrics.None && previous == metrics) {
                        await _wake.WaitAsync(sampleInterval, _stop.Token).ConfigureAwait(false);
                        continue;
                    }

                    long start = Stopwatch.GetTimestamp();
                    try {
                        _source.Sample(incoming, metrics, _stop.Token);
                        previous = metrics;
                        lock (_gate) {
                            _published.CopyFrom(incoming);
                            _publishedMetrics = metrics;
                            _version++;
                            _receivedTimestamp = Stopwatch.GetTimestamp();
                            _latency = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            _sourceError = null;
                        }

                        failures = 0;
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
                        break;
                    }
                    catch (Exception error) {
                        lock (_gate) {
                            _sourceError = error.Message;
                        }

                        failures = Math.Min(failures + 1, 5);
                    }

                    await _wake.WaitAsync(Math.Max(sampleInterval, failures * 1000), _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
            }
            finally {
                _source.Dispose();
            }
        });
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() => Tick(_surface.App is { } app
        ? Custom.TuiComponentHost.IsVisibleWithin(_surface, app.Root) : _surface.Parent is null);

    internal bool Tick(bool visible) {
        _surface.DeferPresentation = !visible;
        if (_surface.ExitRequested) {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_frameTimestamp, now) < _frameInterval) {
            return true;
        }

        _frameTimestamp = now;
        while (_detailResults.TryDequeue(out var detail)) {
            _surface.CompleteProcessDetails(detail.Id, detail.Text);
        }
        var metrics = _surface.RequiredMetrics;
        if (Interlocked.Exchange(ref _requestedMetrics, (int)metrics) != (int)metrics && _wake.CurrentCount == 0) {
            _wake.Release();
        }

        bool changed = _surface.Dirty;
        bool projection = _surface.Dirty;
        string? batchResults = null;
        if (Monitor.TryEnter(_gate)) {
            try {
                if (_actionVersion != _shownActionVersion) {
                    _shownActionVersion = _actionVersion;
                    _surface.ActionMessage = _actionMessage;
                    _surface.ActionBusy = false;
                    if (_batchResults) {
                        batchResults = _actionMessage;
                    }
                    changed = true;
                }

                if (_surface.SourceError != _sourceError) {
                    _surface.SourceError = _sourceError;
                    changed = true;
                }

                if (!_surface.Paused && _version != _displayedVersion && _publishedMetrics == metrics) {
                    _snapshot.CopyFrom(_published);
                    _displayedVersion = _version;
                    _snapshotReceivedTimestamp = _receivedTimestamp;
                    _surface.SetFrame(_snapshot);
                    _surface.LatencyMilliseconds = _latency;
                    _surface.SnapshotAgeSeconds = 0;
                    _age = 0;
                    changed = true;
                    projection = true;
                }

                if (!_surface.Minimal) {
                    int age = _snapshotReceivedTimestamp == 0 ? -1 : (int)Stopwatch.GetElapsedTime(_snapshotReceivedTimestamp, now).TotalSeconds;
                    if (age != _age) {
                        _age = age;
                        _surface.SnapshotAgeSeconds = age;
                        changed = true;
                    }
                }
            }
            finally {
                Monitor.Exit(_gate);
            }
        }

        if (batchResults is not null) {
            _surface.ShowSessionResults(batchResults);
        }
        if (changed && visible) {
            if (projection && _surface.View == MonitorView.Processes) {
                _surface.Model.Update(_snapshot.Processes.AsSpan(0, _snapshot.ProcessCount));
            }

            _surface.Dirty = false;
            _surface.Refresh();
        }
        else if (changed) {
            _surface.Dirty = true;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose() {
        _stop.Cancel();
        try {
            _worker.GetAwaiter().GetResult();
        }
        finally {
            _stop.Dispose();
            _wake.Dispose();
        }
    }
}
