using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace fullscreen.Charts;
/// <summary>A literal property name or a Select-Object-style calculated property.</summary>
internal sealed record ObjectChartProperty(string Name, object? Expression = null) {
    internal static ObjectChartProperty Parse(object value) {
        if (value is PSObject wrapper) {
            value = wrapper.BaseObject;
        }

        if (value is string property && !string.IsNullOrWhiteSpace(property)) {
            return new(property);
        }

        if (value is not IDictionary dictionary) {
            throw new ArgumentException("Use a property name or @{ Name = 'Header'; Expression = { $_.Property } }.");
        }

        string? name = null;
        object? expression = null;
        bool hasName = false, hasExpression = false;
        foreach (DictionaryEntry entry in dictionary) {
            string key = entry.Key as string ?? throw new ArgumentException("Calculated-property keys must be strings.");
            object? item = entry.Value is PSObject ps ? ps.BaseObject : entry.Value;
            switch (key.ToLowerInvariant()) {
                case "name":
                case "label":
                case "n":
                case "l":
                    if (hasName || item is not string text) {
                        throw new ArgumentException("Specify one string Name or Label for the calculated property.");
                    }

                    hasName = true;
                    name = text;
                    break;
                case "expression":
                case "e":
                    if (hasExpression) {
                        throw new ArgumentException("Specify Expression only once.");
                    }

                    hasExpression = true;
                    expression = item;
                    break;
                default:
                    throw new ArgumentException($"Unknown calculated-property key '{key}'. Use Name/Label and Expression.");
            }
        }

        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) {
            throw new ArgumentException("The calculated-property header must be a nonempty single line.");
        }

        if (expression is not ScriptBlock && (expression is not string source || string.IsNullOrWhiteSpace(source))) {
            throw new ArgumentException("Expression must be a ScriptBlock or a literal source-property name.");
        }

        return new(name.Trim(), expression);
    }

    internal static ScriptBlock ParseBody(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            throw new ArgumentException("Enter a PowerShell expression using $_ or $PSItem.");
        }

        var script = ScriptBlock.Create(text);
        // Accept either a script body or a pasted { script block } in the editor.
        if (script.Ast is ScriptBlockAst { EndBlock.Statements.Count: 1 } ast
            && ast.EndBlock.Statements[0] is PipelineAst pipeline
            && pipeline.PipelineElements.Count == 1
            && pipeline.PipelineElements[0] is CommandExpressionAst { Expression: ScriptBlockExpressionAst expression }) {
            return expression.ScriptBlock.GetScriptBlock();
        }

        return script;
    }

    internal string Body => Expression is ScriptBlock script ? script.ToString() : "$_.'" + ((string?)Expression ?? Name).Replace("'", "''", StringComparison.Ordinal) +
        "'";
}

internal sealed record ObjectChartCalculated(
    string Key,
    ObjectChartProperty Definition,
    object?[] Values,
    int ErrorCount
);
