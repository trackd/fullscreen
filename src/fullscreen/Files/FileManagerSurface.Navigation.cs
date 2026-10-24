using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Files;

public sealed partial class FileManagerSurface {
    private readonly List<FileManagerModel> _tabs = [];
    private readonly HashSet<string> _bookmarks = new(FileManagerModel.PathComparer);
    /// <summary>Gets the index of the active directory tab.</summary>
    public int ActiveTab { get; private set; }
    /// <summary>Gets the number of open directory tabs.</summary>
    public int TabCount => Math.Max(1, _tabs.Count);
    /// <summary>Gets the bookmarked directory paths.</summary>
    public IReadOnlyCollection<string> Bookmarks => _bookmarks;

    /// <summary>Opens another directory tab.</summary>
    public void NewTab() {
        if (Busy || TabCount >= 12 || Model.CurrentPath.Length == 0) {
            return;
        }

        if (_tabs.Count == 0) {
            _tabs.Add(Model);
        }

        var next = new FileManagerModel {
            ShowHidden = Model.ShowHidden,
            Sort = Model.Sort,
            Descending = Model.Descending
        };
        next.Apply(Model.Snapshot, selectPath: Model.Selected?.FullPath);
        _tabs.Add(next);
        SwitchTab(_tabs.Count - 1);
    }

    /// <summary>Activates a directory tab by index.</summary>
    /// <param name="index">The zero-based entry index.</param>
    public void SwitchTab(int index) {
        if (Busy || index < 0 || index >= _tabs.Count || index == ActiveTab) {
            return;
        }

        Model = _tabs[index];
        ActiveTab = index;
        SetPreview(null);
        Navigate(Model.CurrentPath, false, Model.Selected?.FullPath);
        Refresh();
    }

    /// <summary>Closes the active directory tab when another tab can remain active.</summary>
    public void CloseTab() {
        if (Busy || _tabs.Count <= 1) {
            return;
        }

        _tabs.RemoveAt(ActiveTab);
        ActiveTab = Math.Min(ActiveTab, _tabs.Count - 1);
        Model = _tabs[ActiveTab];
        SetPreview(null);
        Navigate(Model.CurrentPath, false, Model.Selected?.FullPath);
        Refresh();
    }

    /// <summary>Adds or removes the current directory from bookmarks.</summary>
    public void ToggleBookmark() {
        if (Model.CurrentPath.Length == 0) {
            return;
        }

        if (!_bookmarks.Remove(Model.CurrentPath)) {
            _bookmarks.Add(Model.CurrentPath);
        }

        Status = _bookmarks.Contains(Model.CurrentPath) ? "Bookmarked for this session · b to open" : "Bookmark removed";
        Refresh();
    }

    private void ShowBookmarks() {
        var items = _bookmarks.Order(FileManagerModel.PathComparer).Select(path => new MenuItem(FileReader.Sanitize(path), () => {
            if (!Busy) {
                Navigate(path);
            }
        })).ToList();
        if (items.Count == 0) {
            items.Add(new MenuItem("No bookmarks · B pins the current directory", () => {
            }));
        }

        ContextMenuService.Show(this, [.. items], Bounds.X + 2, Bounds.Y + 2);
    }

    private void RenderTabs(CellBuffer buffer) {
        int width = Math.Min(20, Math.Max(4, (Bounds.Width - 18) / TabCount));
        int visible = Math.Max(1, (Bounds.Width - 18) / width);
        int first = Math.Max(0, ActiveTab - visible + 1);
        for (int i = first; i < Math.Min(TabCount, first + visible); i++) {
            string path = _tabs.Count == 0 ? Model.CurrentPath : _tabs[i].CurrentPath;
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            Write(
                buffer,
                1 + (i - first) * width,
                0,
                $" {i + 1} {(name.Length == 0 ? path : name)} ",
                i == ActiveTab ? SelectedStyle : Muted,
                width - 1
            );
        }
    }
}
