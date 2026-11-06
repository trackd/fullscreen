using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
    private bool _exportingPixels;
    private static readonly JsonSerializerOptions SettingsJson = new() {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new ChartColorConverter()
        }
    };
    /// <summary>Captures serializable chart presentation settings without copying data.</summary>
    /// <returns>The current presentation settings.</returns>
    public ChartSettings CaptureSettings() =>
        new() {
            GraphKind = GraphKind,
            ReversalAmount = ReversalAmount,
            FlameReverse = FlameReverse,
            FlameFrameRows = FlameFrameRows,
            FlameFocusId = FlameFocusId,
            TaskOffset = TaskOffset,
            Title = Title,
            Minimum = Minimum,
            Maximum = Maximum,
            MinimumX = MinimumX,
            MaximumX = MaximumX,
            ShowAxes = ShowAxes,
            ShowLegend = ShowLegend,
            ShowTooltips = ShowTooltips,
            XAxis = XAxis,
            YAxis = YAxis,
            Thresholds = [.. Thresholds],
            Annotations = [.. Annotations],
            SelectedRange = SelectedRange,
            Series = [.. GetData(Math.Max(1, Bounds.Width)).Select(series => new ChartSeriesStyle(series.Property, series.Color, series.Visible, series.LineStyle, series.Marker) { Role = series.Role })]
        };
    /// <summary>Applies presentation settings while retaining the chart&apos;s data source.</summary>
    /// <param name="settings">The chart presentation settings to apply.</param>
    public void ApplySettings(ChartSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.GraphKind) || settings.Title is null || settings.XAxis is null
            || settings.YAxis is null
            || settings.Thresholds is null
            || settings.Annotations is null
            || settings.Series is null) {
            throw new ArgumentException("Invalid chart settings.");
        }

        if (settings.Series.Any(style => style is null || style.Name is null || !Enum.IsDefined(style.LineStyle)
            || !Enum.IsDefined(style.Marker) || !Enum.IsDefined(style.Role))
            || settings.XAxis.Unit is null
            || settings.YAxis.Unit is null
            || settings.XAxis.Title is null
            || settings.YAxis.Title is null
            || settings.XAxis.Format is null
            || settings.YAxis.Format is null) {
            throw new ArgumentException("Invalid chart styles.");
        }

        if (settings.Thresholds.Any(item => item is null || !double.IsFinite(item.Value) || item.Label is null)
            || settings.Annotations.Any(item => item is null || !double.IsFinite(item.X) || !double.IsFinite(item.Y) || item.Text is null)) {
            throw new ArgumentException("Invalid chart annotations.");
        }

        if (settings.SelectedRange is { } selected
            && (!double.IsFinite(selected.MinimumX) || !double.IsFinite(selected.MaximumX)
            || selected.MinimumX > selected.MaximumX)) {
            throw new ArgumentException("Invalid selection limits.");
        }

        if (settings.Minimum is { } minimum && !double.IsFinite(minimum)
            || settings.Maximum is { } maximum && !double.IsFinite(maximum)
            || settings.Minimum > settings.Maximum) {
            throw new ArgumentException("Invalid Y limits.");
        }

        if (settings.MinimumX > settings.MaximumX || settings.MinimumX is { } x0 && !double.IsFinite(x0)
            || settings.MaximumX is { } x1 && !double.IsFinite(x1)) {
            throw new ArgumentException("Invalid X limits.");
        }

        if (!Enum.IsDefined(settings.XAxis.Scale) || !Enum.IsDefined(settings.YAxis.Scale)
            || settings.XAxis.TickCount is < 2 or > 20
            || settings.YAxis.TickCount is < 2 or > 20) {
            throw new ArgumentException("Invalid axis settings.");
        }

        if (settings.XAxis.Scale == ChartScale.Logarithmic
            && (settings.MinimumX <= 0 || settings.MaximumX <= 0
            || settings.GraphKind == GraphKind.HorizontalBars && (settings.Minimum <= 0 || settings.Maximum <= 0))
            || settings.YAxis.Scale == ChartScale.Logarithmic && (settings.Minimum <= 0 || settings.Maximum <= 0)) {
            throw new ArgumentException("Logarithmic axis limits must be positive.");
        }

        if (settings.YAxis.Scale == ChartScale.Logarithmic
            && settings.GraphKind is GraphKind.HorizontalBars or GraphKind.StackedBars or GraphKind.Heatmap) {
            throw new ArgumentException("This chart uses linear or categorical Y positions.");
        }

        ChartRadial.ValidateOptions(settings.GraphKind, settings.Minimum, settings.Maximum,
            settings.XAxis.Scale, settings.YAxis.Scale, settings.Thresholds.Length, settings.Annotations.Length);
        ChartGrid.ValidateOptions(settings.GraphKind, settings.XAxis.Scale, settings.YAxis.Scale);
        ChartStackedArea.ValidateOptions(settings.GraphKind, settings.Minimum, settings.Maximum, settings.XAxis.Scale, settings.YAxis.Scale);
        ChartMarket.ValidateOptions(settings.GraphKind, settings.ReversalAmount, settings.Minimum, settings.Maximum,
            settings.MinimumX, settings.MaximumX, settings.XAxis.Scale, settings.YAxis.Scale, settings.Thresholds.Length, settings.Annotations.Length);
        if (settings.GraphKind == GraphKind.Flamegraph && (settings.Minimum is not null || settings.Maximum is not null
            || settings.MinimumX is not null || settings.MaximumX is not null || settings.XAxis.Scale == ChartScale.Logarithmic
            || settings.YAxis.Scale == ChartScale.Logarithmic || settings.Thresholds.Length != 0 || settings.Annotations.Length != 0
            || settings.SelectedRange is not null || settings.FlameFocusId is null)) {
            throw new ArgumentException("Flamegraph uses intrinsic inclusive widths/depth rows, not numeric bounds/overlays.");
        }
        if (settings.FlameFrameRows is < 1 or > 8) {
            throw new ArgumentException("FlameFrameRows must be between 1 and 8 terminal rows.");
        }
        _ = settings.XAxis.FormatValue(1);
        if (settings.GraphKind == GraphKind.Gantt) {
            ChartSchedule.ValidateOptions(settings.Minimum, settings.Maximum, settings.XAxis.Scale, settings.YAxis.Scale, settings.Thresholds.Length, settings.Annotations.Length, settings.TaskOffset);
        }
        _ = settings.YAxis.FormatValue(1);
        if (ChartTopology.Supports(settings.GraphKind)) {
            ChartTopology.ValidateOptions(settings.Minimum, settings.Maximum, settings.MinimumX, settings.MaximumX,
                settings.XAxis.Scale, settings.YAxis.Scale, settings.Thresholds.Length, settings.Annotations.Length, settings.SelectedRange);
        }
        GraphKind = settings.GraphKind;
        ReversalAmount = settings.ReversalAmount;
        FlameReverse = settings.FlameReverse;
        FlameFrameRows = settings.FlameFrameRows;
        FlameFocusId = settings.FlameFocusId;
        TaskOffset = settings.TaskOffset;
        Title = settings.Title;
        Minimum = settings.Minimum;
        Maximum = settings.Maximum;
        MinimumX = settings.MinimumX;
        MaximumX = settings.MaximumX;
        XAxis = settings.XAxis;
        YAxis = settings.YAxis;
        ShowAxes = settings.ShowAxes;
        ShowLegend = settings.ShowLegend;
        ShowTooltips = settings.ShowTooltips;
        Thresholds.Clear();
        foreach (var threshold in settings.Thresholds) {
            Thresholds.Add(threshold);
        }

        Annotations.Clear();
        foreach (var annotation in settings.Annotations) {
            Annotations.Add(annotation);
        }

        var data = GetData(Math.Max(1, Bounds.Width));
        for (int index = 0; index < data.Length; index++) {
            var style = index < settings.Series.Length && settings.Series[index].Name == data[index].Property ? settings.Series[index] : settings.Series.FirstOrDefault(style => style.Name == data[index].Property);
            if (style is not null) {
                SetSeriesStyle(index, style);
            }
        }

        SelectRange(settings.SelectedRange);
    }

    /// <summary>Serializes the current presentation settings as JSON.</summary>
    /// <returns>JSON containing chart presentation settings.</returns>
    public string ExportSettings() =>
        JsonSerializer.Serialize(CaptureSettings(), SettingsJson);
    /// <summary>Reads and applies chart presentation settings from JSON.</summary>
    /// <param name="json">The serialized JSON text.</param>
    public void ImportSettings(string json) =>
        ApplySettings(JsonSerializer.Deserialize<ChartSettings>(json, SettingsJson) ?? throw new ArgumentException("Chart settings are empty."));
    /// <summary>Exports the complete chart, including axes, labels and annotations, as standalone SVG.</summary>
    /// <param name="columns">The logical width in terminal columns. Defaults to 100.</param>
    /// <param name="rows">The logical height in terminal rows. Defaults to 30.</param>
    /// <param name="cellWidth">The width of one terminal cell in pixels. Defaults to 8.</param>
    /// <param name="cellHeight">The height of one terminal cell in pixels. Defaults to 16.</param>
    /// <returns>The SVG document text.</returns>
    public string ExportSvg(int columns = 100, int rows = 30, int cellWidth = 8, int cellHeight = 16) {
        if (columns is < 1 or > 2000 || rows is < 1 or > 1000 || cellWidth is < 1 or > 64
            || cellHeight is < 1 or > 128) {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        var snapshot = new Chart {
            Palette = Palette,
            MinHeight = rows,
            MaxHeight = rows,
            _exportingPixels = true
        };
        snapshot.SetData(GetData(columns));
        snapshot.ApplySettings(CaptureSettings());
        snapshot.FlameSearch = FlameSearch;
        snapshot.SelectedIndex = SelectedIndex;
        var buffer = VisualSnapshotRenderer.Render(snapshot, columns, rows);
        var svg = PrepareSvgText(CellBufferSvgExporter.Export(
            buffer,
            CellBufferSvgExportOptions.Default with {
                CellWidthPx = cellWidth,
                CellHeightPx = cellHeight,
                AutoCrop = false,
                FillBackground = true
            }
        ), cellHeight);
        var plotBounds = snapshot.PlotBounds;
        if (plotBounds.Width <= 0 || plotBounds.Height <= 0) {
            return svg.ToString(SaveOptions.DisableFormatting);
        }

        var plot = new SixelPlot();
        snapshot.DrawPixels(plot, plotBounds.Width * cellWidth, plotBounds.Height * cellHeight);
        var exclusions = snapshot.AnnotationBounds.ToArray();
        var pixels = new StringBuilder($"<g transform=\"translate({plotBounds.X * cellWidth} {plotBounds.Y * cellHeight})\" shape-rendering=\"crispEdges\">");
        bool Hidden(int x, int y) {
            if (exclusions.Length == 0) {
                return false;
            }
            int cellX = plotBounds.X + x / cellWidth;
            int cellY = plotBounds.Y + y / cellHeight;
            foreach (var area in exclusions) {
                if (area.Contains(cellX, cellY)) {
                    return true;
                }
            }
            return false;
        }
        for (int y = 0; y < plot.Height; y++) {
            for (int x = 0; x < plot.Width;) {
                byte color = plot.Pixels[y * plot.Width + x];
                if (color == 0 || Hidden(x, y)) {
                    x++;
                    continue;
                }

                int end = x + 1;
                while (end < plot.Width && plot.Pixels[y * plot.Width + end] == color && !Hidden(end, y)) {
                    end++;
                }

                var rgb = plot.Palette[color].ToRgb();
                pixels.Append(
                    CultureInfo.InvariantCulture,
                    $"<rect x=\"{x}\" y=\"{y}\" width=\"{end - x}\" height=\"1\" fill=\"rgb({rgb.R},{rgb.G},{rgb.B})\"/>"
                );
                x = end;
            }
        }

        pixels.Append("</g>");
        var root = svg.Root!;
        var raster = XElement.Parse(pixels.ToString());
        foreach (var element in raster.DescendantsAndSelf()) {
            element.Name = root.Name.Namespace + element.Name.LocalName;
        }
        root.Add(raster);
        var text = root.Elements(root.Name.Namespace + "text").ToArray();
        foreach (var element in text) {
            element.Remove();
            root.Add(element);
        }
        return svg.ToString(SaveOptions.DisableFormatting);
    }

    private static XDocument PrepareSvgText(string svg, int cellHeight) {
        var document = XDocument.Parse(svg);
        var root = document.Root!;
        var ns = root.Name.Namespace;
        var style = root.Element(ns + "style")!;
        // Font size is an em, not a cell's line-box height. Center a smaller em
        // within the row and clip font fallback glyphs to that row.
        style.Value = style.Value
            .Replace($"font-size:{cellHeight}px", $"font-size:{(cellHeight * .75).ToString(CultureInfo.InvariantCulture)}px", StringComparison.Ordinal)
            .Replace("dominant-baseline:text-before-edge", "dominant-baseline:central", StringComparison.Ordinal);
        var definitions = new XElement(ns + "defs");
        var rows = new HashSet<int>();
        foreach (var text in root.Elements(ns + "text")) {
            int y = int.Parse(text.Attribute("y")!.Value, CultureInfo.InvariantCulture);
            text.SetAttributeValue("y", y + cellHeight / 2d);
            text.SetAttributeValue("lengthAdjust", "spacingAndGlyphs");
            string id = "chart-text-row-" + y.ToString(CultureInfo.InvariantCulture);
            text.SetAttributeValue("clip-path", $"url(#{id})");
            if (rows.Add(y)) {
                definitions.Add(new XElement(ns + "clipPath",
                    new XAttribute("id", id),
                    new XAttribute("clipPathUnits", "userSpaceOnUse"),
                    new XElement(ns + "rect",
                        new XAttribute("x", 0),
                        new XAttribute("y", y),
                        new XAttribute("width", root.Attribute("width")!.Value),
                        new XAttribute("height", cellHeight))));
            }
        }
        root.AddFirst(definitions);
        return document;
    }

    /// <summary>Exports the pixel plot as PNG; use SVG when axes and terminal text are required.</summary>
    /// <param name="width">The output image width in pixels. Defaults to 1000.</param>
    /// <param name="height">The output image height in pixels. Defaults to 500.</param>
    /// <returns>The encoded PNG image bytes.</returns>
    public byte[] ExportPng(int width = 1000, int height = 500) {
        var plot = new SixelPlot();
        DrawPixels(plot, width, height);
        using var result = new MemoryStream();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string name, ReadOnlySpan<byte> payload) {
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, payload.Length);
            result.Write(size);
            byte[] type = Encoding.ASCII.GetBytes(name);
            result.Write(type);
            result.Write(payload);
            uint crc = uint.MaxValue;
            foreach (byte value in type) {
                crc = Crc(crc, value);
            }

            foreach (byte value in payload) {
                crc = Crc(crc, value);
            }

            BinaryPrimitives.WriteUInt32BigEndian(size, ~crc);
            result.Write(size);
        }

        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) {
            byte[] row = new byte[1 + width * 3];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var color = plot.Palette[plot.Pixels[y * width + x]].ToRgb();
                    row[1 + x * 3] = color.R;
                    row[2 + x * 3] = color.G;
                    row[3 + x * 3] = color.B;
                }

                zip.Write(row);
            }
        }

        Chunk("IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        Chunk("IEND", []);
        return result.ToArray();
    }

    private static uint Crc(uint crc, byte value) {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) {
            crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }

        return crc;
    }

    private sealed class ChartColorConverter : JsonConverter<Color> {
        public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) {
            string text = reader.GetString() ?? "";
            if (text.Length != 7 || text[0] != '#') {
                throw new JsonException("Colors must be #RRGGBB.");
            }

            return Color.Rgb(
                Convert.ToByte(text.Substring(1, 2), 16),
                Convert.ToByte(text.Substring(3, 2), 16),
                Convert.ToByte(text.Substring(5, 2), 16)
            );
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) {
            var rgb = value.ToRgb();
            writer.WriteStringValue($"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");
        }
    }
}
