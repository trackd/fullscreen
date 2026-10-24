namespace fullscreen.Custom;

internal static partial class NativeTuiControls {
    internal static bool SupportsInput(string name) =>
        name is "TextBox" or "TextArea" or "Select" or "CheckBox" or "MaskedInput" or "PromptEditor"
        or "NumberBox"
        or "Slider"
        or "Switch"
        or "RadioButton"
        or "ColorPicker"
        or "EnumSelect"
        or "HScrollBar"
        or "VScrollBar"
        or "TabControl"
        or "ContentSwitcher"
        or "Collapsible";
    private static bool SupportsData(string name) =>
        name is "Table" or "DataGridControl" or "BarChart" or "BreakdownChart" or "ProgressTaskGroup"
        or "LineChart"
        or "Sparkline"
        or "LogControl"
        or "DocumentFlow"
        or "TreeView"
        or "Canvas"
        or "DataPresenter"
        or "ComputedVisual"
        or "ListBox"
        or "OptionList"
        or "RadioButtonList"
        or "SelectionList";
    private static bool SupportsOverlay(string name) =>
        name is "Dialog" or "Popup" or "CommandPalette" or "Toast" or "SearchReplacePopup";
    private static bool SupportsSelection(string name) =>
        name is "ListBox" or "OptionList" or "RadioButtonList" or "SelectionList" or "TreeView"
        or "DataGridControl";
    internal static string Category(string name) {
        if (SupportsOverlay(name)) {
            return "Native overlay";
        }
        if (IsContainer(name)) {
            return "Native layout";
        }
        if (SupportsInput(name)) {
            return "Native input";
        }
        if (name is "BarChart" or "BreakdownChart" or "LineChart" or "Sparkline"
            or "ProgressBar" or "ProgressTaskGroup" or "Spinner") {
            return "Native visualization";
        }
        if (SupportsData(name)) {
            return "Native data";
        }
        if (name is "MenuBar" or "CommandBar") {
            return "Native commands";
        }
        return "Native display";
    }
    internal static string[] Properties(string name) {
        var result = new List<string>();
        if (SupportsInput(name) || SupportsData(name) || SupportsOverlay(name)
            || name is "TextBlock" or "Markup" or "Paragraph" or "TextFiglet" or "Placeholder" or "Link"
            or "ProgressBar") {
            result.Add("Bind");
        }

        if (SupportsData(name) || name is "MenuBar" or "CommandBar" or "CommandPalette") {
            result.Add("Items");
        }

        if (SupportsSelection(name)) {
            result.Add("SelectionBind");
        }

        if (name is "Table" or "DataGridControl") {
            result.Add("Columns");
        }

        if (name is "Popup" or "Toast" or "SearchReplacePopup") {
            result.Add("Target");
        }

        if (IsContainer(name)) {
            result.Add("Children");
        }

        return [.. result];
    }

    internal static string[] Events(string name) {
        var result = new List<string>();
        if (SupportsInput(name)) {
            result.Add("Change");
        }

        if (SupportsSelection(name)) {
            result.Add("Selection");
        }

        if (name == "DataGridControl") {
            result.Add("Edit");
        }

        if (typeof(XenoAtom.Terminal.UI.Controls.OptionList<string>).IsAssignableFrom(TypeOf(name))
            || name == "PromptEditor") {
            result.Add("Submit");
        }

        if (name is "MenuBar" or "CommandBar" or "CommandPalette") {
            result.Add("Click");
        }

        if (SupportsOverlay(name)) {
            result.AddRange(["Open", "Close"]);
        }

        return [.. result];
    }
}
