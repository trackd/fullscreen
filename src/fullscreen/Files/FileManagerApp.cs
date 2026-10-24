using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Diagnostics;
using fullscreen.Hosting;
using XenoAtom.Terminal.UI;

namespace fullscreen.Files;
/// <summary>One cancellable I/O worker; only Tick mutates visuals. Obsolete reads never publish.</summary>
public sealed class FileManagerApp : IFullscreenApp {
    private readonly FileManagerSurface _surface;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Work> _requests = Channel.CreateUnbounded<Work>(new() {
        SingleReader = true,
        SingleWriter = true
    });
    private readonly ConcurrentQueue<Action> _completed = new();
    private readonly Task _worker;
    private long _readVersion;
    private bool _disposed;
    private bool _hasError;
    private FileSystemWatcher? _watcher;
    private long _refreshRequested;
    private bool _parentVisible, _previewVisible;
    private sealed record Work(long Version, Func<CancellationToken, Task<Action?>> Execute, bool Operation = false);
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => _surface;
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public FileManagerSurface Surface => _surface;
    /// <summary>Gets the directory currently displayed.</summary>
    public string CurrentPath => _surface.Model.CurrentPath;
    /// <summary>Gets the filesystem paths accepted by the user.</summary>
    public string[] ChosenPaths => _surface.Model.OperationPaths();

    /// <summary>Initializes a new FileManagerApp instance.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    /// <param name="hidden">Whether the filesystem entry is hidden. Defaults to false.</param>
    /// <param name="sort">The process sort field. Defaults to FileSort.Name.</param>
    /// <param name="descending">Whether sorting uses descending order. Defaults to false.</param>
    /// <param name="preview">The preview content, or null to clear the preview. Defaults to true.</param>
    /// <param name="minimal">Whether reduced-chrome layout is enabled. Defaults to false.</param>
    public FileManagerApp(
        string path,
        bool hidden = false,
        FileSort sort = FileSort.Name,
        bool descending = false,
        bool preview = true,
        bool minimal = false
    ) : this(path, hidden, sort, descending, preview, minimal, false) {
    }

