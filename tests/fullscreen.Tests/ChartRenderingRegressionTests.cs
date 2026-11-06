using System.Security.Cryptography;
using System.Text;
using fullscreen.Charts;
using fullscreen.Themes;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ChartRenderingRegressionTests {
    [TestMethod]
    [DataRow(GraphKind.Line, "8CE3793937D99C21C8A50E7B361095FE701DEA6B53913E14F89F968028CE32D7", "DFBA17B507C1CC05C637F033D7B3B5060CC204DA6137FADDF068ADA9A1CEBF71")]
    [DataRow(GraphKind.Area, "AB4F0709347300F549DF91063D4108FA61DB0C9EC7A46E224DF034B171621699", "9D6E10AA40DCEE004D746D54F143F10E2FDA7184758D325263AA875BD472DC26")]
    [DataRow(GraphKind.Bars, "A78ED0ABA144F65FF67178654510F88836DEA7015B2AEB0DCC0BE229F5D2B155", "C788112BB8B0B3E50488560EF3DDA06256B16CD84800A70BA41A5537040CB43F")]
    [DataRow(GraphKind.Step, "4738C05B0C61DA1D4732F9F8EBC9587A9D2817A818D3FA5D2016F14BC8B1A8B8", "0D0F612F5D49032ADEADDE390C51F56B784EBEE24A2A4B0A8C5412DABCA3F952")]
    [DataRow(GraphKind.Scatter, "EA0700C6765E3182DC5F5DDEC6EEC19487659F0130EC3F9D8BC2E2357C90DEB2", "820CDF5C0987BF7EF5A43391203C8587CAD45D6C25DB17C968C32CF70FA8C3C5")]
    [DataRow(GraphKind.Stem, "270A097106B88F44D29629803175F20E9E39E84260FD940D92C5323999267CA8", "AD66C931BD920B3BDFF69119D256242A02660CC438B679FACD7F518361AED757")]
    [DataRow(GraphKind.Histogram, "AFC0B47F0FDF87AD372F2FBAAE77E1A9B89639E6285130E71C566AA0FAD31D53", "9D9C00D5E412898560331780433FB52296B17A60C0375B1FBFB9F576DE32F53E")]
    [DataRow(GraphKind.BoxPlot, "616EF46E21DF397D7D7474D62C67750A1C8C111C8D38515FE901BA6BD15E762E", "2F625E82662B41C42BC5BC59F7D2D201A7F7DA1041F5CE4C30037CA95870A457")]
    [DataRow(GraphKind.HorizontalBars, "8D9356756DE94AE586041F536C499CD6546CFF9C86F3AC9E0947D09387D6E68B", "296461A18F2F8B51F7BD1EEB0DF66472997D0AA687A6D95A6EFEEFB0F790CC9E")]
    [DataRow(GraphKind.StackedBars, "6F8233F29F40DD1369E6337DBA54113CC3AF66B26DA01242F368B22C5861E2F0", "3955E028413AC866558DE7B31EB87FCE3078765347CFD85B341B08ABF97AFAD9")]
    [DataRow(GraphKind.Heatmap, "8E675A6EF1454EDFC6480A8C2A5E78D1496B56D65C8DB20B5D825D4B0BC37423", "7F512B2ED169CEC79A3880C0E8E0F68B16E804D9A27C60EFD49BB681CA54CE44")]
    [DataRow(GraphKind.ErrorBars, "3724BA6681C8CEF0204C3A580DDC429DA96814CADAD47BBCF16A64D65C36D760", "B80D368FFAA0355B08B675304E7D3141EE5A34928B0301DC764D40915B25C0B2")]
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
        Assert.AreEqual(expectedCells, cellHash, "Cell output changed from its verified baseline; Heatmap uses the completed matrix layout and StackedBars uses distinct categorical colors.");
        Assert.AreEqual(expectedPixels, pixelHash, "Pixel output changed from its verified baseline.");
    }
}
