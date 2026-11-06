using System.Collections;
using System.Management.Automation;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NumericChartTests {
    [TestMethod]
    [DataRow(3, false)]
    [DataRow(3, true)]
    [DataRow(30, false)]
    [DataRow(30, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    public void EdgeBarsRetainTheSameWidthAsInteriorBars(int count, bool grouped) {
        const int width = 120, height = 31;
        double[] values = [.. Enumerable.Repeat(5d, count)];
        var chart = new Chart(values) { GraphKind = GraphKind.Bars, Minimum = 0, Maximum = 10 };
        if (grouped) { chart.Series.Add(new ChartSeries("Other", values)); }
        var plot = new SixelPlot();
        chart.DrawPixels(plot, width, height);
        int groups = grouped ? 2 : 1;
        int span = Math.Max(1, width / count - 1);
        for (int series = 0; series < groups; series++) {
            byte color = grouped ? (byte)(2 + series) : (byte)5;
            var runs = new List<int>();
            int length = 0;
            for (int x = 0; x <= width; x++) {
                if (x < width && plot.Pixels[20 * width + x] == color) { length++; }
                else if (length > 0) { runs.Add(length); length = 0; }
            }
            Assert.HasCount(count, runs, "Every observation must keep its complete bar, including both edge groups.");
            int expectedWidth = span * (series + 1) / groups - span * series / groups;
            CollectionAssert.AreEqual(Enumerable.Repeat(expectedWidth, count).ToArray(), runs.ToArray());
        }
    }
    [TestMethod]
    public void SparseAreaFillsBetweenSamplesAndBoxPlotsHandleLargeSignedValues() {
        var chart = new Chart([2, 8]) { GraphKind = GraphKind.Area, Minimum = 0, Maximum = 10 };
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 101, 101);
        Assert.IsGreaterThan((byte)1, plot.Pixels[80 * 101 + 50], "Sparse area charts must fill between observations, not only at their columns.");
        chart.GraphKind = GraphKind.Line;
        chart.DrawPixels(plot, 101, 101);
        Assert.AreEqual((byte)0, plot.Pixels[80 * 101 + 50], "Line charts leave the corresponding area empty.");
        var extremes = new Chart([-double.MaxValue, double.MaxValue]) { GraphKind = GraphKind.BoxPlot };
        extremes.DrawPixels(plot, 101, 101);
        Assert.IsTrue(plot.Pixels.ToArray().Any(pixel => pixel > 1));
        Assert.AreEqual(0d, ChartStatistics.Summarize([-double.MaxValue, double.MaxValue]).Median);
    }
    [TestMethod]
    public async Task NumericListChangesRepaintAndMouseRaisesSelectionEvents() {
        var chart = new Chart([-10, 10]) { GraphKind = GraphKind.Scatter, Minimum = -10, Maximum = 10 };
        await using var ui = new DesignerUxTests.UiSession(chart, () => { }, 80, 20);
        int outputLength = ui.Output.Length;
        chart.Values[0] = 0;
        ui.Tick();
        Assert.IsGreaterThan(outputLength, ui.Output.Length, "Changing Values must schedule a real terminal repaint.");
        int selected = -1, activated = -1;
        chart.PointSelected += index => selected = index;
        chart.PointActivated += index => activated = index;
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 79, 0, () => selected == 1);
        Assert.AreEqual(1, chart.SelectedIndex);
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 79, 0, () => true);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 79, 0, () => activated == 1);
    }
    [TestMethod]
    public void NumericChartMatchesXenoAtomValuesAndRangeSignatures() {
        foreach (string name in new[] { "Values", "Minimum", "Maximum" }) {
            Assert.AreEqual(typeof(LineChart).GetProperty(name)!.PropertyType, typeof(Chart).GetProperty(name)!.PropertyType, name);
        }
        var chart = new Chart([2, double.NaN, -4]);
        Assert.IsNull(chart.Minimum);
        Assert.IsNull(chart.Maximum);
        Assert.AreEqual(-4d, chart.GetData(100)[0].Points[2].Y);
        Assert.IsNull(chart.GetData(100)[0].Points[1].Y);
        chart.Values[0] = 9;
        chart.Values.Add(11);
        Assert.AreEqual(9d, chart.GetData(100)[0].Points[0].Y);
        Assert.HasCount(4, chart.GetData(100)[0].Points);
    }
    [TestMethod]
    public void NumericCollectionsPreserveCoordinatesMultipleSeriesAndMutations() {
        var chart = new Chart([1, 2, 3]);
        foreach (double x in new[] { 0d, 2d, 100d }) { chart.XValues.Add(x); }
        var comparison = new ChartSeries("Comparison", [-2, 5, double.NaN]);
        chart.Series.Add(comparison);
        var data = chart.GetData(100);
        Assert.HasCount(2, data);
        Assert.AreEqual(100d, data[0].Points[2].X);
        Assert.AreEqual(-2d, data[1].Points[0].Y);
        Assert.IsNull(data[1].Points[2].Y);
        comparison.Values[0] = -8;
        Assert.AreEqual(-8d, chart.GetData(100)[1].Points[0].Y);
        chart.XValues.RemoveAt(0);
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetData(100));
        chart.XValues.Clear();
        Assert.AreEqual(2d, chart.GetData(100)[0].Points[2].X);
    }
    [TestMethod]
    [DataRow(GraphKind.Line)]
    [DataRow(GraphKind.Area)]
    [DataRow(GraphKind.Bars)]
    [DataRow(GraphKind.Step)]
    [DataRow(GraphKind.Scatter)]
    [DataRow(GraphKind.Stem)]
    [DataRow(GraphKind.Histogram)]
    [DataRow(GraphKind.BoxPlot)]
    public void EveryGraphUsesTheSameNumericControlForPixelsAndCells(GraphKind kind) {
        var chart = new Chart([-5, 0, double.NaN, 8, 2]) { GraphKind = kind, Minimum = -10, Maximum = 10 };
        chart.Series.Add(new ChartSeries("Other", [4, -3, 6, double.NaN, 1]));
        string snapshot = string.Join('\n', VisualSnapshotRenderer.Render(chart, 60, 15).ToMarkupLines());
        Assert.IsTrue(snapshot.Contains('•') || snapshot.Contains('█') || snapshot.Contains('═'), kind.ToString());
        var plot = new SixelPlot();
        chart.DrawPixels(plot, 180, 60);
        Assert.IsTrue(plot.Pixels.ToArray().Any(pixel => pixel >= 2), kind.ToString());
        Assert.AreEqual(new ChartRange(kind is GraphKind.Histogram or GraphKind.BoxPlot ? -10 : 0, kind is GraphKind.Histogram or GraphKind.BoxPlot ? 10 : 4, -10, 10), chart.Range(chart.GetData(60)));
    }
    [TestMethod]
    public void CustomTuiNumericBindingsRefreshWithoutCreatingTimeHistory() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"plot","Type":"Chart","Text":"Numeric values","Bind":"samples","Minimum":-10,"Maximum":10,"GraphKind":"ColumnLine","ShowLegend":true}}""");
        using var app = new CustomTuiApp(document, new Hashtable { ["samples"] = new object?[] { -2d, null, PSObject.AsPSObject(7d) } });
        var chart = Assert.IsInstanceOfType<Chart>(app.Controls["plot"]);
        Assert.IsFalse(chart is BuiltInTrend);
        Assert.AreEqual(GraphKind.ColumnLine, chart.GraphKind);
        Assert.IsTrue(chart.ShowLegend);
        var saved = TuiDocument.FromJson(document.ToJson());
        Assert.IsTrue(saved.Root.ShowLegend);
        Assert.AreEqual("ColumnLine", saved.Root.GraphKind);
        CollectionAssert.AreEqual(new[] { -2d, double.NaN, 7d }, chart.Values.ToArray());
        app.State["samples"] = new[] { 9d, -8d };
        app.Tick();
        CollectionAssert.AreEqual(new[] { 9d, -8d }, chart.Values.ToArray());
        Assert.AreEqual(-10d, chart.Minimum);
        Assert.AreEqual(10d, chart.Maximum);
        Assert.Contains("Numeric values", string.Join('\n', VisualSnapshotRenderer.Render(app.Root, 70, 12).ToMarkupLines()));
    }
    [TestMethod]
    public void NumericBoundsValidateAndRoundTripInDefinitions() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"plot","Type":"Chart","Minimum":-10,"Maximum":-1}}""");
        var clone = TuiDocument.FromJson(document.ToJson());
        Assert.AreEqual(-10d, clone.Root.Minimum);
        Assert.AreEqual(-1d, clone.Root.Maximum);
        Assert.ThrowsExactly<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"plot","Type":"Chart","Minimum":10,"Maximum":1}}"""));
        var chart = new Chart([1, 2]) { Minimum = double.NaN };
        Assert.ThrowsExactly<ArgumentException>(() => chart.Range(chart.GetData(10)));
        chart.Minimum = 3;
        chart.Maximum = 2;
        Assert.ThrowsExactly<ArgumentException>(() => chart.Range(chart.GetData(10)));
    }
}
