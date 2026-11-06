using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;

public partial class Chart {
    private readonly ChartRadial _radial = new();
    private (GraphKind Kind, ChartRange Range, ChartAxis Axis)? _radialLabelKey;
    private string _radialRangeLabel = "";
    private (ChartDataSeries[] Data, long Version, Rectangle Bounds, GraphKind Kind, bool Axes)? _radialLabelsKey;
    private RadialLabel[] _radialLabels = [];
    private readonly record struct RadialLabel(Rectangle Bounds, string Text);

    private RadialLabel[] GetRadialLabels(ChartDataSeries[] data) {
        var bounds = PlotBounds;
        var key = (data, DataVersion, bounds, GraphKind, ShowAxes);
        if (_radialLabelsKey == key) {
            return _radialLabels;
        }
        var labels = new List<RadialLabel>();
        bool empty = true;
        foreach (var series in data) {
            foreach (var point in series.Span) {
                empty &= GraphKind == GraphKind.Pie ? point.Y is not > 0 : point.Y is null;
            }
        }
        if (empty) {
            string text = GraphKind == GraphKind.Pie ? "No positive values" : "No radial values";
            labels.Add(new(new(bounds.X, bounds.Y, Math.Min(text.Length, bounds.Width), 1), text));
        }
        if (ShowAxes && GraphKind == GraphKind.Radar && data.Length > 0) {
            int categories = 0;
            foreach (var series in data) {
                categories = Math.Max(categories, series.Count);
            }
            var (CenterX, CenterY, RadiusX, RadiusY) = ChartRadial.Layout(GraphKind, bounds.Width, bounds.Height, true);
            int stride = Math.Max(1, (int)Math.Ceiling(categories / (double)Math.Max(3, bounds.Width / 8)));
            for (int index = 0; index < categories; index += stride) {
                string label = "";
                foreach (var series in data) {
                    if (index < series.Count) {
                        label = series.Points[index].Label;
                        break;
                    }
                }
                double angle = Math.Tau * index / categories - Math.PI / 2;
                int x = (int)Math.Round(CenterX + Math.Cos(angle) * RadiusX);
                int y = (int)Math.Round(CenterY + Math.Sin(angle) * RadiusY);
                int length = Math.Min(label.Length, Math.Max(1, bounds.Width / Math.Max(2, Math.Min(categories, 8))));
                x = Math.Clamp(x - length / 2, 0, Math.Max(0, bounds.Width - length));
                labels.Add(new(new(bounds.X + x, bounds.Y + y, length, 1), label));
            }
        }
        _radialLabels = [.. labels];
        _radialLabelsKey = key;
        return _radialLabels;
    }

    private void RenderRadialLabels(CellBuffer buffer, ChartDataSeries[] data, ChartRange range) {
        foreach (var label in GetRadialLabels(data)) {
            WriteFeature(buffer, label.Bounds.X, label.Bounds.Y, label.Text, Palette.Body.WithForeground(Palette.ChartLabel), label.Bounds.Width);
        }
        if (!ShowAxes) {
            return;
        }
        var key = (GraphKind, range, YAxis);
        if (_radialLabelKey != key) {
            _radialRangeLabel = GraphKind == GraphKind.Pie ? "Slices: share of positive total"
                : "Radial scale: " + YAxis.FormatValue(range.MinY) + " .. " + YAxis.FormatValue(range.MaxY);
            _radialLabelKey = key;
        }
        var bounds = PlotBounds;
        WriteFeature(buffer, bounds.X, bounds.Bottom, _radialRangeLabel, Palette.Body.WithForeground(Palette.ChartLabel), bounds.Width);
    }

    private Color RadialColor(int index) =>
        GraphKind == GraphKind.Gauge && index >= 2 ? Palette.GaugeColor(index - 2)
        : index == 1 ? Palette.ChartGridColor : DefaultSeriesColor(index - 2);

