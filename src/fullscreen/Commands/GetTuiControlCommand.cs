using System.Management.Automation;
using fullscreen.Custom;

namespace fullscreen.Commands;
/// <summary>Discover reusable fullscreen controls as native PowerShell data.</summary>
[Cmdlet(VerbsCommon.Get, "TuiControl")]
[OutputType(typeof(PSObject))]
public sealed class GetTuiControlCommand : PSCmdlet {
    /// <summary>Control names or wildcard patterns. Defaults to all controls. Literal names complete
    /// without I/O.</summary>
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    [ArgumentCompleter(typeof(TuiControlArgumentCompleter))]
    public string[] Name { get; set; } = ["*"];

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        foreach (string name in Name) {
            var pattern = new WildcardPattern(name, WildcardOptions.IgnoreCase);
            bool found = false;
            foreach (var control in TuiControlCatalog.All.OrderBy(control => control.Category).ThenBy(control => control.Name)) {
                if (pattern.IsMatch(control.Name)) {
                    found = true;
                    var result = new PSObject();
                    result.TypeNames.Insert(0, "fullscreen.TuiControl");
                    void Add(
                        string property,
                        object value
                    ) =>
                        result.Properties.Add(new PSNoteProperty(property, value));
                    Add("Name", control.Name);
                    Add("Category", control.Category);
                    Add("Description", control.Description);
                    Add("ReusedBy", control.ReusedBy);
                    Add("Binding", control.Binding);
                    Add("Events", control.Events);
                    Add("Properties", control.Properties);
                    Add("Live", control.Live);
                    Add("NativeProperties", control.NativeProperties.Select(property => {
                        var detail = new PSObject();
                        detail.Properties.Add(new PSNoteProperty("Name", property.Name));
                        detail.Properties.Add(new PSNoteProperty("Type", property.Type));
                        detail.Properties.Add(new PSNoteProperty("Choices", property.Choices));
                        detail.Properties.Add(new PSNoteProperty("Description", property.Description));
                        return detail;
                    }).ToArray());
                    Add("PropertyDetails", TuiControlCatalog.Properties(control.Name).Select(property => {
                        var detail = new PSObject();
                        detail.Properties.Add(new PSNoteProperty("Name", property.Name));
                        detail.Properties.Add(new PSNoteProperty("Type", property.Type));
                        detail.Properties.Add(new PSNoteProperty("DefaultValue", property.DefaultValue));
                        detail.Properties.Add(new PSNoteProperty("Choices", property.Choices));
                        detail.Properties.Add(new PSNoteProperty("Group", property.Group));
                        detail.Properties.Add(new PSNoteProperty("Description", property.Description));
                        return detail;
                    }).ToArray());
                    WriteObject(result, false);
                }
            }

            if (!found && !WildcardPattern.ContainsWildcardCharacters(name)) {
                WriteError(new ErrorRecord(
                    new ArgumentException($"Unknown TUI control '{name}'."),
                    "FullscreenTuiControlNotFound",
                    ErrorCategory.ObjectNotFound,
                    name
                ));
            }
        }
    }
}
