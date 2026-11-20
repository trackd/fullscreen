using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Custom;

/// <summary>A shared action used by shortcuts, menus, command bars and palettes.</summary>
public sealed class TuiCommand {
    /// <summary>Stable event ID; invocation emits Kind=Command.</summary>
    public string Id { get; set; } = "";
    /// <summary>Displayed action label.</summary>
    public string Label { get; set; } = "";
    /// <summary>Optional shortcut, such as Ctrl+S.</summary>
    public string Gesture { get; set; } = "";
    /// <summary>Control whose descendants provide this action's context; blank uses the root.</summary>
    public string Scope { get; set; } = "";
    /// <summary>Optional boolean state key enabling the action.</summary>
    public string EnabledBind { get; set; } = "";
    /// <summary>Optional boolean state key showing the action.</summary>
    public string VisibleBind { get; set; } = "";

    internal KeyGesture? Key => Gesture.Length == 0 ? null : KeyGesture.TryParse(Gesture, out var key)
        ? key : throw new ArgumentException($"Command '{Id}': invalid Gesture '{Gesture}'.");
}
