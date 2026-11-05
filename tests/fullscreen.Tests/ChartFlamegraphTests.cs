using System.Collections;
using System.Management.Automation;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using fullscreen.Charts;
using fullscreen.Custom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartFlamegraphTests {
    [TestMethod]
    public void WideFrameNamesUseWholeCellBoundsForDrawingAndSixelMasks() {
        var chart = new Chart { GraphKind = GraphKind.Flamegraph };
        chart.Frames.Add(new("root", null, "\u4e2d\u6587", 1));
        foreach (int width in new[] { 5, 6, 20 }) {
            var lines = VisualSnapshotRenderer.Render(chart, width, 10).ToMarkupLines();
            Assert.Contains(width == 5 ? "\u4e2d" : "\u4e2d\u6587", string.Join('\n', lines));
            var mask = chart.AnnotationBounds.Single();
            Assert.AreEqual(width == 5 ? 2 : 4, mask.Width);
            var geometry = new ChartGeometry();
            geometry.Build(chart.GetRenderData(width), chart.RenderRange(chart.GetRenderData(width)),
                GraphKind.Flamegraph, chart.PlotBounds.Width, chart.PlotBounds.Height, flameFrameHeight: chart.FlameFrameRows);
            var label = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Text);
            Assert.AreEqual(mask.Width, label.X1 - label.X0 + 1);
        }
    }

    public TestContext TestContext { get; set; }

    private static ChartFrame[] ExampleFrames() => [
        new("root", null, "main", 2),
        new("a", "root", "worker", 3),
        new("b", "a", "parse", 5),
        new("c", "root", "worker", 1),
        new("d", "c", "parse", 4),
        new("other", null, "other", 5)
    ];
    private static Chart Example() {
        var chart = new Chart { GraphKind = GraphKind.Flamegraph, ShowLegend = true, Title = "Flamegraph" };
        chart.Frames.AddRange(ExampleFrames());
        return chart;
    }

    [TestMethod]
    public void ExclusiveWeightsAggregateInclusiveWidthsAndIdenticalPrefixes() {
        var tree = new ChartFlameTree(ExampleFrames());
        Assert.HasCount(4, tree.Nodes);
        Assert.AreEqual(20d, tree.Total);
        Assert.AreEqual(15d, tree.Nodes.Single(node => node.Name == "main").Inclusive);
        Assert.AreEqual(4d, tree.Nodes.Single(node => node.Name == "worker").Self);
        Assert.AreEqual(13d, tree.Nodes.Single(node => node.Name == "worker").Inclusive);
        Assert.AreEqual(9d, tree.Nodes.Single(node => node.Name == "parse").Inclusive);
        var worker = tree.Data[0].Points.Single(point => point.Label == "worker");
        CollectionAssert.AreEqual(new[] { 1, 3 }, worker.SourceIndices!);
        Assert.IsInstanceOfType<ChartFrame>(worker.Source);
        var geometry = new ChartGeometry();
        geometry.Build(tree.Data, ChartRange.From(tree.Data, GraphKind.Flamegraph, null, null), GraphKind.Flamegraph, 100, 30);
        var main = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "main");
        var other = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "other");
        var child = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "worker");
        Assert.AreEqual(75, main.X1 + 1);
        Assert.AreEqual(75, other.X0);
        Assert.AreEqual(29, main.Y1);
        Assert.AreEqual(28, child.Y1);
        Assert.AreEqual(main.Y0, main.Y1, "Each depth occupies exactly one cell row.");
        Assert.AreEqual(child.Y0, child.Y1);
        Assert.AreNotEqual(main.Shade, child.Shade);
        Assert.IsTrue(child.X0 >= main.X0 && child.X1 <= main.X1);
        Assert.AreEqual(10, child.X0, "Own-prefix self samples precede child paths in sorted folded-stack order.");
        Assert.AreEqual(main.X1, child.X1);
    }

    [TestMethod]
    public void InvalidHierarchiesRejectDuplicatesParentsCyclesWeightsAndOverflow() {
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", null, "a", 1), new("a", null, "b", 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", "missing", "a", 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", "b", "a", 1), new("b", "a", "b", 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", "a", "a", 1)]));
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity }) {
            Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", null, "a", invalid)]));
        }
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", null, "", 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", null, "a\nb", 1)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree([new("a", null, "a", double.MaxValue), new("b", "a", "b", double.MaxValue)]));
        var chain = Enumerable.Range(0, 1025).Select(index => new ChartFrame(index.ToString(CultureInfo.InvariantCulture), index == 0 ? null : (index - 1).ToString(CultureInfo.InvariantCulture), "frame", 1)).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => new ChartFlameTree(chain));
        Assert.AreEqual(0d, new ChartFlameTree([new("a", null, "a", 0)]).Total);
    }

    [TestMethod]
    public void OutOfOrderParentsEmptyTreesAndConstrainedDepthsRemainValid() {
        var frames = ExampleFrames().Reverse().ToArray();
        var tree = new ChartFlameTree(frames);
        Assert.AreEqual(20d, tree.Total);
        var chart = new Chart { GraphKind = GraphKind.Flamegraph };
        Assert.IsEmpty(chart.GetRenderData(80)[0].Points);
        chart.Frames.AddRange(Enumerable.Range(0, 512).Select(index => new ChartFrame(index.ToString(CultureInfo.InvariantCulture),
            index == 0 ? null : (index - 1).ToString(CultureInfo.InvariantCulture), $"frame{index}", 1)));
        var data = chart.GetRenderData(80);
        var geometry = new ChartGeometry();
        foreach (var size in new[] { (1, 1), (3, 2), (40, 8) }) {
            geometry.Build(data, chart.RenderRange(data), chart.GraphKind, size.Item1, size.Item2);
            Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X1 < size.Item1 && shape.Y0 >= 0 && shape.Y1 < size.Item2));
            Assert.AreEqual(size.Item2, geometry.Primitives.Count(shape => shape.Kind == ChartPrimitiveKind.Rectangle));
        }
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(GraphKind.Flamegraph));
        chart.Minimum = 0;
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
        tree.Data[0].Visible = false;
        geometry.Build(tree.Data, ChartRange.From(tree.Data, GraphKind.Flamegraph, null, null), GraphKind.Flamegraph, 80, 24);
        Assert.IsEmpty(geometry.Primitives, "Hiding the weight series must hide its hierarchy rather than ignoring the viewer's saved series style.");
    }

    [TestMethod]
    public void FocusAndReverseShareDrawingPickingAndPreserveSourceIdentity() {
        var chart = Example();
        _ = VisualSnapshotRenderer.Render(chart, 80, 24);
        var tree = chart.GetFlameTree();
        var geometry = new ChartGeometry();
        geometry.Build(tree.Data, chart.RenderRange(tree.Data), chart.GraphKind, chart.PlotBounds.Width, chart.PlotBounds.Height, flameFrameHeight: chart.FlameFrameRows);
        var worker = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "worker");
        int x = chart.PlotBounds.X + worker.X0, y = chart.PlotBounds.Y + worker.Y0;
        Assert.AreEqual(1, chart.Pick(x, y));
        chart.Selection = new(0, "", 1, 1, 13, "worker", chart.Frames[1]);
        Assert.AreSame(chart.Frames[1], chart.Selection!.Source);
        chart.FocusSelectedFrame();
        Assert.AreEqual("a", chart.FlameFocusId);
        chart.FlameReverse = true;
        geometry.Build(tree.Data, chart.RenderRange(tree.Data), chart.GraphKind, chart.PlotBounds.Width, chart.PlotBounds.Height,
            flameReverse: true, flameFocus: chart.FlameFocus(tree), flameFrameHeight: chart.FlameFrameRows);
        worker = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "worker");
        Assert.AreEqual(0, worker.Y0);
        Assert.AreEqual(chart.PlotBounds.Width - 1, worker.X1);
        Assert.AreEqual(1, chart.Pick(chart.PlotBounds.X, chart.PlotBounds.Y));
        chart.FlameParent();
        Assert.AreEqual("root", chart.FlameFocusId);
        chart.FlameParent();
        Assert.AreEqual("", chart.FlameFocusId);
        chart.FlameFocusId = "other";
        geometry.Build(tree.Data, chart.RenderRange(tree.Data), chart.GraphKind, 80, 24, flameFocus: chart.FlameFocus(tree));
        var only = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle);
        Assert.AreEqual(23, only.Y1);
        chart.FlameFocusId = "missing";
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
    }

    [TestMethod]
    public void FixedRowsAlphabeticalPathsAndSearchMatchTheConventionalLayout() {
        var frames = new ChartFrame[] {
            new("root", null, "main", 2),
            new("z", "root", "Zulu", 3),
            new("a", "root", "Alpha", 4),
            new("z2", "root", "Zulu", 1),
            new("bz", "z", "Beta", 5),
            new("az", "z2", "Able", 5)
        };
        var tree = new ChartFlameTree(frames);
        var geometry = new ChartGeometry();
        var range = ChartRange.From(tree.Data, GraphKind.Flamegraph, null, null);
        geometry.Build(tree.Data, range, GraphKind.Flamegraph, 200, 320, flameFrameHeight: 16);
        var rectangles = geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle).ToArray();
        Assert.IsTrue(rectangles.All(shape => shape.Y1 - shape.Y0 + 1 == 16));
        Assert.IsTrue(rectangles.All(shape => shape.Y0 >= 272), "A shallow tree leaves unused height, never stretches its depths.");
        var alpha = rectangles.Single(shape => tree.Nodes[shape.Point].Name == "Alpha");
        var zulu = rectangles.Single(shape => tree.Nodes[shape.Point].Name == "Zulu");
        var able = rectangles.Single(shape => tree.Nodes[shape.Point].Name == "Able");
        var beta = rectangles.Single(shape => tree.Nodes[shape.Point].Name == "Beta");
        Assert.IsLessThan(zulu.X0, alpha.X0);
        Assert.IsLessThan(beta.X0, able.X0, "Children from merged parents must still sort alphabetically.");
        Assert.AreEqual(1, tree.Data[0].Points[zulu.Point].Index, "Sorting does not change original identity.");
        CollectionAssert.AreEqual(new[] { 1, 3 }, tree.Data[0].Points[zulu.Point].SourceIndices!);
        geometry.Build(tree.Data, range, GraphKind.Flamegraph, 200, 320, flameReverse: true, flameFrameHeight: 16);
        Assert.IsTrue(geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle).All(shape => shape.Y1 < 48));
        var chart = new Chart { GraphKind = GraphKind.Flamegraph, FlameSearch = "a", ShowLegend = true };
        chart.Frames.AddRange(frames);
        _ = chart.GetRenderData(80);
        Assert.AreEqual(100d, chart.FlameMatchPercent, "Matching main and descendants must not double-count nested sample population.");
        chart.FlameSearch = "Zulu";
        _ = chart.GetRenderData(80);
        Assert.AreEqual(70d, chart.FlameMatchPercent);
        Assert.Contains("Search 'Zulu': 70.0%", chart.ExportSvg(80, 24), "Export snapshots must retain transient search and match highlighting.");
        var matched = chart.GetFlameTree();
        geometry.Build(matched.Data, chart.RenderRange(matched.Data), chart.GraphKind, 80, 24);
        Assert.AreEqual((byte)6, geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && matched.Nodes[shape.Point].Name == "Zulu").Shade);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100; iteration++) {
            matched.Search("Zulu");
            geometry.Build(matched.Data, chart.RenderRange(matched.Data), chart.GraphKind, 80, 24);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void FlamegraphWarmRenderingPickingAndExportsRetainBuffers() {
        var chart = Example();
        string cells = string.Join('\n', VisualSnapshotRenderer.Render(chart, 80, 24).ToMarkupLines());
        Assert.Contains("worker", cells);
        Assert.Contains("parse", cells);
        var plot = new SixelPlot();
        for (int index = 0; index < 10; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X, chart.PlotBounds.Bottom - 1);
        }
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        Assert.AreEqual("F7922F729243A99FA72A94AEC40452DF8851E198A7DE2ED9D4642DCA6B578386", hash);
        Assert.AreEqual((byte)0, plot.Pixels[plot.Width * plot.Height - 1], "Two-pixel rounded corners do not extend beyond the original frame bounds.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
            _ = chart.Pick(chart.PlotBounds.X, chart.PlotBounds.Bottom - 1);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        var tree = chart.GetFlameTree();
        var geometry = new ChartGeometry();
        geometry.Build(tree.Data, chart.RenderRange(tree.Data), chart.GraphKind, chart.PlotBounds.Width, chart.PlotBounds.Height, flameFrameHeight: chart.FlameFrameRows);
        var parent = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "main");
        int blankX = chart.PlotBounds.X + parent.X0, blankY = chart.PlotBounds.Y + parent.Y0 - 1;
        Assert.AreEqual(-1, chart.Pick(blankX, blankY), "Blank exclusive-self space must not choose nearest child.");
        chart.SelectedIndex = 1;
        chart.DrawPixels(plot, 240, 120);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) {
            chart.DrawPixels(plot, 240, 120);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        var svg = XDocument.Parse(chart.ExportSvg(80, 24));
        Assert.Contains("worker", svg.ToString());
        Assert.IsTrue(chart.AnnotationBounds.Any(), "SVG/Sixel must exclude overlaid frame labels.");
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71 }, chart.ExportPng(320, 160)[..4]);
        var copy = Example();
        chart.FlameFocusId = "a";
        chart.FlameReverse = true;
        copy.ImportSettings(chart.ExportSettings());
        Assert.AreEqual("a", copy.FlameFocusId);
        Assert.IsTrue(copy.FlameReverse);
    }

    [TestMethod]
    public async Task PublicFrameProjectionAndCustomEventsRetainAggregatedOriginalRows() {
        PSObject[] rows = [.. ExampleFrames().Select(frame => PSObject.AsPSObject(new {
            Id = frame.FrameId,
            Parent = frame.ParentId,
            Function = frame.Name,
            Samples = frame.Weight
        }))];
        using var app = new ObjectChartApp(rows, null, ["Samples"], GraphKind.Flamegraph, "Profile",
            new("Id", "Parent", "Function"), CancellationToken.None);
        await using (var ui = new DesignerUxTests.UiSession(app.Root, () => { }, 80, 30)) {
            var chart = app.Surface.Plot;
            var tree = chart.GetFlameTree();
            var geometry = new ChartGeometry();
            geometry.Build(tree.Data, chart.RenderRange(tree.Data), chart.GraphKind, chart.PlotBounds.Width, chart.PlotBounds.Height, flameFrameHeight: chart.FlameFrameRows);
            var worker = geometry.Primitives.Single(shape => shape.Kind == ChartPrimitiveKind.Rectangle && tree.Nodes[shape.Point].Name == "worker");
            int x = chart.PlotBounds.X + worker.X0, y = chart.PlotBounds.Y + worker.Y0;
            ChartSelection? invoked = null;
            chart.PointInvoked += selection => invoked = selection;
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, x, y, () => chart.Selection is not null);
            Assert.AreSame(rows[1], chart.Selection!.Source);
            Assert.Contains("self 4; inclusive 13", chart.Selection.SeriesName);
            Assert.AreEqual("a", chart.FlameFocusId, "Click zooms to the selected frame.");
            ui.Mouse(TerminalMouseKind.Move, TerminalMouseButton.None, chart.PlotBounds.X + 1,
                chart.PlotBounds.Bottom - 1, () => chart.FlameHover is not null);
            Assert.AreSame(rows[1], chart.FlameHover!.Source);
            ui.Key(TerminalKey.End, () => chart.Selection?.PointIndex == 2);
            Assert.AreSame(rows[2], chart.Selection!.Source);
            ui.Key(TerminalKey.Home, () => chart.Selection?.PointIndex == 1);
            CollectionAssert.AreEqual(new[] { 1, 3 }, tree.Data[0].Points.Single(point => point.Label == "worker").SourceIndices!);
            ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, x, y, () => true);
            ui.Key(TerminalKey.Enter, () => invoked is not null);
            Assert.AreSame(rows[1], invoked!.Source);
            chart.FlameFocusId = "a";
            using var copy = new ObjectChartApp(rows);
            copy.ImportConfiguration(app.ExportConfiguration());
            Assert.AreEqual("a", copy.Surface.Plot.FlameFocusId);
            Assert.AreEqual(20d, copy.Surface.Plot.GetFlameTree().Total);
        }
        var document = TuiDocument.FromJson("""{"Root":{"Id":"flame","Type":"Chart","Bind":"frames","GraphKind":"Flamegraph","FrameIdProperty":"Id","ParentIdProperty":"Parent","FrameNameProperty":"Function","WeightProperty":"Samples","SelectionBind":"selected"}}""");
        using var custom = new CustomTuiApp(document, new Hashtable { ["frames"] = rows });
        var customChart = Assert.IsInstanceOfType<Chart>(custom.Controls["flame"]);
        await using var customUi = new DesignerUxTests.UiSession(custom.Root, () => _ = custom.Tick(), 80, 24);
        Assert.AreEqual(0, customChart.Pick(customChart.PlotBounds.X, customChart.PlotBounds.Bottom - 1));
        Assert.AreSame(rows[0], customChart.GetFlameTree().Data[0].Points[0].Source);
        customUi.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, customChart.PlotBounds.X,
            customChart.PlotBounds.Bottom - 1, () => custom.State.Contains("selected"));
        Assert.AreSame(rows[0], custom.State["selected"]);
        custom.State["frames"] = new[] { rows[5] };
        custom.Tick();
        Assert.AreEqual(5d, customChart.GetFlameTree().Total);
    }

    [TestMethod]
    public void ConfigurableRowsPreserveWidthsDepthPickingAndCachedGeometry() {
        var chart = Example();
        chart.MinHeight = 40;
        chart.MaxHeight = 40;
        foreach (int rows in new[] { 1, 2, 4, 8 }) {
            chart.FlameFrameRows = rows;
            _ = VisualSnapshotRenderer.Render(chart, 80, 40);
            var tree = chart.GetFlameTree();
            var geometry = new ChartGeometry();
            foreach (bool reverse in new[] { false, true }) {
                chart.FlameReverse = reverse;
                geometry.Build(tree.Data, chart.RenderRange(tree.Data), GraphKind.Flamegraph, chart.PlotBounds.Width, chart.PlotBounds.Height,
                    flameReverse: reverse, flameFrameHeight: rows);
                var frames = geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle).ToArray();
                Assert.IsTrue(frames.All(shape => shape.Y1 - shape.Y0 + 1 == rows));
                var worker = frames.Single(shape => tree.Nodes[shape.Point].Name == "worker");
                Assert.AreEqual(1, chart.Pick(chart.PlotBounds.X + worker.X0, chart.PlotBounds.Y + worker.Y0));
                Assert.AreEqual(1, chart.Pick(chart.PlotBounds.X + worker.X1, chart.PlotBounds.Y + worker.Y1));
                Assert.AreEqual(52, worker.X1 - worker.X0 + 1, "Frame height never changes inclusive width.");
            }
            var copy = Example();
            copy.ImportSettings(chart.ExportSettings());
            Assert.AreEqual(rows, copy.FlameFrameRows);
        }
        chart.FlameFrameRows = 0;
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
        chart.FlameFrameRows = 9;
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
        var thin = new Chart { GraphKind = GraphKind.Flamegraph };
        thin.Frames.AddRange([new("root", null, "main", 0), new("tiny", "root", "a", 1), new("large", "root", "b", 999)]);
        var pixels = new SixelPlot();
        thin.DrawPixels(pixels, 1000, 64);
        Assert.IsGreaterThan((byte)1, pixels.Pixels[0], "A one-pixel frame retains its color rather than becoming entirely border.");
        Assert.AreEqual((byte)0, pixels.Pixels[63 * 1000], "Rounded corners stay inside inclusive frame bounds.");
    }
}
