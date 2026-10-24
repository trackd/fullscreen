using System.Text.Json;
using System.Text.Json.Serialization;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartApp {
    private sealed record FieldReference(string Name, bool Calculated = false);
    private sealed record SavedCalculation(string Name, string Body);
    private sealed record SavedStyle(
        string Name,
        string? Color,
        bool Visible,
        ChartLineStyle LineStyle,
        ChartMarker Marker
    );
    private sealed record Configuration {
        public int Version { get; init; } = 1;
        public FieldReference? X { get; init; }
        public FieldReference[] Y { get; init; } = [];
        public FieldReference? Rank { get; init; }
        public FieldReference? Group { get; init; }
        public FieldReference? Lower { get; init; }
        public FieldReference? Upper { get; init; }
        public ObjectChartFilter Filter { get; init; }
        public int Count { get; init; } = 30;
        public ChartAggregate Aggregate { get; init; }
        public SavedCalculation[] Calculations { get; init; } = [];
        public SavedStyle[] Styles { get; init; } = [];
        public JsonElement Chart { get; init; }
    }

    private static readonly JsonSerializerOptions ConfigurationJson = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
    internal string ExportConfiguration() {
        FieldReference? Field(string? key) =>
            key is null ? null : Data.GetCalculated(key) is { } column ? new(column.Definition.Name, true) : new(key);
        using var view = JsonDocument.Parse(Surface.Plot.ExportSettings());
        return JsonSerializer.Serialize(
            new Configuration {
                X = Field(XProperty),
                Y = [.. YProperties.Select(key => Field(key)!)],
                Rank = Field(RankProperty),
                Group = Field(GroupProperty),
                Lower = Field(LowerProperty),
                Upper = Field(UpperProperty),
                Filter = FilterMode,
                Count = Count,
                Aggregate = Aggregate,
                Calculations = [.. Data.Calculated.Select(column => new SavedCalculation(column.Definition.Name, column.Definition.Body))],
                Styles = [.. Surface.Series.Select(series => new SavedStyle(
                    series.Property,
                    series.Color is { } color ? ColorText(color) : null,
                    series.Visible,
                    series.LineStyle,
                    series.Marker
                ))],
                Chart = view.RootElement.Clone()
            },
            ConfigurationJson
        );
    }

    internal void ImportConfiguration(string json) {
        var settings = JsonSerializer.Deserialize<Configuration>(json, ConfigurationJson) ?? throw new ArgumentException("Configuration is empty.");
        if (settings.Version != 1 || !Enum.IsDefined(settings.Filter) || !Enum.IsDefined(settings.Aggregate)
            || settings.Count is < 1 or > 1000000
            || settings.Y is null
            || settings.Calculations is null
            || settings.Styles is null) {
            throw new ArgumentException("Invalid chart configuration.");
        }

        var next = new ObjectChartData(Data.Objects, _token);
        foreach (var calculation in settings.Calculations) {
            next.Store(next.Calculate(new(calculation.Name, ObjectChartProperty.ParseBody(calculation.Body)), null, _token));
        }

        string? Resolve(FieldReference? field) =>
            field is null ? null : field.Calculated ? next.Calculated.SingleOrDefault(column => column.Definition.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))?.Key ?? throw new ArgumentException($"Calculated field '{field.Name}' was not found.") : next.Resolve(field.Name);
        string? x = Resolve(settings.X), rank = Resolve(settings.Rank), group = Resolve(settings.Group), lower = Resolve(settings.Lower), upper = Resolve(settings.Upper);
        string[] y = [.. settings.Y.Select(field => Resolve(field)!)];
        var view = new Chart();
        view.ImportSettings(settings.Chart.GetRawText());
        var styles = settings.Styles.GroupBy(style => style.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => {
            var style = group.Last();
            return new SeriesAppearance(ParseColor(style.Color), style.Visible, style.LineStyle, style.Marker);
        }, StringComparer.Ordinal);
        var previous = (Data, XProperty, YProperties, FilterMode, Count, RankProperty, GroupProperty, Aggregate, LowerProperty, UpperProperty, RowSubset);
        var oldView = Surface.Plot.CaptureSettings();
        var oldStyles = new Dictionary<string, SeriesAppearance>(_styles);
        try {
            Data = next;
            GroupProperty = group;
            Aggregate = settings.Aggregate;
            LowerProperty = lower;
            UpperProperty = upper;
            RowSubset = null;
            _styles.Clear();
            foreach (var pair in styles) {
                _styles.Add(pair.Key, pair.Value);
            }

            Apply(x, y, view.GraphKind, settings.Filter, settings.Count, rank);
            Surface.Plot.ImportSettings(settings.Chart.GetRawText());
            Surface.ReplaceSource(next);
            _automaticAxisFormatting = false;
        }
        catch {
            (Data, XProperty, YProperties, FilterMode, Count, RankProperty, GroupProperty, Aggregate, LowerProperty, UpperProperty, RowSubset) = previous;
            _styles.Clear();
            foreach (var pair in oldStyles) {
                _styles.Add(pair.Key, pair.Value);
            }

            Replot();
            Surface.Plot.ApplySettings(oldView);
            Surface.ReplaceSource(Data);
            throw;
        }
    }

    private void LoadConfigurationDialog() {
        var path = new TextBox("chart.json");
        Editor(
            "Load chart configuration",
            [Field("Path", path)],
            () => ImportConfiguration(File.ReadAllText(Path.GetFullPath(path.Text ?? "chart.json", WorkingDirectory)))
        );
    }
}
