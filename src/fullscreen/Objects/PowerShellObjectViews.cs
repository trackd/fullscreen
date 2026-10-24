using System.Globalization;
using System.Management.Automation;
using fullscreen.Tables;
using Microsoft.PowerShell.Commands;
using System.Text.Json;
using System.Collections;

namespace fullscreen.Objects;
/// <summary>Selects the representation used to display PowerShell objects.</summary>
public enum ObjectView {
    /// <summary>Shows the formatted view.</summary>
    Formatted = 0,
    /// <summary>Shows the expanded view.</summary>
    Expanded = 1,
    /// <summary>Shows the properties view.</summary>
    Properties = Expanded,
    /// <summary>Shows the json view.</summary>
    Json = 2
}

/// <summary>Display metadata resolution follows ConsoleGuiTools TypeGetter and Out-GridView TableView.</summary>
public static class PowerShellObjectViews {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        MaxDepth = 256
    };
    private sealed record ColumnKey(string Label, string Expression, DisplayEntryValueType Kind, string? Format);
    private sealed record Column(int Index, string Label, PSPropertyExpression Expression, string? Format);
    /// <summary>Uses PowerShell formatting to build a display table for the supplied objects.</summary>
    /// <param name="objects">The original PowerShell objects retained by the view.</param>
    /// <param name="property">The property names to display, or null to use PowerShell formatting defaults.
    /// Defaults to null.</param>
    /// <returns>The formatted table model.</returns>
    public static TableModel Format(IReadOnlyList<PSObject> objects, string[]? property = null) {
        if (property is { Length: > 0 }) {
            return ObjectSnapshot.Create(objects, property);
        }

        if (objects.Count == 0) {
            return new TableModel(["Value"]);
        }

        var formats = new Dictionary<string, TableControl?>();
        var rowPlans = new Dictionary<TableControlRow, Column[]>();
        var definitions = new Dictionary<ColumnKey, Column>();
        var labels = new List<string>();
        var widths = new List<int>();
        var right = new List<bool>();
        var plans = new Column[objects.Count][];
        Column Define(
            string label,
            string expression,
            DisplayEntryValueType kind,
            string? format = null,
            int width = 0,
            bool aligned = false
        ) {
            var key = new ColumnKey(label, expression, kind, format);
            if (definitions.TryGetValue(key, out var existing)) {
                return existing;
            }

            if (labels.Count == ObjectSnapshot.MaximumColumns) {
                throw new InvalidOperationException("Display metadata exceeds 256 columns. Choose -Property to limit columns.");
            }

            string unique = label;
            int suffix = 2;
            while (labels.Contains(unique, StringComparer.OrdinalIgnoreCase)) {
                unique = $"{label} ({suffix++})";
            }

            var accessor = kind == DisplayEntryValueType.ScriptBlock ? new PSPropertyExpression(ScriptBlock.Create(expression)) : new PSPropertyExpression(expression);
            var column = new Column(labels.Count, label, accessor, format);
            labels.Add(unique);
            widths.Add(width);
            right.Add(aligned);
            definitions.Add(key, column);
            return column;
        }

        for (int index = 0; index < objects.Count; index++) {
            var item = objects[index];
            string signature = string.Join('\0', item.TypeNames);
            if (!formats.TryGetValue(signature, out var table)) {
                table = FindTable(item);
                formats.Add(signature, table);
            }

            if (table is not null && SelectRow(table, item) is { } row) {
                if (!rowPlans.TryGetValue(row, out var columns)) {
                    columns = new Column[row.Columns.Count];
                    for (int i = 0; i < columns.Length; i++) {
                        var entry = row.Columns[i];
                        var header = i < table.Headers.Count ? table.Headers[i] : null;
                        if (entry.DisplayEntry is null) {
                            var defaultRow = table.Rows.FirstOrDefault(candidate => candidate.SelectedBy is null);
                            if (defaultRow is not null && i < defaultRow.Columns.Count) {
                                entry = defaultRow.Columns[i];
                            }
                        }

                        if (entry.DisplayEntry is null) {
                            throw new InvalidOperationException("The table metadata contains an unresolved row placeholder.");
                        }

                        string label = string.IsNullOrEmpty(header?.Label) ? entry.DisplayEntry.Value : header.Label;
                        columns[i] = Define(
                            label,
                            entry.DisplayEntry.Value,
                            entry.DisplayEntry.ValueType,
                            entry.FormatString,
                            header?.Width ?? 0,
                            header?.Alignment == Alignment.Right || entry.Alignment == Alignment.Right
                        );
                    }

                    rowPlans.Add(row, columns);
                }

                plans[index] = columns;
            }
            else if (Scalar(item.BaseObject)) {
                plans[index] = [Define(item.BaseObject.GetType().Name, "$_", DisplayEntryValueType.ScriptBlock)];
            }
            else {
                var standard = item.Members["PSStandardMembers"] as PSMemberSet;
                var defaultSet = standard?.Members["DefaultDisplayPropertySet"] as PSPropertySet;
                IEnumerable<string> names = defaultSet?.ReferencedPropertyNames is { Count: > 0 } defaults ? defaults : item.Properties.Where(info => info.IsGettable).Select(info => info.Name);
                plans[index] = [.. names.Take(ObjectSnapshot.MaximumColumns).Select(name => Define(name, name, DisplayEntryValueType.Property))];
                if (plans[index].Length == 0) {
                    plans[index] = [Define("Value", "$_", DisplayEntryValueType.ScriptBlock)];
                }
            }
        }

        var model = new TableModel([.. labels]) {
            RightAligned = [.. right],
            ColorByType = true
        };
        for (int index = 0; index < objects.Count; index++) {
            var item = objects[index];
            var cells = new string[labels.Count];
            Array.Fill(cells, "");
            var values = new object?[labels.Count];
            foreach (var column in plans[index]) {
                try {
                    var results = column.Expression.GetValues(item);
                    if (results.Count == 0) {
                        continue;
                    }

                    if (results[0].Exception is not null) {
                        cells[column.Index] = "<property error>";
                        continue;
                    }

                    object? value = results[0].Result;
                    values[column.Index] = value is PSObject ps ? ps.BaseObject : value;
                    string display = column.Format is { Length: > 0 } format ? string.Format(CultureInfo.CurrentCulture, "{0:" + format +
                        "}", values[column.Index]) : value?.ToString() ?? "null";
                    cells[column.Index] = model.DisplayCell(index, column.Index, display);
                    // Out-GridView uses the actual file timestamp as the sort value even when
                    // the display database supplies a script that renders it as a string.
                    if (item.BaseObject is FileSystemInfo
                        && column.Label.Equals("LastWriteTime", StringComparison.OrdinalIgnoreCase)) {
                        values[column.Index] = item.Properties["LastWriteTime"]?.Value;
                    }
                }
                catch (Exception) {
                    cells[column.Index] = "<property error>";
                }
            }

            model.Rows.Add(new(index, item, cells, values));
        }

        model.PreferredWidths = [..widths.Select((
            width,
            index
        ) => Math.Clamp(Math.Max(width, Math.Min(40, labels[index].Length)) + 2, 12, 60))];
        if (model.PreferredWidths.Length > 0) {
            model.PreferredWidths[^1] = 0;
        }

        model.Rebuild();
        return model;
    }

    private static TableControl? FindTable(PSObject item) {
        // Bind arguments; never interpolate caller-supplied type names into script text.
        using var pipeline = PowerShell.Create(RunspaceMode.CurrentRunspace);
        pipeline.AddCommand("Microsoft.PowerShell.Utility\\Get-FormatData").AddParameter(
            "TypeName",
            item.TypeNames.SelectMany(name => name.StartsWith("Deserialized.", StringComparison.OrdinalIgnoreCase) ? new[] {
                name,
                name[13..]
            } : [name]).Distinct(StringComparer.OrdinalIgnoreCase).Select(WildcardPattern.Escape).ToArray()
        ).AddParameter("PowerShellVersion", PSVersionInfo.PSVersion);
        var definitions = pipeline.Invoke<ExtendedTypeDefinition>();
        if (pipeline.HadErrors) {
            throw new InvalidOperationException(pipeline.Streams.Error[0].ToString());
        }

        foreach (string name in item.TypeNames) {
            string typeName = name.StartsWith("Deserialized.", StringComparison.OrdinalIgnoreCase) ? name[13..] : name;
            foreach (var definition in definitions) {
                if (!definition.TypeNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && !definition.TypeNames.Contains(typeName, StringComparer.OrdinalIgnoreCase)) {
                    continue;
                }

                foreach (var view in definition.FormatViewDefinition) {
                    if (view.Control is TableControl table) {
                        return table;
                    }
                }
            }
        }

        return null;
    }

    private static TableControlRow? SelectRow(TableControl table, PSObject item) {
        TableControlRow? fallback = null;
        foreach (var row in table.Rows) {
            if (row.SelectedBy is null
                || (row.SelectedBy.TypeNames.Count == 0 && row.SelectedBy.SelectionCondition.Count == 0)) {
                fallback ??= row;
                continue;
            }

            foreach (string type in row.SelectedBy.TypeNames) {
                if (item.TypeNames.Any(name => name.Equals(type, StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Deserialized." + type, StringComparison.OrdinalIgnoreCase))) {
                    return row;
                }
            }

            foreach (var condition in row.SelectedBy.SelectionCondition) {
                var expression = condition.ValueType == DisplayEntryValueType.ScriptBlock ? new PSPropertyExpression(ScriptBlock.Create(condition.Value)) : new PSPropertyExpression(condition.Value);
                var values = expression.GetValues(item);
                if (values.Count > 0 && values[0].Exception is null && LanguagePrimitives.IsTrue(values[0].Result)) {
                    return row;
                }
            }
        }

        return fallback ?? table.Rows.FirstOrDefault();
    }

    private static bool Scalar(object value) =>
        value is string or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Enum
        || value.GetType().IsPrimitive;
    /// <summary>Converts an object to JSON using PowerShell&apos;s serialization semantics.</summary>
    /// <param name="value">The input value to format, display, or append.</param>
    /// <param name="depth">The maximum JSON nesting depth.</param>
    /// <returns>The JSON representation.</returns>
    public static string Json(object value, int depth) {
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
        int remaining = 10000;
        object? Snapshot(object? item, int level) {
            if (item is null) {
                return null;
            }

            var wrapped = PSObject.AsPSObject(item);
            object raw = wrapped.BaseObject;
            if (raw is null) {
                return null;
            }

            if (raw is IntPtr or UIntPtr) {
                return raw.ToString();
            }

            if (raw is double number && !double.IsFinite(number) || raw is float single && !float.IsFinite(single)) {
                return raw.ToString();
            }

            if (Scalar(raw)) {
                return raw is Enum ? raw.ToString() : raw;
            }

            if (--remaining < 0) {
                return "<display limit>";
            }

            if (level > depth) {
                return "<maximum depth>";
            }

            object identity = raw is PSCustomObject ? wrapped : raw;
            if (!path.Add(identity)) {
                return "<circular reference>";
            }

            try {
                if (raw is IDictionary map) {
                    var result = new Dictionary<string, object?>();
                    foreach (DictionaryEntry entry in map) {
                        if (result.Count == 1000 || remaining < 0) {
                            break;
                        }

                        result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""] = Snapshot(entry.Value, level + 1);
                    }

                    return result;
                }

                if (raw is System.Collections.IEnumerable sequence) {
                    var result = new List<object?>();
                    foreach (object? child in sequence) {
                        if (result.Count == 1000 || remaining < 0) {
                            break;
                        }

                        result.Add(Snapshot(child, level + 1));
                    }

                    return result;
                }

                var properties = new Dictionary<string, object?>();
                foreach (var property in wrapped.Properties) {
                    if (!property.IsGettable) {
                        continue;
                    }

                    if (properties.Count == 1000 || remaining < 0) {
                        break;
                    }

                    try {
                        properties[property.Name] = Snapshot(property.Value, level + 1);
                    }
                    catch (Exception) {
                        properties[property.Name] = "<property error>";
                    }
                }

                return properties;
            }
            finally {
                path.Remove(identity);
            }
        }

        return JsonSerializer.Serialize(Snapshot(value, 0), JsonOptions);
    }
}
