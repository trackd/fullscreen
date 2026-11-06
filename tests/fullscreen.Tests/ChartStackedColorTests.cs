using System.Collections;
using System.Xml.Linq;
using fullscreen.Charts;
using fullscreen.Themes;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartStackedColorTests {
    private static ChartDataSeries[] Data() => [
        new("A", [new(0, 0, 2, "First"), new(1, 1, 2, "Second")]),
        new("B", [new(0, 0, 3, "First"), new(1, 1, 3, "Second")]),
        new("C", [new(0, 0, 4, "First"), new(1, 1, 4, "Second")])
    ];

    private static string Rgb(Color color) {
        var value = color.ToRgb();
        return $"rgb({value.R},{value.G},{value.B})";
    }

    private static int Distance(Color first, Color second) {
        var a = first.ToRgb();
        var b = second.ToRgb();
        return (a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B);
    }

    [TestMethod]
    [DataRow(GraphKind.StackedBars)]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    [DataRow(GraphKind.Marimekko)]
    public void AutomaticStackColorsAreDistinctAndMatchCellsRasterAndLegends(GraphKind kind) {
        foreach (string name in new[] { "Default", "Monokai", "Light+" }) {
            var palette = ThemeCatalog.Load(name);
            var chart = new Chart { Palette = palette, GraphKind = kind, ShowLegend = true };
            chart.SetData(Data());
            var pixels = new SixelPlot();
            chart.DrawPixels(pixels, 800, 512);
            Assert.AreEqual(palette.ChartBlue, chart.PixelKey.Ink, "Headless raster export must apply the current palette before any UI draw.");
            Assert.AreEqual(palette.Background, pixels.Palette[0]);
            var buffer = VisualSnapshotRenderer.Render(chart, 100, 32);
            var cells = XDocument.Parse(CellBufferSvgExporter.Export(buffer));
            Color[] expected = [palette.ChartBlue, palette.ChartOrange, palette.ChartPurple];
            Assert.IsGreaterThanOrEqualTo(10_000, Distance(expected[0], expected[1]), name);
            Assert.IsGreaterThanOrEqualTo(10_000, Distance(expected[1], expected[2]), name);
            Assert.AreEqual(9, pixels.ColorCount, "Only gauge rendering activates the expanded gradient palette.");
            for (int series = 0; series < expected.Length; series++) {
                Assert.AreEqual(expected[series], pixels.Palette[2 + series], $"{name} {kind}");
                Assert.IsTrue(pixels.Pixels.Contains((byte)(2 + series)));
                Assert.IsTrue(cells.Descendants().Any(element => element.Name.LocalName == "text"
                    && element.Value.Contains('\u2588') && (string?)element.Attribute("fill") == Rgb(expected[series])),
                    $"{name} {kind}: native bands must use their actual series colors.");
                Assert.IsTrue(cells.Descendants().Any(element => element.Name.LocalName == "text"
                    && element.Value.Contains($" {chart.GetData(100)[series].Property}", StringComparison.Ordinal)
                    && (string?)element.Attribute("fill") == Rgb(expected[series])),
                    $"{name} {kind}: legend and band colors must match.");
            }
            Assert.AreEqual(palette.ChartOrange, Chart.AutomaticSeriesColor(palette, palette.ChartBlue, GraphKind.Area, 1));
        }
    }

    [TestMethod]
    [DataRow(GraphKind.StackedBars)]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    [DataRow(GraphKind.Marimekko)]
    public void ExplicitStackColorsAndCustomThemeHonorOverridesAndStableSourceIdentity(GraphKind kind) {
        var palette = ThemeCatalog.FromDictionary(new Hashtable {
            ["Background"] = "#102030",
            ["ChartBlue"] = "#10C0E0",
            ["ChartPurple"] = "#C030E0",
            ["ChartYellow"] = "#E0B010",
            ["ChartGreen"] = "#20B080",
            ["ChartRed"] = "#F03060"
        });
        var data = Data();
        data[0].Color = Color.Rgb(240, 80, 20);
        data[1].Color = Color.Rgb(30, 80, 240);
        var chart = new Chart { Palette = palette, GraphKind = kind, ShowLegend = true };
        chart.SetData(data);
        _ = VisualSnapshotRenderer.Render(chart, 100, 32);
        var pixels = new SixelPlot();
        chart.DrawPixels(pixels, 800, 512);
        Assert.AreEqual(data[0].Color, pixels.Palette[2]);
        Assert.AreEqual(data[1].Color, pixels.Palette[3]);
        Assert.AreEqual(palette.ChartPurple, pixels.Palette[4]);
        data[0].Visible = false;
        chart.DrawPixels(pixels, 800, 512);
        Assert.AreEqual(data[1].Color, pixels.Palette[2]);
        Assert.AreEqual(palette.ChartPurple, pixels.Palette[3], "Hiding a series must not renumber automatic source colors.");
        var projected = chart.GetRenderData(100);
        Assert.AreEqual(1, projected[0].SourceIndex);
        Assert.AreEqual(2, projected[1].SourceIndex);
        Assert.AreEqual(palette.ChartPurple, projected[1].Color);
        Assert.IsNull(data[2].Color, "Automatic colors must not become persistent explicit overrides.");
    }

    [TestMethod]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.StackedBars)]
    [DataRow(GraphKind.Radar)]
    public void EightSeriesUseTheSameIndependentColorsInCellsAndRaster(GraphKind kind) {
        var palette = ThemeCatalog.Load("Dark+");
        var data = Enumerable.Range(0, 8).Select(index => new ChartDataSeries($"Series{index}",
            [new(0, 0, index + 1, "A"), new(1, 1, index + 2, "B"), new(2, 2, index + 1, "C")])).ToArray();
        var chart = new Chart { Palette = palette, GraphKind = kind, ShowLegend = true };
        chart.SetData(data);
        var cells = XDocument.Parse(CellBufferSvgExporter.Export(VisualSnapshotRenderer.Render(chart, 160, 40)));
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 640, 320);
        for (int index = 0; index < data.Length; index++) {
            var color = palette.SeriesColor(index);
            Assert.AreEqual(color, plot.Palette[index + 2]);
            Assert.IsTrue(plot.Pixels.Contains((byte)(index + 2)), $"{kind} must draw series {index}.");
            Assert.IsTrue(cells.Descendants().Any(element => element.Name.LocalName == "text"
                && element.Value.Contains($" Series{index}", StringComparison.Ordinal)
                && (string?)element.Attribute("fill") == Rgb(color)));
        }
        Assert.AreEqual(palette.ChartBlue, Chart.AutomaticSeriesColor(palette, palette.ChartBlue, kind, 8));
    }

    [TestMethod]
    public void HeatmapUsesCustomSequentialEndpointsInBothRenderers() {
        var palette = ThemeCatalog.FromDictionary(new Hashtable {
            ["ChartSequentialLow"] = "#123456",
            ["ChartSequentialHigh"] = "#ABCDEF"
        });
        var chart = new Chart { Palette = palette, GraphKind = GraphKind.Heatmap };
        chart.SetData([new("Values", [new(0, 0, 0, "Low"), new(1, 1, 10, "High")])]);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 160, 80);
        Assert.AreEqual(palette.ChartSequentialLow, plot.Palette[2]);
        Assert.AreEqual(palette.ChartSequentialHigh, plot.Palette[6]);
        var svg = CellBufferSvgExporter.Export(VisualSnapshotRenderer.Render(chart, 80, 20));
        Assert.Contains(Rgb(palette.ChartSequentialLow), svg);
        Assert.Contains(Rgb(palette.ChartSequentialHigh), svg);
    }
}
