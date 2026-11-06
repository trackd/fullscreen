using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartStackedAreaTests {
    private static ChartDataSeries[] Data(double?[] first, double?[] second) => [
        new("A", [.. first.Select((value, index) => new ChartPoint(10 + index, index, value, $"Sample {index}") { Source = $"A{index}" })]),
        new("B", [.. second.Select((value, index) => new ChartPoint(10 + index, index, value, $"Sample {index}") { Source = $"B{index}" })])
    ];

    [TestMethod]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    public async Task AlignedAreasUseSummedBoundariesAndDisjointExactSourceGeometry(GraphKind kind) {
        var data = Data([2, 6], [6, 2]);
        var range = ChartRange.From(data, kind, null, null);
        Assert.AreEqual(kind == GraphKind.StackedArea ? 8d : 100d, range.MaxY);
        var geometry = new ChartGeometry();
        geometry.Build(data, range, kind, 17, 12);
        for (int x = 0; x < 17; x++) {
            var columns = geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle && shape.X0 == x).ToArray();
            Assert.HasCount(2, columns);
            Assert.AreEqual(11, columns[0].Y1);
            Assert.AreEqual(0, columns[1].Y0);
            Assert.AreEqual(columns[0].Y0 - 1, columns[1].Y1, "Adjacent components must share a boundary without overlapping pixels.");
        }
        var midpoint = geometry.Primitives.First(shape => shape.X0 == 8 && shape.Series == 0);
        Assert.AreEqual(6, midpoint.Y0);
        Assert.AreEqual(1, midpoint.Point);
        var chart = new Chart { GraphKind = kind, ShowLegend = true, MinHeight = 24, MaxHeight = 24 };
        chart.SetData(data);
        _ = VisualSnapshotRenderer.Render(chart, 100, 32);
        geometry.Build(data, chart.RenderRange(data), kind, chart.PlotBounds.Width, chart.PlotBounds.Height);
        var first = geometry.Primitives.First(shape => shape.Series == 0);
        Assert.AreEqual(10, chart.Pick(chart.PlotBounds.X + first.X0, chart.PlotBounds.Y + first.Y1));
        await using (var ui = new DesignerUxTests.UiSession(chart, () => { }, 100, 32)) {
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, chart.PlotBounds.X + first.X0,
                chart.PlotBounds.Y + first.Y1, () => chart.Selection is not null);
            Assert.AreSame(data[0].Points[0].Source, chart.Selection?.Source);
            Assert.AreEqual(0, chart.Selection?.SeriesIndex);
        }
        Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X - 1, chart.PlotBounds.Y));
        var plot = new SixelPlot();
        for (int index = 0; index < 20; index++) {
            chart.DrawPixels(plot, 800, 512);
            chart.Pick(chart.PlotBounds.X + first.X0, chart.PlotBounds.Y + first.Y1);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 800, 512);
            chart.Pick(chart.PlotBounds.X + first.X0, chart.PlotBounds.Y + first.Y1);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        var settings = chart.CaptureSettings();
        chart.ApplySettings(settings);
        Assert.AreEqual(kind, chart.GraphKind);
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(kind));
    }

    [TestMethod]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    public void MissingComponentsLeaveWholeStackGapsAndViewportClipsRatherThanRemovingEndpoints(GraphKind kind) {
        var data = Data([2, null, 4, 6], [6, 2, 4, 2]);
        var geometry = new ChartGeometry();
        geometry.Build(data, ChartRange.From(data, kind, null, null), kind, 31, 16);
        Assert.IsFalse(geometry.Primitives.Any(shape => shape.X0 is > 0 and < 20), "Missing samples must not become zero or bridge adjacent intervals.");
        Assert.IsTrue(geometry.Primitives.Any(shape => shape.X0 == 20));
        var isolated = Data([1, null, 3, null, 2], [2, 1, 2, 1, 3]);
        geometry.Build(isolated, ChartRange.From(isolated, kind, null, null), kind, 14, 20);
        var isolatedColumn = geometry.Primitives.Where(shape => shape.X0 == 6).ToArray();
        Assert.HasCount(2, isolatedColumn);
        Assert.IsTrue(isolatedColumn.All(shape => shape.Point == 2),
            "An isolated valid sample must remain source-selectable at its rounded column, not disappear between pixels.");
        var chart = new Chart { GraphKind = kind };
        chart.SetData(Data([2, 6], [6, 2]));
        chart.SetView(.25, .75);
        var rendered = chart.GetRenderData(100);
        Assert.IsNotNull(rendered[0].Points[0].Y);
        Assert.IsNotNull(rendered[0].Points[1].Y);
        geometry.Build(rendered, chart.RenderRange(rendered), kind, 21, 12);
        Assert.HasCount(42, geometry.Primitives);
        Assert.AreEqual(8, geometry.Primitives[0].Y0);
    }

    [TestMethod]
    public void NormalizationIsOverflowSafeAndZeroTotalsDoNotFabricatePercentages() {
        var huge = Data([double.MaxValue, double.MaxValue], [double.MaxValue, double.MaxValue]);
        Assert.ThrowsExactly<ArgumentException>(() => ChartRange.From(huge, GraphKind.StackedArea, null, null));
        var range = ChartRange.From(huge, GraphKind.PercentStackedArea, null, null);
        var geometry = new ChartGeometry();
        geometry.Build(huge, range, GraphKind.PercentStackedArea, 11, 20);
        Assert.AreEqual(10, geometry.Primitives[0].Y0);
        var zero = Data([0, 0], [0, 0]);
        geometry.Build(zero, ChartRange.From(zero, GraphKind.PercentStackedArea, null, null), GraphKind.PercentStackedArea, 11, 20);
        Assert.HasCount(0, geometry.Primitives);
        geometry.Build(Data([1], [3]), new(0, 0, 0, 100), GraphKind.PercentStackedArea, 11, 20);
        Assert.HasCount(2, geometry.Primitives);
        Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 == 5));
        geometry.Build(Data([1], [3]), new(-1, 3, 0, 100), GraphKind.PercentStackedArea, 13, 20);
        Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 == 3));
        var uneven = Data([1, 1], [7, 13]);
        geometry.Build(uneven, new(0, 1, 0, 100), GraphKind.PercentStackedArea, 1001, 20);
        Assert.IsTrue(geometry.Primitives.Where(shape => shape.Series == 1).All(shape => shape.Y0 == 0),
            "Rounding must not leave a one-pixel blank strip above a complete normalized stack.");
        uneven[0].Visible = false;
        geometry.Build(uneven, new(0, 1, 0, 100), GraphKind.PercentStackedArea, 11, 20);
        Assert.HasCount(11, geometry.Primitives);
        Assert.IsTrue(geometry.Primitives.All(shape => shape.Series == 1 && shape.Y0 == 0 && shape.Y1 == 19));
    }

    [TestMethod]
    public void InvalidValuesCoordinatesAndPercentBoundsRejectExplicitly() {
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.Validate(Data([-1, 2], [3, 4])));
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.Validate(Data([double.NaN, 2], [3, 4])));
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.Validate(Data([1], [3, 4])));
        var misaligned = Data([1, 2], [3, 4]);
        misaligned[1].Points[1] = misaligned[1].Points[1] with { X = 2 };
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.Validate(misaligned));
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.ValidateOptions(GraphKind.StackedArea, null, null, ChartScale.Linear, ChartScale.Logarithmic));
        Assert.ThrowsExactly<ArgumentException>(() => ChartStackedArea.ValidateOptions(GraphKind.PercentStackedArea, 0, 100, ChartScale.Linear, ChartScale.Linear));
        var chart = new Chart { GraphKind = GraphKind.PercentStackedArea };
        Assert.ThrowsExactly<ArgumentException>(() => chart.Zoom(2, vertical: true));
        Assert.ThrowsExactly<ArgumentException>(() => chart.Pan(.25, vertical: true));
    }
}
