using System.Reflection;

namespace fullscreen.Custom;
/// <summary>Describes a control type, its bindings, events, and configurable properties.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Category">The control catalog group.</param>
/// <param name="Description">The human-readable description.</param>
/// <param name="ReusedBy">The component that reuses this control.</param>
/// <param name="Binding">The supported state-binding behavior.</param>
/// <param name="Events">The control event names.</param>
/// <param name="Properties">The property names to display, or null to infer columns.</param>
/// <param name="Live">Whether the control supports live updates.</param>
public sealed record TuiControlInfo(
    string Name,
    string Category,
    string Description,
    string ReusedBy,
    string Binding,
    string[] Events,
    string[] Properties,
    bool Live
) {
    /// <summary>Gets or sets the names of supported native control properties.</summary>
    public TuiPropertyInfo[] NativeProperties { get; init; } = [];
}

/// <summary>Describes an editable control property, including its default value and available choices.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Type">The supported control type name.</param>
/// <param name="DefaultValue">The default value used when the option is omitted.</param>
/// <param name="Choices">The allowed property values.</param>
/// <param name="Group">The property editor group.</param>
/// <param name="Description">The human-readable description.</param>
public sealed record TuiPropertyInfo(
    string Name,
    string Type,
    object? DefaultValue,
    string[] Choices,
    string Group,
    string Description
);
/// <summary>Public discovery contract shared by cmdlet validation, completion and the designer.</summary>
public static class TuiControlCatalog {
    private static readonly TuiNode Defaults = new();
    private static readonly string[] NativeOptionProperties = ["Options"];
    private static readonly string[] EnumTypeProperties = ["EnumType"];
    private static readonly PropertyInfo[] NodeProperties = [.. typeof(TuiNode).GetProperties().Where(property => property.CanWrite)];
    internal static string TypeDescription(Type type) =>
        type == typeof(int?) ? "a whole number, or blank for automatic sizing" : type == typeof(int) ? "a whole number" : type == typeof(double?) ? "a finite number, or blank for automatic scaling" : type == typeof(double) ? "a finite number" : type == typeof(bool) ? "true or false" : type == typeof(string[]) ? "a list of text values" : "text";
    /// <summary>Describes the editable properties supported by a control type.</summary>
    /// <param name="type">The supported control catalog name.</param>
    /// <returns>The control&apos;s property descriptions.</returns>
    public static IReadOnlyList<TuiPropertyInfo> Properties(string type) =>
        [..Describe(type).Properties.Where(name => name != "Children").Select(name =>
    {
        var property = NodeProperties.Single(property => property.Name == name);
        string[] choices = name switch
        {
            "Type" => (string[])TuiDocument.ControlTypes.Clone(),
            "GraphKind" => type == "PerformanceMonitor" ? [..Monitoring.MonitorSurface.TimeGraphTypes.Select(kind => kind.ToString())] : Enum.GetNames<Charts.GraphKind>(),
            "View" => Enum.GetNames<Monitoring.MonitorView>(),
            "EnumType" => NativeTuiControls.EnumNames,
            "HorizontalAlignment" or "VerticalAlignment" => Enum.GetNames<XenoAtom.Terminal.UI.Align>(),
            _ => []
        };
        string description = name switch
        {
            "Id" => "Unique control name used by event handlers.",
            "TabTitle" => "Tab caption when this control is a tab page; blank uses Id.",
            "Bind" => "Shared state key supplying this control's value.",
            "EdgeBind" => "Separate diagram-edge state collection; Bind supplies node records.",
            "XProperty" => "Optional category/coordinate field in Bind's numeric object rows; requires YProperty.",
            "YProperty" => "Up to 64 unique numeric object-row fields; each becomes a named series with original source identity.",
            "LowerProperty" => "Optional lower-bound field for object-bound ErrorBars.",
            "UpperProperty" => "Optional upper-bound field for object-bound ErrorBars.",
            "SeriesRole" => "Optional Auto/Column/Line roles, one per YProperty in order, for ColumnLine.",
            "SelectionBind" => "State key receiving the original selected object or path.",
            "Minimum" or "Maximum" => "Chart bound; blank scales automatically.",
            "ReversalAmount" => "Positive fixed Kagi reversal in price units; default 1.",
            "FrameIdProperty" => "Flamegraph unique ID field; default FrameId.",
            "ParentIdProperty" => "Flamegraph parent ID field; null/empty values are roots.",
            "FrameNameProperty" => "Flamegraph display-name field; empty uses IDs.",
            "WeightProperty" => "Flamegraph exclusive self-weight field; default Weight.",
            "Foreground" or "Background" => "#RRGGBB; blank inherits the theme.",
            "Columns" => "Property names, one per line.",
            "Items" => "Selection items, one per line.",
            "RefreshInterval" => "Sampling interval in milliseconds, 100–60000.",
            "IncludeProcesses" => "Load account-owned processes, CPU and memory; L toggles at runtime. False collects only login sessions.",
            "Width" or "Height" => "Cell count, 1–1000; blank sizes automatically.",
            "Close" => "Close the TUI after this button's callback.",
            "Value" => "Progress from zero to one.",
            "ShowAxes" => "Display supported chart axes and tick labels; defaults to false.",
            "Language" => "TextMate language id (rust) or extension (.rs); blank is Markdown.",
            "Path" => "File or directory path; relative paths resolve from the definition directory.",
            "SecondaryPath" => "Right pane directory; blank uses Path. Relative paths resolve from the definition directory.",
            "Target" => "Existing control ID used as a popup anchor, toast host or log search target.",
            "Options" => "Native properties as a JSON object. Inspect NativeProperties for accepted names and types.",
            "EnumType" => "Native enum type (or DayOfWeek/ConsoleColor) used by EnumSelect.",
            _ => name
        };
        string group = name is "Foreground" or "Background" or "Bold" or "HorizontalAlignment" or "VerticalAlignment" ? "Style" : name is "Width" or "Height" or "Spacing" ? "Layout" : "Control";
        return new TuiPropertyInfo(
            name,
            Nullable.GetUnderlyingType(property.PropertyType)is { } underlying ? underlying.Name +
            "?" : property.PropertyType.Name,
            property.GetValue(Defaults),
            choices,
            group,
            description
        );
    })];
    /// <summary>Gets the available entries.</summary>
    public static IEnumerable<TuiControlInfo> All => TuiDocument.ControlTypes.Select(Describe);

