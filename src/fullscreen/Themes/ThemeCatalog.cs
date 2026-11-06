using System.Collections;
using System.Globalization;
using XenoAtom.Terminal.UI;

namespace fullscreen.Themes;

public static partial class ThemeCatalog {
    private readonly record struct Entry(
        string Name,
        string? Alias,
        uint Background,
        uint Foreground,
        uint Panel,
        uint Muted,
        uint Accent,
        uint Selection,
        uint SelectedText,
        uint Success,
        uint Warning,
        uint Error,
        uint Purple,
        uint[] Colors
    );
    /// <summary>Gets the available built-in theme names.</summary>
    public static IEnumerable<string> Names {
        get {
            yield return "Default";
            foreach (var entry in BuiltIns) {
                yield return entry.Name;
            }
        }
    }

    /// <summary>Loads a named built-in theme.</summary>
    /// <param name="name">The built-in theme name.</param>
    /// <returns>The requested theme palette.</returns>
    public static ThemePalette Load(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase)) {
            return ThemePalette.Default;
        }

        foreach (var entry in BuiltIns) {
            if (name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
                || name.Equals(entry.Alias, StringComparison.OrdinalIgnoreCase)) {
                return new(
                    entry.Name,
                    Rgb(entry.Background),
                    Rgb(entry.Foreground),
                    Rgb(entry.Panel),
                    Rgb(entry.Muted),
                    Rgb(entry.Accent),
                    Rgb(entry.Selection),
                    Rgb(entry.SelectedText),
                    Rgb(entry.Success),
                    Rgb(entry.Warning),
                    Rgb(entry.Error),
                    Rgb(entry.Purple),
                    colors: Array.ConvertAll(entry.Colors, Rgb)
                );
            }
        }

        throw new ArgumentException(
            $"Unknown theme '{name}'. Use a built-in name (Tab to complete), or a hashtable of palette colors. Theme files are not supported.",
            nameof(name)
        );
    }

    /// <summary>Creates a theme palette from named custom color values.</summary>
    /// <param name="values">The custom palette entries keyed by color name.</param>
    /// <returns>The custom theme palette.</returns>
    public static ThemePalette FromDictionary(IDictionary values) {
        ArgumentNullException.ThrowIfNull(values);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in values) {
            if (entry.Key is not string key || !IsKey(key)) {
                throw new ArgumentException(
                    $"Unknown theme key '{entry.Key}'. Use Name, Base, Background, Foreground, Panel, Muted, Accent, Selection, SelectedText, Success, Warning, Error, Purple, {string.Join(", ", ThemePalette.RoleNames)}.",
                    nameof(values)
                );
            }

            if (entry.Value is not string text || string.IsNullOrWhiteSpace(text)) {
                throw new ArgumentException($"Theme value '{key}' must be a nonempty string.", nameof(values));
            }

            if (!overrides.TryAdd(key, text)) {
                throw new ArgumentException($"Duplicate theme key '{key}'.", nameof(values));
            }
        }

        var basis = Load(overrides.GetValueOrDefault("Base", "Default"));
        if (overrides.Count == 0) {
            return basis;
        }

        basis.Panel.TryGetBackground(out var panel);
        basis.Muted.TryGetForeground(out var muted);
        basis.Selected.TryGetBackground(out var selection);
        basis.Selected.TryGetForeground(out var selectedText);
        var colors = new Color[ThemePalette.RoleNames.Length];
        for (int i = 0; i < colors.Length; i++) {
            colors[i] = Pick(ThemePalette.RoleNames[i], basis.RoleColor(i));
        }
        return new(
            overrides.GetValueOrDefault("Name", "Custom"),
            Pick("Background", basis.Background),
            Pick("Foreground", basis.Foreground),
            Pick("Panel", panel),
            Pick("Muted", muted),
            Pick("Accent", basis.Accent),
            Pick("Selection", selection),
            Pick("SelectedText", selectedText),
            Pick("Success", basis.Success),
            Pick("Warning", basis.Warning),
            Pick("Error", basis.Error),
            Pick("Purple", basis.Purple),
            colors: colors
        );
        Color Pick(
            string key,
            Color fallback
        ) =>
            overrides.TryGetValue(key, out string? value) ? Parse(value, key) : fallback;
    }

    private static bool IsKey(string key) =>
        key.ToUpperInvariant() is "NAME" or "BASE" or "BACKGROUND" or "FOREGROUND" or "PANEL" or "MUTED" or "ACCENT"
        or "SELECTION"
        or "SELECTEDTEXT"
        or "SUCCESS"
        or "WARNING"
        or "ERROR"
        or "PURPLE" || ThemePalette.RoleNames.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static Color Rgb(uint rgb) =>
        Color.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Color Parse(string value, string key) {
        if (!value.StartsWith('#') || value.Length is not (4 or 7) || !uint.TryParse(
            value.AsSpan(1),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out uint rgb
        )) {
            throw new ArgumentException($"Theme color '{key}' must use #RGB or #RRGGBB hexadecimal notation.");
        }

        if (value.Length == 4) {
            rgb = ((rgb & 0xF00) << 8 | (rgb & 0xF0) << 4 | rgb & 0xF) * 17;
        }

        return Rgb(rgb);
    }
}
