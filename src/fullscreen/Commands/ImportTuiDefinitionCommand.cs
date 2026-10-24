using System.Management.Automation;
using fullscreen.Custom;

namespace fullscreen.Commands;
/// <summary>Load and validate a saved TUI definition as native PowerShell data.</summary>
[Cmdlet(VerbsData.Import, "TuiDefinition")]
[OutputType(typeof(PSObject))]
public sealed class ImportTuiDefinitionCommand : TuiDefinitionCommand {
    /// <summary>Literal local FileSystem path. LiteralPath and FullName are aliases. Accepts pipeline input
    /// by property name, including FileInfo.FullName.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("LiteralPath", "FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = "";

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        try {
            WriteObject(TuiDocument.Load(ResolvePath(Path)).ToPowerShell(), false);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            InvalidDefinition(error, Path);
        }
    }
}
