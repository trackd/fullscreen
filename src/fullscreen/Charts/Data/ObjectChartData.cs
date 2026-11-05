using System.Collections;
using System.Globalization;
using System.Management.Automation;
using Microsoft.PowerShell.Commands;

namespace fullscreen.Charts;

internal enum ObjectChartFilter {
    All,
    First,
    Last,
    Top,
    Bottom
}

/// <summary>Retains complete pipeline objects; plotting is a replaceable projection.</summary>
internal sealed partial class ObjectChartData {
    private readonly Dictionary<string, ObjectChartCalculated> _calculated = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _sourceProperties, _sourceNumericProperties;
    internal IReadOnlyCollection<ObjectChartCalculated> Calculated => _calculated.Values;
    internal IReadOnlyList<PSObject> Objects { get; }
    internal string[] Properties { get; private set; }
    internal string[] NumericProperties { get; private set; }
    internal string? DefaultYProperty { get; }

    internal ObjectChartData(IReadOnlyList<PSObject> objects, CancellationToken token = default) {
        Objects = objects.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numericNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var types = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        void Add(string name, string typeName) {
            if (names.Contains(name)) {
                if (NumericType(typeName)) {
                    numericNames.Add(name);
                    unknownNames.Remove(name);
                }

                return;
            }

            if (ordered.Count >= 256) {
                return;
            }

            names.Add(name);
            ordered.Add(name);
            if (NumericType(typeName)) {
                numericNames.Add(name);
            }
            else if (typeName is "System.Object" or "") {
                unknownNames.Add(name);
            }
        }

        foreach (var item in Objects) {
            token.ThrowIfCancellationRequested();
            if (IsScalar(item.BaseObject)) {
                Add("Value", item.BaseObject.GetType().FullName ?? "");
                continue;
            }

            if (item.BaseObject is IDictionary dictionary) {
                foreach (DictionaryEntry entry in dictionary) {
                    if (entry.Key is string name) {
                        Add(name, entry.Value?.GetType().FullName ?? "System.Object");
                    }
                }

                continue;
            }

            // Adapted CLR members are uniform for a PowerShell type. Dynamic
            // objects retain their union of fields without evaluating any getter.
            if (item.BaseObject is not PSCustomObject
                && !types.Add(item.TypeNames.FirstOrDefault() ?? item.BaseObject.GetType().FullName ?? "System.Object")) {
                continue;
            }

            foreach (var property in item.Properties) {
                if (property.IsGettable) {
                    string typeName;
                    try {
                        typeName = property.TypeNameOfValue;
                    }
                    catch (ExtendedTypeSystemException) {
                        typeName = "System.Object";
                    }

                    Add(property.Name, typeName);
                }
            }
        }

        Properties = _sourceProperties = [.. ordered];
        DefaultYProperty = ordered.FirstOrDefault(numericNames.Contains);
        // PowerShell exposes numeric process aliases such as SI before useful
        // measurements. Choose a known measurement using metadata alone.
        if (Objects.Count > 0 && Objects[0].BaseObject is System.Diagnostics.Process
            && numericNames.Contains(nameof(System.Diagnostics.Process.WorkingSet64))) {
            DefaultYProperty = nameof(System.Diagnostics.Process.WorkingSet64);
        }
        // Unknown script/object-valued members remain selectable, after fields
        // known to be numeric. Selecting a field is what authorizes reading it.
        NumericProperties = _sourceNumericProperties = [
            ..ordered.Where(numericNames.Contains),
            ..ordered.Where(unknownNames.Contains)
        ];
    }

    private static bool NumericType(string name) {
        const string nullablePrefix = "System.Nullable`1[[";
        if (name.StartsWith(nullablePrefix, StringComparison.Ordinal)) {
            int end = name.IndexOf(',', nullablePrefix.Length);
            return end > 0 && NumericType(name[nullablePrefix.Length..end]);
        }

        return name is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32"
            or "System.UInt32"
            or "System.Int64"
            or "System.UInt64"
            or "System.Single"
            or "System.Double"
            or "System.Decimal"
            or "System.TimeSpan";
    }

