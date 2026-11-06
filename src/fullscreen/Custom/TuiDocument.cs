using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace fullscreen.Custom;
/// <summary>Versioned data contract. No Visual, delegate, assembly-qualified type or executable expression is serialized.</summary>
public sealed partial class TuiDocument {
    /// <summary>Gets or sets the serialized layout schema version.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; set; } = "Custom TUI";
    /// <summary>Gets or sets the state exposed to the current view or callback.</summary>
    public Dictionary<string, JsonElement> State { get; set; } = [];
    /// <summary>Gets or sets the top-level control definition.</summary>
    public TuiNode Root { get; set; } = new() {
        Id = "root",
        Type = "VStack"
    };

    internal static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 64,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    /// <summary>Gets the supported control type names.</summary>
    public static string[] ControlTypes { get; } = [
        "VStack",
        "HStack",
        "Text",
        "TextBox",
        "TextArea",
        "Button",
        "CheckBox",
        "Select",
        "Progress",
        "Border",
        "Code",
        "Image",
        "Rule",
        "Tabs",
        "Chart",
        "FTable",
        "FLogView",
        "FileBrowser",
        "PerformanceMonitor",
        "UserList",
        "FileCommander",
        "ObjectExplorer",
        "Spectre",
        ..NativeTuiControls.Names.Where(name => name is not ("VStack" or "HStack" or "TextBox" or "TextArea" or "Button" or "CheckBox" or "Select"
            or "Border"
            or "Rule"))
    ];

    /// <summary>Serializes the document as JSON.</summary>
    /// <returns>The JSON layout text.</returns>
    public string ToJson() {
        Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Parses and validates a JSON layout.</summary>
    /// <param name="json">The serialized JSON text.</param>
    /// <returns>The parsed document.</returns>
    public static TuiDocument FromJson(string json) =>
        ParseJson(json, validateTargets: true);
    private static TuiDocument ParseJson(string json, bool validateTargets) {
        if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) {
            throw new ArgumentException("TUI definitions are limited to 1 MiB.");
        }

        try {
            var document = JsonSerializer.Deserialize<TuiDocument>(json, JsonOptions) ?? throw new ArgumentException("Expected a TUI document.");
            document.ValidateCore(validateTargets);
            return document;
        }
        catch (JsonException error) {
            throw new ArgumentException(DescribeJsonError(json, error), error);
        }
    }

    private static string DescribeJsonError(string json, JsonException error) {
        try {
            using var input = JsonDocument.Parse(
                json,
                new JsonDocumentOptions {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64
                }
            );
            var owner = input.RootElement;
            string[] parts = (error.Path ?? "$").Split('.');
            foreach (string part in parts.Skip(1).SkipLast(1)) {
                int bracket = part.IndexOf('[');
                string name = bracket < 0 ? part : part[..bracket];
                owner = owner.EnumerateObject().First(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
                if (bracket >= 0) {
                    owner = owner[int.Parse(part.AsSpan(bracket + 1, part.Length - bracket - 2), CultureInfo.InvariantCulture)];
                }
            }

            string field = parts[^1].Split('[')[0];
            var propertyInfo = typeof(TuiNode).GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (propertyInfo is not null && owner.ValueKind == JsonValueKind.Object) {
                string id = owner.EnumerateObject().FirstOrDefault(property => property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)).Value.ToString();
                string value = owner.EnumerateObject().FirstOrDefault(property => property.Name.Equals(field, StringComparison.OrdinalIgnoreCase)).Value.ToString();
                if (value.Length > 80) {
                    value = value[..80] + "…";
                }

                return $"Control '{id}', property '{propertyInfo.Name}': expected {TuiControlCatalog.TypeDescription(propertyInfo.PropertyType)}; received '{value}'.";
            }
        }
        catch (Exception detailError) when (detailError is JsonException or InvalidOperationException or FormatException or OverflowException
            or IndexOutOfRangeException
            or ArgumentOutOfRangeException) {
        }

        return $"Invalid TUI definition at {error.Path ?? "$"} (line {(error.LineNumber ?? 0) + 1}). Check JSON syntax, property names and value types with Get-TuiControl.";
    }

