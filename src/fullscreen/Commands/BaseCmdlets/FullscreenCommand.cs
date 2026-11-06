using System.Management.Automation;
using System.Collections;
using fullscreen.Themes;

namespace fullscreen.Commands;
/// <summary>Provides theme resolution and shared parameters for fullscreen cmdlets.</summary>
public abstract class FullscreenCommand : PSCmdlet {
    /// <summary>Gets or sets the palette used by the application.</summary>
    [Parameter]
    [ThemeInputTransformation]
    [ValidateNotNull]
    [ValidateTheme]
    [ArgumentCompleter(typeof(ThemeArgumentCompleter))]
    public object Theme { get; set; } = "Default";

    /// <summary>Gets or sets whether reduced-chrome layout is enabled.</summary>
    [Parameter]
    public SwitchParameter Minimal { get; set; }

    /// <summary>Resolves the cmdlet&apos;s named or custom theme.</summary>
    /// <returns>The palette used to render the application.</returns>
    protected ThemePalette ResolveTheme() {
        object value = Theme is PSObject wrapped ? wrapped.BaseObject : Theme;
        return value switch {
            string name => ThemeCatalog.Load(name),
            IDictionary colors => ThemeCatalog.FromDictionary(colors),
            _ => throw new ArgumentException("Theme must be a built-in name or a hashtable of palette colors.")
        };
    }
}
