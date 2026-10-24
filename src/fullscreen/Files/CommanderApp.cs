using System.Collections.Concurrent;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Files;
/// <summary>Runs a two-pane file manager and coordinates file operations.</summary>
public sealed class CommanderApp : IFullscreenApp {
    private readonly FileManagerApp _left, _right;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<Action> _completed = new();
    private Task _io = Task.CompletedTask;
    private bool _exit, _disposed;
    private bool _ioBusy;
    private (string, string, string, string, int) _last;
    /// <summary>Gets the visual surface rendered by the application.</summary>
    public CommanderSurface Surface { get; }
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root => Surface;
    /// <summary>Gets the directory currently displayed.</summary>
    public string CurrentPath => Surface.Active.Model.CurrentPath;
    /// <summary>Gets the filesystem paths accepted by the user.</summary>
    public string[] ChosenPaths => Surface.Active.Model.OperationPaths();
    /// <summary>Gets whether a background operation is in progress.</summary>
    public bool Busy => _ioBusy || Surface.Active.Busy || Surface.Other.Busy;

    /// <summary>Initializes a new CommanderApp instance.</summary>
    /// <param name="leftPath">The initial directory of the left pane.</param>
    /// <param name="rightPath">The initial directory of the right pane.</param>
    /// <param name="hidden">Whether the filesystem entry is hidden. Defaults to false.</param>
    public CommanderApp(string leftPath, string rightPath, bool hidden = false) {
        _left = new(leftPath, hidden, FileSort.Name, false, false, false, true);
        _right = new(rightPath, hidden, FileSort.Name, false, false, false, true);
        Surface = new(_left.Surface, _right.Surface);
        Surface.FunctionRequested += Execute;
    }

    private void Execute(int key) {
        if (key == 10) {
            _exit = true;
            return;
        }

        if (_ioBusy || Surface.Active.Busy || (key is 5 or 6 or 11 && Surface.Other.Busy)) {
            return;
        }

        switch (key) {
            case 1:
                Help();
                break;
            case 2:
                Surface.Active.RenameSelected();
                break;
            case 3:
                OpenFile();
                break;
            case 5:
                Surface.Active.TransferTo(Surface.Other.Model.CurrentPath, false);
                break;
            case 6:
                Surface.Active.TransferTo(Surface.Other.Model.CurrentPath, true);
                break;
            case 7:
                Surface.Active.CreateFolder();
                break;
            case 8:
                Surface.Active.TrashSelected();
                break;
            case 9:
                Menu();
                break;
            case 11:
                DeleteSelected();
                break;
        }
    }

