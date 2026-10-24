using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using fullscreen.Themes;

namespace fullscreen.Commands;
/// <summary>Converts monitor view names and aliases to the monitor view enumeration.</summary>
public sealed class MonitorViewTransformationAttribute : ArgumentTransformationAttribute {
    /// <inheritdoc/>
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) {
        object value = inputData is PSObject wrapper ? wrapper.BaseObject : inputData;
        return value is string name && name.Equals("Overview", StringComparison.OrdinalIgnoreCase) ? Monitoring.MonitorView.Processes : value;
    }
}

/// <summary>Convert PSCustomObject theme properties to the same native dictionary contract as a hashtable.</summary>
public sealed class ThemeInputTransformationAttribute : ArgumentTransformationAttribute {
    /// <inheritdoc/>
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) {
        if (inputData is not PSObject wrapper) {
            return inputData;
        }

        if (wrapper.BaseObject is not PSCustomObject) {
            return wrapper.BaseObject;
        }

        var colors = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var property in wrapper.Properties) {
            colors.Add(property.Name, property.Value);
        }

        return colors;
    }
}

/// <summary>Validates named themes and custom palette definitions.</summary>
public sealed class ValidateThemeAttribute : ValidateArgumentsAttribute {
    /// <inheritdoc/>
    protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics) {
        object? value = arguments is PSObject wrapper ? wrapper.BaseObject : arguments;
        try {
            if (value is string name) {
                _ = ThemeCatalog.Load(name);
            }
            else if (value is IDictionary colors) {
                _ = ThemeCatalog.FromDictionary(colors);
            }
            else {
                throw new ArgumentException("Theme must be a built-in name, hashtable or PSCustomObject of palette colors.");
            }
        }
        catch (ArgumentException error) {
            throw new ValidationMetadataException(error.Message);
        }
    }
}

/// <summary>Complete known object properties without evaluating an upstream pipeline or property getters.</summary>
public sealed class ObjectPropertyArgumentCompleter : IArgumentCompleter {
    /// <inheritdoc/>
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters
    ) {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fakeBoundParameters["InputObject"] is { } input) {
            object value = input is PSObject wrapper ? wrapper.BaseObject : input;
            if (value is IEnumerable items && value is not string && value is not IDictionary) {
                int count = 0;
                foreach (object item in items) {
                    AddProperties(item);
                    if (++count >= 64) {
                        break;
                    }
                }
            }
            else {
                AddProperties(input);
            }
        }

        if (fakeBoundParameters["Property"] is IEnumerable properties && properties is not string) {
            foreach (object property in properties) {
                if (property is string name && !WildcardPattern.ContainsWildcardCharacters(name)) {
                    names.Add(name);
                }
            }
        }

        string prefix = wordToComplete.Trim('\'', '"');
        foreach (string name in names) {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                yield return new CompletionResult(
                    "'" + name.Replace("'", "''", StringComparison.Ordinal) +
                    "'",
                    name,
                    CompletionResultType.Property,
                    "Object property: " + name
                );
            }
        }

        void AddProperties(object item) {
            foreach (var property in PSObject.AsPSObject(item).Properties) {
                names.Add(property.Name);
            }
        }
    }
}
