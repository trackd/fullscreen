using System.Collections;
using System.Management.Automation;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CustomChartSeriesTests {
    [TestMethod]
    [DataRow(GraphKind.ColumnLine)]
    [DataRow(GraphKind.Radar)]
    [DataRow(GraphKind.Heatmap)]
    [DataRow(GraphKind.Marimekko)]
    [DataRow(GraphKind.StackedArea)]
    [DataRow(GraphKind.PercentStackedArea)]
    public async Task ObjectSeriesPreserveNamesGapsSourcesAndSelectionAcrossUpdates(GraphKind kind) {
        var first = new Hashtable { ["Category"] = "North", ["Actual"] = 8, ["Target"] = 6 };
        var second = PSObject.AsPSObject(new { Category = "South", Actual = 4, Target = (double?)null });
        var third = new Hashtable { ["Category"] = "West", ["Actual"] = 2, ["Target"] = 7 };
        var document = new TuiDocument {
            Root = new() {
                Id = "chart", Type = "Chart", Bind = "rows", GraphKind = kind.ToString(),
                XProperty = "Category", YProperty = ["Actual", "Target"], SeriesRole = ["Column", "Line"],
                SelectionBind = "selected", Height = 24, ShowLegend = true
            }
        };
        var copy = TuiDataFile.Parse(TuiDataFile.Generate(TuiDocument.FromJson(document.ToJson())));
        Assert.AreEqual("Category", copy.Root.XProperty);
        CollectionAssert.AreEqual(new[] { "Actual", "Target" }, copy.Root.YProperty);
        CollectionAssert.AreEqual(new[] { "Column", "Line" }, copy.Root.SeriesRole);
        using var app = new CustomTuiApp(copy, new Hashtable { ["rows"] = new object[] { first, second, third } });
        var chart = (Chart)app.Controls["chart"];
        var data = chart.GetRenderData(100);
        Assert.HasCount(2, data);
        Assert.AreEqual("Actual", data[0].Property);
        Assert.AreEqual("Target", data[1].Property);
        Assert.AreEqual("South", data[0].Points[1].Label);
        Assert.AreEqual(ChartScale.Category, chart.XAxis.Scale);
        Assert.AreEqual(ChartSeriesRole.Column, data[0].Role);
        Assert.AreEqual(ChartSeriesRole.Line, data[1].Role);
        Assert.IsNull(data[1].Points[1].Y);
        Assert.AreSame(first, data[0].Points[0].Source);
        Assert.AreSame(second, data[1].Points[1].Source);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => _ = app.Tick(), 100, 32);
        var geometry = new ChartGeometry();
        geometry.Build(data, chart.RenderRange(data), kind, chart.PlotBounds.Width, chart.PlotBounds.Height);
        (int X, int Y)? hit = null;
        for (int y = 0; y < chart.PlotBounds.Height && hit is null; y++) {
            for (int x = 0; x < chart.PlotBounds.Width && hit is null; x++) {
                if (chart.Pick(chart.PlotBounds.X + x, chart.PlotBounds.Y + y) == 0) {
                    hit = (chart.PlotBounds.X + x, chart.PlotBounds.Y + y);
                }
            }
        }
        Assert.IsNotNull(hit);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, hit.Value.X, hit.Value.Y,
            () => app.State.ContainsKey("selected"));
        Assert.AreSame(first, app.State["selected"]);
        app.Tick();
        var retained = chart.GetRenderData(100);
        app.Tick();
        Assert.AreSame(retained, chart.GetRenderData(100), "Unchanged sampling must not invalidate retained geometry.");
        retained[1].Visible = false;
        first["Actual"] = 11;
        app.State["rows"] = new object[] { first, second, third };
        app.Tick();
        var updated = chart.GetData(100);
        Assert.AreEqual(11d, updated[0].Points[0].Y);
        Assert.AreSame(first, updated[0].Points[0].Source);
        Assert.IsFalse(updated[1].Visible);
        Assert.AreEqual(ChartSeriesRole.Column, updated[0].Role);
        var plot = new SixelPlot();
        for (int iteration = 0; iteration < 20; iteration++) {
            chart.DrawPixels(plot, 800, 512);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100; iteration++) {
            chart.DrawPixels(plot, 800, 512);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void ErrorBoundsSurvivePortableDefinitionsAndChangedSourceProjection() {
        var first = new Hashtable { ["Sample"] = 1, ["Mean"] = 10, ["Low"] = 7, ["High"] = 15 };
        var document = new TuiDocument {
            Root = new() {
                Id = "chart",
                Type = "Chart",
                GraphKind = "ErrorBars",
                Bind = "estimates",
                XProperty = "Sample",
                YProperty = ["Mean"],
                LowerProperty = "Low",
                UpperProperty = "High"
            }
        };
        var copy = TuiDataFile.Parse(TuiDataFile.Generate(TuiDocument.FromJson(document.ToJson())));
        Assert.AreEqual("Low", copy.Root.LowerProperty);
        Assert.AreEqual("High", copy.Root.UpperProperty);
        using var app = new CustomTuiApp(copy, new Hashtable { ["estimates"] = new object[] { first } });
        var chart = (Chart)app.Controls["chart"];
        var point = chart.GetData(100)[0].Points[0];
        Assert.AreEqual(7d, point.Lower);
        Assert.AreEqual(15d, point.Upper);
        Assert.AreSame(first, point.Source);
        first["Low"] = 6;
        first["High"] = null;
        app.State["estimates"] = new object[] { first };
        app.Tick();
        point = chart.GetData(100)[0].Points[0];
        Assert.AreEqual(6d, point.Lower);
        Assert.IsNull(point.Upper);
        Assert.AreSame(first, point.Source);
        copy.Root.GraphKind = "Line";
        Assert.ThrowsExactly<ArgumentException>(copy.Validate);
        copy.Root.GraphKind = "ErrorBars";
        copy.Root.LowerProperty = "Unknown";
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(copy,
            new Hashtable { ["estimates"] = new object[] { first } }));
    }

    [TestMethod]
    public void ObjectSeriesRejectAmbiguousOrInvalidSelectorsAndAllowEmptyCollections() {
        var document = new TuiDocument { Root = new() { Type = "Chart", Id = "chart", Bind = "rows", YProperty = ["A", "a"] } };
        Assert.ThrowsExactly<ArgumentException>(document.Validate);
        document.Root.YProperty = ["Value"];
        document.Root.SeriesRole = ["bad"];
        Assert.ThrowsExactly<ArgumentException>(document.Validate);
        document.Root.SeriesRole = [];
        using var empty = new CustomTuiApp(document, new Hashtable { ["rows"] = Array.Empty<object>() });
        Assert.HasCount(1, ((Chart)empty.Controls["chart"]).GetData(100));
        Assert.HasCount(0, ((Chart)empty.Controls["chart"]).GetData(100)[0].Points);
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(document, new Hashtable { ["rows"] = 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => new CustomTuiApp(document, new Hashtable { ["rows"] = new object?[] { null } }));
        document.Root.GraphKind = "Gantt";
        Assert.ThrowsExactly<ArgumentException>(document.Validate);
    }
}