    private void DrawRadialCells(CellBuffer buffer, ChartDataSeries[] data) {
        var bounds = PlotBounds;
        _radial.Build(data, RenderRange(data), GraphKind);
        if (_radial.Empty) {
            WriteFeature(buffer, bounds.X, bounds.Y, GraphKind == GraphKind.Pie ? "No positive values" : "No radial values", Palette.Muted, bounds.Width);
            return;
        }
        if (GraphKind != GraphKind.Radar) {
            var (CenterX, CenterY, RadiusX, RadiusY) = ChartRadial.Layout(GraphKind, bounds.Width, bounds.Height, true);
            for (int y = 0; y < bounds.Height; y++) {
                for (int x = 0; x < bounds.Width; x++) {
                    int color = _radial.Sample((x - CenterX) / RadiusX, (y - CenterY) / RadiusY, out _, out bool overflow);
                    if (color > 0) {
                        buffer.WriteText(bounds.X + x, bounds.Y + y, overflow ? "!" : "█", Palette.Body.WithForeground(RadialColor(color)));
                    }
                }
                for (int index = 0; index < data[0].Count; index++) {
                    if (data[0].Points[index].Index != SelectedIndex) {
                        continue;
                    }
                    var anchor = _radial.Anchor(index);
                    if (double.IsFinite(anchor.X)) {
                        var (X, Y) = ChartRadial.Position(anchor, GraphKind, bounds.Width, bounds.Height, true);
                        buffer.WriteText(bounds.X + (int)Math.Round(X), bounds.Y + (int)Math.Round(Y), "◆", Palette.AccentStyle);
                    }
                }
            }
        }
        else {
            foreach (var segment in _radial.Segments) {
                var (X, Y) = ChartRadial.Position(segment.From, GraphKind, bounds.Width, bounds.Height, true);
                var to = ChartRadial.Position(segment.To, GraphKind, bounds.Width, bounds.Height, true);
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(to.X - X), Math.Abs(to.Y - Y))));
                var color = segment.Grid ? RadialColor(1) : data[segment.From.Series].Color ?? RadialColor(segment.From.Color);
                var style = Palette.Body.WithForeground(color);
                for (int step = 0; step <= steps; step++) {
                    if (!segment.Grid && !RadialStrokeVisible(data[segment.From.Series].LineStyle, step)) {
                        continue;
                    }
                    int x = (int)Math.Round(X + (to.X - X) * step / steps);
                    int y = (int)Math.Round(Y + (to.Y - Y) * step / steps);
                    buffer.WriteText(bounds.X + x, bounds.Y + y, segment.Grid ? "·" : "•", style);
                }
            }
            foreach (var vertex in _radial.Vertices) {
                if (!double.IsFinite(vertex.X)) {
                    continue;
                }
                var (X, Y) = ChartRadial.Position(vertex, GraphKind, bounds.Width, bounds.Height, true);
                var point = data[vertex.Series].Points[vertex.Point];
                if (vertex.Overflow || point.Index == SelectedIndex || data[vertex.Series].Marker != ChartMarker.None) {
                    buffer.WriteText(bounds.X + (int)Math.Round(X), bounds.Y + (int)Math.Round(Y),
                        vertex.Overflow ? "!" : point.Index == SelectedIndex ? "◆" : "•",
                        Palette.Body.WithForeground(data[vertex.Series].Color ?? RadialColor(vertex.Color)));
                }
            }
        }
    }

    private static bool RadialStrokeVisible(ChartLineStyle style, int step) =>
        style == ChartLineStyle.Solid || style == ChartLineStyle.Dashed && step % 6 < 4
        || style == ChartLineStyle.Dotted && step % 3 == 0;

    private ChartSelection? PickRadial(ChartDataSeries[] data, int x, int y) {
        var bounds = PlotBounds;
        _radial.Build(data, RenderRange(data), GraphKind);
        var hit = _radial.Pick(x - bounds.X, y - bounds.Y, bounds.Width, bounds.Height, true);
        if (hit.Series < 0) {
            return null;
        }
        var series = data[hit.Series];
        var point = series.Points[hit.Point];
        return new(series.SourceIndex < 0 ? hit.Series : series.SourceIndex, series.Property, point.Index,
            point.X, point.Y!.Value, point.Label, point.Source);
    }
}

