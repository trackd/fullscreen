using System.Text.Json;
using System.Text.Json.Serialization;

namespace fullscreen.Custom;

[JsonConverter(typeof(JsonStringEnumConverter<DesignerPropertiesMode>))]
internal enum DesignerPropertiesMode {
    Always,
    OnDemand,
    Hidden
}

/// <summary>Editor preferences are separate from the user's TUI definition.</summary>
internal sealed class TuiDesignerPreferences {
    public DesignerPropertiesMode Properties { get; set; }
    public int Height { get; set; } = 5;
    internal static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Fullscreen",
        "designer.json"
    );

    internal static TuiDesignerPreferences Load(string? path) {
        if (path is not null) {
            try {
                var preferences = JsonSerializer.Deserialize<TuiDesignerPreferences>(File.ReadAllText(path));
                if (preferences is not null && Enum.IsDefined(preferences.Properties)) {
                    preferences.Height = Math.Clamp(preferences.Height, 4, 12);
                    return preferences;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) {
                // An absent or damaged preference file must not prevent editing.
            }
        }

        return new();
    }

    internal void Save(string path) {
        string target = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, target, true);
        }
        finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }
}