    /// <summary>Looks up the catalog description of a supported control.</summary>
    /// <param name="name">The supported control catalog name.</param>
    /// <returns>The control description.</returns>
    public static TuiControlInfo Describe(string name) {
        if (!TuiDocument.ControlTypes.Contains(name, StringComparer.Ordinal)) {
            throw new ArgumentException($"Unknown TUI control '{name}'.");
        }

        string[] common = [
            "Id",
            "Type",
            "TabTitle",
            "Text",
            "Width",
            "Height",
            "Enabled",
            "Foreground",
            "Background",
            "Bold",
            "HorizontalAlignment",
            "VerticalAlignment"
        ];
        var (category, description, reusedBy, binding, events, properties, live) = name switch {
            "FTable" => ("Data", "Filterable, sortable object table with native selection.", "fobject, fgit, fgh, fado", "Bind: object collection; SelectionBind: original selected object", [
                "Selection",
                "Submit"
            ], [
                "Bind",
                "Columns",
                "SelectionBind",
                "Minimal"
            ], false),
            "FLogView" => ("Data", "Severity-colored parsed log snapshot; accepts plain or JSON log lines.", "flog", "Bind: collection of log lines; SelectionBind: original selected line", [
                "Selection",
                "Submit"
            ], [
                "Bind",
                "Items",
                "SelectionBind",
                "Minimal"
            ], false),
            "FileBrowser" => ("Application", "File browser with rich previews, tabs and file operations.", "ffile", "SelectionBind: selected file path", [
                "Selection",
                "Close"
            ], [
                "Path",
                "SelectionBind",
                "Minimal"
            ], true),
            "FileCommander" => ("Application", "Dual-pane file commander with previews and file operations.", "fmc", "SelectionBind: selected file path in the active pane", [
                "Selection",
                "Close"
            ], [
                "Path",
                "SecondaryPath",
                "SelectionBind"
            ], true),
            "ObjectExplorer" => ("Data", "Deep object inspection with back navigation, formatted/expanded/JSON views and search.", "fobject", "Bind: object collection; SelectionBind: selected PowerShell object", ["Selection"], [
                "Bind",
                "Columns",
                "SelectionBind",
                "Minimal"
            ], false),
            "PerformanceMonitor" => ("Application", "Local monitor with Sixel charts and native fallback.", "ftop", "None; owns its local collector", ["Close"], [
                "View",
                "RefreshInterval",
                "Minimal",
                "GraphKind",
                "IncludeProcesses"
            ], true),
            "UserList" => ("Data", "Login sessions with idle/logon times, client details and optional expandable account processes.", "ftop Users", "SelectionBind: selected UserViewRow, including typed Logins and optional Process", [
                "Selection",
                "Close"
            ], [
                "SelectionBind",
                "RefreshInterval",
                "Minimal",
                "IncludeProcesses"
            ], true),
            "Chart" => ("Visualization", "Numeric, hierarchical, interval or relationship chart with Sixel and terminal-cell rendering; numeric scalar bindings use a history adapter.", "ftop, Out-Chart", "Bind: numeric collection, frames, tasks or nodes; EdgeBind: separate diagram edges; SelectionBind receives the original selected record", ["Selection", "Invoke"], [
                "Bind",
                "EdgeBind",
                "XProperty",
                "YProperty",
                "LowerProperty",
                "UpperProperty",
                "SeriesRole",
                "Minimum",
                "Maximum",
                "GraphKind",
                "ShowLegend",
                "ShowAxes",
                "ReversalAmount",
                "FrameIdProperty",
                "ParentIdProperty",
                "FrameNameProperty",
                "WeightProperty",
                "FlameReverse",
                "FlameFrameRows",
                "StartProperty",
                "EndProperty",
                "TaskNameProperty",
                "ProgressProperty",
                "SelectionBind"
            ], false),
            "Code" => ("Display", "Optional TextMate syntax highlighting; Markdown unless Language is set. Plain text without the module.", "ffile, fmc, fobject", "Bind: source text", [], [
                "Bind",
                "Language"
            ], false),
            "Image" => ("Display", "Optional Sixel module image; needs a Sixel terminal. Shows an install notice otherwise.", "ffile", "Bind: image path", [], [
                "Bind",
                "Path"
            ], false),
            "Spectre" => ("Display", "Optional Spectre IRenderable adapter; uses the caller's Spectre assembly.", "Custom TUIs, TextMate", "Bind: runtime IRenderable object", [], ["Bind"], false),
            "VStack" or "HStack" => ("Layout", "Composable layout container.", "All TUIs", "None", [], [
                "Children",
                "Spacing"
            ], false),
            "Border" or "Tabs" => ("Layout", "Composable layout container.", "All TUIs", "None", [], ["Children"], false),
            "Button" => ("Input", "Button with queued PowerShell click callback.", "All TUIs", "None", ["Click"], ["Close"], false),
            "Text" => ("Display", "Bound or static text label.", "All TUIs", "Bind: display value", [], ["Bind"], false),
            "Progress" => ("Visualization", "Bound progress indicator.", "All TUIs", "Bind: number from 0 to 1", [], [
                "Bind",
                "Value"
            ], false),
            "Rule" => ("Display", "Horizontal separator.", "All TUIs", "None", [], [], false),
            "CheckBox" => ("Input", "Bound boolean input.", "All TUIs", "Bind: boolean", ["Change"], [
                "Bind",
                "Checked"
            ], false),
            "Select" => ("Input", "Bound string selection.", "All TUIs", "Bind: selected string", ["Change"], [
                "Bind",
                "Items"
            ], false),
            _ when NativeTuiControls.Contains(name) => (NativeTuiControls.Category(name), "Native XenoAtom " + name +
                " widget.", "XenoAtom.Terminal.UI", "Bind: native value or data; Options: native properties", NativeTuiControls.Events(name), NativeTuiControls.Properties(name), false),
            _ => ("Input", "Native editable text input.", "All TUIs", "Bind: editable string", ["Change"], ["Bind"], false)
        };
        if (!NativeTuiControls.IsContainer(name) && NativeTuiControls.Contains(name)) {
            properties = [.. properties.Where(property => property != "Children")];
        }

        return new(
            name,
            category,
            description,
            reusedBy,
            binding,
            events,
            [
                ..common,
                ..properties,
                ..NativeTuiControls.Contains(name) ? NativeOptionProperties : [],
                ..name == "EnumSelect" ? EnumTypeProperties : []
            ],
            live
        ) {
            NativeProperties = NativeTuiControls.Contains(name) ? [..NativeTuiControls.ConfigurableProperties(name).Select(property => new TuiPropertyInfo(
                property.Name,
                property.PropertyType.Name,
                null,
                property.PropertyType.IsEnum ? Enum.GetNames(property.PropertyType) : [],
                "Native",
                "Set with Options."
            ))] : []
        };
    }

    /// <summary>Validates the option names and values of a control node.</summary>
    /// <param name="node">The control definition being validated.</param>
    public static void ValidateOptions(TuiNode node) {
        var allowed = Describe(node.Type).Properties;
        foreach (var property in NodeProperties) {
            if (property.Name == "Children" || allowed.Contains(property.Name, StringComparer.Ordinal)) {
                continue;
            }

            object? value = property.GetValue(node), expected = property.GetValue(Defaults);
            bool same = value is System.Collections.IDictionary dictionary && dictionary.Count == 0
                || (value is string[] array && expected is string[] empty ? array.SequenceEqual(empty) : Equals(value, expected));
            if (!same) {
                throw new ArgumentException($"Control '{node.Id}' ({node.Type}) does not support '{property.Name}'.");
            }
        }
    }

    internal static void ResetUnsupported(TuiNode node) {
        var allowed = Describe(node.Type).Properties;
        foreach (var property in NodeProperties) {
            if (property.Name != "Children" && !allowed.Contains(property.Name, StringComparer.Ordinal)) {
                property.SetValue(node, property.GetValue(Defaults));
            }
        }
    }
}
