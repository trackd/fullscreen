using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using fullscreen.Hosting;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Development;
/// <summary>One serialized I/O worker; cached lazy pages and UI-thread-only publication.</summary>
public sealed partial class DeveloperApp : IFullscreenApp {
    private readonly IDeveloperBackend _backend;
    private readonly TimeSpan _refreshInterval;
    private readonly bool _readOnly;
    private readonly int[] _visibleViews;
    private static readonly int[] AzureViewOrder = [0, 1, 2, 3, 6, 4, 5];
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Work> _requests = Channel.CreateBounded<Work>(32);
    private readonly ConcurrentQueue<Action> _completed = new();
    private readonly Task _worker;
    private readonly Dictionary<(int View, int Page), TableModel> _pages = [];
    private readonly Dictionary<(int View, string Key), long> _ids = [];
    private long _nextId;
    private long _version;
    private long _lastRefresh;
    private long _selectionChanged;
    private string? _selectedKey;
    private string? _loadingDetailKey;
    private bool _expanded;
    private bool _mutation;
    private bool _error;
    private bool _disposed;
    private string _detail = "";
    private string _summary = "";
    private int _page;
    private bool _previewEnabled;
    private PipelineMonitor? _pipelineMonitor;
    private DeveloperItem? _requestedPipeline;
    private static readonly string[] AzureOptionalColumns = ["Type", "Board column", "Parent", "Priority", "Remaining", "Iteration", "Tags"];
    /// <summary>Gets the resource or monitor view index.</summary>
    public int View { get; private set; }
    /// <summary>Gets whether a background operation is in progress.</summary>
    public bool Busy { get; private set; }
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public TableSurface Surface { get; }
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root { get; }
    private readonly VStack _dashboard = new() { IsVisible = false, HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch };

