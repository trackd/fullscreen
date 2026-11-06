using System.Security.Cryptography;
using System.Text;
using fullscreen.Charts;
using fullscreen.Themes;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ChartRenderingRegressionTests {
    [TestMethod]
    [DataRow(GraphKind.Line, "6ECF9148CD855F953AA7E1414AEBAAF4972ACCBAA4D22D858160892AC19664D7", "DFBA17B507C1CC05C637F033D7B3B5060CC204DA6137FADDF068ADA9A1CEBF71")]
    [DataRow(GraphKind.Area, "0B0867CF2213C27378889883A277F473A2148CC339D49CBF5B2AF7E6D2A88031", "9D6E10AA40DCEE004D746D54F143F10E2FDA7184758D325263AA875BD472DC26")]
    [DataRow(GraphKind.Bars, "42CB49F43142388601C0B96E94616EB290CB99DDA8251ED14110BBC08651E67A", "07783DEEC68EC547B497D9159079DCFD0439081B0AA4C41C0E35345A353DCE8F")]
    [DataRow(GraphKind.Step, "D12B9A5D026B20DBFD45D23C5C8D7EC8D1E7346C4D1F8397E6929E8E9CA1C24A", "0D0F612F5D49032ADEADDE390C51F56B784EBEE24A2A4B0A8C5412DABCA3F952")]
    [DataRow(GraphKind.Scatter, "AE8ADA158CA3DE6514ADDE70FED13E3857B5E6C5255629D29726EB4C7DD20263", "820CDF5C0987BF7EF5A43391203C8587CAD45D6C25DB17C968C32CF70FA8C3C5")]
    [DataRow(GraphKind.Stem, "CA7B1B0CD25E0A6BD690F6BC3128D632981E8893E86FA97DC72D5FCB5D08B078", "AD66C931BD920B3BDFF69119D256242A02660CC438B679FACD7F518361AED757")]
    [DataRow(GraphKind.Histogram, "BC8FE3B4D83B5A86CCBABFB4FE3AEB814E66C5BEC8B7587C7A24F6F28B127827", "9D9C00D5E412898560331780433FB52296B17A60C0375B1FBFB9F576DE32F53E")]
    [DataRow(GraphKind.BoxPlot, "7946050E05664572C00AEE3F7F5F74761A470D7281A80E28AEEC1D9718592001", "2F625E82662B41C42BC5BC59F7D2D201A7F7DA1041F5CE4C30037CA95870A457")]
    [DataRow(GraphKind.HorizontalBars, "C5FB883ACFD183BE44F4F79525EFABF3EB7D13AEE28532AE40CBF6355642A2AC", "2A9D49A2519237FCF9EC1FD8AE564BD2B45148B5EB416CB9FBF2CF55440106BB")]
    [DataRow(GraphKind.StackedBars, "937211A8675C9484336C0289CF8623FD92000CC60F66D71D6F21F983BC3F5D59", "3955E028413AC866558DE7B31EB87FCE3078765347CFD85B341B08ABF97AFAD9")]
    [DataRow(GraphKind.Heatmap, "DFEDAF3F38F6F450BE6370DDBCC2DABDE82902AD5503DF99D582F3540669E0C4", "7F512B2ED169CEC79A3880C0E8E0F68B16E804D9A27C60EFD49BB681CA54CE44")]
    [DataRow(GraphKind.ErrorBars, "FBD3C8A853863D45B95839A375A90B07243C450E9D34FFAAC3AFD16B6F18D2BC", "B80D368FFAA0355B08B675304E7D3141EE5A34928B0301DC764D40915B25C0B2")]
    public void CellAndPixelOutputPreservesSignedGapsStylesAndOverlays(GraphKind kind, string expectedCells, string expectedPixels) {
        var chart = new Chart {
            GraphKind = kind,
            ShowAxes = true,
            ShowLegend = true,
            Title = "Regression",
            SelectedIndex = 2
        };
        var first = new ChartSeries("First", [-4, 2, double.NaN, 8, 3]);
        var second = new ChartSeries("Second", [3, -2, 1, 6, 100]);
        first.LowerValues.AddRange([-5, 1, double.NaN, 7, 2]);
        first.UpperValues.AddRange([-3, 3, double.NaN, 9, 4]);
        chart.Series.Add(first);
        chart.Series.Add(second);
        chart.SetSeriesStyle(0, new("First", LineStyle: ChartLineStyle.Dashed, Marker: ChartMarker.Diamond));
        chart.Thresholds.Add(new(1, "limit"));
        chart.Annotations.Add(new(1, 2, "note"));
        ThemeCatalog.Load("Monokai").Apply(chart);
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 48, 12).ToMarkupLines());
        var raster = new SixelPlot();
        chart.DrawPixels(raster, 96, 48);
        string cellHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells)));
        string pixelHash = Convert.ToHexString(SHA256.HashData(raster.Pixels));
        Console.WriteLine($"{kind}: cell {cellHash}, pixel {pixelHash}");
        Assert.AreEqual(expectedCells, cellHash, "Cell output changed from its semantic chart-palette baseline.");
        Assert.AreEqual(expectedPixels, pixelHash, "Pixel output changed from its verified baseline.");
    }
}
