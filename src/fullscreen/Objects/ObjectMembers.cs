using System.Management.Automation;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using fullscreen.Tables;
using fullscreen.Themes;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Text;

namespace fullscreen.Objects;

internal static partial class ObjectMembers {
    [GeneratedRegex(@"(?:[\p{L}_][\w`]*[.+])*[\p{L}_][\w`]*", RegexOptions.CultureInvariant, 50)]
    private static partial Regex TypeNames();

    internal static string ShortType(string text, StringBuilder? buffer = null) {
        string names = TypeNames().Replace(text, match => {
            string name = match.Value;
            int separator = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
            if (separator >= 0) { name = name[(separator + 1)..]; }
            int arity = name.IndexOf('`');
            if (arity >= 0) { name = name[..arity]; }
            return name switch {
                "Void" => "void",
                "Object" => "object",
                "String" => "string",
                "Boolean" => "bool",
                "Char" => "char",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Int16" => "short",
                "UInt16" => "ushort",
                "Int32" => "int",
                "UInt32" => "uint",
                "Int64" => "long",
                "UInt64" => "ulong",
                "Single" => "float",
                "Double" => "double",
                "Decimal" => "decimal",
                _ => name
            };
        });
        var result = buffer ?? new StringBuilder(names.Length);
        result.Clear();
        var brackets = new Stack<bool>();
        for (int i = 0; i < names.Length; i++) {
            char character = names[i];
            if (character == '[') {
                bool generic = i > 0 && (char.IsLetterOrDigit(names[i - 1]) || names[i - 1] == '_')
                    && i + 1 < names.Length && names[i + 1] is not (']' or ',');
                brackets.Push(generic);
                result.Append(generic ? '<' : '[');
            }
            else if (character == ']' && brackets.TryPop(out bool generic)) {
                result.Append(generic ? '>' : ']');
            }
            else {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    internal static TableModel Create(object value) {
        var model = new TableModel(["Kind", "Type", "Name", "Value / signature"]) {
            ColorByType = true
        };
        var source = PSObject.AsPSObject(value);
        var metadata = new MemberMetadata(source.BaseObject.GetType());
        var formatter = new MemberFormatter();
        foreach (var member in source.Members.OrderBy(member => member is PSMethodInfo)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)) {
            if (model.Rows.Count >= 1000) { break; }
            if (member is PSMethodInfo method) {
                if (method.Name.StartsWith("get_", StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", StringComparison.Ordinal)) { continue; }
                if (method is PSScriptMethod script) {
                    AddSignature(member, formatter.Script(script));
                }
                else if (method is PSCodeMethod code) {
                    AddSignature(member, formatter.Method(code.CodeReference, code.Name, skipTarget: true));
                }
                else if (method is PSParameterizedProperty && metadata.Indexers.TryGetValue(method.Name, out var indexers)) {
                    foreach (var indexer in indexers) {
                        if (model.Rows.Count >= 1000) { break; }
                        AddSignature(member, formatter.Indexer(indexer));
                    }
                }
                else if (method is PSMethod && metadata.Methods.TryGetValue(method.Name, out var methods)) {
                    foreach (var entry in methods) {
                        if (model.Rows.Count >= 1000) { break; }
                        AddSignature(member, formatter.Method(entry.Method, method.Name, entry.Contract));
                    }
                }
                else {
                    // Custom adapters may expose members with no CLR or script metadata.
                    foreach (string signature in method.OverloadDefinitions) {
                        if (model.Rows.Count >= 1000) { break; }
                        Add(member, formatter.TypeName(method.TypeNameOfValue), null, signature);
                    }
                }
            }
            else if (member is PSPropertyInfo property) {
                object? result = null;
                string? error = null;
                string type = "object";
                try {
                    Type? declared = metadata.PropertyType(source, property);
                    if (declared is not null) { type = formatter.TypeName(declared); }
                    if (property.IsGettable) { result = property.Value; }
                    else { error = "<write-only>"; }
                    if (declared is null) {
                        type = result is not null ? formatter.TypeName(result is PSObject wrapped ? wrapped.BaseObject.GetType() : result.GetType())
                            : formatter.TypeName(property.TypeNameOfValue);
                    }
                }
                catch (ExtendedTypeSystemException failure) {
                    error = "<property error: " + TableModel.Text(failure.Message, 256) + ">";
                }
                Add(member, type, result, error);
            }
        }
        model.Rebuild();
        return model;

        void AddSignature(PSMemberInfo member, FormattedMember signature) =>
            Add(member, signature.Type, null, signature.Text, signature.Tokens);

        void Add(PSMemberInfo member, string type, object? result, string? display, Token[]? tokens = null) {
            int id = model.Rows.Count;
            string text = display is null ? result is Type typeValue ? formatter.TypeName(typeValue)
                : model.DisplayCell(id, 3, result) : TableModel.Text(display);
            model.Rows.Add(new(id, tokens is null ? member : new Signature(member, tokens),
                [member.MemberType.ToString(), TableModel.Text(type), TableModel.Text(member.Name), text],
                [member.MemberType, type, member.Name, result], text));
        }
    }

    internal static void Style(TableModel model, ThemePalette palette) {
        foreach (var row in model.Rows) {
            bool method = row.Original is PSMethodInfo || row.Original is Signature { Member: PSMethodInfo };
            model.CellStyles[(row.Id, 0)] = [new(0, row.Cells[0].Length, Style(palette.Keyword))];
            model.CellStyles[(row.Id, 1)] = [new(0, row.Cells[1].Length, Style(palette.Type))];
            model.CellStyles[(row.Id, 2)] = [new(0, row.Cells[2].Length, Style(method ? palette.Function : palette.Property))];
            if (row.Original is Signature signature) {
                var runs = new StyledRun[signature.Tokens.Length + 1];
                runs[0] = new(0, row.Cells[3].Length, Style(palette.Punctuation));
                for (int i = 0; i < signature.Tokens.Length; i++) {
                    var token = signature.Tokens[i];
                    var color = token.Kind switch {
                        TokenKind.Keyword => palette.Keyword,
                        TokenKind.Type => palette.Type,
                        TokenKind.Name => method ? palette.Function : palette.Property,
                        TokenKind.Literal => palette.StringLiteral,
                        TokenKind.Number => palette.Number,
                        TokenKind.Parameter => palette.Parameter,
                        TokenKind.Operator => palette.Operator,
                        _ => palette.DefaultToken
                    };
                    runs[i + 1] = new(token.Start, token.Length, Style(color));
                }
                model.CellStyles[(row.Id, 3)] = runs;
            }
            else if (row.Original is PSMethodInfo) {
                model.CellStyles[(row.Id, 3)] = [new(0, row.Cells[3].Length, Style(palette.Foreground))];
            }
        }

        static Style Style(Color color) => default(Style).WithForeground(color);
    }

    internal sealed record Signature(PSMemberInfo Member, Token[] Tokens);
    internal enum TokenKind { Keyword, Type, Name, Parameter, Literal, Number, Operator, Expression }
    internal readonly record struct Token(int Start, int Length, TokenKind Kind);
    private readonly record struct FormattedMember(string Type, string Text, Token[] Tokens);

    private readonly record struct MethodEntry(MethodInfo Method, Type? Contract);
    private readonly record struct IndexerEntry(PropertyInfo Property, Type? Contract, ParameterInfo[] Parameters);

    private sealed class MemberMetadata {
        internal Dictionary<string, List<MethodEntry>> Methods { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, List<IndexerEntry>> Indexers { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Type> _properties = new(StringComparer.OrdinalIgnoreCase);

        internal MemberMetadata(Type type) {
            Add(type, null, BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                _properties.TryAdd(field.Name, field.FieldType);
            }
            foreach (Type contract in type.GetInterfaces()) {
                Add(contract, contract, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            }
        }

        private void Add(Type type, Type? contract, BindingFlags flags) {
            foreach (var method in type.GetMethods(flags)) {
                if (method.IsSpecialName) { continue; }
                if (!Methods.TryGetValue(method.Name, out var entries)) { Methods.Add(method.Name, entries = []); }
                entries.Add(new(method, contract));
            }
            foreach (var property in type.GetProperties(flags)) {
                var parameters = property.GetIndexParameters();
                if (parameters.Length == 0) {
                    if (contract is null) { _properties.TryAdd(property.Name, property.PropertyType); }
                    continue;
                }
                if (!Indexers.TryGetValue(property.Name, out var entries)) { Indexers.Add(property.Name, entries = []); }
                if (!entries.Any(entry => SameParameters(entry.Parameters, parameters))) {
                    entries.Add(new(property, contract, parameters));
                }
            }
        }

        private static bool SameParameters(ParameterInfo[] left, ParameterInfo[] right) {
            if (left.Length != right.Length) { return false; }
            for (int i = 0; i < left.Length; i++) {
                if (left[i].ParameterType != right[i].ParameterType) { return false; }
            }
            return true;
        }

        internal Type? PropertyType(PSObject source, PSPropertyInfo property, int depth = 0) =>
            property switch {
                PSProperty => _properties.GetValueOrDefault(property.Name),
                PSCodeProperty code => code.GetterCodeReference?.ReturnType
                    ?? code.SetterCodeReference?.GetParameters().LastOrDefault()?.ParameterType,
                PSAliasProperty alias when alias.ConversionType is not null => alias.ConversionType,
                PSAliasProperty alias when depth < 16 && source.Properties[alias.ReferencedMemberName] is { } target =>
                    PropertyType(source, target, depth + 1),
                _ => null
            };
    }
}
