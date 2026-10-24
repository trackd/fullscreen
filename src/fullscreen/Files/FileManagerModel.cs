namespace fullscreen.Files;
/// <summary>Selects the field used to order file entries.</summary>
public enum FileSort {
    /// <summary>Orders filesystem entries by name.</summary>
    Name,
    /// <summary>Orders filesystem entries by size.</summary>
    Size,
    /// <summary>Orders filesystem entries by modified.</summary>
    Modified,
    /// <summary>Orders filesystem entries by type.</summary>
    Type
}

/// <summary>Stores file metadata used by directory listing and preview views.</summary>
/// <param name="FullPath">The absolute filesystem path.</param>
/// <param name="Name">The display name.</param>
/// <param name="IsDirectory">Whether the filesystem entry is a directory.</param>
/// <param name="IsLink">Whether the filesystem entry is a symbolic link or reparse point.</param>
/// <param name="Length">The file length in bytes.</param>
/// <param name="Modified">The last modification timestamp.</param>
/// <param name="Hidden">Whether the filesystem entry is hidden.</param>
public readonly record struct FileEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    bool IsLink,
    long Length,
    DateTime Modified,
    bool Hidden
);
/// <summary>Contains a directory listing and optional parent-directory entries.</summary>
/// <param name="Path">The filesystem or resource path.</param>
/// <param name="Entries">The active directory entries.</param>
/// <param name="ParentEntries">The optional parent-directory entries.</param>
/// <param name="Truncated">Whether the source exceeded the retained output limit. Defaults to false.</param>
public sealed record DirectorySnapshot(
    string Path,
    FileEntry[] Entries,
    FileEntry[] ParentEntries,
    bool Truncated = false
);
/// <summary>Identifies how a file preview should be presented.</summary>
public enum PreviewKind {
    /// <summary>Identifies a text preview.</summary>
    Text,
    /// <summary>Identifies a markdown preview.</summary>
    Markdown,
    /// <summary>Identifies a directory preview.</summary>
    Directory,
    /// <summary>Identifies a binary preview.</summary>
    Binary,
    /// <summary>Identifies a image preview.</summary>
    Image,
    /// <summary>Identifies a unavailable preview.</summary>
    Unavailable
}

/// <summary>Stores preview content and its presentation kind.</summary>
/// <param name="Path">The filesystem or resource path.</param>
/// <param name="Kind">The operation or event category.</param>
/// <param name="Text">The source text to parse or display.</param>
public sealed record FilePreview(string Path, PreviewKind Kind, string Text);
/// <summary>UI-owned projection. File metadata and contents are loaded separately on the worker.</summary>
public sealed class FileManagerModel {
    /// <summary>Gets the platform-appropriate path comparer.</summary>
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly List<FileEntry> _visible = [];
    private readonly List<string> _history = [];
    private readonly Dictionary<string, string> _remembered = new(PathComparer);
    private int _historyIndex = -1;
    /// <summary>Gets the directory snapshot to apply.</summary>
    public DirectorySnapshot Snapshot { get; private set; } = new("", [], []);
    /// <summary>Gets the active directory entries.</summary>
    public IReadOnlyList<FileEntry> Entries => _visible;
    /// <summary>Gets the identifiers or paths marked for an operation.</summary>
    public HashSet<string> Marked { get; } = new(PathComparer);
    /// <summary>Gets or sets whether hidden directory entries are included.</summary>
    public bool ShowHidden { get; set; }
    /// <summary>Gets or sets the text or expression used to restrict visible rows.</summary>
    public string Filter { get; set; } = "";
    /// <summary>Gets or sets the process sort field.</summary>
    public FileSort Sort { get; set; }
    /// <summary>Gets or sets whether sorting uses descending order.</summary>
    public bool Descending { get; set; }
    /// <summary>Gets the zero-based selected row or point index.</summary>
    public int SelectedIndex { get; private set; }
    /// <summary>Gets the zero-based scroll offset.</summary>
    public int Offset { get; private set; }
    /// <summary>Gets the currently selected row or entry.</summary>
    public FileEntry? Selected => _visible.Count == 0 ? null : _visible[SelectedIndex];
    /// <summary>Gets the directory currently displayed.</summary>
    public string CurrentPath => Snapshot.Path;

    /// <summary>Applies a directory snapshot and optionally records navigation history.</summary>
    /// <param name="snapshot">The directory snapshot to apply.</param>
    /// <param name="addHistory">Whether to append the directory to navigation history. Defaults to
    /// true.</param>
    /// <param name="selectPath">The path to select after rebuilding, or null to preserve selection.
    /// Defaults to null.</param>
    public void Apply(DirectorySnapshot snapshot, bool addHistory = true, string? selectPath = null) {
        if (Selected is { } old) {
            _remembered[CurrentPath] = old.FullPath;
        }

        bool same = PathComparer.Equals(CurrentPath, snapshot.Path);
        Snapshot = snapshot;
        if (!same) {
            Filter = "";
            Offset = 0;
        }

        if (addHistory && (_historyIndex < 0 || !PathComparer.Equals(_history[_historyIndex], snapshot.Path))) {
            if (_historyIndex + 1 < _history.Count) {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }

            _history.Add(snapshot.Path);
            _historyIndex = _history.Count - 1;
        }

        Rebuild(selectPath ?? (_remembered.TryGetValue(snapshot.Path, out string? remembered) ? remembered : null));
    }

