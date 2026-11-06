using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;

namespace fullscreen.Objects;

internal static partial class ObjectMembers {
    private sealed class MemberFormatter {
        private readonly StringBuilder _text = new(256);
        private readonly StringBuilder _typeText = new(64);
        private readonly List<Token> _tokens = [];
        private readonly Dictionary<Type, string> _types = [];
        private readonly Dictionary<string, string> _typeNames = new(StringComparer.Ordinal);

        internal string TypeName(string name) {
            if (_typeNames.TryGetValue(name, out string? display)) { return display; }
            Type? type = new PSTypeName(name).Type;
            if (type is null && LanguagePrimitives.ConvertTypeNameToPSTypeName(name) is { Length: > 2 } mapped) {
                type = new PSTypeName(mapped[1..^1]).Type;
            }
            display = type is not null ? TypeName(type) : ShortType(name, _typeText);
            _typeNames.Add(name, display);
            return display;
        }

        internal string TypeName(Type type) {
            if (_types.TryGetValue(type, out string? name)) { return name; }
            _typeText.Clear();
            AppendType(_typeText, type);
            name = _typeText.ToString();
            _types.Add(type, name);
            return name;
        }

        private static void AppendType(StringBuilder text, Type type) {
            if (type.IsByRef) { AppendType(text, type.GetElementType()!); return; }
            if (type.IsPointer) { AppendType(text, type.GetElementType()!); text.Append('*'); return; }
            if (type.IsArray) {
                Type element = type.GetElementType()!;
                while (element.IsArray) { element = element.GetElementType()!; }
                AppendType(text, element);
                for (Type? array = type; array?.IsArray == true; array = array.GetElementType()) {
                    text.Append('[').Append(',', array.GetArrayRank() - 1).Append(']');
                }
                return;
            }
            if (type.IsFunctionPointer) {
                text.Append("delegate*");
                if (type.IsUnmanagedFunctionPointer) {
                    text.Append(" unmanaged");
                    Type[] conventions = type.GetFunctionPointerCallingConventions();
                    if (conventions.Length > 0) {
                        text.Append('[');
                        for (int i = 0; i < conventions.Length; i++) {
                            if (i > 0) { text.Append(", "); }
                            text.Append(conventions[i].Name.AsSpan("CallConv".Length));
                        }
                        text.Append(']');
                    }
                }
                text.Append('<');
                foreach (Type parameter in type.GetFunctionPointerParameterTypes()) {
                    AppendType(text, parameter);
                    text.Append(", ");
                }
                AppendType(text, type.GetFunctionPointerReturnType());
                text.Append('>');
                return;
            }
            if (Nullable.GetUnderlyingType(type) is Type underlying) {
                AppendType(text, underlying);
                text.Append('?');
                return;
            }
            string? alias = type == typeof(void) ? "void" : type == typeof(object) ? "object"
                : type == typeof(nint) ? "nint" : type == typeof(nuint) ? "nuint"
                : type.IsEnum ? null : Type.GetTypeCode(type) switch {
                    TypeCode.Boolean => "bool",
                    TypeCode.Char => "char",
                    TypeCode.String => "string",
                    TypeCode.Byte => "byte",
                    TypeCode.SByte => "sbyte",
                    TypeCode.Int16 => "short",
                    TypeCode.UInt16 => "ushort",
                    TypeCode.Int32 => "int",
                    TypeCode.UInt32 => "uint",
                    TypeCode.Int64 => "long",
                    TypeCode.UInt64 => "ulong",
                    TypeCode.Single => "float",
                    TypeCode.Double => "double",
                    TypeCode.Decimal => "decimal",
                    _ => null
                };
            if (alias is not null) { text.Append(alias); return; }
            if (type.IsGenericParameter) { text.Append(type.Name); return; }
            Type[] arguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
            int position = 0;
            AppendNamedType(text, type, arguments, ref position);
        }

