using System.Collections;
using System.Management.Automation;
using fullscreen.Themes;
using XenoAtom.Terminal.UI;

namespace fullscreen.Commands;
/// <summary>Inspect built-in fullscreen palettes as native PowerShell data.</summary>
[Cmdlet(VerbsCommon.Get, "FullscreenTheme")]
[OutputType(typeof(PSObject))]
public sealed class GetFullscreenThemeCommand : PSCmdlet {
    /// <summary>Built-in palette names or wildcard patterns. Defaults to all themes. Names and historical
    /// VS Code aliases are case-insensitive; Tab completes canonical names.</summary>
    [Parameter(Position = 0)]
    [ArgumentCompleter(typeof(ThemeArgumentCompleter))]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = ["*"];

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        foreach (string name in Name) {
            if (WildcardPattern.ContainsWildcardCharacters(name)) {
                var pattern = new WildcardPattern(name, WildcardOptions.IgnoreCase);
                foreach (string builtIn in ThemeCatalog.Names) {
                    if (pattern.IsMatch(builtIn)) {
                        WriteTheme(ThemeCatalog.Load(builtIn));
                    }
                }
            }
            else {
                try {
                    WriteTheme(ThemeCatalog.Load(name));
                }
                catch (ArgumentException error) {
                    WriteError(new ErrorRecord(error, "FullscreenThemeNotFound", ErrorCategory.ObjectNotFound, name));
                }
            }
        }
    }

    private void WriteTheme(ThemePalette palette) {
        palette.Panel.TryGetBackground(out var panel);
        palette.Muted.TryGetForeground(out var muted);
        palette.Selected.TryGetBackground(out var selection);
        palette.Selected.TryGetForeground(out var selectedText);
        var colors = new Hashtable(StringComparer.OrdinalIgnoreCase) {
            ["Name"] = palette.Name,
            ["Background"] = Hex(palette.Background),
            ["Foreground"] = Hex(palette.Foreground),
            ["Panel"] = Hex(panel),
            ["Muted"] = Hex(muted),
            ["Accent"] = Hex(palette.Accent),
            ["Selection"] = Hex(selection),
            ["SelectedText"] = Hex(selectedText),
            ["Success"] = Hex(palette.Success),
            ["Warning"] = Hex(palette.Warning),
            ["Error"] = Hex(palette.Error),
            ["Purple"] = Hex(palette.Purple)
        };
        var result = new PSObject();
        for (int i = 0; i < ThemePalette.RoleNames.Length; i++) {
            colors[ThemePalette.RoleNames[i]] = Hex(palette.RoleColor(i));
        }
        result.TypeNames.Insert(0, "fullscreen.Theme");
        result.Properties.Add(new PSNoteProperty("Name", palette.Name));
        result.Properties.Add(new PSNoteProperty("Colors", colors));
        string Swatch(
            string label,
            Color color
        ) =>
            $"\u001b[48;2;{palette.Background.R};{palette.Background.G};{palette.Background.B}m\u001b[38;2;{color.R};{color.G};{color.B}m {label} ■ \u001b[0m";
        result.Properties.Add(new PSNoteProperty(
            "Preview",
            string.Join(
                "  ",
                Swatch("Text", palette.Foreground),
                Swatch("Accent", palette.Accent),
                Swatch("Success", palette.Success),
                Swatch("Warning", palette.Warning),
                Swatch("Error", palette.Error)
            )
        ));
        WriteObject(result, false);
    }

    private static string Hex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
