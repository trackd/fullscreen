using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using fullscreen.Hosting;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Development;
/// <summary>One serialized I/O worker; cached lazy pages and UI-thread-only publication.</summary>
public sealed partial class DeveloperApp : IFullscreenApp {
    private readonly IDeveloperBackend _backend;
    private readonly TimeSpan _refreshInterval;
    private readonly bool _readOnly;
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
    private static readonly string[] AzureOptionalColumns = ["Type", "Board column", "Parent", "Priority", "Remaining", "Iteration", "Tags"];
    /// <summary>Gets the resource or monitor view index.</summary>
    public int View { get; private set; }
    /// <summary>Gets whether a background operation is in progress.</summary>
    public bool Busy { get; private set; }
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public TableSurface Surface { get; }
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => Surface;

    private sealed record Work(long Version, Func<CancellationToken, Task<Action>> Execute, bool Mutation = false);
    /// <summary>Initializes a new DeveloperApp instance.</summary>
    /// <param name="backend">The resource backend owned by the application.</param>
    /// <param name="initialView">The view index activated at startup. Defaults to 0.</param>
    /// <param name="refreshSeconds">The interval in seconds between resource refreshes. Defaults to
    /// 30.</param>
    /// <param name="readOnly">Whether actions that change external state are disabled. Defaults to
    /// false.</param>
    /// <param name="minimal">Whether reduced-chrome layout is enabled. Defaults to false.</param>
    public DeveloperApp(
        IDeveloperBackend backend,
        int initialView = 0,
        int refreshSeconds = 30,
        bool readOnly = false,
        bool minimal = false
    ) {
        _backend = backend;
        _previewEnabled = backend is not AzureDevOpsBackend;
        _refreshInterval = TimeSpan.FromSeconds(refreshSeconds);
        _readOnly = readOnly;
        Surface = new(new TableModel(["Loading"]), backend.Title) {
            Tabs = backend.Tabs,
            DocumentTitle = "DETAILS",
            DocumentHint = "read-only · source data / diff",
            DocumentHelp = "Backspace list · Tab views · arrows scroll · F5 refresh · A actions · Q close",
            ExtraHelp = "A actions · F5 refresh · [/] pages · Shift↑↓ preview",
            Minimal = minimal
        };
        Surface.CellAccent = (row, column) => {
            string name = Surface.Model.Columns[column];
            if (name is not ("Status" or "Conclusion" or "Result" or "State")) {
                return null;
            }

            string value = row.Cells[column];
            if (value.StartsWith("✓ ", StringComparison.Ordinal)) {
                return Surface.Palette.Success;
            }

            if (value.StartsWith("✗ ", StringComparison.Ordinal)) {
                return Surface.Palette.Error;
            }

            if (value.StartsWith("● ", StringComparison.Ordinal)) {
                return Surface.Palette.Warning;
            }

            if (value.StartsWith("○ ", StringComparison.Ordinal)) {
                return Surface.Palette.Foreground;
            }

            return value.ToLowerInvariant() switch {
                "success" or "succeeded" or "completed" or "active" => Surface.Palette.Success,
                "failure" or "failed" or "timed_out" or "startup_failure" or "action_required" => Surface.Palette.Error,
                "queued" or "pending" or "in_progress" or "inprogress" or "waiting" or "requested"
                    or "partiallysucceeded" => Surface.Palette.Warning,
                "cancelled" or "canceled" or "skipped" or "disabled_manually" or "disabled_inactivity" => Surface.Palette.Foreground,
                _ => null
            };
        };
        Surface.InspectRequested += _ => {
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
                SwitchView(view);
            }
        };
        Surface.Shortcut += key => {
            switch (key.ToLowerInvariant()) {
                case "u":
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
                    if (_backend is AzureDevOpsBackend && View is 2 or 3 && !Busy) {
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
                    if (!Busy && _backend is AzureDevOpsBackend && View == 6) {
                        ShowBacklogLevels();
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
        SwitchView(initialView);
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

        View = view;
        _analyticsRequested = false;
        _page = 0;
        Surface.ActiveTab = view;
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

    private void Load() {
        if (_mutation) {
            return;
        }

        _error = false;
        Busy = true;
        _selectedKey = null;
        _loadingDetailKey = null;
        _detail = "";
        Surface.SetPreview("DETAILS", _previewEnabled ? "Loading…" : null);
        long version = Interlocked.Increment(ref _version);
        int view = View, page = _page;
        Surface.Status = "Loading " + _backend.Tabs[view] + $" · page {page + 1}";
        Surface.Refresh();
        _lastRefresh = Stopwatch.GetTimestamp();
        Queue(new(version, async token => {
            var result = await _backend.LoadAsync(view, page, token).ConfigureAwait(false);
            return () => {
                var previous = Surface.Model;
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
        while (_completed.TryDequeue(out var completion)) {
            completion();
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
                Load();
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
}