    private sealed record Work(long Version, Func<CancellationToken, Task<Action>> Execute, bool Mutation = false);
    /// <summary>Initializes a new DeveloperApp instance.</summary>
    /// <param name="backend">The resource backend owned by the application.</param>
    /// <param name="initialView">The view index activated at startup. Defaults to 0.</param>
    /// <param name="refreshSeconds">The interval in seconds between resource refreshes. Defaults to
    /// 30.</param>
    /// <param name="readOnly">Whether actions that change external state are disabled. Defaults to
    /// false.</param>
    /// <param name="minimal">Whether reduced-chrome layout is enabled. Defaults to false.</param>
    /// <param name="excludeTabs">Resource tabs hidden without loading their pages.</param>
    public DeveloperApp(
        IDeveloperBackend backend,
        int initialView = 0,
        int refreshSeconds = 30,
        bool readOnly = false,
        bool minimal = false,
        string[]? excludeTabs = null
    ) {
        _backend = backend;
        var excluded = new HashSet<string>(excludeTabs ?? [], StringComparer.OrdinalIgnoreCase);
        _visibleViews = [.. (backend is AzureDevOpsBackend ? AzureViewOrder : Enumerable.Range(0, backend.Tabs.Length))
            .Where(view => !excluded.Contains(backend.Tabs[view].Replace(" ", "", StringComparison.Ordinal)))];
        if (_visibleViews.Length == 0) {
            throw new ArgumentException("At least one tab must remain visible.", nameof(excludeTabs));
        }
        _previewEnabled = backend is not AzureDevOpsBackend;
        _refreshInterval = TimeSpan.FromSeconds(refreshSeconds);
        _readOnly = readOnly;
        Surface = new(new TableModel(["Loading"]), backend.Title) {
            Tabs = [.. _visibleViews.Select(view => backend.Tabs[view])],
            DocumentTitle = "DETAILS",
            DocumentHint = "read-only · source data / diff",
            DocumentHelp = "Backspace list · Tab views · arrows scroll · F5 refresh · A actions · Q close",
            ExtraHelp = "A actions · F5 refresh · [/] pages · Shift↑↓ preview",
            Minimal = minimal
        };
        Root = backend is AzureDevOpsBackend
            ? new VStack(Surface, _dashboard) { Spacing = 1, HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch }
            : Surface;
        Surface.CellAccent = (row, column) => {
            string name = Surface.Model.Columns[column];
            if (name is not ("Status" or "Conclusion" or "Result" or "State")) {
                return null;
            }

            return DeveloperStatus.Color(row.Cells[column], Surface.Palette);
        };
        Surface.RowAccent = row => {
            bool pullRequests = _backend is GitHubBackend && View == 1 || _backend is AzureDevOpsBackend && View == 4;
            bool runs = _backend is GitHubBackend && View == 2 || _backend is AzureDevOpsBackend && View == 5;
            int column = Array.IndexOf(Surface.Model.Columns, pullRequests ? "State" : runs ? "Result" : "");
            return column >= 0 ? DeveloperStatus.Color(row.Cells[column], Surface.Palette) : null;
        };
        Surface.DocumentAccent = line => DeveloperStatus.LineColor(line, Surface.Palette);
        Surface.InspectRequested += _ => {
            if (_backend is IPipelineBackend pipelines && pipelines.IsPipelineView(View)
                && Surface.Model.Current?.Original is DeveloperItem run) {
                RequestPipelineMonitor();
                return;
            }
            if (Busy) {
                if (!_mutation && _loadingDetailKey is not null
                    && Surface.Model.Current?.Original is DeveloperItem pending
                    && pending.Key == _loadingDetailKey) {
                    _expanded = true;
                    Surface.Status = "Opening details when ready…";
                    Surface.Refresh();
                }

                return;
            }

            if (_backend is AzureDevOpsBackend && View == 0 && Surface.Model.Current?.Original is DeveloperItem board) {
                _showBoardWhenLoaded = true;
                Execute(_backend.Operations(0, board).Single(), "");
                return;
            }

            _expanded = true;
            if (_detail.Length > 0 && Surface.Model.Current?.Original is DeveloperItem selected) {
                ShowExpanded(selected, _detail);
            }
            else if (Surface.Model.Current?.Original is DeveloperItem item) {
                Detail(item);
            }
        };
        if (backend is AzureDevOpsBackend) {
            UpdateAzureCommands();
        }

        Surface.ContextRequested += ShowActions;
        Surface.TabRequested += view => {
            if (!_mutation) {
                SwitchView(_visibleViews[view]);
            }
        };
        Surface.Shortcut += key => {
            switch (key.ToLowerInvariant()) {
                case "y":
                    if (_backend is AzureDevOpsBackend && View is not (4 or 5) && !Busy) {
                        LoadBoardsAnalytics();
                    }
                    break;
                case "u":
                    if (_backend is AzureDevOpsBackend && View == 0 && !Busy) {
                        LoadBoardsAnalytics();
                        break;
                    }
                    if (_backend is AzureDevOpsBackend && (AzureDevOpsBackend.IsWorkItemView(View) || View == 5)) {
                        ShowAnalytics();
                    }
                    break;
                case "v":
                    if (_backend is AzureDevOpsBackend) {
                        TogglePreview();
                    }
                    break;
                case "i":
                    if (_backend is AzureDevOpsBackend && View is 1 or 2 or 3 && !Busy) {
                        ShowSprints();
                    }
                    break;
                case "k":
                    if (_backend is AzureDevOpsBackend && AzureDevOpsBackend.IsWorkItemView(View)) {
                        ShowKanban();
                    }

                    break;
                case "m":
                    if (!Busy && _backend is AzureDevOpsBackend && AzureDevOpsBackend.IsWorkItemView(View)) {
                        Execute(_backend.Operations(View, null).First(), "");
                    }

                    break;
                case "l":
                    if (!Busy && _backend is AzureDevOpsBackend) {
                        if (View == 0) {
                            ShowBoardLevels();
                        }
                        else if (View == 6) {
                            ShowBacklogLevels();
                        }
                    }
                    break;
                case "t":
                    if (!Busy && _backend is AzureDevOpsBackend && View == 1) {
                        ShowWorkItemTypes();
                    }
                    break;
                case "a":
                    ShowActions(Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
                    break;
                case "refresh":
                    if (!Busy) {
                        if (_expanded && Surface.Model.Current?.Original is DeveloperItem selected) {
                            _error = false;
                            Detail(selected);
                        }
                        else {
                            Load();
                        }
                    }

                    break;
                case "back":
                    _expanded = false;
                    Surface.SetDocument(null);
                    break;
                case "[":
                    if (!Busy && _page > 0) {
                        _page--;
                        Load();
                    }

                    break;
                case "]":
                    // Allow at most one empty page past the end.
                    if (!Busy && Surface.Model.Rows.Count > 0) {
                        _page++;
                        Load();
                    }

                    break;
            }
        };
        _worker = Task.Run(async () => {
            try {
                await foreach (var work in _requests.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false)) {
                    if (!work.Mutation && work.Version != Interlocked.Read(ref _version)) {
                        continue;
                    }

                    try {
                        Action apply = await work.Execute(_stop.Token).ConfigureAwait(false);
                        _completed.Enqueue(() => {
                            if (work.Mutation || work.Version == _version) {
                                apply();
                            }
                        });
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
                        break;
                    }
                    catch (Exception error) {
                        _completed.Enqueue(() => {
                            if (work.Mutation || work.Version == _version) {
                                Busy = false;
                                _mutation = false;
                                _error = true;
                                Surface.Status = "ERROR · " + TableModel.Text(error.Message);
                                Surface.Refresh();
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
            }
        });
        SwitchView(_visibleViews.Contains(initialView) ? initialView : _visibleViews[0]);
    }

    /// <summary>Switches the resource view and starts loading its first page.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    public void SwitchView(int view) {
        if (view < 0 || view >= _backend.Tabs.Length) {
            throw new ArgumentOutOfRangeException(nameof(view));
        }

        if (_mutation) {
            return;
        }
        if (!_visibleViews.Contains(view)) {
            Surface.Status = "That tab is excluded. Restart without its -ExcludeTab entry to open it.";
            Surface.Refresh();
            return;
        }

        View = view;
        if (_backend is AzureDevOpsBackend) {
            _dashboard.IsVisible = view == 0;
            Surface.MaxHeight = view == 0 ? 12 : int.MaxValue;
        }
        _requestedPipeline = null;
        _analyticsRequested = false;
        _page = 0;
        Surface.ActiveTab = Array.IndexOf(_visibleViews, view);
        _expanded = false;
        Surface.SetDocument(null);
        Surface.ResetColumns();
        Surface.StretchColumn = _backend is AzureDevOpsBackend && AzureDevOpsBackend.IsWorkItemView(view) ? 2 : -1;
        if (_backend is AzureDevOpsBackend) {
            UpdateAzureCommands();
        }
        if (_pages.TryGetValue((view, 0), out var model)) {
            Surface.Model = model;
            Surface.PreferredWidths = model.PreferredWidths;
        }
        else {
            Surface.Model = new(["Loading"]);
        }

        Load();
    }

    private void Queue(Work work) {
        if (!_requests.Writer.TryWrite(work)) {
            Busy = false;
            _mutation = false;
            Surface.Status = "Request queue is busy; retry after the current request completes.";
            Surface.Refresh();
        }
    }

    private void Load(bool background = false) {
        if (_mutation) {
            return;
        }

        _error = false;
        Busy = true;
        if (!background) {
            _selectedKey = null;
            _loadingDetailKey = null;
            _detail = "";
            Surface.SetPreview("DETAILS", _previewEnabled ? "Loading…" : null);
        }
        long version = Interlocked.Increment(ref _version);
        int view = View, page = _page;
        if (!background) {
            Surface.Status = "Loading " + _backend.Tabs[view] + $" · page {page + 1}";
            Surface.Refresh();
        }
        _lastRefresh = Stopwatch.GetTimestamp();
        Queue(new(version, async token => {
            var result = await _backend.LoadAsync(view, page, token).ConfigureAwait(false);
            return () => {
                var previous = Surface.Model;
                bool unchanged = previous.Columns.SequenceEqual(result.Columns)
                    && (Surface.PreferredWidths ?? []).SequenceEqual(result.Widths ?? [])
                    && previous.Rows.Count == result.Items.Length;
                for (int row = 0; unchanged && row < result.Items.Length; row++) {
                    var item = result.Items[row];
                    unchanged = previous.Rows[row].Original is DeveloperItem old && old.Key == item.Key
                        && old.Cells.SequenceEqual(item.Cells) && Equals(old.State, item.State);
                }
                if (background && unchanged) {
                    Busy = false;
                    _summary = result.Summary;
                    if (Surface.Status != result.Summary) {
                        Surface.Status = result.Summary;
                        Surface.Refresh();
                    }
                    if (view == 0 && _backend is AzureDevOpsBackend sameBoards) {
                        LoadBoardDashboard(sameBoards);
                    }
                    return;
                }
                if (background) {
                    _selectedKey = null;
                    _detail = "";
                }
                var model = new TableModel(result.Columns) {
                    Filter = previous.Filter,
                    PreferredWidths = result.Widths,
                    Descending = previous.Descending
                };
                int MatchColumn(int index) =>
                    index >= 0 && index < previous.Columns.Length ? Array.FindIndex(
                        model.Columns,
                        name => name.Equals(previous.Columns[index], StringComparison.OrdinalIgnoreCase)
                    ) : -1;
                model.SortColumn = MatchColumn(previous.SortColumn);
                model.GroupColumn = MatchColumn(previous.GroupColumn);
                if (_backend is AzureDevOpsBackend && view == 6 && !previous.Columns.Contains("Order", StringComparer.Ordinal)) {
                    model.SortColumn = Array.IndexOf(model.Columns, "Order");
                    model.Descending = false;
                }
                for (int column = 0; column < previous.Columns.Length; column++) {
                    int target = MatchColumn(column);
                    if (target >= 0) {
                        model.Enabled[target] = previous.Enabled[column];
                    }
                }

                long? selected = previous.Current?.Id;
                foreach (var item in result.Items) {
                    if (!_ids.TryGetValue((view, item.Key), out long id)) {
                        id = _nextId++;
                        _ids.Add((view, item.Key), id);
                    }

                    model.Rows.Add(new(
                        id,
                        item,
                        [.. item.Cells.Select(cell => TableModel.Text(cell))],
                        [.. item.Cells.Select(SortValue)]
                    ));
                }

                model.Rebuild();
                int index = model.Visible.FindIndex(row => row.Id == selected);
                if (index >= 0) {
                    model.Select(index, Math.Max(1, Surface.Bounds.Height - 6));
                }

                _pages[(view, page)] = model;
                if (_pages.Count > 32) {
                    _pages.Remove(_pages.Keys.First(key => key != (view, page)));
                }

                var retained = new HashSet<(int View, string Key)>();
                foreach (var cached in _pages) {
                    foreach (var row in cached.Value.Rows) {
                        if (row.Original is DeveloperItem entry) {
                            retained.Add((cached.Key.View, entry.Key));
                        }
                    }
                }

                foreach (var key in _ids.Keys.Where(key => !retained.Contains(key)).ToArray()) {
                    _ids.Remove(key);
                }

                Surface.Model = model;
                Surface.PreferredWidths = result.Widths;
                Surface.Title = _backend.Title + (_readOnly ? " · READ ONLY" : "");
                if (_backend is AzureDevOpsBackend && AzureDevOpsBackend.IsWorkItemView(view)) {
                    foreach (string column in AzureOptionalColumns) {
                        int columnIndex = Array.IndexOf(model.Columns, column);
                        if (columnIndex >= 0 && !previous.Columns.Contains(column, StringComparer.Ordinal)) {
                            model.Enabled[columnIndex] = false;
                        }
                    }
                }
                _summary = result.Summary;
                Surface.Status = result.Summary;
                Busy = false;
                if (_backend is AzureDevOpsBackend) {
                    UpdateAzureCommands();
                }
                Surface.Refresh();
                if (_showBoardWhenLoaded && view == 1) {
                    _showBoardWhenLoaded = false;
                    ShowKanban();
                }
                if (view == 0 && _backend is AzureDevOpsBackend ado) {
                    LoadBoardDashboard(ado);
                }
            };
        }));
    }

    private void Detail(DeveloperItem item) {
        long version = Interlocked.Increment(ref _version);
        int view = View;
        Busy = true;
        _loadingDetailKey = item.Key;
        Surface.SetPreview("DETAILS · " + item.Key, _previewEnabled ? "Loading details…" : null);
        Queue(new(version, async token => {
            string text = await _backend.DetailAsync(view, item, token).ConfigureAwait(false);
            return () => {
                _detail = text;
                _loadingDetailKey = null;
                Busy = false;
                _error = false;
                Surface.Status = _summary;
                Surface.SetPreview("DETAILS · " + item.Key + " · Enter expand", _previewEnabled ? text : null);
                if (_expanded) {
                    ShowExpanded(item, text);
                }

                Surface.Refresh();
            };
        }));
    }

    private void ShowActions(int x, int y) {
        if (Busy) {
            return;
        }

        var item = Surface.Model.Current?.Original as DeveloperItem;
        var operations = _backend.Operations(View, item);
        var menu = operations.Where(operation => !_readOnly || operation.ReadOnly).Select(operation => new MenuItem(operation.Label, () => Prompt(operation))).ToList();
        if (item is not null && _backend is IPipelineBackend pipelines && pipelines.IsPipelineView(View)) {
            menu.Insert(0, new MenuItem("Monitor run · jobs / logs / artifacts", RequestPipelineMonitor));
            menu.Insert(1, new MenuItem("Run overview", () => { _expanded = true; Detail(item); }));
        }
        if (menu.Count == 0) {
            menu.Add(new MenuItem(_readOnly ? "Read-only mode · no write actions" : "Inspect the selected item to load available actions", () => {
            }));
        }

        ContextMenuService.Show(Surface, [.. menu], x, y);
    }

    private void Prompt(DeveloperOperation operation) {
        if (operation.ReadOnly) {
            Execute(operation, "");
            return;
        }

        if (operation.InputLabel is null) {
            Confirm(operation, "");
            return;
        }

        Dialog? dialog = null;
        var description = new TextBlock(Files.FileReader.Sanitize(operation.Description, true)) { Wrap = true };
        TextBox? input = operation.InputLabel is null ? null : new TextBox {
            Text = operation.InitialText
        };
        TextArea? multiline = operation.Label.Contains("description", StringComparison.OrdinalIgnoreCase)
            || operation.Label.Contains("discussion", StringComparison.OrdinalIgnoreCase)
            || operation.Label.Contains("comment", StringComparison.OrdinalIgnoreCase) ? new TextArea {
                Text = operation.InitialText,
                MinHeight = 6,
                MaxHeight = 12
            }

        : null;
        var content = new VStack(description);
        if (input is not null) {
            content.Add(new TextBlock(operation.InputLabel!));
            content.Add((Visual?)multiline ?? input);
        }

        content.Add(new HStack(DeveloperButton("Cancel", () => dialog!.Close()), DeveloperButton("Continue", () => {
            string text = multiline?.Text ?? input?.Text ?? "";
            dialog!.Close();
            Confirm(operation, text);
        })));
        dialog = new Dialog {
            Title = new TextBlock(operation.Label),
            IsModal = true,
            Width = Math.Max(24, Math.Min(88, Surface.Bounds.Width - 4)),
            Content = content
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
        if (input is not null) {
            Surface.App?.Focus((Visual?)multiline ?? input);
        }
    }

    private void Confirm(DeveloperOperation operation, string text) {
        Dialog? dialog = null;
        string preview = operation.Description + (operation.InputLabel is null ? "" : "\n\n" + operation.InputLabel +
            ":\n" + text);
        var cancel = DeveloperButton("Cancel", () => dialog!.Close());
        cancel.AutoFocus = true;
        dialog = new Dialog {
            Title = new TextBlock("Confirm · " + operation.Label),
            IsModal = true,
            Width = Math.Max(24, Math.Min(88, Surface.Bounds.Width - 4)),
            Content = new VStack(new ScrollViewer(new TextBlock(Files.FileReader.Sanitize(preview, true)) { Wrap = true }) {
                MaxHeight = Math.Max(1, Surface.Bounds.Height - 10), HorizontalScrollEnabled = false
            }, new HStack(cancel, DeveloperButton("Confirm", () => {
                dialog!.Close();
                Execute(operation, text);
                AppFocus();
            })))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
        Surface.App?.Focus(cancel);
    }

    private void AppFocus() =>
        Surface.App?.Focus(Surface);
    private void Execute(DeveloperOperation operation, string text) {
        if (Busy) {
            return;
        }
        if (operation.TargetView is { } destination && !_visibleViews.Contains(destination)) {
            Surface.Status = "That tab is excluded. Restart without its -ExcludeTab entry to open it.";
            Surface.Refresh();
            return;
        }

        if (_readOnly && !operation.ReadOnly) {
            return;
        }

        bool changesContext = !operation.ReadOnly || operation.TargetView.HasValue;
        _mutation = changesContext;
        Busy = true;
        long version = Interlocked.Increment(ref _version);
        Surface.Status = "WORKING · " + operation.Label;
        Surface.Refresh();
        Queue(new(version, async token => {
            string result = await operation.Execute(text, token).ConfigureAwait(false);
            return () => {
                _mutation = false;
                Busy = false;
                if (operation.TargetView is { } target) {
                    _pages.Clear();
                    _ids.Clear();
                    _expanded = false;
                    Surface.SetDocument(null);
                    SwitchView(target);
                }
                else if (operation.ReadOnly) {
                    _detail = result;
                    _expanded = true;
                    Surface.SetDocument(result);
                }
                else {
                    _expanded = false;
                    Surface.SetDocument(null);
                    Load();
                }

                Surface.Status = "DONE · " + operation.Label + (operation.ReadOnly ? "" : " · " + TableModel.Text(result, 200));
                Surface.Refresh();
            };
        }, changesContext));
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        if (_pipelineMonitor is { } monitor) {
            monitor.Tick();
            if (monitor.Closed) {
                monitor.Dispose();
                _pipelineMonitor = null;
                AppFocus();
            }
            return !Surface.ExitRequested;
        }
        while (_completed.TryDequeue(out var completion)) {
            completion();
        }
        if (!Busy && _requestedPipeline is { } run && _backend is IPipelineBackend pipelines && pipelines.IsPipelineView(View)) {
            _requestedPipeline = null;
            OpenPipelineMonitor(pipelines, run);
            return !Surface.ExitRequested;
        }

        if (_analyticsRequested && !Busy) {
            _analyticsRequested = false;
            if (!_error) {
                ShowAnalytics();
            }
        }

        if (!Busy && !_mutation && !_expanded) {
            string? key = (Surface.Model.Current?.Original as DeveloperItem)?.Key;
            if (key != _selectedKey) {
                _selectedKey = key;
                _error = false;
                _selectionChanged = Stopwatch.GetTimestamp();
                _detail = "";
                Surface.SetPreview("DETAILS", _previewEnabled ? "Loading selection…" : null);
            }
            else if (_previewEnabled && !_error && !Surface.Minimal && Surface.Bounds.Width >= 100 && _detail.Length == 0
                && Surface.Model.Current?.Original is DeveloperItem item
                && Stopwatch.GetElapsedTime(_selectionChanged).TotalMilliseconds >= 100) {
                Detail(item);
            }
            else if (!_error && Stopwatch.GetElapsedTime(_lastRefresh) >= _refreshInterval) {
                Load(background: true);
            }
        }

        return !Surface.ExitRequested;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _pipelineMonitor?.Dispose();
        _stop.Cancel();
        _requests.Writer.TryComplete();
        try {
            _worker.GetAwaiter().GetResult();
        }
        finally {
            _stop.Dispose();
            if (_backend is IDisposable disposable) {
                disposable.Dispose();
            }
        }
    }

    private static object SortValue(string text) {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) {
            return number;
        }

        if (text.Length >= 19 && text[4] == '-'
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) {
            return timestamp;
        }

        return text;
    }

    private void OpenPipelineMonitor(IPipelineBackend backend, DeveloperItem run) {
        if (_pipelineMonitor is not null) {
            return;
        }
        _pipelineMonitor = new(backend, run, Surface.Palette, Surface.Bounds.Width, Surface.Bounds.Height);
        _pipelineMonitor.Show();
    }

    private void RequestPipelineMonitor() {
        if (_backend is not IPipelineBackend pipelines || !pipelines.IsPipelineView(View)
            || Surface.Model.Current?.Original is not DeveloperItem run || _mutation) {
            return;
        }
        if (Busy) {
            _requestedPipeline = run;
            Surface.Status = "Opening run monitor when the current request finishes…";
            Surface.Refresh();
        }
        else {
            OpenPipelineMonitor(pipelines, run);
        }
    }
}
