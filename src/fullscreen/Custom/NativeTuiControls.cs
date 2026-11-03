using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using System.Collections.Concurrent;

namespace fullscreen.Custom;
/// <summary>Native widgets use the UI assembly already shipped by Fullscreen.</summary>
internal static partial class NativeTuiControls {
    private static readonly Dictionary<string, Type> Types = CreateTypes();
    private static readonly Dictionary<string, Type> Enums = typeof(Visual).Assembly.GetExportedTypes().Where(type => type.IsEnum).Append(typeof(DayOfWeek)).Append(typeof(ConsoleColor)).GroupBy(type => type.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Configurables = new();
    private static readonly JsonSerializerOptions Options = new() {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
    public static IEnumerable<string> Names => Types.Keys.Order(StringComparer.Ordinal);
    internal static string[] EnumNames => [.. Enums.Keys.Order(StringComparer.Ordinal)];

    public static bool Contains(string name) =>
        Types.ContainsKey(name);
    internal static bool IsContainer(string name) =>
        name is "Accordion" or "Border" or "Button" or "Center" or "Collapsible" or "ComputedVisual"
        or "ContentSwitcher"
        or "DockLayout"
        or "Footer"
        or "Grid"
        or "GridCell"
        or "Group"
        or "Header"
        or "HSplitter"
        or "VSplitter"
        or "OptionListItem"
        or "Padder"
        or "Popup"
        or "Dialog"
        or "Spinner"
        or "ScrollViewer"
        or "StatusBar"
        or "Switch"
        or "TabControl"
        or "Toast"
        or "ToastHost"
        or "TooltipHost"
        or "ValidationPresenter"
        or "WindowLayer"
        or "WrapHStack"
        or "WrapVStack"
        or "ZStack"
        or "VStack"
        or "HStack";
    internal static Type TypeOf(string name) =>
        Types[name];
    internal static Type TypeOf(TuiNode node) =>
        node.Type == "EnumSelect" ? Enums.TryGetValue(node.EnumType, out var type) ? typeof(EnumSelect<>).MakeGenericType(type) : throw new ArgumentException($"Unknown native EnumType '{node.EnumType}'.") : TypeOf(node.Type);
    private static Dictionary<string, Type> CreateTypes() {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(Visual).Assembly.GetExportedTypes().Where(type => type.Namespace == typeof(Button).Namespace && !type.IsAbstract
            && typeof(Visual).IsAssignableFrom(type))) {
            if (type == typeof(CodeEditor)) {
                continue;
            }

            string name = type.Name.Split('`')[0];
            var concrete = type.IsGenericTypeDefinition ? type.MakeGenericType(name switch {
                "NumberBox" or "Slider" => typeof(double),
                "EnumSelect" => typeof(DayOfWeek),
                "DataPresenter" => typeof(object),
                _ => typeof(string)
            }) : type;
            result.Add(name, concrete);
        }

        return result;
    }

    internal static PropertyInfo[] ConfigurableProperties(string name) =>
        ConfigurableProperties(TypeOf(name));
    private static PropertyInfo[] ConfigurableProperties(Type type) =>
        Configurables.GetOrAdd(
            type,
            static type => [.. type.GetProperties().Where(property => property.SetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0
            && IsDataType(property.PropertyType))]
        );
    private static bool IsDataType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(object) || type == typeof(string) || type == typeof(bool)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(TimeSpan)
            || type == typeof(Color)
            || type == typeof(Thickness)
            || type == typeof(ValidationMessage);
    }

    internal static void Validate(TuiNode node) {
        if (!Contains(node.Type)) {
            return;
        }

        var type = TypeOf(node);
        foreach (var (name, value) in node.Options) {
            if (node.Type == "Grid" && name is "Rows" or "Columns") {
                _ = GridLengths(value);
                continue;
            }

            var property = ConfigurableProperties(type).FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Control '{node.Id}' ({node.Type}) has no configurable native property '{name}'.");
            _ = ConvertValue(value, property.PropertyType);
        }
    }

