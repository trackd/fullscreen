using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace fullscreen.Themes;
/// <summary>Completes the names of built-in fullscreen themes.</summary>
public sealed class ThemeArgumentCompleter : IArgumentCompleter {
    /// <inheritdoc/>
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters
    ) {
        string prefix = wordToComplete.Trim('\'', '"');
        return ThemeCatalog.Names.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => new CompletionResult(
            "'" + x.Replace("'", "''", StringComparison.Ordinal) +
            "'",
            x,
            CompletionResultType.ParameterValue,
            "Fullscreen theme: " + x
        ));
    }
}
