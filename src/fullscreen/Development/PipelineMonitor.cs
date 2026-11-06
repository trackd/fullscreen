using System.Diagnostics;
using fullscreen.Files;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Development;

internal sealed class PipelineMonitor : IDisposable {
    private readonly IPipelineBackend _backend;
    private readonly DeveloperItem _run;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, long> _ids = new(StringComparer.Ordinal);
    private Task<(PipelineSnapshot Snapshot, string? Selection, PipelineLog? Log)>? _pending;
    private CancellationTokenSource? _pollStop;
    private Task? _download;
    private (PipelineArtifact Artifact, string Destination)? _requestedDownload;
    private string? _downloadPath;
    private string _downloadStatus = "";
    private PipelineSnapshot? _snapshot;
    private PipelineLog? _log;
    private string? _logSelection;
    private string? _requestedSelection;
    private long _lastPoll;
    private DateTimeOffset? _updatedAt;
    private bool _paused;
    private bool _follow = true;
    private bool _expanded;
    private bool _closed;
    private readonly Dialog _dialog;
    internal TableSurface Surface { get; }
    internal bool Closed => _closed;
    internal bool Loading => _pending is not null;

    internal PipelineMonitor(IPipelineBackend backend, DeveloperItem run, Themes.ThemePalette palette, int width, int height) {
        _backend = backend;
        _run = run;
        Surface = new(new(["Type", "Name", "Status", "Duration"]), "RUN MONITOR") {
            Tabs = ["Flow", "Artifacts"],
            Palette = palette,
            PreferredWidths = [10, 48, 22, 14],
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            PriorityShortcuts = ["p", "P", "f", "F", "d", "D"],
            FooterHelp = "Enter logs / download · Backspace flow · / search · Q close",
            ExtraHelp = "P pause refresh · F follow tail · D download · F5 refresh",
            DocumentTitle = "JOB / TASK LOG",
            DocumentHelp = "Backspace flow · P pause refresh · F follow tail · / search · Q close"
        };
        Surface.CellAccent = (row, column) => Surface.ActiveTab == 0 && column == 2 ? DeveloperStatus.Color(row.Cells[column], palette) : null;
        Surface.RowAccent = row => Surface.ActiveTab == 0 ? DeveloperStatus.Color(row.Cells[2], palette) : null;
        Surface.DocumentAccent = line => DeveloperStatus.LineColor(line, palette);
        Surface.InspectRequested += _ => {
            if (Surface.ActiveTab == 1) {
                PromptDownload();
            }
            else {
                _logSelection = (Surface.Model.Current?.Original as PipelineNode)?.Id;
                _expanded = true;
                Surface.SetDocument(LogText());
                Surface.UpdateLiveDocument(LogText(), _follow);
            }
        };
        Surface.TabRequested += tab => {
            _expanded = false;
            Surface.SetDocument(null);
            Surface.ActiveTab = tab;
            Rebuild();
        };
        Surface.BackRequested = () => {
            if (_expanded) {
                _expanded = false;
                Surface.SetDocument(null);
            }
            else {
                Close();
            }
            return true;
        };
        Surface.Shortcut += key => {
            switch (key.ToLowerInvariant()) {
                case "p": _paused = !_paused; break;
                case "f": ToggleFollow(); break;
                case "d": if (Surface.ActiveTab == 1) { PromptDownload(); } break;
                case "refresh": _lastPoll = 0; _paused = false; break;
            }
            UpdateStatus();
        };
        Surface.ToolbarCommands = [
            ("P Pause refresh", () => { _paused = !_paused; UpdateStatus(); }),
            ("F Follow tail", ToggleFollow),
            ("F5 Refresh", () => { _lastPoll = 0; _paused = false; }),
            ("Close", Close)
        ];
        _dialog = new Dialog {
            Title = new TextBlock("Pipeline / Actions run"),
            Content = Surface,
            IsModal = true,
            Width = Math.Max(24, width - 2),
            Height = Math.Max(10, height - 2)
        };
        _dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                Close();
                e.Handled = true;
            }
        });
    }

    internal void Show() {
        _dialog.Show();
        Surface.App?.Focus(Surface);
    }

    private string? Selection => _expanded ? _logSelection : (Surface.Model.Current?.Original as PipelineNode)?.Id;

    internal void Tick() {
        if (_closed) {
            return;
        }
        if (Surface.ExitRequested) {
            Close();
            return;
        }
        if (_pending is not null && _requestedSelection != Selection) {
            _pollStop?.Cancel();
        }
        if (_download is { IsCompleted: true } download) {
            _download = null;
            try {
                download.GetAwaiter().GetResult();
                _downloadStatus = "Downloaded archive: " + _downloadPath;
            }
            catch (Exception error) {
                _downloadStatus = "ERROR · artifact download · " + TableModel.Text(error.Message);
            }
            UpdateStatus();
        }
        if (_pending is { IsCompleted: true } pending) {
            _pending = null;
            _pollStop?.Dispose();
            _pollStop = null;
            try {
                var result = pending.GetAwaiter().GetResult();
                bool rowsChanged = _snapshot is null || !_snapshot.Nodes.AsSpan().SequenceEqual(result.Snapshot.Nodes)
                    || !_snapshot.Artifacts.AsSpan().SequenceEqual(result.Snapshot.Artifacts);
                bool headingChanged = _snapshot?.Title != result.Snapshot.Title || _snapshot?.Status != result.Snapshot.Status;
                var previousLog = _log;
                _snapshot = result.Snapshot;
                _updatedAt = DateTimeOffset.Now;
                if (result.Selection == Selection) {
                    _log = result.Log;
                    _logSelection = result.Selection;
                }
                if (rowsChanged) {
                    Rebuild();
                }
                else {
                    if (headingChanged) {
                        Surface.Title = _snapshot.Title + " · " + _snapshot.Status;
                        Surface.Refresh();
                    }
                    if (previousLog != _log) {
                        UpdateLog();
                    }
                }
                UpdateStatus();
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested) {
                _lastPoll = 0;
            }
            catch (Exception error) {
                _paused = true;
                Surface.Status = "ERROR · refresh paused · " + TableModel.Text(error.Message) + " · F5 retry";
                Surface.Refresh();
            }
        }
        if (_pending is null && _download is null && _requestedDownload is { } transfer) {
            _requestedDownload = null;
            _download = Task.Run(() => DownloadAsync(transfer.Artifact, transfer.Destination));
            _downloadStatus = "Downloading archive… · " + transfer.Destination;
            UpdateStatus();
        }
        if (_pending is not null || _download is not null) {
            return;
        }
        string? selected = Selection;
        bool changed = selected != _requestedSelection;
        if (_paused && !changed) {
            return;
        }
        if (!changed && _lastPoll != 0 && Stopwatch.GetElapsedTime(_lastPoll).TotalSeconds < 3) {
            return;
        }
        _requestedSelection = selected;
        if (changed) {
            _log = null;
            if (_expanded) {
                Surface.UpdateLiveDocument("Loading selected node…", false);
            }
            else {
                Surface.SetPreview("LOG · Enter expand", "Loading selected node…");
            }
        }
        _lastPoll = Stopwatch.GetTimestamp();
        _pollStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var token = _pollStop.Token;
        _pending = Task.Run(() => RefreshAsync(selected, token), token);
    }

    private async Task<(PipelineSnapshot, string?, PipelineLog?)> RefreshAsync(string? selected, CancellationToken token) {
        var snapshot = await _backend.ReadPipelineAsync(_run, token).ConfigureAwait(false);
        var node = snapshot.Nodes.FirstOrDefault(entry => entry.Id == selected);
        PipelineLog? log = null;
        if (node is not null) {
            var descendants = Descendants(snapshot.Nodes, node.Id);
            bool Active(PipelineNode entry) => entry.LogId is not null &&
                (entry.Status.Contains("progress", StringComparison.OrdinalIgnoreCase) || entry.Status.Contains("running", StringComparison.OrdinalIgnoreCase));
            var target = descendants.LastOrDefault(entry => Active(entry) && entry.Kind is "Task" or "Step")
                ?? descendants.LastOrDefault(Active)
                ?? descendants.LastOrDefault(entry => entry.LogId is not null) ?? node;
            try {
                log = await _backend.ReadPipelineLogAsync(_run, target, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException or TimeoutException) {
                log = new("", "ERROR · selected log unavailable · " + TableModel.Text(error.Message) + "\nStatus monitoring continues; the log will be retried.");
            }
            log = log with { Notice = target.Name + "\n" + node.Details + (target.Id != node.Id ? "\n" + target.Details : "") + "\n" + log.Notice };
        }
        return (snapshot, selected, log);
    }

    private static PipelineNode[] Descendants(PipelineNode[] nodes, string id) {
        var included = new HashSet<string>(StringComparer.Ordinal) { id };
        bool changed;
        do {
            changed = false;
            foreach (var node in nodes) {
                if (node.ParentId is { } parent && included.Contains(parent)) {
                    changed |= included.Add(node.Id);
                }
            }
        } while (changed);
        return [.. nodes.Where(node => node.Id != id && included.Contains(node.Id))];
    }

    private string LogText() => FileReader.Sanitize((_log?.Notice ?? "Select a job or task to load its logs.") + "\n\n" + (_log?.Text ?? ""), true);

    private void Rebuild() {
        if (_snapshot is null) {
            return;
        }
        var previous = Surface.Model;
        var model = new TableModel(Surface.ActiveTab == 0 ? ["Type", "Name", "Status", "Duration"] : ["Artifact", "Size", "Availability"]);
        bool sameView = previous.Columns.SequenceEqual(model.Columns);
        if (sameView) {
            model.Filter = previous.Filter;
            model.SortColumn = previous.SortColumn;
            model.GroupColumn = previous.GroupColumn;
            model.Descending = previous.Descending;
            previous.Enabled.CopyTo(model.Enabled, 0);
            foreach (var filter in previous.ColumnFilters) {
                model.ColumnFilters.Add(filter.Key, filter.Value);
            }
        }
        void Add(string key, object original, string[] cells) {
            if (!_ids.TryGetValue(key, out long id)) {
                id = _ids.Count;
                _ids.Add(key, id);
            }
            model.Rows.Add(new(id, original, [.. cells.Select(cell => TableModel.Text(cell))], [.. cells.Cast<object>()]));
        }
        if (Surface.ActiveTab == 0) {
            var byId = _snapshot.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var children = _snapshot.Nodes.Where(node => node.ParentId is not null).ToLookup(node => node.ParentId!, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(PipelineNode node, int depth) {
                var pending = new Stack<(PipelineNode Node, int Depth)>();
                pending.Push((node, depth));
                while (pending.TryPop(out var entry)) {
                    if (!visited.Add(entry.Node.Id)) {
                        continue;
                    }
                    Add("node:" + entry.Node.Id, entry.Node, [entry.Node.Kind, new string(' ', Math.Min(entry.Depth, 12) * 2) + entry.Node.Name, entry.Node.Status, entry.Node.Duration]);
                    foreach (var child in children[entry.Node.Id].Reverse()) {
                        pending.Push((child, entry.Depth + 1));
                    }
                }
            }
            foreach (var root in _snapshot.Nodes.Where(node => node.ParentId is null || !byId.ContainsKey(node.ParentId))) {
                Visit(root, 0);
            }
            foreach (var node in _snapshot.Nodes) {
                Visit(node, 0);
            }
        }
        else {
            foreach (var artifact in _snapshot.Artifacts) {
                Add("artifact:" + artifact.Id, artifact, [artifact.Name, artifact.Size, artifact.Expired ? "Expired" : "Available"]);
            }
        }
        model.Rebuild();
        int index = sameView ? model.Visible.FindIndex(row => row.Id == previous.Current?.Id) : 0;
        if (sameView) {
            model.Select(previous.Offset, 1);
        }
        model.Select(Math.Max(0, index), Math.Max(1, Surface.Bounds.Height - 6));
        Surface.Model = model;
        Surface.Title = _snapshot.Title + " · " + _snapshot.Status;
        Surface.PreferredWidths = Surface.ActiveTab == 0 ? [10, 48, 22, 14] : [48, 16, 20];
        UpdateLog();
        Surface.Refresh();
    }

    private void UpdateLog() {
        if (_expanded) {
            Surface.UpdateLiveDocument(LogText(), _follow);
        }
        else {
            if (Surface.ActiveTab == 0) {
                Surface.UpdateLivePreview("LOG · Enter expand", LogText(), _follow);
            }
            else {
                Surface.SetPreview("ARTIFACTS", "Select an artifact and press Enter or D to save its archive.\nExisting files are never overwritten; archives are not extracted.\nLimit: 1 GiB per download.");
            }
        }
    }

    private void UpdateStatus() {
        string status = (_downloadStatus.Length > 0 ? _downloadStatus + " · " : "") +
            (_paused ? "PAUSED" : "Refreshing every 3s") + " · " + (_follow ? "Follow tail ON" : "Follow tail OFF") +
            (_updatedAt is { } updated ? " · Updated " + updated.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "") +
            " · " + (_snapshot?.Notice ?? "Loading run…");
        if (Surface.Status != status) {
            Surface.Status = status;
            Surface.Refresh();
        }
    }

    private void PromptDownload() {
        if (_download is not null || _requestedDownload is not null || Surface.Model.Current?.Original is not PipelineArtifact artifact) {
            return;
        }
        if (artifact.Expired) {
            Surface.Status = "Artifact expired; it cannot be downloaded.";
            Surface.Refresh();
            return;
        }
        var path = new TextBox(Path.Combine(Environment.CurrentDirectory, "artifact-" + Guid.NewGuid().ToString("N")[..8] + ".zip"));
        var validation = new TextBlock { Wrap = true };
        Dialog? prompt = null;
        prompt = new Dialog {
            Title = new TextBlock("Download artifact · " + FileReader.Sanitize(artifact.Name, true)),
            IsModal = true,
            Width = Math.Max(24, Math.Min(90, Surface.Bounds.Width - 2)),
            Content = new VStack(new TextBlock("Save archive to a new file (no overwrite or extraction):"), path, validation,
                new HStack(new Button("Cancel").Click(() => prompt!.Close()), new Button("Download").Click(() => {
                    try {
                        string destination = Path.GetFullPath(path.Text ?? "");
                        if (File.Exists(destination) || Directory.Exists(destination)) {
                            throw new IOException("Destination already exists; choose a new filename.");
                        }
                        _downloadPath = destination;
                        _requestedDownload = (artifact, destination);
                        _downloadStatus = "Artifact download queued… · " + destination;
                        UpdateStatus();
                        prompt!.Close();
                    }

                    catch (Exception error) {
                        Surface.Status = "ERROR · " + TableModel.Text(error.Message);
                        validation.Text = Surface.Status;
                        Surface.Refresh();
                    }
                })))
        };
        prompt.Show();
        Surface.App?.Focus(path);
    }

    private void ToggleFollow() {
        _follow = !_follow;
        if (_expanded) {
            Surface.UpdateLiveDocument(LogText(), _follow);
        }
        else if (Surface.ActiveTab == 0) {
            Surface.UpdateLivePreview("LOG · Enter expand", LogText(), _follow);
        }
        UpdateStatus();
    }

    private async Task DownloadAsync(PipelineArtifact artifact, string destination) =>
        await _backend.DownloadPipelineArtifactAsync(_run, artifact, destination, _stop.Token).ConfigureAwait(false);

    private void Close() {
        if (!_closed) {
            _closed = true;
            _stop.Cancel();
            _dialog.Close();
        }
    }

    public void Dispose() {
        Close();
        // The backend owns its transport; finish cancellation before the parent disposes it.
        try {
            Task.WhenAll(_pending ?? Task.CompletedTask, _download ?? Task.CompletedTask).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or InvalidOperationException
            or TimeoutException or System.Text.Json.JsonException or NotSupportedException or UnauthorizedAccessException) {
            // Failures have no remaining UI after the monitor is closed.
        }
        _pollStop?.Dispose();
        _stop.Dispose();
    }
}