    internal static void Configure(Visual visual, TuiNode node) {
        foreach (var (name, value) in node.Options) {
            if (visual is Grid grid && name is "Rows" or "Columns") {
                if (name == "Rows") {
                    grid.RowDefinitions.Clear();
                    foreach (var length in GridLengths(value)) {
                        grid.RowDefinitions.Add(new() { Height = length });
                    }
                }
                else {
                    grid.ColumnDefinitions.Clear();
                    foreach (var length in GridLengths(value)) {
                        grid.ColumnDefinitions.Add(new() { Width = length });
                    }
                }

                continue;
            }

            var property = ConfigurableProperties(visual.GetType()).Single(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            try {
                property.SetValue(visual, ConvertValue(value, property.PropertyType));
            }
            catch (TargetInvocationException error) {
                throw new ArgumentException(
                    $"Control '{node.Id}', native property '{name}': {error.InnerException?.Message}",
                    error.InnerException
                );
            }
        }
    }

    private static object? ConvertValue(JsonElement value, Type type) {
        if (type == typeof(object)) {
            return TuiDocument.Plain(value);
        }

        if (value.ValueKind == JsonValueKind.Null && Nullable.GetUnderlyingType(type) is not null) {
            return null;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(ValidationMessage)) {
            if (value.ValueKind == JsonValueKind.String) {
                return new ValidationMessage(ValidationSeverity.Error, new TextBlock(value.GetString() ?? ""));
            }

            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("Text", out var message)) {
                throw new ArgumentException("Validation Message accepts text or { Text, Severity }.");
            }

            var severity = value.TryGetProperty("Severity", out var level) ? Enum.Parse<ValidationSeverity>(level.GetString() ?? "Error", true) : ValidationSeverity.Error;
            return new ValidationMessage(severity, new TextBlock(message.GetString() ?? ""));
        }

        if (type == typeof(Thickness)) {
            if (value.ValueKind == JsonValueKind.Number) {
                return new Thickness(value.GetInt32());
            }

            int[] edges = [.. value.EnumerateArray().Select(edge => edge.GetInt32())];
            return edges.Length switch {
                2 => new Thickness(edges[0], edges[1], edges[0], edges[1]),
                4 => new Thickness(edges[0], edges[1], edges[2], edges[3]),
                _ => throw new ArgumentException("Padding/Margin accepts one number or [horizontal, vertical] or [left, top, right, bottom].")
            };
        }

        if (type == typeof(Color)) {
            string text = value.GetString() ?? "";
            if (text.Length is not (7 or 9) || text[0] != '#'
                || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) {
                throw new ArgumentException("Native colors must be #RRGGBB or #RRGGBBAA.");
            }

            return text.Length == 9 ? Color.RgbA((byte)(rgb >> 24), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : Color.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        try {
            return value.Deserialize(type, Options);
        }
        catch (JsonException error) {
            throw new ArgumentException($"Invalid native {type.Name} value: {value}.", error);
        }
    }

    private static GridLength[] GridLengths(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 128) {
            throw new ArgumentException("Grid Rows/Columns must be arrays of at most 128 lengths: Auto, *, 2*, or fixed cell counts.");
        }

        return [..value.EnumerateArray().Select(length =>
        {
            string text = length.ValueKind == JsonValueKind.Number ? length.GetRawText() : length.GetString() ?? "";
            if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return GridLength.Auto;
            }

            if (text.EndsWith('*') && (text == "*" || double.TryParse(
                text.AsSpan(0, text.Length - 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double weight
            ) && double.IsFinite(weight) && weight > 0))
            {
                return GridLength.Star(text == "*" ? 1 : double.Parse(text.AsSpan(0, text.Length - 1), CultureInfo.InvariantCulture));
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int cells) && cells >= 0
                && cells <= 10000)
            {
                return GridLength.Fixed(cells);
            }

            throw new ArgumentException($"Invalid grid length '{text}'.");
        })];
    }
}