    internal string BindGroupBy(object specification, CancellationToken token = default) {
        object value = specification is PSObject wrapper ? wrapper.BaseObject : specification;
        if (value is not ScriptBlock script) {
            return BindProperties([value], token)[0];
        }

        string name = "Group";
        for (int suffix = 2; _calculated.Values.Any(column => column.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++) {
            name = "Group " + suffix.ToString(CultureInfo.InvariantCulture);
        }

        var calculated = Calculate(new(name, script), null, token);
        Store(calculated);
        return calculated.Key;
    }

    internal string Label(string key) =>
        _calculated.TryGetValue(key, out var column) ? column.Definition.Name : key;
    internal string MenuLabel(string key) =>
        _calculated.TryGetValue(key, out var column) ? column.Definition.Name + (column.ErrorCount == 0 ? " (custom)" : $" (custom; {column.ErrorCount} {(column.ErrorCount == 1 ? "error" : "errors")})") : key;
    internal object? Value(
        int index,
        string key
    ) =>
        _calculated.TryGetValue(key, out var column) ? column.Values[index] : Read(Objects[index], key);
    internal ObjectChartCalculated? GetCalculated(string key) =>
        _calculated.GetValueOrDefault(key);
    internal string[] BindProperties(IReadOnlyList<object> specifications, CancellationToken token) {
        var properties = specifications.Select(ObjectChartProperty.Parse).ToArray();
        var keys = new string[properties.Length];
        for (int index = 0; index < keys.Length; index++) {
            if (properties[index].Expression is not null) {
                var column = Calculate(properties[index], null, token);
                Store(column);
                keys[index] = column.Key;
            }
        }

        for (int index = 0; index < keys.Length; index++) {
            keys[index] ??= Resolve(properties[index].Name);
        }

        return keys;
    }

    internal ObjectChartCalculated Calculate(ObjectChartProperty property, string? key, CancellationToken token) {
        if (_calculated.Values.Any(column => column.Key != key
            && column.Definition.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException($"A custom field named '{property.Name}' already exists. Edit it or choose another header.");
        }

        if (property.Expression is null) {
            throw new ArgumentException("A calculated property needs an Expression.");
        }

        var values = new object?[Objects.Count];
        var expression = property.Expression is ScriptBlock script ? new PSPropertyExpression(script) : null;
        int errors = 0;
        Exception? firstError = null;
        for (int index = 0; index < values.Length; index++) {
            token.ThrowIfCancellationRequested();
            try {
                if (expression is null) {
                    values[index] = Read(Objects[index], (string)property.Expression);
                    continue;
                }

                var results = expression.GetValues(Objects[index]);
                if (results.Count == 0) {
                    continue;
                }

                if (results.Count != 1) {
                    throw new ArgumentException("Expression must return one value per object.");
                }

                if (results[0].Exception is { } error) {
                    throw error;
                }

                object? value = results[0].Result;
                if (value is PSObject ps) {
                    value = ps.BaseObject;
                }

                if (value is Array array) {
                    if (array.Length > 1) {
                        throw new ArgumentException("Expression must return one value per object.");
                    }

                    value = array.Length == 0 ? null : array.GetValue(0);
                }

                values[index] = value;
            }
            catch (PipelineStoppedException) {
                throw;
            }
            catch (Exception error) {
                errors++;
                firstError ??= error;
            }
        }

        if (values.Length > 0 && errors == values.Length) {
            throw new ArgumentException("Expression failed for every object: " + firstError!.Message, firstError);
        }

        return new(key ?? "\0" + Guid.NewGuid().ToString("N"), property, values, errors);
    }

    internal void Store(ObjectChartCalculated column) {
        _calculated[column.Key] = column;
        RebuildProperties();
    }

    internal void RemoveCalculated(string key) {
        _calculated.Remove(key);
        RebuildProperties();
    }

    private void RebuildProperties() {
        Properties = [.. _sourceProperties, .. _calculated.Keys];
        NumericProperties = [
            .._sourceNumericProperties,
            .._calculated.Values.Where(column => column.Values.Any(value => Number(value).HasValue)).Select(column => column.Key)
        ];
    }

    private static bool IsScalar(object value) =>
        value is string or decimal or DateTime or DateTimeOffset or TimeSpan || value.GetType().IsPrimitive;
    internal static object? Read(PSObject item, string property) {
        try {
            if (property.Equals("Value", StringComparison.OrdinalIgnoreCase) && IsScalar(item.BaseObject)) {
                return item.BaseObject;
            }

            if (item.BaseObject is IDictionary dictionary) {
                foreach (DictionaryEntry entry in dictionary) {
                    if (entry.Key is string key && key.Equals(property, StringComparison.OrdinalIgnoreCase)) {
                        return entry.Value;
                    }
                }

                return null;
            }

            return item.Properties[property]?.Value;
        }
        catch (Exception) {
            return null;
        }
    }

    internal static double? Number(object? value) {
        if (value is PSObject wrapped) {
            value = wrapped.BaseObject;
        }

        double number;
        if (value is TimeSpan duration) {
            number = duration.TotalSeconds;
        }
        else if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double
            or decimal) {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        else {
            return null;
        }

        return double.IsFinite(number) ? number : null;
    }

    internal string Resolve(string property) =>
        Properties.FirstOrDefault(name => name.Equals(property, StringComparison.OrdinalIgnoreCase)) ?? _calculated.Values.FirstOrDefault(column => column.Definition.Name.Equals(property, StringComparison.OrdinalIgnoreCase))?.Key ?? throw new ArgumentException($"Property '{property}' was not found in the input objects.");
    internal int[] Select(ObjectChartFilter mode, int count, string? rankProperty, CancellationToken token = default) {
        if (!Enum.IsDefined(mode) || count is < 1 or > 1000000) {
            throw new ArgumentException("Choose a valid filter and a count between 1 and 1000000.");
        }

        token.ThrowIfCancellationRequested();
        IEnumerable<int> indices = Enumerable.Range(0, Objects.Count);
        if (mode == ObjectChartFilter.All) {
            return [.. indices];
        }

        if (mode == ObjectChartFilter.First) {
            return [.. indices.Take(count)];
        }

        if (mode == ObjectChartFilter.Last) {
            return [.. indices.Skip(Math.Max(0, Objects.Count - count))];
        }

        if (rankProperty is null) {
            return [];
        }

        string rank = Resolve(rankProperty);
        var values = indices.Select(index => {
            token.ThrowIfCancellationRequested();
            return (Index: index, Value: Number(Value(index, rank)));
        }).Where(entry => entry.Value.HasValue);
        var ordered = mode == ObjectChartFilter.Top ? values.OrderByDescending(entry => entry.Value) : values.OrderBy(entry => entry.Value);
        return [.. ordered.ThenBy(entry => entry.Index).Take(count).Select(entry => entry.Index)];
    }

    internal ChartDataSeries[] Project(
        string? xProperty,
        IReadOnlyList<string> yProperties,
        CancellationToken token = default
    ) =>
        Project(xProperty, yProperties, null, token);
    internal ChartDataSeries[] Project(
        string? xProperty,
        IReadOnlyList<string> yProperties,
        IReadOnlyList<int>? indices,
        CancellationToken token = default
    ) {
        string? xName = xProperty is null ? null : Resolve(xProperty);
        var rows = indices ?? Enumerable.Range(0, Objects.Count).ToArray();
        var xValues = rows.Select(index => xName is null ? null : Value(index, xName)).ToArray();
        bool continuous = xName is not null && xValues.Where(value => value is not null).Any()
            && xValues.Where(value => value is not null).All(IsCoordinate);
        var result = new List<ChartDataSeries>();
        foreach (string property in yProperties.Distinct(StringComparer.OrdinalIgnoreCase)) {
            string name = Resolve(property);
            var points = new ChartPoint[rows.Count];
            for (int position = 0; position < points.Length; position++) {
                token.ThrowIfCancellationRequested();
                int index = rows[position];
                object? xValue = xValues[position];
                double x = continuous ? Coordinate(xValue) ?? double.NaN : position;
                double? y = xName is not null && xValue is null ? null : Number(Value(index, name));
                if (!double.IsFinite(x)) {
                    y = null;
                    x = position;
                }

                points[position] = new(
                    index,
                    x,
                    y,
                    xName is null ? (position + 1).ToString(CultureInfo.InvariantCulture) : Convert.ToString(xValue, CultureInfo.InvariantCulture) ?? "—"
                ) {
                    Source = Objects[index]
                };
            }

            result.Add(new(Label(name), points));
        }

        return [.. result];
    }

    private static double? Coordinate(object? value) =>
        value switch {
            PSObject ps => Coordinate(ps.BaseObject),
            DateTime time => time.ToUniversalTime().Ticks / (double)TimeSpan.TicksPerSecond,
            DateTimeOffset time => time.UtcTicks / (double)TimeSpan.TicksPerSecond,
            _ => Number(value)
        };
    private static bool IsCoordinate(object? value) =>
        value is PSObject ps ? IsCoordinate(ps.BaseObject) : value is DateTime or DateTimeOffset or TimeSpan or byte or sbyte or short or ushort or int or uint
        or long
        or ulong
        or float
        or double
        or decimal;
}