    /// <summary>Reapplies filtering and sorting while preserving the requested selection.</summary>
    /// <param name="selectPath">The path to select after rebuilding, or null to preserve selection.
    /// Defaults to null.</param>
    public void Rebuild(string? selectPath = null) {
        selectPath ??= Selected?.FullPath;
        _visible.Clear();
        foreach (var entry in Snapshot.Entries) {
            if ((ShowHidden || !entry.Hidden)
                && (Filter.Length == 0 || entry.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))) {
                _visible.Add(entry);
            }
        }

        _visible.Sort(Compare);
        SelectedIndex = 0;
        if (selectPath is not null) {
            for (int i = 0; i < _visible.Count; i++) {
                if (PathComparer.Equals(_visible[i].FullPath, selectPath)) {
                    SelectedIndex = i;
                    break;
                }
            }
        }

        Offset = Math.Clamp(Offset, 0, Math.Max(0, _visible.Count - 1));
    }

    private int Compare(FileEntry left, FileEntry right) {
        if (left.Name == ".." || right.Name == "..") {
            return left.Name == right.Name ? 0 : left.Name == ".." ? -1 : 1;
        }

        if (left.IsDirectory != right.IsDirectory) {
            return left.IsDirectory ? -1 : 1;
        }

        int result = Sort switch {
            FileSort.Size => left.Length.CompareTo(right.Length),
            FileSort.Modified => left.Modified.CompareTo(right.Modified),
            FileSort.Type => StringComparer.OrdinalIgnoreCase.Compare(Path.GetExtension(left.Name), Path.GetExtension(right.Name)),
            _ => 0
        };
        if (result == 0) {
            result = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        return Descending ? -result : result;
    }

    /// <summary>Moves the selection and keeps it within the visible page.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <param name="pageSize">The number of entries visible in the pane.</param>
    public void Move(
        int delta,
        int pageSize
    ) =>
        Select(
            (int)Math.Clamp((long)SelectedIndex + delta, 0, Math.Max(0, _visible.Count - 1)),
            pageSize
        );
    /// <summary>Selects a visible entry and adjusts the scroll offset.</summary>
    /// <param name="index">The zero-based entry index.</param>
    /// <param name="pageSize">The number of entries visible in the pane.</param>
    public void Select(int index, int pageSize) {
        SelectedIndex = Math.Clamp(index, 0, Math.Max(0, _visible.Count - 1));
        if (SelectedIndex < Offset) {
            Offset = SelectedIndex;
        }

        if (SelectedIndex >= Offset + pageSize) {
            Offset = Math.Max(0, SelectedIndex - pageSize + 1);
        }
    }

    /// <summary>Adjusts the scroll offset to include the selected entry.</summary>
    /// <param name="pageSize">The number of entries visible in the pane.</param>
    public void KeepVisible(int pageSize) =>
        Select(SelectedIndex, pageSize);
    /// <summary>Toggles the mark on the selected entry.</summary>
    public void ToggleMark() {
        if (Selected is { Name: not ".." } entry && !Marked.Remove(entry.FullPath)) {
            Marked.Add(entry.FullPath);
        }
    }

    /// <summary>Toggles marks for the current visible entries.</summary>
    public void ToggleAll() {
        bool all = _visible.Count > 0 && _visible.All(e => e.Name == ".." || Marked.Contains(e.FullPath));
        foreach (var entry in _visible) {
            if (entry.Name == "..") {
                continue;
            }

            if (all) {
                Marked.Remove(entry.FullPath);
            }
            else {
                Marked.Add(entry.FullPath);
            }
        }
    }

    /// <summary>Returns marked paths, or the selected path when nothing is marked.</summary>
    /// <returns>The source paths for the next file operation.</returns>
    public string[] OperationPaths() =>
        Marked.Count > 0 ? [.. Marked] : Selected is { Name: not ".." } entry ? [entry.FullPath] : [];
    /// <summary>Moves through directory navigation history.</summary>
    /// <param name="delta">The signed number of entries to move.</param>
    /// <returns>The history path, or null when the requested step is unavailable.</returns>
    public string? History(int delta) {
        int next = Math.Clamp(_historyIndex + delta, 0, Math.Max(0, _history.Count - 1));
        if (next == _historyIndex || _history.Count == 0) {
            return null;
        }

        _historyIndex = next;
        return _history[next];
    }
}
