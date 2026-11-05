using System.Collections;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Xml.Linq;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartGanttTests {
    private static Chart Example() {
        var chart = new Chart { GraphKind = GraphKind.Gantt, Title = "Release", ShowAxes = true, ShowLegend = true };
        var start = DateTimeOffset.UnixEpoch;
        chart.Tasks.AddRange([
            new("Build", start, start.AddHours(4), .5),
            new("Test", start.AddHours(2), start.AddHours(6), 0),
            new("Review", start.AddHours(4), start.AddHours(7)),
            new("Ship", start.AddHours(8), start.AddHours(8))
        ]);
        return chart;
    }

    [TestMethod]
    public void ScheduleAbsoluteIntervalsPreservesOverlapProgressAndMilestones() {
        var chart = Example();
        var data = chart.GetRenderData(80);
        var range = chart.RenderRange(data);
        Assert.AreEqual(21, Array.IndexOf(Enum.GetValues<GraphKind>(), GraphKind.Gantt));
        Assert.AreEqual(8 * 3600d, range.MaxX - range.MinX);
        Assert.AreEqual(0d, data[0].Points[3].Y);
        var geometry = new ChartGeometry();
        geometry.Build(data, range, GraphKind.Gantt, 80, 160, flameFrameHeight: 16);
        var bars = geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle).ToArray();
        Assert.HasCount(5, bars);
        Assert.AreEqual(new ChartPrimitive(ChartPrimitiveKind.Rectangle, 0, 16, 39, 31, Shade: 2, Point: 0), bars[0]);
        Assert.AreEqual(19, bars[1].X1);
        Assert.AreEqual(48, bars[2].Y0);
        Assert.AreEqual(79, bars[4].X0);
        Assert.AreEqual(79, bars[4].X1);
        Assert.AreEqual(112, bars[4].Y0);
        Assert.IsTrue(geometry.Primitives.Any(shape => shape.Text == "*"));
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(GraphKind.Gantt));
        geometry.Build(data, range, GraphKind.Gantt, 80, 4, taskOffset: 2);
        Assert.AreEqual(2, geometry.Primitives[0].Point);
        Assert.AreEqual(1, geometry.Primitives[0].Y0);
        foreach (var size in new[] { (1, 1), (2, 7), (9, 3) }) {
            geometry.Build(data, range, GraphKind.Gantt, size.Item1, size.Item2);
            Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X1 < size.Item1 && shape.Y0 >= 0 && shape.Y1 < size.Item2));
        }
    }

    [TestMethod]
    public void ScheduleInvalidInputsAndExplicitTimezoneAreRejectedOrNormalized() {
        var start = DateTimeOffset.UnixEpoch;
        Assert.ThrowsExactly<ArgumentException>(() => new ChartSchedule([new("", start, start)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartSchedule([new("bad\nname", start, start)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartSchedule([new("bad", start, start.AddSeconds(-1))]));
        foreach (double value in new[] { -1d, 1.1, double.NaN, double.PositiveInfinity }) {
            Assert.ThrowsExactly<ArgumentException>(() => new ChartSchedule([new("bad", start, start, value)]));
        }
        var rows = new[] {
            PSObject.AsPSObject(new { Task = "UTC", Start = "2026-01-01T10:00:00Z", End = "2026-01-01T11:00:00Z" }),
            PSObject.AsPSObject(new { Task = "Offset", Start = "2026-01-01T12:00:00+02:00", End = "2026-01-01T13:00:00+02:00" })
        };
        var projected = new ObjectChartData(rows).ProjectTasks("Start", "End", "Task", null, [0, 1]);
        Assert.AreEqual(projected[0].Points[0].X, projected[0].Points[1].X);
        Assert.AreSame(rows[1], projected[0].Points[1].Source);
        foreach (object invalid in new object[] { 24d, new DateTime(2026, 1, 1), "2026-01-01T10:00:00", "not a date" }) {
            var data = new ObjectChartData([PSObject.AsPSObject(new { Task = "bad", Start = invalid, End = start })]);
            Assert.ThrowsExactly<ArgumentException>(() => data.ProjectTasks("Start", "End", "Task", null, [0]));
        }
        var extrema = new Chart { GraphKind = GraphKind.Gantt, ShowAxes = true };
        extrema.Tasks.Add(new("All time", DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.IsNotNull(XDocument.Parse(extrema.ExportSvg(80, 10)));
        var only = new Chart { GraphKind = GraphKind.Gantt, ShowAxes = true };
        only.Tasks.Add(new("End", DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
        Assert.IsNotNull(XDocument.Parse(only.ExportSvg(80, 10)));
        var invalidOptions = Example();
        invalidOptions.Minimum = 0;
        Assert.ThrowsExactly<ArgumentException>(() => invalidOptions.GetRenderData(80));
        invalidOptions.Minimum = null;
        invalidOptions.XAxis = new() { Scale = ChartScale.Category };
        Assert.ThrowsExactly<ArgumentException>(() => invalidOptions.GetRenderData(80));
    }

    [TestMethod]
    public void GanttRenderAndPickWarmBuffersAndExactIntervalBounds() {
        var chart = Example();
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 80, 24).ToMarkupLines());
        Assert.Contains("Build", cells);
        Assert.Contains("milestone", cells);
        var plot = new SixelPlot();
        for (int iteration = 0; iteration < 10; iteration++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y + 1);
        }
        Assert.AreEqual(0, chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y + 1));
        Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X + 60, chart.PlotBounds.Y + 1));
        Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100; iteration++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X + 1, chart.PlotBounds.Y + 1);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        Assert.AreEqual("AC5532A38A6FE16ED364EC2A5E638046477653DED70F18852792734AB3AF8AD5", hash);
        var svg = XDocument.Parse(chart.ExportSvg(80, 24));
        Assert.Contains("Build", svg.ToString());
        Assert.IsTrue(chart.AnnotationBounds.All(bounds => bounds.Bottom <= chart.PlotBounds.Bottom));
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(240, 120)[..4]);
        var start = chart.Tasks[0].Start;
        chart.SetView(ChartSchedule.Seconds(start.AddHours(1)), ChartSchedule.Seconds(start.AddHours(3)));
        Assert.AreEqual(0, chart.Pick(chart.PlotBounds.X, chart.PlotBounds.Y + 1), "Tasks beginning before the viewport must remain visible when they intersect it.");
        chart.TaskOffset = 2;
        var copy = Example();
        copy.ImportSettings(chart.ExportSettings());
        Assert.AreEqual(2, copy.TaskOffset);
        chart.ResetView();
        Assert.AreEqual(0, chart.TaskOffset);
    }

    [TestMethod]
    public async Task GanttHostAndCustomBindingsPreserveOriginalTaskSelectionAndRangeIntersections() {
        var start = DateTimeOffset.UnixEpoch;
        PSObject[] rows = [
            PSObject.AsPSObject(new { Label = "Build", From = start, To = start.AddHours(4), Done = .5 }),
            PSObject.AsPSObject(new { Label = "Test", From = start.AddHours(2), To = start.AddHours(6), Done = 0d }),
            PSObject.AsPSObject(new { Label = "Ship", From = start.AddHours(6), To = start.AddHours(6), Done = 1d })
        ];
        using var app = new ObjectChartApp(rows, null, null, GraphKind.Gantt, "Plan", new(), CancellationToken.None, new("From", "To", "Label", "Done"));
        await using (var ui = new DesignerUxTests.UiSession(app.Root, () => { }, 80, 24)) {
            var chart = app.Surface.Plot;
            int x = chart.PlotBounds.X + 1, y = chart.PlotBounds.Y + 1;
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => chart.Selection is not null);
            Assert.AreSame(rows[0], chart.Selection!.Source);
            Assert.Contains("UTC", chart.Selection.SeriesName);
            Assert.AreEqual(0, app.Surface.SelectedIndex);
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
            ui.Key(TerminalKey.Down, () => chart.TaskOffset == 1);
            chart.SelectRange(new(ChartSchedule.Seconds(start.AddHours(3)), ChartSchedule.Seconds(start.AddHours(3.5))));
            CollectionAssert.AreEqual(new[] { 0, 1 }, app.ChosenIndices());
            using var copy = new ObjectChartApp(rows);
            copy.ImportConfiguration(app.ExportConfiguration());
            Assert.AreEqual("From", copy.TaskProperties.Start);
            Assert.AreSame(rows[0], copy.Surface.Series[0].Points[0].Source);
            using var deferred = new ObjectChartApp(rows, null, null, GraphKind.Gantt, "Plan", new(), CancellationToken.None, initialize: false);
            deferred.ImportConfiguration(app.ExportConfiguration());
            Assert.AreSame(rows[1], deferred.Surface.Series[0].Points[1].Source);
            app.ReplaceObjects(rows);
            Assert.IsEmpty(app.YProperties);
        }
        var document = TuiDocument.FromJson("""{"Root":{"Id":"tasks","Type":"Chart","Bind":"plan","GraphKind":"Gantt","StartProperty":"From","EndProperty":"To","TaskNameProperty":"Label","ProgressProperty":"Done","SelectionBind":"selected"}}""");
        using var custom = new CustomTuiApp(document, new Hashtable { ["plan"] = rows });
        var customChart = Assert.IsInstanceOfType<Chart>(custom.Controls["tasks"]);
        await using var customUi = new DesignerUxTests.UiSession(custom.Root, () => _ = custom.Tick(), 80, 24);
        customUi.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, customChart.PlotBounds.X + 1,
            customChart.PlotBounds.Y + 1, () => custom.State.Contains("selected"));
        Assert.AreSame(rows[0], custom.State["selected"]);
        custom.State["plan"] = new[] { rows[2] };
        custom.Tick();
        Assert.AreEqual(1, customChart.GetData(80)[0].Count);
    }
}