    /// <summary>Converts a PowerShell-compatible layout object into a validated document.</summary>
    /// <param name="value">The JSON-compatible object or PowerShell layout definition to convert.</param>
    /// <returns>The converted document.</returns>
    public static TuiDocument FromObject(object value) {
        if (value is PSObject wrapper && wrapper.BaseObject is not PSCustomObject) {
            value = wrapper.BaseObject;
        }

        return value is string json ? FromJson(json) : FromJson(JsonSerializer.Serialize(Normalize(value, 0), JsonOptions));
    }

    internal static TuiDocument FromControl(object value) =>
        ParseJson(JsonSerializer.Serialize(Normalize(value, 0), JsonOptions), validateTargets: false);
    /// <summary>Reads and validates a layout from a file.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    /// <returns>The loaded document.</returns>
    public static TuiDocument Load(string path) {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) {
            throw new ArgumentException("TUI definitions are limited to 1 MiB.");
        }

        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        return Path.GetExtension(path).Equals(".psd1", StringComparison.OrdinalIgnoreCase) ? TuiDataFile.Parse(text) : FromJson(text);
    }

    /// <summary>Writes the document to a layout file.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    public void Save(string path) => Save(path, overwrite: true);

    /// <summary>Writes the document to a layout file, optionally preserving an existing file.</summary>
    /// <param name="path">The filesystem path.</param>
    /// <param name="overwrite">Whether an existing layout may be replaced.</param>
    public void Save(string path, bool overwrite) {
        string text = Path.GetExtension(path).Equals(".psd1", StringComparison.OrdinalIgnoreCase) ? TuiDataFile.Generate(this) : ToJson();
        if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024) {
            throw new ArgumentException("TUI definitions are limited to 1 MiB.");
        }

        WriteAtomic(path, text, overwrite);
    }

    internal static void WriteAtomic(string path, string text, bool overwrite = true) {
        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
            File.Move(temporary, target, overwrite);
        }
        finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Checks document structure, control types, and identifiers, throwing on invalid
    /// definitions.</summary>
    public void Validate() =>
        ValidateCore(validateTargets: true);
    private void ValidateCore(bool validateTargets) {
        if (Version != 1 || Root is null || State is null || Title is null || Title.Length > 512) {
            throw new ArgumentException("Expected version 1, a title (at most 512 characters), State and Root.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, TuiNode>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        Visit(Root, 0);
        foreach (var node in nodes.Values) {
            if (node.Target is null || node.Target.Length > 64
                || node.Target.Length > 0
                && (!ControlIdPattern().IsMatch(node.Target) || validateTargets && !nodes.ContainsKey(node.Target))) {
                throw new ArgumentException($"Control '{node.Id}': Target must identify a control in this definition.");
            }

            if (node.Type == "SearchReplacePopup"
                && (node.Target.Length == 0 || validateTargets && nodes[node.Target].Type != "LogControl")) {
                throw new ArgumentException($"Control '{node.Id}': SearchReplacePopup.Target must identify a LogControl.");
            }
        }

        void Visit(TuiNode node, int depth) {
            if (++count > 512 || depth > 24) {
                throw new ArgumentException("TUI definitions allow at most 512 controls and 24 levels.");
            }

            if (node is null || node.Id is null || !ControlIdPattern().IsMatch(node.Id)) {
                throw new ArgumentException("Control IDs must be unique names, 1–64 characters, beginning with a letter or underscore.");
            }

            if (!ids.Add(node.Id)) {
                throw new ArgumentException($"Duplicate control ID '{node.Id}'. Each control needs a unique ID (case-insensitive).");
            }

            nodes.Add(node.Id, node);
            if (node.TabTitle is null || node.TabTitle.Length > 128) {
                throw new ArgumentException($"Control '{node.Id}', property 'TabTitle': use at most 128 characters.");
            }

            if (!ControlTypes.Contains(node.Type, StringComparer.Ordinal)) {
                throw new ArgumentException($"Unsupported control type '{node.Type}'.");
            }

            if (node.Text is null || node.Text.Length > 65536 || node.Bind is null || node.Bind.Length > 128
                || node.Children is null
                || node.Items is null
                || node.Items.Length > 10000
                || node.Items.Any(x => x is null || x.Length > 4096)) {
                throw new ArgumentException("Invalid control text, binding, items or children.");
            }

            if (node.Width is < 1 or > 1000 || node.Height is < 1 or > 1000 || node.Spacing is < 0 or > 8
                || !double.IsFinite(node.Value)
                || node.Value is < 0 or > 1) {
                throw new ArgumentException("Width/Height must be 1–1000, spacing 0–8, progress 0–1.");
            }

            ValidateColor(node.Foreground);
            ValidateColor(node.Background);
            if (!Enum.TryParse<XenoAtom.Terminal.UI.Align>(node.HorizontalAlignment, true, out var horizontal)
                || !Enum.IsDefined(horizontal)
                || !Enum.TryParse<XenoAtom.Terminal.UI.Align>(node.VerticalAlignment, true, out var vertical)
                || !Enum.IsDefined(vertical)) {
                throw new ArgumentException("Alignment must be Start, Center, End or Stretch.");
            }

            if (node.Minimum is { } minimum && !double.IsFinite(minimum)
                || node.Maximum is { } maximum && !double.IsFinite(maximum)
                || node.Minimum > node.Maximum) {
                throw new ArgumentException("Chart Minimum and Maximum must be finite or null, with Minimum no greater than Maximum.");
            }

            if (!Enum.TryParse<Charts.GraphKind>(node.GraphKind, true, out var graphKind)
                || !Enum.IsDefined(graphKind)) {
                throw new ArgumentException("GraphKind must be " + string.Join(", ", Enum.GetNames<Charts.GraphKind>()) +
                    ".");
            }
            if (node.Type == "Chart") {
                Charts.ChartStackedArea.ValidateOptions(graphKind, node.Minimum, node.Maximum, Charts.ChartScale.Linear, Charts.ChartScale.Linear);
                if (node.XProperty is null || node.XProperty.Length > 128 || node.YProperty is null
                    || node.YProperty.Length > 64 || node.YProperty.Any(property => string.IsNullOrWhiteSpace(property) || property.Length > 128)
                    || node.YProperty.Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.YProperty.Length) {
                    throw new ArgumentException("Chart XProperty and unique YProperty selectors must contain at most 128 characters; use at most 64 series.");
                }
                if (node.SeriesRole is null || node.SeriesRole.Length > 0 && node.SeriesRole.Length != node.YProperty.Length
                    || node.SeriesRole.Any(role => !Enum.TryParse<Charts.ChartSeriesRole>(role, true, out var parsed) || !Enum.IsDefined(parsed))) {
                    throw new ArgumentException("Chart SeriesRole must contain one Auto/Column/Line role per YProperty.");
                }
                if (node.LowerProperty is null || node.UpperProperty is null
                    || node.LowerProperty.Length > 128 || node.UpperProperty.Length > 128
                    || node.LowerProperty.Length > 0 && string.IsNullOrWhiteSpace(node.LowerProperty)
                    || node.UpperProperty.Length > 0 && string.IsNullOrWhiteSpace(node.UpperProperty)
                    || (node.LowerProperty.Length > 0 || node.UpperProperty.Length > 0)
                    && (graphKind != Charts.GraphKind.ErrorBars || node.YProperty.Length == 0)) {
                    throw new ArgumentException("LowerProperty and UpperProperty require object-bound ErrorBars and field names of 1-128 characters.");
                }
                if (node.YProperty.Length > 0 && (string.IsNullOrWhiteSpace(node.Bind)
                    || Charts.ChartTopology.Supports(graphKind) || graphKind is Charts.GraphKind.Flamegraph or Charts.GraphKind.Gantt)) {
                    throw new ArgumentException("Chart YProperty requires Bind for numeric object rows, not frames, tasks or topology.");
                }
                if (node.XProperty.Length > 0 && node.YProperty.Length == 0) {
                    throw new ArgumentException("Chart XProperty requires YProperty object-series selectors.");
                }
                if (Charts.ChartTopology.Supports(graphKind)) {
                    if (string.IsNullOrWhiteSpace(node.Bind) || string.IsNullOrWhiteSpace(node.EdgeBind) || node.EdgeBind.Length > 128) {
                        throw new ArgumentException("Relationship Chart requires Bind for nodes and EdgeBind for a separate edge collection.");
                    }
                    Charts.ChartTopology.ValidateOptions(node.Minimum, node.Maximum, null, null,
                        Charts.ChartScale.Linear, Charts.ChartScale.Linear, 0, 0, null);
                }
                if (graphKind == Charts.GraphKind.Gantt) {
                    Charts.ChartSchedule.ValidateOptions(node.Minimum, node.Maximum, Charts.ChartScale.Time, Charts.ChartScale.Linear, 0, 0, 0);
                }
                Charts.ChartMarket.ValidateOptions(graphKind, node.ReversalAmount, node.Minimum, node.Maximum,
                    null, null, Charts.ChartScale.Linear, Charts.ChartScale.Linear, 0, 0);
                if (node.FlameFrameRows is < 1 or > 8) {
                    throw new ArgumentException("FlameFrameRows must be between 1 and 8.");
                }
                if (graphKind == Charts.GraphKind.Flamegraph && (node.Minimum is not null || node.Maximum is not null)) {
                    throw new ArgumentException("Flamegraph uses inclusive widths and depth rows, not numeric bounds.");
                }
                foreach (string property in new[] { node.FrameIdProperty, node.ParentIdProperty, node.WeightProperty }) {
                    if (string.IsNullOrWhiteSpace(property) || property.Length > 128) {
                        throw new ArgumentException("Flamegraph field selectors must contain 1-128 characters.");
                    }
                }
                if (!string.IsNullOrEmpty(node.FrameNameProperty) && (string.IsNullOrWhiteSpace(node.FrameNameProperty) || node.FrameNameProperty.Length > 128)) {
                    throw new ArgumentException("FrameNameProperty must contain 1-128 characters when specified.");
                }
                foreach (string property in new[] { node.StartProperty, node.EndProperty, node.TaskNameProperty }) {
                    if (string.IsNullOrWhiteSpace(property) || property.Length > 128) {
                        throw new ArgumentException("Gantt field selectors must contain 1-128 characters.");
                    }
                }
                if (!string.IsNullOrEmpty(node.ProgressProperty) && (string.IsNullOrWhiteSpace(node.ProgressProperty) || node.ProgressProperty.Length > 128)) {
                    throw new ArgumentException("ProgressProperty must contain 1-128 characters when specified.");
                }
            }

            if (node.Type == "PerformanceMonitor" && !Monitoring.MonitorSurface.TimeGraphTypes.Contains(graphKind)) {
                throw new ArgumentException("PerformanceMonitor requires a time-based GraphKind.");
            }

            if (node.Columns is null || node.Columns.Length > 256
                || node.Columns.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 128)
                || node.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Columns.Length) {
                throw new ArgumentException("Columns must contain at most 256 unique property names of 1–128 characters.");
            }

            if (node.SelectionBind is null || node.SelectionBind.Length > 128
                || node.SelectionBind.Length > 0 && !NativeTuiControls.Contains(node.Type)
                && node.Type is not ("FTable" or "FLogView" or "FileBrowser" or "UserList" or "FileCommander"
                or "ObjectExplorer" or "Chart")) {
                throw new ArgumentException("SelectionBind is supported by Table, LogView, FileBrowser, FileCommander, ObjectExplorer, UserList and Chart and allows at most 128 characters.");
            }

            if (node.SelectionBind.Length > 0
                && node.SelectionBind.Equals(node.Bind, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("SelectionBind and Bind must use different state keys.");
            }

            if (node.Path is null || node.Path.Length > 4096 || node.SecondaryPath is null
                || node.SecondaryPath.Length > 4096
                || node.RefreshInterval is < 100 or > 60000
                || node.View is null
                || !Enum.TryParse<Monitoring.MonitorView>(node.View, true, out var monitorView)
                || !Enum.IsDefined(monitorView)) {
                throw new ArgumentException("Path must be at most 4096 characters; RefreshInterval is 100–60000 ms; View is Processes, Services, Network, Gpu, Users or Dashboard.");
            }

            if (!node.IsContainer && node.Children.Count != 0
                || node.Type is "Border" or "Button" && node.Children.Count > 1) {
                throw new ArgumentException("Only layout controls can contain children; Border allows one child.");
            }

            if (node.Bind.Length != 0 && !NativeTuiControls.Contains(node.Type)
                && node.Type is not ("Text" or "TextBox" or "TextArea" or "CheckBox" or "Select" or "Progress" or "Code"
                or "Image"
                or "Chart"
                or "FTable"
                or "FLogView"
                or "ObjectExplorer"
                or "Spectre")) {
                throw new ArgumentException("This control type does not support Bind.");
            }

            TuiControlCatalog.ValidateOptions(node);
            if (node.Options is null || node.Options.Count > 128) {
                throw new ArgumentException("Options must contain at most 128 native properties.");
            }

            NativeTuiControls.Validate(node);
            foreach (var child in node.Children) {
                Visit(child, depth + 1);
            }
        }
    }

    private static void ValidateColor(string color) {
        if (color is null || color.Length != 0 && !RgbHexColorPattern().IsMatch(color)) {
            throw new ArgumentException("Colors must be empty (inherit theme) or #RRGGBB.");
        }
    }

    /// <summary>Creates an initial editable TUI document.</summary>
    /// <returns>A new starter document.</returns>
    public static TuiDocument Starter() =>
        new() {
            Title = "My application",
            Root = new() {
                Id = "root",
                Type = "VStack",
                Spacing = 1,
                Children = [new()
            {
                Id = "heading",
                Type = "Text",
                Text = "Welcome to your TUI"
            }, new()
            {
                Id = "name",
                Type = "TextBox",
                Text = "World",
                Bind = "name"
            }, new()
            {
                Id = "submit",
                Type = "Button",
                Text = "Submit",
                Close = true
            }

                ]
            }
        };
    /// <summary>Converts the document into objects suitable for PowerShell inspection.</summary>
    /// <returns>The PowerShell representation of the layout.</returns>
    public PSObject ToPowerShell() =>
        Object(JsonSerializer.SerializeToElement(this, JsonOptions));
    internal static object? Plain(JsonElement value) =>
        value.ValueKind switch {
            JsonValueKind.Object => Dictionary(value),
            JsonValueKind.Array => value.EnumerateArray().Select(Plain).ToArray(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt64(out long integer) ? (object)integer : value.GetDouble(),
            _ => null
        };
    private static PSObject Object(JsonElement value) {
        var result = new PSObject();
        foreach (var property in value.EnumerateObject()) {
            result.Properties.Add(new PSNoteProperty(property.Name, Plain(property.Value)));
        }

        return result;
    }

    private static Hashtable Dictionary(JsonElement value) {
        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject()) {
            result[property.Name] = Plain(property.Value);
        }

        return result;
    }

    private static object? Normalize(object? value, int depth) {
        if (depth > 60) {
            throw new ArgumentException("TUI definition nesting is too deep.");
        }

        if (value is PSObject wrapper) {
            if (wrapper.BaseObject is not PSCustomObject) {
                return Normalize(wrapper.BaseObject, depth + 1);
            }

            return wrapper.Properties.ToDictionary(x => x.Name, x => Normalize(x.Value, depth + 1));
        }

        if (value is IDictionary dictionary) {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry pair in dictionary) {
                if (pair.Key is not string key) {
                    throw new ArgumentException("TUI keys must be strings.");
                }

                result.Add(key, Normalize(pair.Value, depth + 1));
            }

            return result;
        }

        if (value is string or bool or byte or short or int or long or float or double or decimal || value is null) {
            return value;
        }

        if (value is IEnumerable sequence) {
            return sequence.Cast<object?>().Select(x => Normalize(x, depth + 1)).ToArray();
        }

        throw new ArgumentException("Definitions contain only PowerShell objects, dictionaries, arrays and JSON values; pass handlers separately to Invoke-Tui.");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ControlIdPattern();
    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex RgbHexColorPattern();
}

/// <summary>Describes one control and its children in a TUI document.</summary>
public sealed class TuiNode {
    /// <summary>Gets or sets the stable resource or process identifier.</summary>
    public string Id { get; set; } = "";

    private string? _language;
    /// <summary>Code control TextMate language id or extension; blank is Markdown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language {
        get => _language;
        set => _language = string.IsNullOrEmpty(value) ? null : value.Length <= 64 ? value : throw new ArgumentException("Language allows at most 64 characters.");
    }
    /// <summary>Gets or sets the supported control type name.</summary>
    public string Type { get; set; } = "Text";
    /// <summary>Gets or sets the object-view configuration.</summary>
    public Dictionary<string, JsonElement> Options { get; set; } = [];
    /// <summary>Gets or sets the destination data buffer or error target.</summary>
    public string Target { get; set; } = "";
    /// <summary>Gets or sets the enumeration type used by an enum selector.</summary>
    public string EnumType { get; set; } = "DayOfWeek";
    /// <summary>Gets or sets the source text to parse or display.</summary>
    public string Text { get; set; } = "";
    /// <summary>Gets or sets the title displayed on a tab.</summary>
    public string TabTitle { get; set; } = "";
    /// <summary>Gets or sets the state key bound to the control&apos;s value.</summary>
    public string Bind { get; set; } = "";
    /// <summary>Gets or sets the original items represented by the group or page.</summary>
    public string[] Items { get; set; } = [];
    /// <summary>Gets or sets the table column names or column definitions.</summary>
    public string[] Columns { get; set; } = [];
    /// <summary>Gets or sets the state key updated when selection changes.</summary>
    public string SelectionBind { get; set; } = "";
    /// <summary>Gets or sets the filesystem or resource path.</summary>
    public string Path { get; set; } = "";
    /// <summary>Gets or sets the secondary directory used by a two-pane file manager.</summary>
    public string SecondaryPath { get; set; } = "";
    /// <summary>Gets or sets the resource or monitor view index.</summary>
    public string View { get; set; } = "Dashboard";
    /// <summary>Gets or sets the delay in seconds between object-chart refreshes.</summary>
    public int RefreshInterval { get; set; } = 1000;
    /// <summary>Gets or sets whether owned processes are included beneath user sessions.</summary>
    public bool IncludeProcesses { get; set; }
    /// <summary>Gets or sets whether reduced-chrome layout is enabled.</summary>
    public bool Minimal { get; set; }
    /// <summary>Gets or sets the requested width.</summary>
    public int? Width { get; set; }
    /// <summary>Gets or sets the requested height.</summary>
    public int? Height { get; set; }
    /// <summary>Gets or sets the number of cells between child controls.</summary>
    public int Spacing { get; set; }
    /// <summary>Gets or sets the foreground text color.</summary>
    public string Foreground { get; set; } = "";
    /// <summary>Gets or sets the background color.</summary>
    public string Background { get; set; } = "";
    /// <summary>Gets or sets whether text uses bold styling.</summary>
    public bool Bold { get; set; }
    /// <summary>Gets or sets the child&apos;s horizontal placement within its available bounds.</summary>
    public string HorizontalAlignment { get; set; } = "Stretch";
    /// <summary>Gets or sets the child&apos;s vertical placement within its available bounds.</summary>
    public string VerticalAlignment { get; set; } = "Start";
    /// <summary>Gets or sets the input value to format, display, or append.</summary>
    public double Value { get; set; }
    /// <summary>Gets or sets the lower Y-axis limit, or null for automatic scaling.</summary>
    public double? Minimum { get; set; }
    /// <summary>Gets or sets the upper chart scale limit.</summary>
    public double? Maximum { get; set; }
    /// <summary>Gets or sets the positive fixed Kagi reversal in price units; default 1.</summary>
    public double ReversalAmount { get; set; } = 1;
    /// <summary>Gets or sets the flamegraph frame-ID field, default FrameId.</summary>
    public string FrameIdProperty { get; set; } = "FrameId";
    /// <summary>Gets or sets the flamegraph parent-ID field, default ParentId.</summary>
    public string ParentIdProperty { get; set; } = "ParentId";
    /// <summary>Gets or sets the flamegraph label field; empty uses frame IDs.</summary>
    public string FrameNameProperty { get; set; } = "";
    /// <summary>Gets or sets the flamegraph exclusive self-weight field, default Weight.</summary>
    public string WeightProperty { get; set; } = "Weight";
    /// <summary>Gets or sets whether flamegraph roots appear at the top.</summary>
    public bool FlameReverse { get; set; }
    /// <summary>Gets or sets terminal rows per flamegraph depth; default 2, range 1-8.</summary>
    public int FlameFrameRows { get; set; } = 2;
    /// <summary>Gets or sets the Gantt absolute start-instant field.</summary>
    public string StartProperty { get; set; } = "Start";
    /// <summary>Gets or sets the Gantt absolute end-instant field.</summary>
    public string EndProperty { get; set; } = "End";
    /// <summary>Gets or sets the Gantt task-label field.</summary>
    public string TaskNameProperty { get; set; } = "Task";
    /// <summary>Gets or sets the optional Gantt completed-fraction field; empty means unknown.</summary>
    public string ProgressProperty { get; set; } = "";
    /// <summary>Gets or sets the separate relationship-edge collection state key; Bind supplies nodes.</summary>
    public string EdgeBind { get; set; } = "";
    /// <summary>Gets or sets the optional numeric object-series category/coordinate field.</summary>
    public string XProperty { get; set; } = "";
    /// <summary>Gets or sets up to 64 named numeric fields projected from Bind's original object rows.</summary>
    public string[] YProperty { get; set; } = [];
    /// <summary>Gets or sets the optional lower-bound field for object-bound ErrorBars.</summary>
    public string LowerProperty { get; set; } = "";
    /// <summary>Gets or sets the optional upper-bound field for object-bound ErrorBars.</summary>
    public string UpperProperty { get; set; } = "";
    /// <summary>Gets or sets optional Auto/Column/Line roles in YProperty order.</summary>
    public string[] SeriesRole { get; set; } = [];
    /// <summary>Gets or sets the visual representation used to draw the data.</summary>
    public string GraphKind { get; set; } = "Area";
    /// <summary>Gets or sets whether a chart shows its wrapping, clickable series legend.</summary>
    public bool ShowLegend { get; set; }
    /// <summary>Gets or sets whether a chart shows hover tooltips. Defaults to true.</summary>
    public bool ShowTooltips { get; set; } = true;
    /// <summary>Gets or sets whether a chart shows supported coordinate axes. Defaults to false.</summary>
    public bool ShowAxes { get; set; }
    /// <summary>Gets or sets whether the checkable control is selected.</summary>
    public bool Checked { get; set; }
    /// <summary>Gets or sets whether the callback requests closing the application.</summary>
    public bool Close { get; set; }
    /// <summary>Gets or sets whether the control accepts user interaction.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the ordered child control definitions.</summary>
    public List<TuiNode> Children { get; set; } = [];

    /// <summary>Gets whether the control can contain other controls.</summary>
    [JsonIgnore]
    public bool IsContainer => Type is "VStack" or "HStack" or "Border" or "Tabs" || NativeTuiControls.IsContainer(Type);
}
