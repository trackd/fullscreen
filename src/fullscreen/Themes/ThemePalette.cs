using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Themes;
/// <summary>Precomputed styles, owned by each surface. Theme configuration never runs in the render loop.</summary>
public sealed class ThemePalette {
    /// <summary>Gets the display name.</summary>
    public string Name { get; }
    /// <summary>Gets whether this palette represents the default theme.</summary>
    public bool IsDefault { get; }
    /// <summary>Gets the background color.</summary>
    public Color Background { get; }
    /// <summary>Gets the foreground text color.</summary>
    public Color Foreground { get; }
    /// <summary>Gets the primary accent color.</summary>
    public Color Accent { get; }
    /// <summary>Gets the color used for successful operations.</summary>
    public Color Success { get; }
    /// <summary>Gets the color used for warnings.</summary>
    public Color Warning { get; }
    /// <summary>Gets the captured standard-error text or error description.</summary>
    public Color Error { get; }
    /// <summary>Gets the palette&apos;s purple accent color.</summary>
    public Color Purple { get; }
    /// <summary>Gets the request or review body.</summary>
    public Style Body { get; }
    /// <summary>Gets the standard table-cell style.</summary>
    public Style TableBody { get; }
    /// <summary>Gets the panel background style.</summary>
    public Style Panel { get; }
    /// <summary>Gets the style used for accented text.</summary>
    public Style AccentStyle { get; }
    /// <summary>Gets the currently selected row or entry.</summary>
    public Style Selected { get; }
    /// <summary>Gets the style used for selected table cells.</summary>
    public Style TableSelected { get; }
    /// <summary>Gets the subdued text style.</summary>
    public Style Muted { get; }
    /// <summary>Gets the subdued table-cell style.</summary>
    public Style TableMuted { get; }
    /// <summary>Gets the style used for highlighted controls and errors.</summary>
    public Style Hot { get; }
    /// <summary>Gets the style used for marked rows.</summary>
    public Style Mark { get; }
    /// <summary>Gets the style used for green chart labels.</summary>
    public Style Green { get; }
    /// <summary>Gets the palette&apos;s purple chart style.</summary>
    public Style PurpleStyle { get; }
    /// <summary>Gets the style used for added lines or entries.</summary>
    public Style Added { get; }
    /// <summary>Gets the style used for removed lines or entries.</summary>
    public Style Removed { get; }
    /// <summary>Gets the fill color used beneath the primary chart line.</summary>
    public Style ChartFill { get; }
    /// <summary>Gets the fill color used beneath the green chart line.</summary>
    public Style ChartGreenFill { get; }
    /// <summary>Gets the fill color used beneath the purple chart line.</summary>
    public Style ChartPurpleFill { get; }
    /// <summary>Gets the color used for chart grid lines.</summary>
    public Style ChartGrid { get; }
    /// <summary>Gets the controls indexed by document identifier.</summary>
    public Theme Controls { get; }
    internal ButtonStyle CompactActionButton { get; }

