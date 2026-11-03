using System.Collections;
using System.Management.Automation;
using fullscreen.Custom;

namespace fullscreen.Commands;
/// <summary>Export a TUI application scaffold or a layout with a companion script.</summary>
[Cmdlet(VerbsData.Export, "TuiDefinition", SupportsShouldProcess = true)]
[OutputType(typeof(PSObject))]
public sealed class ExportTuiDefinitionCommand : TuiDefinitionCommand {
    /// <summary>One version-1 layout to validate and save. Accepts pipeline input.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public PSObject Definition { get; set; } = null!;

    /// <summary>Literal .ps1, .psd1 or .json output path; LiteralPath is an alias. Scripts are never
    /// overwritten. Replacing layout data requires Force.</summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipelineByPropertyName = true)]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = "";

    /// <summary>Return the validated layout as a plain PowerShell object after saving. No output when
    /// WhatIf skips the write.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Retained for compatibility; scaffolding is now created by default. Use Script:$false
    /// with .psd1 or .json to export only layout data.</summary>
    [Parameter]
    public SwitchParameter Script { get; set; }

    /// <summary>For .ps1, load a sibling .tui.psd1 instead of embedding the layout. Data-file exports
    /// already use a companion script. Existing linked scripts are preserved byte for byte.</summary>
    [Parameter]
    public SwitchParameter LinkedLayout { get; set; }

    /// <summary>Allow replacement of existing layout data. Never overwrites application scripts and
    /// never bypasses WhatIf or Confirm.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        try {
            var document = TuiDocument.FromObject(Definition);
            string target = ResolvePath(Path);
            bool createScript = !MyInvocation.BoundParameters.ContainsKey(nameof(Script)) || Script.IsPresent;
            var (script, layout) = TuiScriptExporter.Paths(target, LinkedLayout.IsPresent);
            if (!createScript && (layout is null || LinkedLayout.IsPresent)) {
                throw new ArgumentException("Script:$false requires a .psd1 or .json path without LinkedLayout.");
            }
            string outputs = layout is null ? script : layout +
                (createScript && !File.Exists(script) ? " and new companion script " + script : "");
            if (ShouldProcess(
                outputs,
                layout is null ? "Create commented PowerShell TUI scaffold" :
                    "Save TUI definition (existing scripts are preserved)"
            )) {
                TuiScriptExporter.Export(document, target, LinkedLayout.IsPresent, Force.IsPresent, createScript);
                if (createScript && layout is not null && File.Exists(script)) {
                    WriteVerbose($"Scaffold '{script}' is available; existing scripts are preserved. Edit its Handler and OnTick blocks to add behavior.");
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
