using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;
using fullscreen.Charts;
using fullscreen.Themes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartCompositionTests {
    public TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow(GraphKind.Line, "450E2DB515688FD9AE25F92B49BCCE44BDFA53E18E3C2F4CDEED8CED84CAE0C8", "807CFB6BB764DAA6C5BB0A5F2A1F5ADFCEB1AB0768F1360066AA824FB4224844")]
    [DataRow(GraphKind.Area, "C18D32BE995ED4CE9BD31C6B13965869B9E3D84C5E36C7E78F193FCF59158AEE", "3844E26543AD26EF728AD2387EA8B75A025BD898DC888600D04FF5C1D776DACC")]
    [DataRow(GraphKind.Bars, "F10FAEB606D2D66D8AA3CCEED9A9BB834234A3D5169B9E47D8D2DA0217FA711D", "23FDBEE8B7C527DD11F1AF4BC188E841B7223A3E77843398D003CB746BCF7DA0")]
    [DataRow(GraphKind.ColumnLine, "E787E373201A232D751E913A439C8C6135AF3D5F78BBE6FCB392C4242FCB60F7", "0F5F2B84BF0E9D4031F4F6FF57F19DDA8475D696B520CA6216E07F24778C9D3C")]
    public void MultipleSeriesRenderGapsSignedValuesAndReuseWarmRasterAndLegend(GraphKind kind, string expectedCells, string expectedPixels) {
        var chart = new Chart { GraphKind = kind, ShowLegend = true };
        chart.Series.Add(new("Actual", [-2, double.NaN, 6]));
        chart.Series.Add(new("Target", [4, 8]));
        ThemeCatalog.Load("Monokai").Apply(chart);
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 60, 15).ToMarkupLines());
        Assert.Contains("Actual", cells);
        Assert.Contains("Target", cells);
        Assert.IsGreaterThan(0, chart.PlotBounds.Height);
        Assert.IsNull(chart.GetData(60)[0].Points[1].Y);
        var plot = new SixelPlot();
        for (int index = 0; index < 10; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.PlotBounds;
        }

        Assert.Contains((byte)2, plot.Pixels.ToArray());
        Assert.Contains((byte)3, plot.Pixels.ToArray());
        TestContext.WriteLine($"{kind}: cell {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells)))}, pixel {Convert.ToHexString(SHA256.HashData(plot.Pixels))}");
        Assert.AreEqual(expectedCells, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells))));
        Assert.AreEqual(expectedPixels, Convert.ToHexString(SHA256.HashData(plot.Pixels)));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.PlotBounds;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"{kind}: 100 warmed 240x120 redraws and legend layouts = {allocated} bytes.");
        Assert.AreEqual(0L, allocated);
    }

    [TestMethod]
    public void UnequalGroupedSeriesShareCategorySlotsAndClicksInspectColumnInterior() {
        object first = new(), second = new();
        var chart = new Chart { GraphKind = GraphKind.Bars };
        chart.Series.Add(new("First", [2, 4, -2]));
        chart.Series.Add(new("Second", [8, 6]));
        chart.Series[0].Sources.Add(first);
        chart.Series[1].Sources.Add(second);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        var columns = new ChartColumns(data, chart.GraphKind);
        Assert.AreEqual(columns.Center(data[0].Points[0].X, range, 60), columns.Center(data[1].Points[0].X, range, 60));
        var a = columns.Bounds(0, 0, range, 60);
        var b = columns.Bounds(1, 0, range, 60);
        Assert.IsLessThan(b.X, a.Right, "Grouped bars leave a visible gap between their colors.");
        Assert.IsLessThanOrEqualTo(60, b.Right);
        Assert.AreEqual(12, Array.IndexOf(Enum.GetValues<GraphKind>(), GraphKind.ColumnLine), "Existing enum values must not move.");
    }

    [TestMethod]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.ColumnLine)]
    public async Task ColumnInteriorAndLinePointsRetainSeriesAndOriginalSource(GraphKind kind) {
        object columnSource = new(), lineSource = new();
        var chart = new Chart { GraphKind = kind };
        var column = new ChartSeries("Columns", [10, -2, 4]);
        var line = new ChartSeries("Line", [6, 8]);
        column.Sources.Add(columnSource);
        line.Sources.Add(lineSource);
        chart.Series.Add(column);
        chart.Series.Add(line);
        ChartSelection? selected = null, invoked = null;
        chart.SelectionChanged += value => selected = value;
        chart.PointInvoked += value => invoked = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 60, 20);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        var columns = new ChartColumns(data, kind);
        var bounds = columns.Bounds(0, 0, range, 60);
        int columnX = bounds.X + bounds.Width / 2;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, columnX, 10, () => selected is not null);
        Assert.AreSame(columnSource, selected!.Source);
        Assert.AreEqual(0, selected.SeriesIndex);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, columnX, 10, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, columnX, 10, () => invoked is not null);
        Assert.AreSame(columnSource, invoked!.Source);
        if (kind == GraphKind.ColumnLine) {
            selected = null;
            int x = columns.Center(0, range, 60);
            int y = (int)Math.Round((1 - ChartMath.Fraction(6, range.MinY, range.MaxY)) * 19);
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => selected?.SeriesIndex == 1);
            Assert.AreSame(lineSource, selected!.Source);
            selected = null;
            int nextX = columns.Center(1, range, 60);
            int nextY = (int)Math.Round((1 - ChartMath.Fraction(8, range.MinY, range.MaxY)) * 19);
            int betweenX = x + 3, betweenY = (int)Math.Round(y + (nextY - y) * 3d / (nextX - x));
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, betweenX, betweenY, () => true);
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, betweenX, betweenY, () => selected?.SeriesIndex == 1);
            Assert.AreSame(lineSource, selected!.Source, "A line painted across a column must inspect the line's nearest original point.");
        }
    }

    [TestMethod]
    [DataRow(GraphKind.Line)]
    [DataRow(GraphKind.Area)]
    public async Task MultipleLinesAndAreasInspectTheCorrectSeriesAndOriginalPoint(GraphKind kind) {
        object source = new();
        var chart = new Chart { GraphKind = kind };
        chart.Series.Add(new("First", [10, 1, 4]));
        var second = new ChartSeries("Second", [6, 8]);
        second.Sources.AddRange([new object(), source]);
        chart.Series.Add(second);
        ChartSelection? selected = null;
        chart.SelectionChanged += value => selected = value;
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 60, 20);
        var range = chart.RenderRange(chart.GetRenderData(60));
        int x = (int)Math.Round(ChartMath.Fraction(1, range.MinX, range.MaxX) * 59);
        int y = (int)Math.Round((1 - ChartMath.Fraction(8, range.MinY, range.MaxY)) * 19);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => selected?.SeriesIndex == 1);
        Assert.AreSame(source, selected!.Source);
        Assert.AreEqual(1, selected.PointIndex);
        Assert.AreEqual(8d, selected.Y);
    }

    [TestMethod]
    public void ExplicitRolesDrawLinesOnTopEvenWhenTheColumnSeriesComesLast() {
        var chart = new Chart { GraphKind = GraphKind.ColumnLine };
        chart.Series.Add(new("Line", [8, 8]) { Role = ChartSeriesRole.Line });
        chart.Series.Add(new("Column", [10, 10]) { Role = ChartSeriesRole.Column });
        ThemeCatalog.Load("Monokai").Apply(chart);
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 60, 20);
        var data = chart.GetRenderData(60);
        var range = chart.RenderRange(data);
        var columns = new ChartColumns(data, chart.GraphKind);
        int x = columns.Center(0, range, 60);
        int y = (int)Math.Round((1 - ChartMath.Fraction(8, range.MinY, range.MaxY)) * 19);
        Assert.AreEqual((byte)2, plot.Pixels[y * 60 + x], "The visible line must overlay the column regardless of palette index.");
        chart.SetSeriesVisible(1, false);
        Assert.AreEqual(GraphKind.Line, ChartColumns.Kind(chart.GetRenderData(60)[0], GraphKind.ColumnLine, 0));
    }

    [TestMethod]
    public void AutomaticRolesSettingsLogarithmicAxesAndExportsSurviveVisibilityChanges() {
        var chart = new Chart { GraphKind = GraphKind.ColumnLine, ShowAxes = true, ShowLegend = true };
        chart.Series.Add(new("Actual", [1, 10, double.NaN]));
        chart.Series.Add(new("Target", [2, 20]));
        chart.SetSeriesVisible(0, false);
        var rendered = chart.GetRenderData(60);
        Assert.AreEqual(GraphKind.Line, ChartColumns.Kind(rendered[0], GraphKind.ColumnLine, 0), "Hiding the first series must not turn the remaining line into columns.");
        chart.SetSeriesVisible(0, true);
        chart.YAxis = new() { Scale = ChartScale.Logarithmic };
        Assert.AreEqual(0d, chart.RenderRange(chart.GetRenderData(60)).MinY);
        chart.SetSeriesStyle(0, new("Actual") { Role = ChartSeriesRole.Line });
        chart.SetSeriesStyle(1, new("Target") { Role = ChartSeriesRole.Column });
        string settings = chart.ExportSettings();
        chart.SetSeriesStyle(0, new("Actual"));
        chart.ImportSettings(settings);
        Assert.AreEqual(ChartSeriesRole.Line, chart.Series[0].Role);
        Assert.AreEqual(ChartSeriesRole.Column, chart.Series[1].Role);
        string svg = chart.ExportSvg(60, 15);
        Assert.Contains("[columns]", svg);
        Assert.Contains("Target", svg);
        Assert.Contains("[line]", svg);
        Assert.Contains("Actual", svg);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(60, 15)[..4]);
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(GraphKind.ColumnLine));
        Assert.ThrowsExactly<ArgumentException>(() => chart.SetSeriesStyle(0, new() { Role = (ChartSeriesRole)99 }));
    }

    [TestMethod]
    public async Task WrappedLegendKeepsPlotSpaceAndClicksTheCorrectWrappedEntry() {
        var chart = new Chart { ShowLegend = true };
        chart.Series.Add(new("Alpha", [1, 2]));
        chart.Series.Add(new("Bravo", [3, 4]));
        chart.Series.Add(new("Charlie", [5, 6]));
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 12, 10);
        Assert.AreEqual(3, chart.PlotBounds.Y);
        Assert.AreEqual(7, chart.PlotBounds.Height);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 2, 1, () => !chart.Series[1].Visible);
        Assert.IsTrue(chart.Series[0].Visible);
        Assert.IsFalse(chart.Series[1].Visible);
        Assert.IsTrue(chart.Series[2].Visible);
        var tiny = new Chart { ShowLegend = true };
        tiny.Series.Add(new("Alpha", [1, 2]));
        tiny.Series.Add(new("Bravo", [3, 4]));
        tiny.Series.Add(new("Charlie", [5, 6]));
        string narrow = string.Join('\n', VisualSnapshotRenderer.Render(tiny, 4, 4).ToMarkupLines());
        Assert.IsGreaterThan(0, tiny.PlotBounds.Height);
        Assert.Contains("+", narrow);
    }

    [TestMethod]
    public void ObjectViewerRolesAndConfigurationRetainOriginalObjectsAcrossReprojection() {
        var first = PSObject.AsPSObject(new { Actual = 8, Target = 6, Name = "one" });
        var second = PSObject.AsPSObject(new { Actual = -2, Target = 5, Name = "two" });
        using var app = new ObjectChartApp([first, second], "Name", ["Actual", "Target"], GraphKind.ColumnLine);
        app.SetSeriesRoles([ChartSeriesRole.Line, ChartSeriesRole.Column]);
        app.Surface.Plot.ShowLegend = true;
        string saved = app.ExportConfiguration();
        using var restored = new ObjectChartApp([first, second]);
        restored.ImportConfiguration(saved);
        restored.Configure(restored.XProperty, restored.YProperties, GraphKind.ColumnLine);
        Assert.AreEqual(ChartSeriesRole.Line, restored.Surface.Series[0].Role);
        Assert.AreEqual(ChartSeriesRole.Column, restored.Surface.Series[1].Role);
        Assert.AreSame(first, restored.Surface.Series[0].Points[0].Source);
        Assert.AreSame(second, restored.Surface.Series[1].Points[1].Source);
        Assert.IsTrue(restored.Surface.Plot.ShowLegend);
        Assert.ThrowsExactly<ArgumentException>(() => app.SetSeriesRoles([ChartSeriesRole.Line]));
    }
}
