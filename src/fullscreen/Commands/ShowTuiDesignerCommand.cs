using System.Management.Automation;
using fullscreen.Custom;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Design a custom fullscreen TUI with a live visual preview.</summary>
[Cmdlet(VerbsCommon.Show, "TuiDesigner")]
[Alias("fdesign")]
[OutputType(typeof(PSObject))]
public sealed class ShowTuiDesignerCommand : CustomTuiCommand {
    /// <summary>Literal local layout file to load and save. If missing, opens a starter document targeted
    /// at this path. Without Path, Save prompts for a filename. LiteralPath is an alias. The parent
    /// directory must exist.</summary>
    [Parameter(Position = 0)]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>One version-1 definition as a hashtable, PSCustomObject or JSON string, directly or from
    /// the pipeline. With Path, this definition takes precedence and Path is the save target.</summary>
    [Parameter(ValueFromPipeline = true)]
    public PSObject? Definition { get; set; }

    /// <summary>Return the current valid layout as a plain PowerShell object after the designer closes,
    /// including edits not saved to disk. No XenoAtom or Fullscreen-specific CLR instance is
    /// emitted.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <inheritdoc/>
    protected override void EndProcessing() =>
        RunCustom(stop => {
            var theme = ResolveTheme();
            string? path = Path is null ? null : FilePath(Path);
            var document = Definition is not null ? TuiDocument.FromObject(Definition) : path is not null && File.Exists(path) ? TuiDocument.Load(path) : TuiDocument.Starter();
            using var app = new TuiDesignerApp(document, (
                layout,
                target
            ) => layout.Save(FilePath(target)), path, target => {
                try {
                    return File.Exists(FilePath(target));
                }
                catch (ArgumentException) {
                    return false;
                }
            }, (
                layout,
                target,
                linked
            ) => TuiScriptExporter.Export(layout, FilePath(target), linked, overwriteLayout: true), preferencesPath: TuiDesignerPreferences.DefaultPath);
            FullscreenHost.Run(app, stop, theme, Minimal.IsPresent);
            if (PassThru.IsPresent) {
                WriteObject(app.Document.ToPowerShell(), false);
            }
        }, "FullscreenTuiDesignerFailed");
}
