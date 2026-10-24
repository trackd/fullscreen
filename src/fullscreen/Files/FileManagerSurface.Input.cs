using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Files;

public sealed partial class FileManagerSurface {
    /// <summary>Raised when commander key is requested.</summary>
    public event Action<KeyEventArgs>? CommanderKey;
    private void Navigate(
        string path,
        bool history = true,
        string? selected = null
    ) =>
        NavigateRequested?.Invoke(path, history, selected);
    private void GoParent() {
        if (Model.CurrentPath.Length == 0) {
            return;
        }

        string? parent = Directory.GetParent(Model.CurrentPath)?.FullName;
        if (parent is not null) {
            Navigate(parent, true, Model.CurrentPath);
        }
    }

    private void Open() {
        if (Model.Selected is not { } entry) {
            return;
        }

        if (entry.IsDirectory) {
            Navigate(entry.FullPath);
        }
        else {
            FocusPreview();
        }
    }

    private void FocusPreview() {
        if (!PreviewVisible) {
            return;
        }

        if (_previewVisual is { Focusable: true }) {
            App?.Focus(_previewVisual);
        }
        else if (ParentWidth > 3) {
            SetParentPaneCollapsed(true);
        }
    }

    private void Select(int delta) {
        Model.Move(delta, ListHeight);
        SelectionChanged();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (!HasFocus) {
            if (e.Key is TerminalKey.Escape or TerminalKey.Tab) {
                App?.Focus(this);
                e.Handled = true;
            }

            return;
        }

        if (CommanderPane) {
            CommanderKey?.Invoke(e);
            if (e.Handled) {
                return;
            }
        }

        if (CommanderPane && e.Key == TerminalKey.Insert) {
            Model.ToggleMark();
            Select(1);
            e.Handled = true;
            return;
        }

        if (CommanderPane && e.Char is 'r' or 'R' or TerminalChar.CtrlR
            && (e.Modifiers & TerminalModifiers.Ctrl) != 0) {
            RefreshDirectory();
            e.Handled = true;
            return;
        }

        if (_filtering) {
            if (e.Key is TerminalKey.Enter or TerminalKey.Escape) {
                _filtering = false;
            }
            else if (e.Key == TerminalKey.Backspace && Model.Filter.Length > 0) {
                int last = StringInfo.ParseCombiningCharacters(Model.Filter)[^1];
                Model.Filter = Model.Filter[..last];
                Model.Rebuild();
                SelectionChanged();
            }

            e.Handled = true;
            Refresh();
            return;
        }

        if ((e.Modifiers & TerminalModifiers.Shift) != 0 && e.Key is TerminalKey.Up or TerminalKey.Down) {
            _previewOffset = Math.Clamp(
                _previewOffset + (e.Key == TerminalKey.Up ? -3 : 3),
                0,
                Math.Max(0, _previewLines.Length - ListHeight)
            );
            Refresh();
            e.Handled = true;
            return;
        }

        switch (e.Key) {
            case TerminalKey.Up:
                Select(-1);
                break;
            case TerminalKey.Down:
                Select(1);
                break;
            case TerminalKey.PageUp:
                Select(-ListHeight);
                break;
            case TerminalKey.PageDown:
                Select(ListHeight);
                break;
            case TerminalKey.Home:
                Select(-int.MaxValue);
                break;
            case TerminalKey.End:
                Select(int.MaxValue);
                break;
            case TerminalKey.Left when !CommanderPane && ParentPaneCollapsed && ParentWidth > 0:
                SetParentPaneCollapsed(false);
                break;
            case TerminalKey.Left:
            case TerminalKey.Backspace:
                GoParent();
                break;
            case TerminalKey.Right:
            case TerminalKey.Enter:
                Open();
                break;
            case TerminalKey.Tab:
                FocusPreview();
                break;
            case TerminalKey.F2:
                Rename();
                break;
            case TerminalKey.F5:
                Navigate(Model.CurrentPath, false);
                break;
            case TerminalKey.Escape:
                Model.Filter = "";
                Model.Marked.Clear();
                Model.Rebuild();
                SelectionChanged();
                break;
            default:
                return;
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e) {
        if (!HasFocus) {
            return;
        }

        if (_filtering) {
            AppendFilter(e.Text);
            e.Handled = true;
            return;
        }

        if (CommanderPane && e.Text is "t" or "w" or "{" or "}") {
            return;
        }

        switch (e.Text) {
            case "t":
                NewTab();
                break;
            case "w":
                CloseTab();
                break;
            case "{":
                SwitchTab((ActiveTab + TabCount - 1) % TabCount);
                break;
            case "}":
                SwitchTab((ActiveTab + 1) % TabCount);
                break;
            case "b":
                ShowBookmarks();
                break;
            case "B":
                ToggleBookmark();
                break;
            case "q":
            case "Q":
                ExitRequested = true;
                break;
            case "j":
                Select(1);
                break;
            case "k":
                Select(-1);
                break;
            case "h":
                GoParent();
                break;
            case "l":
                Open();
                break;
            case "/":
                _filtering = true;
                break;
            case ".":
                Model.ShowHidden = !Model.ShowHidden;
                Model.Rebuild();
                SelectionChanged();
                break;
            case "s":
                Model.Sort = (FileSort)(((int)Model.Sort + 1) % 4);
                Model.Rebuild();
                SelectionChanged();
                break;
            case "r":
                Model.Descending = !Model.Descending;
                Model.Rebuild();
                SelectionChanged();
                break;
            case " ":
                Model.ToggleMark();
                Select(1);
                break;
            case "a":
                Model.ToggleAll();
                break;
            case "y":
                Buffer(false);
                break;
            case "x":
                Buffer(true);
                break;
            case "p":
                Paste();
                break;
            case "n":
                NewDirectory();
                break;
            case "g":
                Prompt(
                    "Go to directory",
                    Model.CurrentPath,
                    value => Navigate(Path.GetFullPath(value, Model.CurrentPath.Length > 0 ? Model.CurrentPath : Environment.CurrentDirectory))
                );
                break;
            case "~":
                Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                break;
            case "[":
            case "]":
                string? path = Model.History(e.Text == "[" ? -1 : 1);
                if (path is not null) {
                    Navigate(path, false);
                }

                break;
            case "L":
                PreviewAs();
                break;
            case "?":
                Help();
                break;
            default:
                return;
        }

        e.Handled = true;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnPaste(PasteEventArgs e) {
        if (_filtering) {
            AppendFilter(e.Text);
            e.Handled = true;
        }
    }

    private void AppendFilter(string text) {
        foreach (char c in text) {
            if (!char.IsControl(c) && Model.Filter.Length < 256) {
                Model.Filter += c;
            }
        }

        Model.Rebuild();
        SelectionChanged();
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (!CommanderPane && !Minimal && ParentWidth > 0 && e.Button == TerminalMouseButton.Left
            && e.LocalX <= ParentWidth
            && e.LocalY == 3) {
            SetParentPaneCollapsed(!ParentPaneCollapsed);
            App?.Focus(this);
            e.Handled = true;
            return;
        }

        if (!CommanderPane && !Minimal && e.LocalY == 0 && e.Button == TerminalMouseButton.Left) {
            int width = Math.Min(20, Math.Max(4, (Bounds.Width - 18) / TabCount));
            int visible = Math.Max(1, (Bounds.Width - 18) / width);
            int first = Math.Max(0, ActiveTab - visible + 1);
            if (e.LocalX >= 1 && e.LocalX < 1 + width * Math.Min(TabCount, visible)) {
                SwitchTab(first + (e.LocalX - 1) / width);
            }

            e.Handled = true;
            return;
        }

        if (e.LocalX >= CurrentX && e.LocalX < PreviewX && e.LocalY >= EntryTop && e.LocalY < EntryTop + ListHeight) {
            Model.Select(Model.Offset + e.LocalY - EntryTop, ListHeight);
            App?.Focus(this);
            SelectionChanged();
            if (e.Button == TerminalMouseButton.Right) {
                ShowMenu(e);
            }
            else if (e.Button == TerminalMouseButton.Left && e.ClickCount >= 2) {
                Open();
            }

            e.Handled = true;
        }
        else if (e.Button == TerminalMouseButton.Left && !ParentPaneCollapsed && ParentWidth > 0
            && e.LocalX <= ParentWidth
            && e.LocalY >= 4
            && e.LocalY < 4 + ListHeight
            && ParentEntryAt(e.LocalY - 4) is { } parentEntry) {
            if (parentEntry.IsDirectory) {
                Navigate(parentEntry.FullPath);
            }
            else {
                Navigate(Path.GetDirectoryName(parentEntry.FullPath)!, true, parentEntry.FullPath);
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerWheel(PointerEventArgs e) {
        if (e.RoutingPhase == RoutingPhase.Preview) {
            return;
        }

        if (e.LocalX >= PreviewX) {
            _previewOffset = Math.Clamp(
                _previewOffset + (e.WheelDelta > 0 ? -3 : 3),
                0,
                Math.Max(0, _previewLines.Length - ListHeight)
            );
        }
        else {
            Select(e.WheelDelta > 0 ? -3 : 3);
        }

        e.Handled = true;
        Refresh();
    }

    private void Buffer(bool cut) {
        _clipboard = Model.OperationPaths();
        _cut = cut;
        Status = _clipboard.Length == 0 ? "Nothing selected" : $"{_clipboard.Length} item(s) ready to {(cut ? "move" : "copy")} · navigate then P";
    }

    private void Paste() {
        if (_clipboard.Length == 0 || Busy) {
            return;
        }

        var operation = new FileOperation(
            _cut ? FileOperationKind.Move : FileOperationKind.Copy,
            (string[])_clipboard.Clone(),
            Model.CurrentPath,
            Model.CurrentPath
        );
        Confirm(
            operation.Description +
            "\n" + List(_clipboard) +
            "\n\nTo: " + Model.CurrentPath +
            "\nExisting destinations will not be overwritten.",
            () => OperationRequested?.Invoke(operation)
        );
    }

    private void Rename() {
        if (Model.Selected is not { Name: not ".." } entry || Busy) {
            return;
        }

        Prompt("Rename " + FileReader.Sanitize(entry.Name), entry.Name, name => {
            _ = FileOperations.ChildPath(Model.CurrentPath, name);
            OperationRequested?.Invoke(new(FileOperationKind.Rename, [entry.FullPath], Model.CurrentPath, name));
        });
    }

    private void NewDirectory() {
        if (!Busy) {
            Prompt("Create directory", "", name => {
                _ = FileOperations.ChildPath(Model.CurrentPath, name);
                OperationRequested?.Invoke(new(FileOperationKind.CreateDirectory, [], Model.CurrentPath, name));
            });
        }
    }

    private void PreviewAs() {
        if (Preview is { Kind: PreviewKind.Text or PreviewKind.Markdown } preview) {
            Prompt(
                "Preview as language (TextMate id or extension, empty: automatic)",
                "",
                value => SetPreview(preview, string.IsNullOrWhiteSpace(value) ? null : value.Trim())
            );
        }
    }

    private void Prompt(string title, string initial, Action<string> accepted) {
        Dialog? dialog = null;
        var input = new TextBox {
            Text = initial
        };
        var errorText = new TextBlock("");
        void Apply() {
            try {
                accepted(input.Text ?? "");
                dialog!.Close();
                App?.Focus(this);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) {
                errorText.Text = error.Message;
            }
        }

        dialog = new Dialog {
            Title = new TextBlock(title),
            IsModal = true,
            Width = Math.Max(24, Math.Min(72, Bounds.Width - 4)),
            Content = new VStack(
                input,
                errorText,
                new HStack(new Button("Apply").Click(Apply), new Button("Cancel").Click(() => dialog!.Close()))
            )
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
            else if (e.Key == TerminalKey.Enter) {
                Apply();
                e.Handled = true;
            }
        });
        dialog.Show();
        App?.Focus(input);
    }

    private void Confirm(string text, Action accepted) {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Confirm file operation"),
            IsModal = true,
            Width = Math.Max(24, Math.Min(72, Bounds.Width - 4)),
            Content = new VStack(new TextBlock(FileReader.Sanitize(text, true)), new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Continue").Click(() => {
                dialog!.Close();
                accepted();
                App?.Focus(this);
            })))
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }

    private void ShowMenu(PointerEventArgs e) {
        ContextMenuService.Show(
            this,
            [
                new MenuItem("Enter / preview", Open),
                new MenuItem("Preview as language… (L)", PreviewAs) { IsEnabled = Preview is { Kind: PreviewKind.Text or PreviewKind.Markdown } },
                MenuItem.Separator(),
                new MenuItem("Copy", () => Buffer(false)),
                new MenuItem("Move", () => Buffer(true)),
                new MenuItem("Paste…", Paste) { IsEnabled = _clipboard.Length > 0 },
                new MenuItem("Rename…", Rename),
                new MenuItem("New directory…", NewDirectory),
                MenuItem.Separator(),
                new MenuItem("New tab", NewTab),
                new MenuItem("Toggle bookmark", ToggleBookmark),
                new MenuItem("Bookmarks", ShowBookmarks),
                new MenuItem("Refresh", () => Navigate(Model.CurrentPath, false))
            ],
            e.UiX,
            e.UiY
        );
    }

    private void Help() {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Fullscreen Files"),
            IsModal = true,
            Width = Math.Max(24, Math.Min(76, Bounds.Width - 4)),
            Content = new VStack(
                new TextBlock("hjkl / arrows: browse · Enter: directory / preview\n[ / ]: history · ~: home · g: path · F5: refresh\nt: new tab · {/}: switch · w: close tab\nb: bookmarks · B: toggle current bookmark (session)\n/: filter · .: hidden · s: sort · r: reverse\nSpace: mark · a: mark visible · Escape: clear marks/filter\ny: copy buffer · x: move buffer · p: paste confirmation\nF2: rename · n: create directory · right-click: menu\nTab: focus rich preview · Escape: return to files\nRight again in preview: slim parent pane\nLeft: restore parent pane / return to files\nClick PARENT / rail arrow: toggle parent pane\nShift+Up/Down or preview wheel: scroll text\nL: preview as a TextMate language\nQ: close · copies never overwrite existing items"),
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }
}
