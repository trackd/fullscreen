using fullscreen.Hosting;
using fullscreen.Tables;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal;

namespace fullscreen.Logs;
/// <summary>Displays log batches with severity filtering and optional tail following.</summary>
public sealed class LogViewerApp : IFullscreenApp {
    private readonly LogReader _reader;
    private readonly int _maximumCount;
    private bool _bookmarksOnly;
    /// <summary>Gets whether the log selection follows newly appended entries.</summary>
    public bool FollowTail { get; private set; }
    /// <summary>Gets the minimum severity included in the log view.</summary>
    public LogLevel MinimumLevel { get; private set; }
    /// <summary>Gets the backing table or directory model.</summary>
    public TableModel Model { get; } = new(["Timestamp", "Level", "Source", "Message"]);
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public TableSurface Surface { get; }
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => Surface;
    /// <summary>Gets whether background input has finished.</summary>
    public bool Completed => _reader.Batches.Completion.IsCompleted;

    /// <summary>Initializes a new LogViewerApp instance.</summary>
    /// <param name="paths">The filesystem paths to read or operate on.</param>
    /// <param name="follow">Whether new log content is read after reaching the end of a file. Defaults to
    /// false.</param>
    /// <param name="maximumCount">The maximum number of input rows retained by the selected count filter.
    /// Defaults to 10000.</param>
    /// <param name="pollMilliseconds">The delay in milliseconds between log polling attempts. Defaults to
    /// 250.</param>
    /// <param name="minimumLevel">The minimum severity included in the log view. Defaults to
    /// LogLevel.Trace.</param>
    /// <param name="filter">The text or expression used to restrict visible rows. Defaults to
    /// &quot;&quot;.</param>
    public LogViewerApp(
        string[] paths,
        bool follow = false,
        int maximumCount = 10000,
        int pollMilliseconds = 250,
        LogLevel minimumLevel = LogLevel.Trace,
        string filter = ""
    ) {
        _maximumCount = maximumCount;
        MinimumLevel = minimumLevel;
        FollowTail = follow;
        Model.Filter = filter;
        Model.SortColumn = 0;
        Model.Include = Include;
        Surface = new(Model, "≋ LOGS") {
            PreferredWidths = [25, 13, 24, 0],
            ExtraHelp = "F follow · L severity · B bookmarks · / text, regex:pattern or wildcard:*pattern*"
        };
        Surface.RowAccent = row => ((LogEntry)row.Original).Level switch {
            LogLevel.Error or LogLevel.Critical => Surface.Palette.Error,
            LogLevel.Warning => Surface.Palette.Warning,
            LogLevel.Information => Surface.Palette.Success,
            LogLevel.Debug or LogLevel.Trace => Surface.Palette.Purple,
            _ => null
        };
        Surface.Navigated += () => FollowTail = false;
        Surface.Shortcut += key => {
            switch (key.ToLowerInvariant()) {
                case "f":
                    FollowTail = !FollowTail;
                    break;
                case "b":
                    _bookmarksOnly = !_bookmarksOnly;
                    Model.Rebuild();
                    break;
                case "l":
                    Levels();
                    break;
            }

            Surface.Refresh();
        };
        _reader = new(paths, follow, maximumCount, pollMilliseconds);
    }

    private bool Include(TableRow row) =>
        row.Original is LogEntry entry && (entry.Level == LogLevel.Unknown || entry.Level >= MinimumLevel)
        && (!_bookmarksOnly || Model.Marked.Contains(row.Id));
    /// <summary>Changes the minimum severity included in the log table.</summary>
    /// <param name="level">The lowest included log severity.</param>
    public void SetMinimumLevel(LogLevel level) {
        MinimumLevel = level;
        Model.Rebuild();
        Surface.Refresh();
    }

    private void Levels() {
        var items = Enum.GetValues<LogLevel>().Where(level => level != LogLevel.Unknown).Select(level => new MenuItem(level +
            " and above (+ unknown)", () => SetMinimumLevel(level))).ToArray();
        ContextMenuService.Show(Surface, items, Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        if (_lastTail != FollowTail || _lastMinimum != MinimumLevel || _lastBookmarks != _bookmarksOnly) {
            Surface.Title = "≋ LOGS · " + (FollowTail ? "LIVE TAIL" : "BROWSE") +
                " · " + MinimumLevel +
                "+" + (_bookmarksOnly ? " · BOOKMARKS" : "");
            _lastTail = FollowTail;
            _lastMinimum = MinimumLevel;
            _lastBookmarks = _bookmarksOnly;
            Surface.Refresh();
        }

        bool changed = false;
        for (int batch = 0; batch < 4 && _reader.Batches.TryRead(out var update); batch++) {
            foreach (var entry in update.Entries) {
                Model.Rows.Add(entry.Row());
            }

            if (update.Status is not null) {
                Surface.Status = TableModel.Text(update.Status);
            }

            changed = true;
        }

        if (changed) {
            int remove = Model.Rows.Count - _maximumCount;
            if (remove > 0) {
                for (int i = 0; i < remove; i++) {
                    Model.Marked.Remove(Model.Rows[i].Id);
                }

                Model.Rows.RemoveRange(0, remove);
            }

            Model.Rebuild();
            if (FollowTail) {
                Model.Select(Model.Visible.Count - 1, Math.Max(1, Surface.Bounds.Height - 6));
            }

            Surface.Refresh();
        }

        return !Surface.ExitRequested;
    }

    /// <inheritdoc/>
    public void Dispose() =>
        _reader.Dispose();
    private bool? _lastTail;
    private LogLevel? _lastMinimum;
    private bool? _lastBookmarks;
}
