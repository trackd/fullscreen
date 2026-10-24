using System.Collections;
using System.Management.Automation;
using fullscreen.Custom;

namespace fullscreen.Commands;
/// <summary>Create or validate a TUI definition as native PowerShell data.</summary>
[Cmdlet(VerbsCommon.New, "TuiDefinition", DefaultParameterSetName = "Parts")]
[OutputType(typeof(PSObject))]
public sealed class NewTuiDefinitionCommand : TuiDefinitionCommand {
    /// <summary>Application title, up to 512 characters. Defaults to My application.</summary>
    [Parameter(Position = 0, ParameterSetName = "Parts")]
    [ValidateLength(1, 512)]
    [ValidateNotNullOrEmpty]
    public string Title { get; set; } = "My application";

    /// <summary>Root control as a hashtable or PSCustomObject. Omit to use the starter form.</summary>
    [Parameter(ParameterSetName = "Parts")]
    [ValidateNotNull]
    public PSObject? Root { get; set; }

    /// <summary>Serializable default state as a hashtable. Supply live values separately to
    /// Invoke-Tui.</summary>
    [Parameter(ParameterSetName = "Parts")]
    public Hashtable? State { get; set; }

    /// <summary>Existing version-1 definition as a hashtable, PSCustomObject or JSON string. Accepts
    /// pipeline input and returns a validated copy.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Definition")]
    [ValidateNotNull]
    public PSObject? Definition { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        try {
            var document = Definition is not null ? TuiDocument.FromObject(Definition) : TuiDocument.FromObject(new Hashtable {
                ["Version"] = 1,
                ["Title"] = Title,
                ["State"] = State ?? [],
                ["Root"] = (object?)Root ?? TuiDocument.Starter().ToPowerShell().Properties["Root"].Value
            });
            document.Validate();
            WriteObject(document.ToPowerShell(), false);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            InvalidDefinition(error, Definition ?? Root);
        }
    }
}