        private static void AppendNamedType(StringBuilder text, Type type, Type[] arguments, ref int position) {
            if (type.DeclaringType is { } declaring) {
                AppendNamedType(text, declaring, arguments, ref position);
                text.Append('.');
            }
            string name = type.Name;
            int arity = name.IndexOf('`');
            text.Append(arity < 0 ? name.AsSpan() : name.AsSpan(0, arity));
            int count = type.IsGenericType ? type.GetGenericArguments().Length - position : 0;
            if (count == 0) { return; }
            text.Append('<');
            for (int i = 0; i < count; i++) {
                if (i > 0) { text.Append(", "); }
                AppendType(text, arguments[position++]);
            }
            text.Append('>');
        }

        internal FormattedMember Method(MethodInfo method, string name, Type? contract = null, bool skipTarget = false) {
            Clear();
            Modifiers(method, contract, skipTarget);
            if (method.ReturnType.IsByRef) {
                Word("ref", TokenKind.Keyword);
                if (method.ReturnParameter.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false)) {
                    Word("readonly", TokenKind.Keyword);
                }
            }
            string type = TypeName(method.ReturnType);
            Word(type, TokenKind.Type);
            Qualifier(contract);
            Write(name, TokenKind.Name);
            if (method.IsGenericMethod) {
                _text.Append('<');
                Type[] arguments = method.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++) {
                    if (i > 0) { _text.Append(", "); }
                    Write(TypeName(arguments[i]), TokenKind.Type);
                }
                _text.Append('>');
            }
            _text.Append('(');
            Parameters(method.GetParameters(), skipTarget ? 1 : 0);
            _text.Append(");");
            return Finish(type);
        }

        internal FormattedMember Indexer(IndexerEntry entry) {
            Clear();
            var property = entry.Property;
            Modifiers(property.GetMethod is { IsPublic: true } getter ? getter : property.SetMethod!, entry.Contract);
            string type = TypeName(property.PropertyType);
            Word(type, TokenKind.Type);
            Qualifier(entry.Contract);
            Write("this", TokenKind.Keyword);
            _text.Append('[');
            Parameters(entry.Parameters);
            _text.Append("] { ");
            if (property.GetMethod?.IsPublic == true) { Write("get", TokenKind.Keyword); _text.Append("; "); }
            if (property.SetMethod?.IsPublic == true) { Write("set", TokenKind.Keyword); _text.Append("; "); }
            _text.Append('}');
            return Finish(type);
        }

        internal FormattedMember Script(PSScriptMethod method) {
            Clear();
            Word("object", TokenKind.Type);
            Write(method.Name, TokenKind.Name);
            _text.Append('(');
            var parameters = method.Script.Ast switch {
                ScriptBlockAst block => block.ParamBlock?.Parameters,
                FunctionDefinitionAst function => function.Parameters ?? function.Body.ParamBlock?.Parameters,
                _ => null
            };
            if (parameters is not null) {
                for (int i = 0; i < parameters.Count; i++) {
                    if (i > 0) { _text.Append(", "); }
                    var parameter = parameters[i];
                    var constraint = parameter.Attributes.OfType<TypeConstraintAst>().FirstOrDefault();
                    Type? type = constraint?.TypeName.GetReflectionType();
                    Word(type is null ? constraint is null ? "object" : TypeName(constraint.TypeName.FullName)
                        : TypeName(type), TokenKind.Type);
                    Write(parameter.Name.VariablePath.UserPath, TokenKind.Parameter);
                    if (parameter.DefaultValue is { } expression) {
                        _text.Append(' ');
                        Write("=", TokenKind.Operator);
                        _text.Append(' ');
                        if (expression is ConstantExpressionAst constant) { Literal(constant.Value); }
                        else { Write(Tables.TableModel.Text(expression.Extent.Text), TokenKind.Expression); }
                    }
                }
            }
            _text.Append(");");
            return Finish("object");
        }

        private void Parameters(ParameterInfo[] parameters, int start = 0) {
            for (int i = start; i < parameters.Length; i++) {
                if (i > start) { _text.Append(", "); }
                var parameter = parameters[i];
                if (parameter.IsDefined(typeof(ParamArrayAttribute), false)
                    || parameter.IsDefined(typeof(System.Runtime.CompilerServices.ParamCollectionAttribute), false)) {
                    Word("params", TokenKind.Keyword);
                }
                if (parameter.IsOptional && !parameter.HasDefaultValue) { Word("[Optional]", TokenKind.Keyword); }
                Type type = parameter.ParameterType;
                if (type.IsByRef) {
                    Word(parameter.IsOut && !parameter.IsIn ? "out" : parameter.IsIn ? "in" : "ref", TokenKind.Keyword);
                }
                Word(TypeName(type), TokenKind.Type);
                Write(parameter.Name ?? "arg" + i.ToString(CultureInfo.InvariantCulture), TokenKind.Parameter);
                if (parameter.HasDefaultValue) {
                    _text.Append(' ');
                    Write("=", TokenKind.Operator);
                    _text.Append(' ');
                    Literal(parameter.RawDefaultValue, type);
                }
            }
        }

        private void Modifiers(MethodInfo method, Type? contract, bool code = false) {
            if (contract is not null) { return; }
            Word("public", TokenKind.Keyword);
            if (method.IsStatic && !code) { Word("static", TokenKind.Keyword); }
            if (method.IsAbstract) { Word("abstract", TokenKind.Keyword); }
            else if (method.IsVirtual) {
                if ((method.Attributes & System.Reflection.MethodAttributes.NewSlot) == 0) {
                    if (method.IsFinal) { Word("sealed", TokenKind.Keyword); }
                    Word("override", TokenKind.Keyword);
                }
                else if (!method.IsFinal) { Word("virtual", TokenKind.Keyword); }
            }
        }

        private void Qualifier(Type? contract) {
            if (contract is null) { return; }
            Write(TypeName(contract), TokenKind.Type);
            _text.Append('.');
        }

        private void Literal(object? value, Type? type = null) {
            if (value is null) { Write("null", TokenKind.Keyword); return; }
            if (value is bool boolean) { Write(boolean ? "true" : "false", TokenKind.Keyword); return; }
            if (value is string or char) {
                int start = _text.Length;
                char quote = value is char ? '\'' : '"';
                _text.Append(quote);
                if (value is char character) { Escape(character, quote); }
                else { foreach (char item in (string)value) { Escape(item, quote); } }
                _text.Append(quote);
                _tokens.Add(new(start, _text.Length - start, TokenKind.Literal));
                return;
            }
            if (type?.IsEnum == true) {
                string? name = Enum.GetName(type, value);
                if (name is not null) {
                    Write(TypeName(type), TokenKind.Type);
                    _text.Append('.');
                    Write(name, TokenKind.Literal);
                    return;
                }
                _text.Append('(');
                Write(TypeName(type), TokenKind.Type);
                _text.Append(')');
            }
            Write(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null", TokenKind.Number);
        }

        private void Escape(char character, char quote) {
            if (character == quote || character == '\\') { _text.Append('\\').Append(character); }
            else {
                string? escaped = character switch {
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    '\0' => "\\0",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => null
                };
                if (escaped is not null) { _text.Append(escaped); }
                else if (char.IsControl(character)) { _text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); }
                else { _text.Append(character); }
            }
        }

        private void Write(string text, TokenKind kind) {
            _tokens.Add(new(_text.Length, text.Length, kind));
            _text.Append(text);
        }

        private void Word(string text, TokenKind kind) { Write(text, kind); _text.Append(' '); }

        private void Clear() { _text.Clear(); _tokens.Clear(); }
        private FormattedMember Finish(string type) => new(type, _text.ToString(), [.. _tokens]);
    }
}
