namespace fullscreen.Custom;

public sealed partial class TuiDocument {
    private void ValidateCommands(Dictionary<string, TuiNode> nodes, bool validateTargets) {
        if (Commands is null || Commands.Length > 256) {
            throw new ArgumentException("Commands allows at most 256 shared actions.");
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in Commands) {
            if (command is null || command.Id is null || !ControlIdPattern().IsMatch(command.Id)
                || !ids.Add(command.Id) || nodes.ContainsKey(command.Id)) {
                throw new ArgumentException("Command IDs must be unique names distinct from control IDs.");
            }
            if (string.IsNullOrWhiteSpace(command.Label) || command.Label.Length > 128 || command.Gesture is null
                || command.Scope is null || command.EnabledBind is null || command.EnabledBind.Length > 128
                || command.VisibleBind is null || command.VisibleBind.Length > 128) {
                throw new ArgumentException($"Command '{command.Id}': use a label up to 128 characters and state keys up to 128 characters.");
            }
            if (command.Key is { } key) {
                if (key.Modifiers == XenoAtom.Terminal.TerminalModifiers.None && key.Key is XenoAtom.Terminal.TerminalKey.F4
                    or XenoAtom.Terminal.TerminalKey.F10 or XenoAtom.Terminal.TerminalKey.F12 or XenoAtom.Terminal.TerminalKey.Escape) {
                    throw new ArgumentException($"Command '{command.Id}': Gesture is reserved by the host.");
                }
                string scope = command.Scope.Length == 0 ? Root.Id : command.Scope;
                if (!gestures.Add(scope + ":" + key)) {
                    throw new ArgumentException($"Command '{command.Id}': duplicate Gesture in the same Scope.");
                }
            }
            if (command.Scope.Length > 0 && (!ControlIdPattern().IsMatch(command.Scope)
                || validateTargets && !nodes.ContainsKey(command.Scope))) {
                throw new ArgumentException($"Command '{command.Id}': Scope must identify a control.");
            }
        }
        foreach (var node in nodes.Values) {
            if (node.CommandIds is null || node.CommandIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.CommandIds.Length
                || node.CommandIds.Any(id => id is null || validateTargets && !ids.Contains(id))) {
                throw new ArgumentException($"Control '{node.Id}': CommandIds must reference unique document commands.");
            }
        }
    }
}
