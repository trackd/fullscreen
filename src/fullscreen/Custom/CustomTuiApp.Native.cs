using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal;

namespace fullscreen.Custom;

public sealed partial class CustomTuiApp {
    private readonly ToastHost _toastHost;
    private readonly Dictionary<string, bool> _openOverlays = new(StringComparer.Ordinal);
    private Visual? _commandRoot;
    private Command? _globalExit;
    private static bool OverlayOpen(Visual visual) =>
        visual is SearchReplacePopup search ? search.IsOpen : visual.App is not null;
    private void SyncNativeOverlays() {
        if (_preview || Root.App is null) {
            return;
        }

        if (_globalExit is null) {
            _commandRoot = Root.App.Root;
            _globalExit = new Command {
                Id = "Fullscreen.Tui.ExitAll",
                LabelMarkup = "Exit app",
                Gesture = new KeyGesture(TerminalKey.F10),
                Execute = _ => _closed = true
            };
            _commandRoot.AddCommand(_globalExit);
        }

        foreach (var (node, control) in _native.Where(entry => NativeTuiControls.IsOverlay(entry.Control))) {
            bool wanted = node.Bind.Length > 0 && State.ContainsKey(node.Bind) && Flag(State[node.Bind]);
            bool opened = _openOverlays.GetValueOrDefault(node.Id);
            if (opened && !OverlayOpen(control)) {
                _openOverlays[node.Id] = false;
                if (node.Bind.Length > 0) {
                    State[node.Bind] = false;
                }

                Enqueue(node.Id, "Close", null);
                continue;
            }

            if (wanted == opened) {
                continue;
            }

            if (wanted) {
                switch (control) {
                    case CommandPalette palette:
                        palette.Show();
                        break;
                    case Dialog dialog:
                        dialog.Show();
                        if (!dialog.HasFocusWithin
                            && dialog.EnumerateVisualsDepthFirst().FirstOrDefault(child => child.Focusable) is { } focus) {
                            Root.App.Focus(focus);
                        }

                        break;
                    case Popup popup:
                        popup.Anchor = Controls.GetValueOrDefault(node.Target) ?? Root.App.FocusedElement;
                        popup.Show();
                        if (!popup.HasFocusWithin
                            && popup.EnumerateVisualsDepthFirst().FirstOrDefault(child => child.Focusable) is { } popupFocus) {
                            Root.App.Focus(popupFocus);
                        }

                        break;
                    case Toast toast:
                        (Controls.GetValueOrDefault(node.Target) as ToastHost ?? _toastHost).Show(() => toast);
                        break;
                    case SearchReplacePopup search:
                        search.ArrangeWithin(Root.Bounds);
                        search.OpenFind();
                        break;
                }

                Enqueue(node.Id, "Open", null);
            }
            else {
                CloseOverlay(control);
                Enqueue(node.Id, "Close", null);
            }

            _openOverlays[node.Id] = wanted;
        }
    }

    private static void CloseOverlay(Visual control) {
        switch (control) {
            case CommandPalette palette:
                palette.Close();
                break;
            case Dialog dialog:
                dialog.Close();
                break;
            case Popup popup:
                popup.Close();
                break;
            case Toast toast:
                toast.Dismiss();
                break;
            case SearchReplacePopup search:
                search.Close();
                break;
        }
    }

    private void CloseNativeOverlays() {
        if (_preview) {
            return;
        }

        if (_globalExit is not null) {
            _commandRoot?.Commands.Remove(_globalExit);
            _globalExit = null;
            _commandRoot = null;
        }

        foreach (var (_, control) in _native) {
            if (NativeTuiControls.IsOverlay(control) && OverlayOpen(control)) {
                CloseOverlay(control);
            }
        }
    }
}
