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
            Assert.AreEqual(palette.Accent, chart.PixelKey.Ink, "Headless raster export must apply the current palette before any UI draw.");
            Assert.AreEqual(palette.Background, pixels.Palette[0]);
            var buffer = VisualSnapshotRenderer.Render(chart, 100, 32);
            var cells = XDocument.Parse(CellBufferSvgExporter.Export(buffer));
            Color[] expected = name == "Monokai"
                ? [palette.Accent, palette.Error, palette.Purple]
                : [palette.Accent, palette.Purple, palette.Warning];
            Assert.IsGreaterThanOrEqualTo(10_000, Distance(expected[0], expected[1]), name);
            Assert.IsGreaterThanOrEqualTo(10_000, Distance(expected[1], expected[2]), name);
            Assert.HasCount(7, pixels.Palette, "Keep the existing indexed palette budget.");
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
            Assert.AreEqual(palette.Success, Chart.AutomaticSeriesColor(palette, palette.Accent, GraphKind.Area, 1),
                "Do not change unrelated chart palettes.");
        }
    }

    [TestMethod]
    [DataRow(GraphKind.StackedBars)]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    [DataRow(GraphKind.Marimekko)]
    public void ExplicitStackColorsAndCustomThemeHonorOverridesAndStableSourceIdentity(GraphKind kind) {
        var palette = ThemeCatalog.FromDictionary(new Hashtable {
            ["Background"] = "#102030", ["Accent"] = "#10C0E0", ["Purple"] = "#C030E0",
            ["Warning"] = "#E0B010", ["Success"] = "#20B080", ["Error"] = "#F03060"
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
        Assert.AreEqual(palette.Warning, pixels.Palette[4]);
        data[0].Visible = false;
        chart.DrawPixels(pixels, 800, 512);
        Assert.AreEqual(data[1].Color, pixels.Palette[2]);
        Assert.AreEqual(palette.Warning, pixels.Palette[3], "Hiding a series must not renumber automatic source colors.");
        var projected = chart.GetRenderData(100);
        Assert.AreEqual(1, projected[0].SourceIndex);
        Assert.AreEqual(2, projected[1].SourceIndex);
        Assert.AreEqual(palette.Warning, projected[1].Color);
        Assert.IsNull(data[2].Color, "Automatic colors must not become persistent explicit overrides.");
    }
}
