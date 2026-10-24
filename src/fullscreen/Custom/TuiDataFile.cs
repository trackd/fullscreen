using System.Management.Automation.Language;
using System.Text;
using System.Text.Json;

namespace fullscreen.Custom;
/// <summary>Portable PowerShell literal data. Parsing never invokes a script or a runspace.</summary>
public static class TuiDataFile {
    private static readonly JsonElement NodeDefaults = JsonSerializer.SerializeToElement(new TuiNode(), TuiDocument.JsonOptions);
    /// <summary>Parses a PowerShell data-file layout without executing arbitrary script code.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <returns>The parsed TUI document.</returns>
    public static TuiDocument Parse(string text) {
        if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024) {
            throw new ArgumentException("TUI definitions are limited to 1 MiB.");
        }

        var ast = Parser.ParseInput(text, out _, out var errors);
        if (errors.Length != 0) {
            throw new ArgumentException("Invalid PowerShell data file: " + errors[0].Message);
        }

        if (ast.UsingStatements.Count != 0 || ast.ParamBlock is not null || ast.BeginBlock is not null
            || ast.ProcessBlock is not null
            || ast.CleanBlock is not null
            || ast.DynamicParamBlock is not null
            || ast.EndBlock is not { Statements.Count: 1 } end
            || end.Traps is { Count: > 0 }
            || end.Statements[0] is not PipelineAst { PipelineElements.Count: 1, Background: false } pipeline
            || pipeline.PipelineElements[0] is not CommandExpressionAst { Expression: HashtableAst table, Redirections.Count: 0 }) {
            throw new ArgumentException("A TUI PowerShell data file must contain one literal hashtable. Put executable behavior in a .ps1 script and pass the resulting object to Invoke-Tui.");
        }

        // Large layouts can exceed PowerShell's default 500-key/5000-AST-node limits.
        // Retain literal-only evaluation with our bounded file, AST and document limits.
        if (table.FindAll(_ => true, searchNestedScriptBlocks: true).Take(100001).Count() > 100000) {
            throw new ArgumentException("TUI data files allow at most 100000 syntax nodes.");
        }

        return TuiDocument.FromObject(table.SafeGetValue(skipHashtableSizeCheck: true));
    }

    /// <summary>Serializes a TUI document as PowerShell data-file text.</summary>
    /// <param name="document">The layout document to validate, render, or serialize.</param>
    /// <param name="compact">Whether to omit optional layout whitespace. Defaults to false.</param>
    /// <returns>The generated data-file text.</returns>
    public static string Generate(TuiDocument document, bool compact = false) {
        using var json = JsonDocument.Parse(document.ToJson());
        var output = new StringBuilder();
        Write(json.RootElement, 0);
        return output.AppendLine().ToString();
        void Indent(int depth) =>
            output.Append(' ', depth * 4);
        void String(string value) =>
            output.Append('\'').Append(CodeGeneration.EscapeSingleQuotedStringContent(value)).Append('\'');
        void Write(JsonElement value, int depth, bool node = false, bool children = false) {
            switch (value.ValueKind) {
                case JsonValueKind.Object:
                    output.AppendLine("@{");
                    foreach (var property in value.EnumerateObject()) {
                        if (compact && node && property.Name is not ("Id" or "Type")
                            && NodeDefaults.TryGetProperty(property.Name, out var defaultValue)
                            && JsonElement.DeepEquals(property.Value, defaultValue)) {
                            continue;
                        }

                        Indent(depth + 1);
                        String(property.Name);
                        output.Append(" = ");
                        Write(
                            property.Value,
                            depth + 1,
                            depth == 0 && property.Name == "Root",
                            node && property.Name == "Children"
                        );
                        output.AppendLine();
                    }

                    Indent(depth);
                    output.Append('}');
                    break;
                case JsonValueKind.Array:
                    if (value.GetArrayLength() == 0) {
                        output.Append("@()");
                        break;
                    }

                    output.AppendLine("@(");
                    foreach (var item in value.EnumerateArray()) {
                        // A unary comma preserves nested and singleton arrays in array subexpressions.
                        Indent(depth + 1);
                        output.Append(',');
                        Write(item, depth + 1, children);
                        output.AppendLine();
                    }

                    Indent(depth);
                    output.Append(')');
                    break;
                case JsonValueKind.String:
                    String(value.GetString()!);
                    break;
                case JsonValueKind.Number:
                    output.Append(value.GetRawText());
                    break;
                case JsonValueKind.True:
                    output.Append("$true");
                    break;
                case JsonValueKind.False:
                    output.Append("$false");
                    break;
                case JsonValueKind.Null:
                    output.Append("$null");
                    break;
                default:
                    throw new ArgumentException("Unsupported PowerShell data-file value.");
            }
        }
    }
}