    internal FileManagerApp(
        string path,
        bool hidden,
        FileSort sort,
        bool descending,
        bool preview,
        bool minimal,
        bool commander
    ) {
        _surface = new FileManagerSurface {
            PreviewEnabled = preview,
            Minimal = minimal,
            CommanderPane = commander
        };
        _surface.Model.ShowHidden = hidden;
        _surface.Model.Sort = sort;
        _surface.Model.Descending = descending;
        _surface.NavigateRequested += (path, history, selected) => Navigate(path, history, selected);
        _surface.PreviewRequested += Preview;
        _surface.OperationRequested += Operate;
        _worker = Task.Run(async () => {
            try {
                await foreach (var work in _requests.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false)) {
                    if (!work.Operation && work.Version != Interlocked.Read(ref _readVersion)) {
                        continue;
                    }

                    try {
                        var apply = await work.Execute(_stop.Token).ConfigureAwait(false);
                        if (apply is not null && (work.Operation || work.Version == Interlocked.Read(ref _readVersion))) {
                            _completed.Enqueue(() => {
                                if (work.Operation || work.Version == _readVersion) {
                                    apply();
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
                        break;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                        if (work.Operation || work.Version == Interlocked.Read(ref _readVersion)) {
                            _completed.Enqueue(() => {
                                if (!work.Operation && work.Version != _readVersion) {
                                    return;
                                }

                                _surface.Busy = false;
                                _hasError = true;
                                _surface.HasError = true;
                                _surface.Status = error.Message;
                                _surface.Refresh();
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
            }
        });
        string full = Path.GetFullPath(path);
        if (File.Exists(full)) {
            Navigate(Path.GetDirectoryName(full)!, true, full);
        }
        else {
            Navigate(full, true, null);
        }
    }

    /// <summary>Re-reads the current directory, then shows <paramref name="error"/> if given.</summary>
    internal void Reload(string? error = null) {
        if (!_surface.Busy && _surface.Model.CurrentPath.Length > 0) {
            Navigate(_surface.Model.CurrentPath, false, null, error);
        }
    }

    private void Navigate(string path, bool history, string? selected, string? error = null) {
        _hasError = false;
        _surface.HasError = false;
        long version = Interlocked.Increment(ref _readVersion);
        _surface.Busy = true;
        _surface.Status = "Loading " + path;
        _surface.Refresh();
        bool parent = _parentVisible = !_surface.CommanderPane && !_surface.Minimal && !_surface.ParentPaneCollapsed
            && _surface.Bounds.Width >= 100
            && _surface.Bounds.Height >= 12;
        _requests.Writer.TryWrite(new(version, token => {
            var snapshot = FileReader.ReadDirectory(path, token, parent);
            if (_surface.CommanderPane && Directory.GetParent(snapshot.Path) is { } up) {
                snapshot = snapshot with {
                    Entries = [new(up.FullName, "..", true, false, 0, default, false), .. snapshot.Entries]
                };
            }

            return Task.FromResult<Action?>(() => {
                var old = _surface.Model.Selected;
                _surface.Model.Apply(snapshot, history, selected);
                _surface.Busy = false;
                Watch(snapshot.Path);
                _surface.Status = snapshot.Truncated ? "First 100,000 entries · listing truncated" : "Ready";
                if (error is not null) {
                    _hasError = true;
                    _surface.HasError = true;
                    _surface.Status = error;
                }

                if (old != _surface.Model.Selected || _surface.Preview is null) {
                    _surface.SelectionChanged();
                }
                else {
                    _surface.Refresh();
                }
            });
        }));
    }

    private void Preview(FileEntry? entry) {
        if (_surface.Busy) {
            return;
        }

        if (_hasError) {
            _hasError = false;
            _surface.Status = "Ready";
        }

        long version = Interlocked.Increment(ref _readVersion);
        _surface.SetPreview(null);
        if (entry is null || !_surface.PreviewVisible) {
            return;
        }

        bool hidden = _surface.Model.ShowHidden;
        _requests.Writer.TryWrite(new(version, async token => {
            // TextMate/Sixel render on the UI thread in ~200-300 ms, so only preview where the selection comes to rest.
            await Task.Delay(150, token).ConfigureAwait(false);
            if (version != Interlocked.Read(ref _readVersion)) {
                return null;
            }

            var result = await FileReader.ReadPreviewAsync(entry.Value, hidden, token).ConfigureAwait(false);
            return () => {
                if (_surface.PreviewVisible) {
                    _surface.SetPreview(result);
                }
            };
        }));
    }

    private void Operate(FileOperation operation) {
        if (_surface.Busy) {
            return;
        }

        _hasError = false;
        _surface.Busy = true;
        _surface.Status = operation.Description;
        _surface.Refresh();
        Interlocked.Increment(ref _readVersion);
        _requests.Writer.TryWrite(new(0, async token => {
            string[] items = operation.Kind == FileOperationKind.CreateDirectory ? [operation.Destination] : operation.Sources;
            var (failed, error) = await FileOperations.EachAsync(items, async source => {
                switch (operation.Kind) {
                    case FileOperationKind.Copy:
                        await FileOperations.CopyAsync(source, Path.Combine(operation.Destination, Path.GetFileName(source)), token).ConfigureAwait(false);
                        break;
                    case FileOperationKind.Move:
                        FileOperations.Move(source, Path.Combine(operation.Destination, Path.GetFileName(source)));
                        break;
                    case FileOperationKind.Rename:
                        FileOperations.Rename(source, operation.Destination);
                        break;
                    case FileOperationKind.Trash:
                        FileOperations.Trash(source);
                        break;
                    case FileOperationKind.CreateDirectory:
                        FileOperations.CreateDirectory(operation.Directory, source);
                        break;
                }
            }, token).ConfigureAwait(false);
            return () => {
                _surface.Busy = false;
                _surface.Model.Marked.IntersectWith(failed); // Leave only failures marked so they can be retried.
                _surface.OperationFinished();
                Navigate(_surface.Model.CurrentPath, false, null, error);
            };
        }, Operation: true));
    }

    private void Watch(string path) {
        if (_watcher is not null && FileManagerModel.PathComparer.Equals(_watcher.Path, path)) {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;
        try {
            var watcher = new FileSystemWatcher(path) {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            void Changed(
                object sender,
                FileSystemEventArgs args
            ) =>
                Interlocked.Exchange(ref _refreshRequested, Stopwatch.GetTimestamp());
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Changed += Changed;
            watcher.Renamed += Changed;
            watcher.Error += (_, _) => Interlocked.Exchange(ref _refreshRequested, Stopwatch.GetTimestamp());
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) {
        }
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        while (_completed.TryDequeue(out var action)) {
            action();
        }

        if (!_surface.Busy) {
            bool parent = !_surface.CommanderPane && !_surface.Minimal && !_surface.ParentPaneCollapsed
                && _surface.Bounds.Width >= 100
                && _surface.Bounds.Height >= 12;
            bool preview = _surface.PreviewVisible;
            if (!preview && _surface.Preview is not null) {
                Interlocked.Increment(ref _readVersion);
                _surface.SetPreview(null);
            }

            if (parent != _parentVisible && _surface.Model.CurrentPath.Length != 0) {
                Navigate(_surface.Model.CurrentPath, false, _surface.Model.Selected?.FullPath);
            }
            else if (preview && !_previewVisible && _surface.Model.Selected is { } selected) {
                Preview(selected);
            }

            _previewVisible = preview;
        }

        long changed = Interlocked.Read(ref _refreshRequested);
        if (changed != 0 && !_surface.Busy && !_hasError
            && Stopwatch.GetElapsedTime(changed).TotalMilliseconds >= 300) {
            Interlocked.Exchange(ref _refreshRequested, 0);
            Navigate(_surface.Model.CurrentPath, false, null);
        }

        return !_surface.ExitRequested;
    }

    internal static bool Abandon(Task task) {
        using var timeout = new CancellationTokenSource();
        bool abandoned = Task.WhenAny(task, Task.Delay(2000, timeout.Token)).GetAwaiter().GetResult() != task;
        timeout.Cancel();
        return abandoned;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _requests.Writer.TryComplete();
        _stop.Cancel();
        _watcher?.Dispose();
        // A read on a dead network share ignores cancellation; abandon it instead of hanging the shell, and keep its token alive.
        if (Abandon(_worker)) {
            return;
        }

        try {
            _worker.GetAwaiter().GetResult();
        }
        finally {
            _stop.Dispose();
        }
    }
}

/// <summary>Identifies the file operation requested by the file manager.</summary>
public enum FileOperationKind {
    /// <summary>Requests the copy filesystem operation.</summary>
    Copy,
    /// <summary>Requests the move filesystem operation.</summary>
    Move,
    /// <summary>Requests the rename filesystem operation.</summary>
    Rename,
    /// <summary>Requests the create directory filesystem operation.</summary>
    CreateDirectory,
    /// <summary>Requests the trash filesystem operation.</summary>
    Trash
}

/// <summary>Stores source paths and the destination information for a file operation.</summary>
/// <param name="Kind">The operation or event category.</param>
/// <param name="Sources">The original source objects associated with data points.</param>
/// <param name="Directory">The parent or working directory.</param>
/// <param name="Destination">The destination path or buffer.</param>
public sealed record FileOperation(FileOperationKind Kind, string[] Sources, string Directory, string Destination) {
    /// <summary>Gets the human-readable description.</summary>
    public string Description => Kind + " · " + (Sources.Length == 0 ? Destination : $"{Sources.Length} item(s)");
}
