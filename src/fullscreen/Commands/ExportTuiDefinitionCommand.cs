using System.Collections;
using System.Management.Automation;
using fullscreen.Custom;
using IOPath = System.IO.Path;

namespace fullscreen.Commands;
/// <summary>Validate and save a native PowerShell TUI definition.</summary>
[Cmdlet(VerbsData.Export, "TuiDefinition", SupportsShouldProcess = true)]
[OutputType(typeof(PSObject))]
public sealed class ExportTuiDefinitionCommand : TuiDefinitionCommand {
    /// <summary>One version-1 layout to validate and save. Accepts pipeline input.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public PSObject Definition { get; set; } = null!;

    /// <summary>Literal output FileSystem path; LiteralPath is an alias. Existing files are replaced
    /// atomically after ShouldProcess approves the write.</summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipelineByPropertyName = true)]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = "";

    /// <summary>Return the validated layout as a plain PowerShell object after saving. No output when
    /// WhatIf skips the write.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Export runnable, commented PowerShell scaffolding. Without LinkedLayout, the script embeds
    /// a native hashtable and needs no external layout file. With LinkedLayout, it loads a sibling
    /// .tui.psd1 and later exports preserve custom handlers.</summary>
    [Parameter]
    public SwitchParameter Script { get; set; }

    /// <summary>With Script, export a runnable .ps1 that loads a sibling .tui.psd1 layout. Later exports
    /// update the layout and preserve the linked script byte for byte. An unrelated existing script is
    /// rejected. Without LinkedLayout, Script embeds the layout inline. All individual file writes use
    /// temporary-file replacement.</summary>
    [Parameter]
    public SwitchParameter LinkedLayout { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        try {
            var document = TuiDocument.FromObject(Definition);
            string target = ResolvePath(Path);
            if (LinkedLayout.IsPresent && !Script.IsPresent) {
                throw new ArgumentException("LinkedLayout requires Script.");
            }

            string outputs = LinkedLayout.IsPresent ? target +
                " and " + IOPath.ChangeExtension(target, ".tui.psd1") : target;
            if (ShouldProcess(
                outputs,
                Script.IsPresent ? "Export commented PowerShell TUI scaffold" : "Save TUI definition"
            )) {
                if (Script.IsPresent) {
                    TuiScriptExporter.Export(document, target, LinkedLayout.IsPresent, overwriteLayout: true);
                }
                else {
                    document.Save(target);
                }

                if (PassThru.IsPresent) {
                    WriteObject(document.ToPowerShell(), false);
                }
            }
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            InvalidDefinition(error, Path);
        }
    }
}