    internal ThemePalette(
        string name,
        Color background,
        Color foreground,
        Color panel,
        Color muted,
        Color accent,
        Color selection,
        Color selectedText,
        Color success,
        Color warning,
        Color error,
        Color purple,
        bool isDefault = false
    ) {
        Name = name;
        IsDefault = isDefault;
        Background = background;
        Foreground = foreground;
        Accent = accent;
        Success = success;
        Warning = warning;
        Error = error;
        Purple = purple;
        Body = Style.None.WithForeground(foreground).WithBackground(background);
        TableBody = isDefault ? Body.WithForeground(Color.Rgb(206, 218, 235)) : Body;
        Panel = Body.WithBackground(panel);
        AccentStyle = Body.WithForeground(accent);
        Selected = Body.WithBackground(selection).WithForeground(selectedText);
        TableSelected = isDefault ? TableBody.WithBackground(selection) : Selected;
        Muted = Body.WithForeground(muted);
        TableMuted = isDefault ? TableBody.WithForeground(Color.Rgb(129, 150, 176)) : Muted;
        Hot = Body.WithForeground(isDefault ? Color.Rgb(250, 190, 107) : warning);
        Mark = TableBody.WithForeground(warning);
        Green = Body.WithForeground(success);
        PurpleStyle = Body.WithForeground(purple);
        Added = TableBody.WithForeground(success);
        Removed = TableBody.WithForeground(error);
        ChartFill = Body.WithForeground(Color.Mix(background, accent, 0.24f));
        ChartGreenFill = Body.WithForeground(Color.Mix(background, success, 0.24f));
        ChartPurpleFill = Body.WithForeground(Color.Mix(background, purple, 0.24f));
        ChartGrid = Body.WithForeground(Color.Mix(background, muted, 0.3f));
        Controls = new Theme {
            Foreground = foreground,
            Background = background,
            Surface = panel,
            SurfaceAlt = background,
            PopupSurface = panel,
            ControlFill = panel,
            ControlFillHover = selection,
            ControlFillPressed = selection,
            InputFill = Color.Mix(panel, accent, .12f),
            InputFillFocused = Color.Mix(panel, accent, .24f),
            Border = muted,
            FocusBorder = accent,
            Accent = accent,
            Selection = selection,
            Disabled = muted,
            Primary = accent,
            Success = success,
            Warning = warning,
            Error = error,
            Muted = muted
        };
        var button = AccentStyle.WithBackground(Color.Mix(background, accent, .18f)) | TextStyle.Bold;
        CompactActionButton = ButtonStyle.Default with {
            Padding = new(1, 0, 1, 0),
            ShowBorder = false,
            Normal = button,
            Hovered = Body.WithBackground(accent).WithForeground(background) | TextStyle.Bold,
            Pressed = Selected | TextStyle.Bold,
            Focused = button | TextStyle.Underline
        };
    }

    /// <summary>Gets the default theme palette.</summary>
    public static ThemePalette Default { get; } = new(
        "Default",
        Color.Rgb(13, 20, 32),
        Color.Rgb(205, 216, 232),
        Color.Rgb(20, 30, 46),
        Color.Rgb(126, 148, 172),
        Color.Rgb(88, 213, 230),
        Color.Rgb(34, 67, 86),
        Color.Rgb(233, 249, 255),
        Color.Rgb(112, 225, 176),
        Color.Rgb(240, 192, 103),
        Color.Rgb(255, 115, 130),
        Color.Rgb(188, 161, 249),
        true
    );

    /// <summary>Applies this palette&apos;s styles to a visual tree.</summary>
    /// <param name="visual">The root visual receiving theme styles.</param>
    public void Apply(Visual visual) {
        foreach (var child in visual.EnumerateVisualsDepthFirst()) {
            if (child is ThemedSurface surface) {
                surface.Palette = this;
            }
            if (child is TextBox or TextArea) {
                child.SetStyle(Controls);
            }
        }

        if (visual is ThemedSurface root) {
            root.Palette = this;
        }

        visual.SetStyle(IsDefault && visual is not (TextBox or TextArea) ? Theme.Default : Controls);
    }
}

/// <summary>Provides palette and minimal-layout support for fullscreen visuals.</summary>
public abstract class ThemedSurface : Visual {
    /// <summary>Gets or sets the palette applied to this surface.</summary>
    public ThemePalette Palette { get; internal set; } = ThemePalette.Default;

    private bool _minimal;
    /// <summary>Gets or sets whether reduced-chrome layout is enabled.</summary>
    public bool Minimal {
        get => _minimal;
        set {
            if (_minimal == value) {
                return;
            }

            _minimal = value;
            OnMinimalChanged();
        }
    }

    /// <summary>Updates derived layout state after minimal mode changes.</summary>
    protected virtual void OnMinimalChanged() {
    }
}
