using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using fullscreen.Tables;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartApp {
    internal string? GroupProperty { get; private set; }
    internal ChartAggregate Aggregate { get; private set; }
    internal string? LowerProperty { get; private set; }
    internal string? UpperProperty { get; private set; }
    internal HashSet<int>? RowSubset { get; private set; }
    internal bool Paused { get; set; }
    internal Func<IReadOnlyList<PSObject>>? RefreshSource { get; set; }
    internal int RefreshInterval { get; set; } = 1000;
    internal string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    private TimeProvider _clock = TimeProvider.System;
    internal TimeProvider Clock {
        get => _clock;
        set {
            _clock = value;
            _lastRefresh = value.GetTimestamp();
        }
    }

    private long _lastRefresh = TimeProvider.System.GetTimestamp();
    private readonly List<Dialog> _dataDialogs = [];
    private bool HasDataOverlay {
        get {
            _dataDialogs.RemoveAll(dialog => dialog.Parent is null);
            return _dataDialogs.Count > 0;
        }
    }

    private bool _automaticAxisFormatting = true;
    private readonly Dictionary<string, SeriesAppearance> _styles = new(StringComparer.Ordinal);
    private sealed record SeriesAppearance(
        XenoAtom.Terminal.UI.Color? Color,
        bool Visible,
        ChartLineStyle LineStyle,
        ChartMarker Marker
    );
    private int[] SourceIndices(ChartSelection? selection) {
        if (selection is null || selection.SeriesIndex < 0 || selection.SeriesIndex >= Surface.Series.Length) {
            return [];
        }

        var point = Surface.Series[selection.SeriesIndex].Points.FirstOrDefault(point => point.Index == selection.PointIndex);
        return point.SourceIndices ?? (point.Source is null ? [] : [point.Index]);
    }

    internal int[] ChosenIndices() {
        if (Surface.Plot.SelectedRange is not { } range) {
            return Surface.VisibleIndices;
        }

        var selected = new HashSet<int>();
        foreach (var series in Surface.Series) {
            if (!series.Visible) {
                continue;
            }

            foreach (var point in series.Span) {
                double x = Surface.GraphType == GraphKind.HorizontalBars ? point.Y ?? double.NaN : point.X;
                if (!double.IsFinite(x) || x < range.MinimumX || x > range.MaximumX) {
                    continue;
                }

                if (point.SourceIndices is { } indices) {
                    selected.UnionWith(indices);
                }
                else {
                    selected.Add(point.Index);
                }
            }
        }

        return [.. Surface.VisibleIndices.Where(selected.Contains)];
    }

    internal void SetAggregation(string? groupProperty, ChartAggregate aggregate) {
        if (!Enum.IsDefined(aggregate)) {
            throw new ArgumentOutOfRangeException(nameof(aggregate));
        }

        string? group = groupProperty is null ? null : Data.Resolve(groupProperty);
        if (aggregate == ChartAggregate.None && group is not null) {
            throw new ArgumentException("Choose Count, Sum, Average, Minimum or Maximum to group rows.");
        }

        var old = (GroupProperty, Aggregate);
        GroupProperty = group;
        Aggregate = aggregate;
        try {
            Replot();
        }
        catch {
            (GroupProperty, Aggregate) = old;
            throw;
        }
    }

    internal void SetErrorBounds(object? lower, object? upper) {
        string? Bind(object? property) =>
            property is null ? null : Data.BindProperties([property], _token)[0];
        string? low = Bind(lower), high = Bind(upper);
        (LowerProperty, UpperProperty) = (low, high);
        Replot();
    }

    internal void SetRowSubset(IEnumerable<int>? indices) {
        var subset = indices?.ToHashSet();
        if (subset?.Any(index => index < 0 || index >= Data.Objects.Count) == true) {
            throw new ArgumentOutOfRangeException(nameof(indices));
        }

        RowSubset = subset;
        Surface.Plot.SelectRange(null);
        Replot();
    }

    internal void ReplaceObjects(IReadOnlyList<PSObject> objects) {
        var next = new ObjectChartData(objects, _token);
        foreach (var column in Data.Calculated) {
            next.Store(next.Calculate(column.Definition, column.Key, _token));
        }

        var previous = Data;
        var previousSubset = RowSubset;
        string[] previousY = YProperties;
        Data = next;
        try {
            if (_automaticY && YProperties.Length == 0) {
                YProperties = next.DefaultYProperty is { } initialY ? [initialY] : [];
            }

            RowSubset = null;
            Replot();
            Surface.ReplaceSource(next);
        }
        catch {
            Data = previous;
            RowSubset = previousSubset;
            YProperties = previousY;
            throw;
        }
    }

    internal TableModel CreateDataTable(IEnumerable<int>? rows = null) {
        int[] indices = rows?.ToArray() ?? ChosenIndices();
        string[] columns = [.. new[]
        {
            XProperty
        }.Where(name => name is not null).Cast<string>().Concat(YProperties).Concat(Data.Properties).Distinct(StringComparer.OrdinalIgnoreCase).Take(256)];
        if (columns.Length == 0) {
            columns = ["Value"];
        }

        var model = new TableModel([.. columns.Select(Data.MenuLabel)]) {
            ColorByType = true
        };
        foreach (int index in indices) {
            var values = columns.Select(name => Data.Value(index, name)).ToArray();
            var cells = values.Select((value, column) => model.DisplayCell(index, column, value)).ToArray();
            model.Rows.Add(new(index, Data.Objects[index], cells, values));
        }

        model.Rebuild();
        return model;
    }

    private void ShowData(int[]? indices = null) {
        var model = CreateDataTable(indices);
        var table = new TableSurface(model, "Chart data") {
            Embedded = true,
            Minimal = true,
            MinHeight = Math.Max(4, Surface.Bounds.Height - 10),
            MaxHeight = Math.Max(4, Surface.Bounds.Height - 10)
        };
        Dialog? dialog = null;
        table.InspectRequested += row => {
            Surface.SelectOriginal((int)row.Id);
            Surface.Plot.Selection = null;
            Inspect();
        };
        table.BackRequested = () => {
            dialog!.Close();
            return true;
        };
        var content = new VStack(new TextBlock($"{model.Rows.Count} original rows · / filter · Enter inspect · Space mark · Esc close"), table, new HStack(new Button("Close").Click(() => dialog!.Close()), new Button("Use table rows").Click(() => {
            if (RefreshSource is not null) {
                Paused = true;
            }

            SetRowSubset(model.Marked.Count > 0 ? model.Marked.Select(id => (int)id) : model.Visible.Select(row => (int)row.Id));
            dialog!.Close();
        })));
        dialog = new Dialog {
            Title = new TextBlock("Filtered chart data"),
            IsModal = true,
            Width = Math.Max(20, Surface.Bounds.Width - 2),
            Content = content
        };
        _dataDialogs.Add(dialog);
        dialog.Show();
        table.App?.Focus(table);
    }

    private static HStack Field(
        string label,
        Visual editor
    ) =>
        new(new TextBlock(label) {
            MinWidth = 21,
            MaxWidth = 21
        }, editor);
    private Dialog Editor(string title, IEnumerable<Visual> fields, Action apply) {
        var error = new TextBlock("");
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock(title),
            IsModal = true,
            Width = Math.Clamp(Surface.Bounds.Width - 2, 30, 100),
            Content = new VStack(new ScrollViewer(new VStack([.. fields])) { MaxHeight = Math.Max(3, Surface.Bounds.Height - 8) }, error, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Apply").Click(() => {
                try {
                    apply();
                    dialog!.Close();
                }
                catch (PipelineStoppedException) {
                    throw;
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested) {
                    throw;
                }
                catch (Exception failure) {
                    error.Text = failure.Message;
                }
            })))
        };
        dialog.KeyDown((_, e) => {
            if (!e.Handled && e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
        return dialog;
    }

    private static double? Number(TextBox input) {
        if (string.IsNullOrWhiteSpace(input.Text)) {
            return null;
        }

        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value)) {
            throw new ArgumentException("Enter a finite number, or leave the bound blank for automatic scaling.");
        }

        return value;
    }

    private void EditAxes() {
        var chart = Surface.Plot;
        TextBox xmin = new(chart.MinimumX?.ToString(CultureInfo.InvariantCulture) ?? ""), xmax = new(chart.MaximumX?.ToString(CultureInfo.InvariantCulture) ?? "");
        TextBox ymin = new(chart.Minimum?.ToString(CultureInfo.InvariantCulture) ?? ""), ymax = new(chart.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "");
        string[] scales = Enum.GetNames<ChartScale>();
        Select<string> xscale = new(scales, (int)chart.XAxis.Scale), yscale = new(scales, (int)chart.YAxis.Scale);
        TextBox xlabel = new(chart.XAxis.Title), ylabel = new(chart.YAxis.Title), xunit = new(chart.XAxis.Unit), yunit = new(chart.YAxis.Unit);
        TextBox xformat = new(chart.XAxis.Format), yformat = new(chart.YAxis.Format);
        CheckBox axes = new("Show axes", chart.ShowAxes), legend = new("Show legend", chart.ShowLegend);
        Editor("Axes and view", [
            Field("X minimum", xmin),
            Field("X maximum", xmax),
            Field("Y minimum", ymin),
            Field("Y maximum", ymax),
            Field("X scale", xscale),
            Field("Y scale", yscale),
            Field("X title", xlabel),
            Field("Y title", ylabel),
            Field("X unit", xunit),
            Field("Y unit", yunit),
            Field("X format", xformat),
            Field("Y format", yformat),
            axes,
            legend
        ], () => {
            var settings = chart.CaptureSettings() with {
                MinimumX = Number(xmin),
                MaximumX = Number(xmax),
                Minimum = Number(ymin),
                Maximum = Number(ymax),
                XAxis = chart.XAxis with {
                    Scale = (ChartScale)xscale.SelectedIndex,
                    Title = xlabel.Text ?? "",
                    Unit = xunit.Text ?? "",
                    Format = xformat.Text ?? "G4"
                },
                YAxis = chart.YAxis with {
                    Scale = (ChartScale)yscale.SelectedIndex,
                    Title = ylabel.Text ?? "",
                    Unit = yunit.Text ?? "",
                    Format = yformat.Text ?? "G4"
                },
                ShowAxes = axes.IsChecked,
                ShowLegend = legend.IsChecked
            };
            var validation = new Chart();
            validation.SetData(Surface.Series);
            validation.ApplySettings(settings);
            var range = validation.RenderRange(validation.GetRenderData(100));
            _ = settings.XAxis.FormatValue(settings.XAxis.Inverse(range.MinX));
            _ = settings.YAxis.FormatValue(settings.YAxis.Inverse(range.MinY));
            chart.ApplySettings(settings);
            _automaticAxisFormatting = false;
        });
    }

    private void EditAggregation() {
        string[] fields = ["All rows (no grouping)", .. Data.Properties.Select(Data.MenuLabel), "Custom expression…"];
        var group = new Select<string>(fields, GroupProperty is null ? 0 : Array.IndexOf(Data.Properties, GroupProperty) + 1);
        var aggregate = new Select<string>(Enum.GetNames<ChartAggregate>(), (int)Aggregate);
        var expression = new TextArea("$_.ProcessName") {
            MinHeight = 3,
            MaxHeight = 3,
            AcceptTab = false
        };
        Editor("Group filtered rows", [
            Field("Group by", group),
            Field("Aggregate", aggregate),
            Field("Custom expression · $_ is the input object", expression),
            new Paragraph("Choose Custom expression to group by a scriptblock. Inspect a group to see its original rows.") { Wrap = true }
        ], () => {
            var mode = (ChartAggregate)aggregate.SelectedIndex;
            if (group.SelectedIndex == fields.Length - 1 && mode == ChartAggregate.None) {
                throw new ArgumentException("Choose an aggregate for custom grouping.");
            }

            string? key = group.SelectedIndex <= 0 ? null : group.SelectedIndex == fields.Length - 1 ? Data.BindGroupBy(ObjectChartProperty.ParseBody(expression.Text ?? ""), _token) : Data.Properties[group.SelectedIndex - 1];
            SetAggregation(key, mode);
        });
    }

    private void EditErrorBounds() {
        string[] fields = ["None", .. Data.Properties.Select(Data.MenuLabel)];
        Select<string> lower = new(fields, LowerProperty is null ? 0 : Array.IndexOf(Data.Properties, LowerProperty) + 1), upper = new(fields, UpperProperty is null ? 0 : Array.IndexOf(Data.Properties, UpperProperty) + 1);
        Editor(
            "Absolute error bounds",
            [
                Field("Lower property", lower),
                Field("Upper property", upper)
            ],
            () => SetErrorBounds(
                lower.SelectedIndex <= 0 ? null : Data.Properties[lower.SelectedIndex - 1],
                upper.SelectedIndex <= 0 ? null : Data.Properties[upper.SelectedIndex - 1]
            )
        );
    }

    private void EditSeries() =>
        Menu(Surface.Series.Select((series, index) => new MenuItem(series.Property, () => {
            var visible = new CheckBox("Visible", series.Visible);
            var color = new TextBox(series.Color is { } value ? ColorText(value) : "");
            Select<string> line = new(Enum.GetNames<ChartLineStyle>(), (int)series.LineStyle), marker = new(Enum.GetNames<ChartMarker>(), (int)series.Marker);
            Editor(series.Property.Length == 0 ? "Series" : series.Property, [
                visible,
                Field("Color (#RRGGBB)", color),
                Field("Line", line),
                Field("Marker", marker)
            ], () => {
                _styles[series.Property] = new(
                    ParseColor(color.Text),
                    visible.IsChecked,
                    (ChartLineStyle)line.SelectedIndex,
                    (ChartMarker)marker.SelectedIndex
                );
                Replot();
            });
        })));
    private void EditAnnotations() {
        var type = new Select<string>([
            "Horizontal threshold",
            "Vertical threshold",
            "Text annotation",
            "Clear all"
        ], 0);
        TextBox x = new("0"), y = new("0"), label = new("");
        Editor("Thresholds and annotations", [
            Field("Type", type),
            Field("X / threshold value", x),
            Field("Y (text only)", y),
            Field("Label", label)
        ], () => {
            if (type.SelectedIndex == 3) {
                Surface.Plot.Thresholds.Clear();
                Surface.Plot.Annotations.Clear();
            }
            else if (type.SelectedIndex == 2) {
                Surface.Plot.Annotations.Add(new(Number(x) ?? 0, Number(y) ?? 0, label.Text ?? ""));
            }
            else {
                Surface.Plot.Thresholds.Add(new(Number(x) ?? 0, label.Text ?? "", type.SelectedIndex == 1));
            }
        });
    }

    private static string ColorText(XenoAtom.Terminal.UI.Color color) {
        var rgb = color.ToRgb();
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    private static XenoAtom.Terminal.UI.Color? ParseColor(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }

        if (text.Length != 7 || text[0] != '#') {
            throw new ArgumentException("Use #RRGGBB or leave the color blank.");
        }

        return XenoAtom.Terminal.UI.Color.Rgb(
            Convert.ToByte(text.Substring(1, 2), 16),
            Convert.ToByte(text.Substring(3, 2), 16),
            Convert.ToByte(text.Substring(5, 2), 16)
        );
    }

    private void Export() {
        var format = new Select<string>(
            [
                "SVG (complete chart)",
                "PNG (pixel plot)",
                "JSON (configuration)",
                "CSV (current rows)"
            ],
            0
        );
        var path = new TextBox("chart.svg");
        var replace = new CheckBox("Replace existing file", false);
        Editor("Export chart", [Field("Format", format), Field("Path", path), replace], () => {
            string target = Path.GetFullPath(path.Text ?? "chart.svg", WorkingDirectory);
            if (File.Exists(target) && !replace.IsChecked) {
                throw new IOException("File exists. Choose another path or enable Replace existing file.");
            }

            if (format.SelectedIndex == 0) {
                File.WriteAllText(target, Surface.Plot.ExportSvg());
            }
            else if (format.SelectedIndex == 1) {
                File.WriteAllBytes(target, Surface.Plot.ExportPng());
            }
            else if (format.SelectedIndex == 2) {
                File.WriteAllText(target, ExportConfiguration());
            }
            else {
                File.WriteAllText(target, ExportCsv());
            }
        });
    }

    internal string ExportCsv() {
        var table = CreateDataTable();
        var text = new StringBuilder();
        static string Quote(string value) =>
            "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        text.AppendLine(string.Join(',', table.Columns.Select(Quote)));
        foreach (var row in table.Rows) {
            text.AppendLine(string.Join(',', row.Cells.Select(Quote)));
        }

        return text.ToString();
    }
}
