using System.Globalization;
using System.Text;

namespace fullscreen.Custom;
/// <summary>Executable scaffolding is exported separately from the data-only layout contract.</summary>
public static class TuiScriptExporter {
    /// <summary>Writes a runnable PowerShell TUI script and optionally a linked layout file.</summary>
    /// <param name="document">The layout document to validate, render, or serialize.</param>
    /// <param name="path">The filesystem or resource path.</param>
    /// <param name="linkedLayout">Whether to write a separate layout file loaded by the script. Defaults to
    /// false.</param>
    /// <param name="overwriteLayout">Whether an existing linked layout file may be replaced. Defaults to
    /// false.</param>
    public static void Export(
        TuiDocument document,
        string path,
        bool linkedLayout = false,
        bool overwriteLayout = false
    ) {
        string target = Path.GetFullPath(path);
        if (!Path.GetExtension(target).Equals(".ps1", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Export scripts to a .ps1 path.");
        }

        if (!linkedLayout) {
            TuiDocument.WriteAtomic(target, Generate(document));
            return;
        }

        string layout = Path.ChangeExtension(target, ".tui.psd1"), name = Path.GetFileName(layout);
        string marker = "# Fullscreen linked layout: " + System.Management.Automation.Language.CodeGeneration.EscapeSingleQuotedStringContent(name);
        bool exists = File.Exists(target);
        if (exists && !File.ReadLines(target).Contains(marker, StringComparer.Ordinal)) {
            throw new IOException("The existing script is not linked to this layout. Choose a new .ps1 path to preserve its code.");
        }

        if (!exists && File.Exists(layout) && !overwriteLayout) {
            throw new IOException($"Layout '{layout}' already exists. Choose another script path or explicitly approve replacing the layout.");
        }

        string script = Generate(document, name);
        document.Save(layout);
        if (!exists) {
            TuiDocument.WriteAtomic(target, script);
        }
    }

    /// <summary>Generates a runnable PowerShell TUI script.</summary>
    /// <param name="document">The layout document to validate, render, or serialize.</param>
    /// <param name="layoutFile">The optional layout filename loaded by the generated script. Defaults to
    /// null.</param>
    /// <returns>The generated PowerShell source text.</returns>
    public static string Generate(TuiDocument document, string? layoutFile = null) {
        var script = new StringBuilder("""
<#
.SYNOPSIS
Run a custom TUI created with Fullscreen's designer.
.DESCRIPTION
Edit the layout with Show-TuiDesigner; wire behavior in Handler and OnTick below.
.PARAMETER Theme
Fullscreen theme name.
.PARAMETER DefinitionOnly
Return the layout for designer editing without opening a terminal.
.PARAMETER State
Runtime state overriding layout defaults. Live objects belong here.
.PARAMETER Minimal
Hide surrounding application chrome. F4 toggles it while running.
.PARAMETER TickInterval
Milliseconds between callbacks and binding updates, from 16 to 60000.
.EXAMPLE
./MyTui.ps1
.EXAMPLE
./MyTui.ps1 -DefinitionOnly | Show-TuiDesigner
.EXAMPLE
./MyTui.ps1 -Minimal -State @{ message = 'Ready' } -TickInterval 500
.OUTPUTS
The definition with DefinitionOnly; otherwise the accepted handler result.
.NOTES
Requires Fullscreen. The layout contains data; callbacks stay in this script.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [object]$Theme = 'Default',

    [Parameter()]
    [switch]$DefinitionOnly,

    [Parameter()]
    [hashtable]$State = @{},

    [Parameter()]
    [switch]$Minimal,

    [Parameter()]
    [ValidateRange(16, 60000)]
    [int]$TickInterval = 250
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Module fullscreen)) { Import-Module fullscreen -ErrorAction Stop }

# Linked exports load a sibling PSD1. Re-exporting updates that layout and keeps
# this script intact. Inline exports embed the layout below; export future inline
# edits to a new script and copy the definition to preserve custom handlers.
$definition =

""");
        if (layoutFile is null) {
            script.AppendLine(TuiDataFile.Generate(document, compact: true));
        }
        else {
            string name = System.Management.Automation.Language.CodeGeneration.EscapeSingleQuotedStringContent(layoutFile);
            script.Append("    Import-TuiDefinition -LiteralPath (Join-Path $PSScriptRoot '").Append(name).AppendLine("')");
            script.Append("# Fullscreen linked layout: ").AppendLine(name);
        }

        script.AppendLine(layoutFile is null ? "$layout = $definition | New-TuiDefinition" : "$layout = $definition");
        script.AppendLine("if ($DefinitionOnly.IsPresent) { return $layout }");
        script.AppendLine();
        script.AppendLine("# STATE REFERENCE · generated for this layout");
        script.AppendLine("# State keys are application-defined; add your own keys as your tool grows.");
        script.AppendLine("# $definition.State supplies defaults; entries in runtime $state override those defaults.");
        script.AppendLine("# Callbacks receive the merged, shared hashtable. Read or assign $state['key'],");
        script.AppendLine("# or return @{ State = @{ key = value } } to update bindings.");
        foreach (var entry in document.State) {
            script.Append("#   ").Append(StateKey(entry.Key)).Append(" : layout default (").Append(entry.Value.ValueKind.ToString().ToLowerInvariant()).AppendLine(").");
        }

        foreach (var node in Nodes(document.Root)) {
            if (node.Bind.Length > 0) {
                script.Append("#   ").Append(StateKey(node.Bind)).Append(" <- ").Append(node.Id).Append(" (").Append(node.Type).Append("): ").AppendLine(BindingDescription(node));
            }

            if (node.SelectionBind.Length > 0) {
                script.Append("#   ").Append(StateKey(node.SelectionBind)).Append(" <- ").Append(node.Id).Append(" selection: ").AppendLine(node.Type == "FileBrowser" ? "selected file path string or $null." : node.Type == "FLogView" ? "original selected log entry or $null." : "original selected row object or $null.");
            }
        }

        script.AppendLine("""

# State is shared by bindings and callbacks. Keep live objects, jobs and secrets
# here rather than in the serialized layout. Assign command output to variables.
# Input controls initialize missing bound keys from their layout defaults.
# Selection keys may be absent until selection changes; allow $null when reading them.
# Enabled, styling and Close are layout properties, not automatic runtime state keys.
$handler = @{
    # $tuiEvent.Id = control ID; $tuiEvent.Kind = event name; $tuiEvent.Value = event payload.
    # Start: empty ID, $null value; delivered once when the application starts.
    # Change: new bound input value (text, boolean, number or enum name); state is already updated.
    # Click: button activation (Close flag), or a native menu/command label.
    # Selection / Submit: original selected object (or file path); selection can be $null.
    # Open/Close: native overlay opened/closed, or an embedded component closed.
    # Tick is delivered to OnTick below, not this event handler.
    # Input changes update their Bind key before this callback runs.
    # Return nothing, or ONE hashtable: @{ State = @{ key = value }; Close = $true; Value = ... }
        ':Start' = {
            param($tuiEvent, $state)
            # Initialize data here. Example: $state.message = 'Ready'
        }
""");
        foreach (var node in Nodes(document.Root)) {
            foreach (string kind in TuiControlCatalog.Describe(node.Type).Events) {
                script.Append("        '").Append(node.Id).Append(':').Append(kind).AppendLine("' = {");
                script.AppendLine("            param($tuiEvent, $state)");
                script.Append("            # $tuiEvent.Value: ").AppendLine(EventDescription(node, kind));
                script.AppendLine(kind == "Click" ? "            # Handle this button. Example: return @{ Close = $true; Value = $state }" : "            # Inspect $tuiEvent.Value and update bound $state values here.");
                script.AppendLine("        }");
            }
        }

        script.AppendLine("""
}
$onTick = {
    param($tuiEvent, $state)
    # $tuiEvent.Kind = 'Tick'; Id is empty and Value is $null. Uses the same shared state.
    # Optional periodic updates, e.g. $state.load = @(10.0, 50.0) for a bound Chart.
    # Callbacks run on the UI thread: poll background jobs here rather than waiting.
    # Chart collections contain numbers or $null (gaps); scalar bindings collect history.
}
# Use try/finally to stop and dispose any workers YOU create when the TUI closes.
# See examples/BackgroundJob.ps1 in Fullscreen's repository for that pattern.
$result = $layout | Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State $State -Handler $handler -OnTick $onTick -TickInterval $TickInterval
# F12 toggles diagnostics (timings, repaint, focus); F10 exits from any control.
# Result fields: State = final state snapshot; Value = your handler's return value;
# Cancelled = $true after Escape/cancellation, $false after a successful close;
# LastEvent = last processed control ID (may be empty if no control event occurred).
if (-not $result.Cancelled) { $result.Value }
""");
        return script.ToString();
    }

    private static string BindingDescription(TuiNode node) =>
        node.Type switch {
            "TextBox" or "TextArea" => "editable string; user edits update state before Change.",
            "CheckBox" => "boolean ($true / $false); toggles update state before Change.",
            "Select" => "selected string from this control's Items; changes update state before Change.",
            "Chart" => "numeric collection with NaN/null gaps; scalar bindings use timestamped history.",
            "Progress" => "number from 0 to 1 (fraction complete).",
            "NumberBox" or "Slider" => "number; edits update state before Change.",
            "Switch" or "RadioButton" or "Collapsible" => "boolean; changes update state before Change.",
            "ColorPicker" => "color string; changes update state before Change.",
            "EnumSelect" => "enum name from EnumType; changes update state before Change.",
            "TabControl" or "ContentSwitcher" or "HScrollBar" or "VScrollBar" => "integer index/offset; changes update state before Change.",
            "Dialog" or "Popup" or "Toast" or "CommandPalette" or "SearchReplacePopup" => "boolean; set true to open and false to close. Dismissal resets this key.",
            "Table" or "DataGridControl" or "TreeView" or "ListBox" or "OptionList"
                or "RadioButtonList"
                or "SelectionList" => "collection of data items; replace the collection to refresh.",
            "BarChart" or "BreakdownChart" or "ProgressTaskGroup" => "collection of Label/Value records (Maximum optional for tasks).",
            "LineChart" or "Sparkline" => "collection of numeric samples; replace to refresh.",
            "LogControl" or "DocumentFlow" => "collection of text entries; replace to refresh.",
            "FTable" => "collection of row objects; replace the collection to refresh rows.",
            "FLogView" => "collection of plain-text or JSON log lines; replace to refresh.",
            "Spectre" => "runtime Spectre IRenderable or $null; keep live objects out of layout defaults.",
            "Code" => "source text (Markdown unless Language is set); assign state to update the display.",
            "Image" => "image file path; assign state to show another image.",
            _ => "display value converted to text; assign state to update the display."
        };
    private static string EventDescription(TuiNode node, string kind) =>
        kind switch {
            "Click" => node.Type is "MenuBar" or "CommandBar" or "CommandPalette" ? "activated command label string." : "Close flag ($true or $false). Read other controls' values from $state.",
            "Change" => node.Type is "CheckBox" or "Switch" or "RadioButton" or "Collapsible" ? "$true or $false (new value)." : "new typed input value; see the STATE REFERENCE above.",
            "Selection" when node.Type == "UserList" => "UserViewRow containing typed Logins and an optional Process.",
            "Selection" when node.Type == "SelectionList" => "array of checked strings.",
            "Submit" when node.Type == "PromptEditor" => "accepted prompt text.",
            "Edit" => "TuiGridEdit: Original object and Values dictionary after a native grid edit. Apply values explicitly if required.",
            "Selection" or "Submit" => node.Type == "FileBrowser" ? "selected path string or $null." : node.Type == "FLogView" ? "original selected log entry or $null." : "original selected row object or $null.",
            "Close" => node.Type is "FileBrowser" or "FileCommander" ? "selected path string or $null." : "$null (overlay or component closed).",
            _ => "$null."
        };
    private static string StateKey(string key) {
        // Keys may contain line breaks. Keep generated references on one comment line.
        string literal = System.Management.Automation.Language.CodeGeneration.EscapeSingleQuotedStringContent(key);
        var safe = new StringBuilder("$state['");
        foreach (char character in literal) {
            if (char.IsControl(character) || character is '\u2028' or '\u2029') {
                safe.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else {
                safe.Append(character);
            }
        }

        return safe.Append("']").ToString();
    }

    private static IEnumerable<TuiNode> Nodes(TuiNode node) {
        yield return node;
        foreach (var child in node.Children) {
            foreach (var descendant in Nodes(child)) {
                yield return descendant;
            }
        }
    }
}
