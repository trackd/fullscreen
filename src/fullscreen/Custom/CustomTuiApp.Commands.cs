using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Custom;

public sealed partial class CustomTuiApp {
    private readonly Dictionary<string, Command> _sharedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _commandAvailability = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _commandFooter;
    private Visual? _actionFocus;

    private void InitializeCommands(TextBlock footer) {
        _commandFooter = footer;
        foreach (var definition in _document.Commands) {
            var command = new Command {
                Id = definition.Id,
                LabelMarkup = definition.Label,
                Gesture = definition.Key,
                Presentation = CommandPresentation.CommandBar | CommandPresentation.CommandPalette,
                CanExecute = _ => CommandAvailable(definition),
                IsVisible = _ => CommandAvailable(definition),
                Execute = _ => {
                    if (!_preview && CommandAvailable(definition)) {
                        Enqueue(definition.Id, "Command", null);
                    }
                }
            };
            _sharedCommands.Add(definition.Id, command);
            Controls[definition.Scope.Length == 0 ? _document.Root.Id : definition.Scope].AddCommand(command);
        }
        foreach (var (node, control) in _native) {
            if (control is not MenuBar menu) {
                continue;
            }
            foreach (string id in node.CommandIds) {
                menu.Items.Add(new MenuItem(_sharedCommands[id].LabelMarkup, _sharedCommands[id]));
            }
        }
    }

    private bool CommandAvailable(TuiCommand command) {
        if (_preview || command.EnabledBind.Length > 0 && !Flag(State[command.EnabledBind])
            || command.VisibleBind.Length > 0 && !Flag(State[command.VisibleBind])) {
            return false;
        }
        var scope = Controls[command.Scope.Length == 0 ? _document.Root.Id : command.Scope];
        for (var current = ActionFocus(); current is not null; current = current.Parent) {
            if (ReferenceEquals(current, scope)) {
                return scope.IsVisible && scope.IsEnabled;
            }
        }
        return false;
    }

    private Visual? ActionFocus() {
        var focus = Root.App?.FocusedElement;
        bool commandUi = false;
        bool palette = false;
        Dialog? dialog = null;
        for (var current = focus; current is not null; current = current.Parent) {
            commandUi |= current is CommandPalette or MenuBar or CommandBar || current.GetType().DeclaringType == typeof(MenuBar);
            palette |= current is CommandPalette;
            if (current is Dialog window) {
                dialog ??= window;
            }
        }
        if (!commandUi) {
            return focus;
        }
        if (!palette && dialog is not null) {
            for (var current = _actionFocus; current is not null; current = current.Parent) {
                if (ReferenceEquals(current, dialog)) {
                    return _actionFocus;
                }
            }
            return dialog;
        }
        return _actionFocus?.App == Root.App ? _actionFocus : Controls[_document.Root.Id];
    }

    private void UpdateCommandHints() {
        if (_preview || _sharedCommands.Count == 0 || Root.App is null) {
            return;
        }
        _actionFocus = ActionFocus();
        bool changed = false;
        foreach (var definition in _document.Commands) {
            bool available = CommandAvailable(definition);
            if (_commandAvailability.TryGetValue(definition.Id, out bool old) && old == available) {
                continue;
            }
            _commandAvailability[definition.Id] = available;
            changed = true;
            // Hashtable state is not tracked by XenoAtom. Bump the owner's public
            // command collection version so open palettes/bars refresh predicates.
            var owner = Controls[definition.Scope.Length == 0 ? _document.Root.Id : definition.Scope];
            var command = _sharedCommands[definition.Id];
            int index = owner.Commands.IndexOf(command);
            owner.Commands.RemoveAt(index);
            owner.Commands.Insert(index, command);
        }
        if (!changed) {
            return;
        }
        string[] hints = [.. _document.Commands.Where(command => command.Gesture.Length > 0 && _commandAvailability[command.Id])
            .Take(6).Select(command => command.Gesture + " " + command.Label)];
        _commandFooter!.Text = string.Join(" · ", hints.Append("F10 exit · F12 diagnostics"));
    }
}
