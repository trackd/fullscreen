using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using fullscreen.Custom;

namespace fullscreen.Commands;
/// <summary>Create a validated native control definition for composition in custom TUIs.</summary>
[Cmdlet(VerbsCommon.New, "TuiControl")]
[OutputType(typeof(PSObject))]
public sealed class NewTuiControlCommand : TuiDefinitionCommand {
    /// <summary>A supported control type. Validated names are case-insensitive and tab-complete from the
    /// current catalog.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateSet(typeof(TuiControlTypeValues))]
    public string Type { get; set; } = "Text";

    /// <summary>Unique control ID: 1–64 characters, beginning with a letter or underscore. IDs must be
    /// unique in the composed document.</summary>
    [Parameter(Mandatory = true)]
    [ValidatePattern("^[A-Za-z_][A-Za-z0-9_.-]{0,63}$")]
    public string Id { get; set; } = "";

    /// <summary>Display text, title, initial input text or Markdown. Defaults to an empty string.</summary>
    [Parameter]
    [ValidateLength(0, 65536)]
    public string Text { get; set; } = "";

    /// <summary>State key for the control value. FTable and FLogView read a collection; text controls read
    /// strings; Chart reads a numeric collection. Scalar Chart bindings use the timestamped-history
    /// adapter. Defaults to no binding.</summary>
    [Parameter]
    [ValidateLength(0, 128)]
    public string Bind { get; set; } = "";

    /// <summary>Child definitions for a VStack, HStack, Border or Tabs. Children is an alias. Noncontainers
    /// reject children; Border accepts at most one. Set TabTitle in a child&apos;s Property hashtable to
    /// give its tab a readable caption without changing the child&apos;s event ID or content
    /// Text.</summary>
    [Parameter]
    [Alias("Children")]
    public PSObject[] Child { get; set; } = [];

    /// <summary>Hashtable of control-specific schema properties, for example Columns, SelectionBind, Path,
    /// View, RefreshInterval, Height, Width, Minimal, Maximum or TabTitle. TabTitle accepts up to 128
    /// characters and is used when the control is a tab page; blank uses its Id. Unknown keys and invalid
    /// values are rejected.</summary>
    [Parameter]
    public Hashtable? Property { get; set; }

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        try {
            string canonical = TuiDocument.ControlTypes.First(name => name.Equals(Type, StringComparison.OrdinalIgnoreCase));
            var node = new Hashtable(StringComparer.OrdinalIgnoreCase) {
                ["Id"] = Id,
                ["Type"] = canonical,
                ["Text"] = Text,
                ["Bind"] = Bind,
                ["Children"] = Child
            };
            if (Property is not null) {
                foreach (DictionaryEntry pair in Property) {
                    if (pair.Key is not string key || node.ContainsKey(key)) {
                        throw new ArgumentException("Property keys must be control-specific options; use the named parameters for Id, Type, Text, Bind and Children.");
                    }

                    if (!TuiControlCatalog.Describe(canonical).Properties.Contains(key, StringComparer.OrdinalIgnoreCase)) {
                        throw new ArgumentException($"Control '{Id}' ({canonical}) does not support '{key}'.");
                    }

                    node.Add(key, pair.Value);
                }
            }

            var document = TuiDocument.FromControl(new Hashtable { ["Root"] = node });
            var properties = (IDictionary)document.ToPowerShell().Properties["Root"].Value;
            var result = new PSObject();
            foreach (DictionaryEntry pair in properties) {
                result.Properties.Add(new PSNoteProperty((string)pair.Key, pair.Value));
            }

            WriteObject(result, false);
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            InvalidDefinition(error, Id);
        }
    }
}

/// <summary>Supplies the available control names for PowerShell parameter validation.</summary>
public sealed class TuiControlTypeValues : IValidateSetValuesGenerator {
    /// <inheritdoc/>
    public string[] GetValidValues() =>
        (string[])TuiDocument.ControlTypes.Clone();
}

/// <summary>Completes supported control names without creating a TUI.</summary>
public sealed class TuiControlArgumentCompleter : IArgumentCompleter {
    /// <inheritdoc/>
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters
    ) {
        string prefix = wordToComplete.Trim('\'', '"');
        foreach (string name in TuiDocument.ControlTypes) {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                yield return new CompletionResult(
                    name,
                    name,
                    CompletionResultType.ParameterValue,
                    TuiControlCatalog.Describe(name).Description
                );
            }
        }
    }
}