    private void Menu() =>
        ContextMenuService.Show(Surface, [new MenuItem("View (F3)", () => Execute(3)), new MenuItem("Copy to other pane (F5)", () => Execute(5)), new MenuItem("Move to other pane (F6)", () => Execute(6)), new MenuItem("Create directory (F7)", () => Execute(7)), new MenuItem("Move to local trash (F8)", () => Execute(8)), new MenuItem("Delete permanently… (Delete)", () => Execute(11)), MenuItem.Separator(), new MenuItem("Refresh both panes", () =>
    {
        _left.Surface.RefreshDirectory();
        _right.Surface.RefreshDirectory();
    }), new MenuItem("Open local trash", () => OpenTrash()), new MenuItem("Help", Help), new MenuItem("Quit", () => Execute(10))], Surface.Bounds.X + 2, Surface.Bounds.Y + 1);
    private void DeleteSelected() {
        string[] paths = Surface.Active.Model.OperationPaths();
        if (paths.Length == 0) {
            return;
        }

        string directory = Surface.Active.Model.CurrentPath;
        Dialog? dialog = null;
        var cancel = new Button("Cancel").Click(() => {
            dialog!.Close();
            Surface.App?.Focus(Surface.Active);
        });
        dialog = new Dialog {
            Title = new TextBlock("Delete permanently?"),
            IsModal = true,
            Width = Math.Max(24, Math.Min(88, Surface.Bounds.Width - 4)),
            Content = new VStack(new TextBlock("Permanently delete " + paths.Length +
                " selected item(s), including directory contents?\n" + FileManagerSurface.List(paths) +
                "\n\nThis cannot be undone. F8 moves to recoverable local trash instead."), new HStack(cancel, new Button("Delete permanently").Click(() => {
                    dialog!.Close();
                    if (_ioBusy || Surface.Active.Busy || Surface.Other.Busy) {
                        return;
                    }

                    RunIo(async () => {
                        var (failed, error) = await FileOperations.EachAsync(paths, path => {
                            FileOperations.Delete(path, directory, _stop.Token);
                            return Task.CompletedTask;
                        }, _stop.Token).ConfigureAwait(false);
                        return () => {
                            _left.Surface.Model.Marked.IntersectWith(failed);
                            _right.Surface.Model.Marked.IntersectWith(failed);
                            _left.Reload(Surface.Active == _left.Surface ? error : null);
                            _right.Reload(Surface.Active == _right.Surface ? error : null);
                        };
                    });
                    Surface.App?.Focus(Surface.Active);
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

    private void OpenTrash() {
        string path = Path.Combine(CurrentPath, ".fullscreen-trash");
        if (Directory.Exists(path)) {
            Surface.Active.ShowDirectory(path);
        }
        else {
            Surface.Active.Status = "No .fullscreen-trash in this directory.";
        }
    }

    private void Help() =>
        ShowViewer(
            "Fullscreen Commander",
            new TextBlock("Tab: switch panes · Enter: open directory · arrows/hjkl: navigate\nSpace/Insert: mark · /: filter · g: directory · .: hidden files\nF2: rename · F3: view · F4: minimal · F5: copy to other pane\nF6: move to other pane · F7: mkdir · F8: recoverable local trash\nF9: actions menu · F10/Q: quit · Ctrl+R: refresh\n\nCopies/moves never overwrite destinations. F8 moves items into\n.fullscreen-trash/UUID/original-name beside the source. Recover by\nopening that folder and moving the item back with F6.\nNo shell commands are executed from the UI.")
        );
    private void RunIo(Func<Task<Action>> work) {
        if (_ioBusy) {
            return;
        }

        _ioBusy = true;
        _io = Task.Run(async () => {
            try {
                var apply = await work().ConfigureAwait(false);
                _completed.Enqueue(() => {
                    _ioBusy = false;
                    apply();
                });
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {
            }
            catch (Exception error) {
                _completed.Enqueue(() => {
                    _ioBusy = false;
                    Surface.Active.Status = "ERROR · " + error.Message;
                    Surface.Active.HasError = true;
                    Surface.Active.Refresh();
                });
            }
        });
    }

    private void OpenFile() {
        if (Surface.Active.Model.Selected is not { IsDirectory: false } entry) {
            return;
        }

        RunIo(async () => {
            var preview = await FileReader.ReadPreviewAsync(entry, Surface.Active.Model.ShowHidden, _stop.Token).ConfigureAwait(false);
            return () => ShowViewer(
                entry.Name,
                preview.Kind is PreviewKind.Markdown or PreviewKind.Text ? SyntaxPreview.File(preview.Text, entry.FullPath) : new TextBlock(preview.Text)
            );
        });
    }

    private void ShowViewer(string title, Visual content) {
        Dialog? dialog = null;
        var scroll = new ScrollViewer(content) {
            HorizontalScrollEnabled = false,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        dialog = new Dialog {
            Title = new TextBlock(FileReader.Sanitize(title)),
            IsModal = true,
            Width = Math.Max(30, Surface.Bounds.Width - 6),
            Height = Math.Max(8, Surface.Bounds.Height - 4),
            Content = new VStack(scroll, new Button("Close · Esc").Click(() => {
                dialog!.Close();
                Surface.App?.Focus(Surface.Active);
            }))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                Surface.App?.Focus(Surface.Active);
                e.Handled = true;
            }
        });
        dialog.Show();
        Surface.App?.Focus(scroll);
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        bool running = _left.Tick() & _right.Tick();
        while (_completed.TryDequeue(out var apply)) {
            apply();
        }

        var state = (_left.CurrentPath, _right.CurrentPath, _left.Surface.Status, _right.Surface.Status, _left.Surface.HasFocus ? 0 : _right.Surface.HasFocus ? 1 : Surface.ActivePane);
        if (state != _last) {
            _last = state;
            Surface.Refresh();
        }

        return running && !_exit;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        bool abandoned = FileManagerApp.Abandon(_io);
        try {
            if (!abandoned) {
                _io.GetAwaiter().GetResult();
            }

            _left.Dispose();
        }
        finally {
            _right.Dispose();
            if (!abandoned) {
                _stop.Dispose();
            }
        }
    }
}