internal sealed partial class SixelPlot {
    private readonly ChartRadial _radial = new();
    private void DrawRadial(ChartDataSeries[] data, ChartRange range, GraphKind kind, Themes.ThemePalette? theme, int selected) {
        _radial.Build(data, range, kind);
        var palette = theme ?? Themes.ThemePalette.Default;
        Palette[1] = palette.ChartGridColor;
        for (int color = 2; color <= ColorCount; color++) {
            Palette[color] = kind == GraphKind.Gauge
                ? palette.GaugeColor(color - 2)
                : Chart.AutomaticSeriesColor(palette, _ink, kind, color - 2);
        }
        if (kind == GraphKind.Radar) {
            for (int index = 0; index < Math.Min(Themes.ThemePalette.SeriesColorCount, data.Length); index++) {
                if (data[index].Color is { } color) {
                    Palette[2 + (data[index].SourceIndex < 0 ? index : data[index].SourceIndex) % Themes.ThemePalette.SeriesColorCount] = color;
                }
            }
        }
        _hasPalette = false;
        if (kind != GraphKind.Radar) {
            var (CenterX, CenterY, RadiusX, RadiusY) = ChartRadial.Layout(kind, Width, Height, false);
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    int color = _radial.Sample((x - CenterX) / RadiusX, (y - CenterY) / RadiusY, out _, out _);
                    if (color > 0) {
                        SetPixel(x, y, (byte)color);
                    }
                }
            }
            if (data.Length > 0) {
                for (int index = 0; index < data[0].Count; index++) {
                    if (data[0].Points[index].Index != selected) {
                        continue;
                    }
                    var anchor = _radial.Anchor(index);
                    if (double.IsFinite(anchor.X)) {
                        var (X, Y) = ChartRadial.Position(anchor, kind, Width, Height, false);
                        for (int delta = -2; delta <= 2; delta++) {
                            int x = (int)Math.Round(X), y = (int)Math.Round(Y);
                            RadialPixel(x + delta, y, 1);
                            RadialPixel(x, y + delta, 1);
                        }
                    }
                }
            }
            return;
        }
        foreach (var segment in _radial.Segments) {
            var (X, Y) = ChartRadial.Position(segment.From, kind, Width, Height, false);
            var to = ChartRadial.Position(segment.To, kind, Width, Height, false);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(to.X - X), Math.Abs(to.Y - Y))));
            for (int step = 0; step <= steps; step++) {
                if (!segment.Grid && !RadialVisible(data[segment.From.Series].LineStyle, step)) {
                    continue;
                }
                int x = (int)Math.Round(X + (to.X - X) * step / steps);
                int y = (int)Math.Round(Y + (to.Y - Y) * step / steps);
                if ((uint)x < (uint)Width && (uint)y < (uint)Height) {
                    SetPixel(x, y, (byte)(segment.Grid ? 1 : segment.From.Color));
                }
            }
        }
        foreach (var vertex in _radial.Vertices) {
            if (!double.IsFinite(vertex.X) || !vertex.Overflow && data[vertex.Series].Marker == ChartMarker.None
                && data[vertex.Series].Points[vertex.Point].Index != selected) {
                continue;
            }
            var (X, Y) = ChartRadial.Position(vertex, kind, Width, Height, false);
            int size = vertex.Overflow ? 2 : 1;
            for (int delta = -size; delta <= size; delta++) {
                RadialPixel((int)Math.Round(X) + delta, (int)Math.Round(Y), (byte)vertex.Color);
                RadialPixel((int)Math.Round(X), (int)Math.Round(Y) + delta, (byte)vertex.Color);
            }
        }
    }

    private static bool RadialVisible(ChartLineStyle style, int step) =>
        style == ChartLineStyle.Solid || style == ChartLineStyle.Dashed && step % 12 < 8
        || style == ChartLineStyle.Dotted && step % 5 == 0;

    private void RadialPixel(int x, int y, byte color) {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height) {
            SetPixel(x, y, color);
        }
    }
}
