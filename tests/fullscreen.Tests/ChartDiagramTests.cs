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
public sealed class ChartDiagramTests {
    internal static Chart Example(GraphKind kind) {
        var chart = new Chart { GraphKind = kind, Title = "Order", ShowLegend = true, MinHeight = 32, MaxHeight = 32 };
        chart.Nodes.AddRange([
            new("start", "Start", ChartNodeShape.Terminal),
            new("check", "Approved?", ChartNodeShape.Decision),
            new("ship", "Ship order"),
            new("cancel", "Cancel"),
            new("end", "End", ChartNodeShape.Terminal),
            new("offline", "Isolated")
        ]);
        chart.Edges.AddRange([
            new("start", "check"), new("check", "ship", "Yes"), new("check", "cancel", "No"),
            new("ship", "end"), new("cancel", "end"), new("cancel", "check", "Retry"),
            new("ship", "ship", "Audit"), new("check", "ship", "Express")
        ]);
        return chart;
    }

    [TestMethod]
    [DataRow(GraphKind.FlowChart)]
    [DataRow(GraphKind.NetworkDiagram)]
    public void TopologyRetainsCyclesParallelEdgesAndIsolatedNodesWithBoundedGeometry(GraphKind kind) {
        var chart = Example(kind);
        var data = chart.GetRenderData(100);
        var topology = data[0].Topology!;
        var layout = topology.Layout(kind);
        Assert.AreSame(layout, topology.Layout(kind));
        Assert.HasCount(6, layout.Nodes);
        Assert.HasCount(8, layout.Routes);
        Assert.AreEqual(22, (int)Enum.Parse<GraphKind>("FlowChart"));
        Assert.AreEqual(23, (int)Enum.Parse<GraphKind>("NetworkDiagram"));
        Assert.IsFalse(RegisChartPresenterPrototype.Supports(kind));
        for (int a = 0; a < layout.Nodes.Length; a++) {
            var first = layout.Nodes[a];
            for (int b = a + 1; b < layout.Nodes.Length; b++) {
                var second = layout.Nodes[b];
                Assert.IsTrue(Math.Abs(first.X - second.X) >= (first.Width + second.Width) / 2
                    || Math.Abs(first.Y - second.Y) >= (first.Height + second.Height) / 2, "Node bodies must not overlap.");
            }
        }
        Assert.IsGreaterThan(3, layout.Routes[6].Length, "Self-loops retain an explicit route.");
        var geometry = new ChartGeometry();
        foreach (var (width, height) in new[] { (1, 1), (2, 7), (13, 5), (100, 32), (800, 512) }) {
            geometry.Build(data, chart.RenderRange(data), kind, width, height);
            Assert.IsTrue(geometry.Primitives.All(shape => shape.X0 >= 0 && shape.X0 < width && shape.X1 >= 0 && shape.X1 < width
                && shape.Y0 >= 0 && shape.Y0 < height && shape.Y1 >= 0 && shape.Y1 < height));
        }
        Assert.IsNotNull(XDocument.Parse(chart.ExportSvg(100, 32)));
        Assert.IsNotEmpty(chart.ExportPng(800, 512));
        var restored = Example(kind);
        restored.ImportSettings(chart.ExportSettings());
        Assert.AreEqual(kind, restored.GraphKind);
        Assert.ThrowsExactly<ArgumentException>(() => chart.Zoom(2));
        Assert.ThrowsExactly<ArgumentException>(() => chart.Pan(.2));
    }

    [TestMethod]
    public void TopologyInvalidIdsShapesReferencesAndBoundsAreExplicitErrors() {
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology([new("a", "A"), new("a", "Other")], []));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology([new("", "A")], []));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology([new("a", "bad\nname")], []));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology([new("a", "A", (ChartNodeShape)99)], []));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology([new("a", "A")], [new("a", "missing")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ChartTopology(Enumerable.Range(0, 257).Select(index => new ChartNode(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "N")).ToArray(), []));
        var undirected = new ChartTopology([new("a", "A"), new("b", "B")], [new("a", "b", Directed: false)]);
        Assert.IsNotNull(undirected.Layout(GraphKind.NetworkDiagram));
        Assert.ThrowsExactly<ArgumentException>(() => undirected.Layout(GraphKind.FlowChart));
        var chart = Example(GraphKind.FlowChart);
        chart.Minimum = 0;
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
        chart.Minimum = null;
        chart.Thresholds.Add(new(1));
        Assert.ThrowsExactly<ArgumentException>(() => chart.GetRenderData(80));
        var invalidEdge = PSObject.AsPSObject(new { SourceId = "a", TargetId = "a", Directed = "false" });
        Assert.ThrowsExactly<ArgumentException>(() => ChartTopology.FromObjects([PSObject.AsPSObject(new { NodeId = "a" })], [invalidEdge]));
        var empty = new Chart { GraphKind = GraphKind.FlowChart };
        Assert.IsNotNull(XDocument.Parse(empty.ExportSvg(1, 1)));
    }

    [TestMethod]
    [DataRow(GraphKind.FlowChart)]
    [DataRow(GraphKind.NetworkDiagram)]
    public void DiagramPicksOriginalNodeOrEdgeOnlyAtPaintedGeometryAndWarmPathsAllocateNothing(GraphKind kind) {
        var chart = Example(kind);
        _ = VisualSnapshotRenderer.Render(chart, 100, 32);
        var data = chart.GetRenderData(100);
        var geometry = new ChartGeometry();
        geometry.Build(data, chart.RenderRange(data), kind, chart.PlotBounds.Width, chart.PlotBounds.Height);
        foreach (var edgeLabel in geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Text && shape.Series == 1)) {
            foreach (var body in geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Rectangle && shape.Series == 0)) {
                Assert.IsFalse(edgeLabel.Y0 == body.Y0 && edgeLabel.X0 <= body.X1 && edgeLabel.X1 >= body.X0,
                    "Connection text must not overlap a node body.");
            }
        }
        var label = geometry.Primitives.First(shape => shape.Series == 0 && shape.Kind == ChartPrimitiveKind.Text && shape.Point == 1);
        var selected = chart.Pick(chart.PlotBounds.X + label.X0, chart.PlotBounds.Y + label.Y0);
        Assert.AreEqual(1, selected);
        Assert.AreSame(chart.Nodes[1], data[0].Points[selected].Source);
        ChartPrimitive? edgeHit = null;
        foreach (var shape in geometry.Primitives.Where(shape => shape.Series == 1 && shape.Kind == ChartPrimitiveKind.Line)) {
            var pick = chart.Pick(chart.PlotBounds.X + shape.X0, chart.PlotBounds.Y + shape.Y0);
            if (pick >= chart.Nodes.Count) {
                edgeHit = shape;
                Assert.AreSame(chart.Edges[shape.Point], data[1].Points[pick - chart.Nodes.Count].Source);
                break;
            }
        }
        Assert.IsNotNull(edgeHit);
        Assert.AreEqual(-1, chart.Pick(chart.PlotBounds.X - 1, chart.PlotBounds.Y));
        bool foundEmpty = false;
        for (int y = 0; y < chart.PlotBounds.Height && !foundEmpty; y++) {
            for (int x = 0; x < chart.PlotBounds.Width && !foundEmpty; x++) {
                foundEmpty = chart.Pick(chart.PlotBounds.X + x, chart.PlotBounds.Y + y) < 0;
            }
        }
        Assert.IsTrue(foundEmpty, "Empty space must not choose a nearest node.");
        var plot = new SixelPlot();
        for (int iteration = 0; iteration < 20; iteration++) {
            chart.DrawPixels(plot, 800, 512);
            _ = chart.Pick(chart.PlotBounds.X + label.X0, chart.PlotBounds.Y + label.Y0);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100; iteration++) {
            chart.DrawPixels(plot, 800, 512);
            _ = chart.Pick(chart.PlotBounds.X + label.X0, chart.PlotBounds.Y + label.Y0);
        }
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        string hash = Convert.ToHexString(SHA256.HashData(plot.Pixels));
        Assert.AreEqual(kind == GraphKind.FlowChart
            ? "7FDE61934F8C527D497A576BC79ED652F4509EE59CD98DC3EED9827947C0F292"
            : "1EE7D6053975C6E8D71C62676F2C9FA42296003B92A42AA5891CB2CFDE0B3F2B", hash);
    }

    [TestMethod]
    public void NetworkDiagramStandardViewportRetainsReadableServiceLabels() {
        var chart = new Chart { GraphKind = GraphKind.NetworkDiagram, ShowLegend = true, MinHeight = 36, MaxHeight = 36 };
        chart.Nodes.AddRange([
            new("gateway", "Gateway"), new("web", "Web service"), new("worker", "Worker"), new("queue", "Queue"),
            new("database", "Database"), new("cache", "Cache"), new("monitor", "Monitoring"), new("standby", "Standby")
        ]);
        chart.Edges.AddRange([
            new("gateway", "web"), new("web", "queue"), new("queue", "worker"), new("worker", "database"),
            new("web", "database"), new("web", "cache"), new("monitor", "web"), new("monitor", "worker")
        ]);
        _ = VisualSnapshotRenderer.Render(chart, 100, 36);
        var data = chart.GetRenderData(100);
        var geometry = new ChartGeometry();
        geometry.Build(data, chart.RenderRange(data), chart.GraphKind, chart.PlotBounds.Width, chart.PlotBounds.Height);
        foreach (var label in geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Text && shape.Series == 0)) {
            Assert.IsGreaterThanOrEqualTo(chart.Nodes[label.Point].Name.Length, label.X1 - label.X0 + 1,
                "Standard network labels must not collapse to a letter inside a thin ellipse.");
        }
        Assert.HasCount(chart.Nodes.Count, geometry.Primitives.Where(shape => shape.Kind == ChartPrimitiveKind.Text && shape.Series == 0).ToArray());
    }

    [TestMethod]
    public async Task SeparateCollectionsPreservePowerShellSourcesAndCustomBindings() {
        var nodes = new[] { PSObject.AsPSObject(new { NodeId = "a", Name = "A" }), PSObject.AsPSObject(new { NodeId = "b", Name = "B" }) };
        var edges = new[] { PSObject.AsPSObject(new { SourceId = "a", TargetId = "b", Label = "Calls" }) };
        var topology = ChartTopology.FromObjects(nodes, edges);
        Assert.AreSame(nodes[0], topology.Data[0].Points[0].Source);
        Assert.AreSame(edges[0], topology.Data[1].Points[0].Source);
        Assert.AreEqual(2, topology.Data[1].Points[0].Index);
        var nativeNode = new ChartNode("n", "Native");
        var nativeEdge = new ChartEdge("n", "n", "Loop");
        var nativeTopology = ChartTopology.FromObjects([nativeNode], [nativeEdge]);
        Assert.AreSame(nativeNode, nativeTopology.Data[0].Points[0].Source);
        Assert.AreSame(nativeEdge, nativeTopology.Data[1].Points[0].Source);
        using var viewer = new ObjectChartApp([.. nodes, .. edges], null, null, GraphKind.FlowChart, "Separate", new(), CancellationToken.None, diagram: topology);
        _ = VisualSnapshotRenderer.Render(viewer.Root, 100, 32);
        viewer.Configure(null, [], GraphKind.NetworkDiagram);
        Assert.AreEqual(GraphKind.NetworkDiagram, viewer.Surface.GraphType);
        Assert.ThrowsExactly<ArgumentException>(() => viewer.Configure(null, [], GraphKind.Line));
        Assert.ThrowsExactly<ArgumentException>(() => viewer.SetFilter(ObjectChartFilter.First, 1));
        string config = viewer.ExportConfiguration();
        viewer.ImportConfiguration(config);
        Assert.AreEqual(GraphKind.NetworkDiagram, viewer.Surface.Plot.GraphKind);
        var document = new TuiDocument { Root = new() { Id = "graph", Type = "Chart", GraphKind = "FlowChart", Bind = "nodes", EdgeBind = "edges", SelectionBind = "chosen", Height = 32 } };
        using var app = new CustomTuiApp(TuiDocument.FromJson(document.ToJson()), new Hashtable { ["nodes"] = nodes, ["edges"] = edges });
        var control = (Chart)app.Controls["graph"];
        Assert.AreSame(edges[0], control.GetRenderData(100)[1].Points[0].Source);
        await using (var ui = new DesignerUxTests.UiSession(app.Root, () => _ = app.Tick(), 100, 32)) {
            var geometry = new ChartGeometry();
            var data = control.GetRenderData(100);
            geometry.Build(data, control.RenderRange(data), GraphKind.FlowChart, control.PlotBounds.Width, control.PlotBounds.Height);
            var edge = geometry.Primitives.First(shape => shape.Series == 1 && shape.Kind == ChartPrimitiveKind.Line
                && control.Pick(control.PlotBounds.X + shape.X1, control.PlotBounds.Y + shape.Y1) == 2);
            ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, control.PlotBounds.X + edge.X1,
                control.PlotBounds.Y + edge.Y1, () => app.State.ContainsKey("chosen"));
            Assert.AreSame(edges[0], app.State["chosen"]);
            Assert.IsTrue(app.Tick());
            var previous = control.GetRenderData(100);
            app.Tick();
            Assert.AreSame(previous, control.GetRenderData(100), "Unchanged sampled topology retains its layout/data cache.");
            var replacement = new[] { PSObject.AsPSObject(new { SourceId = "b", TargetId = "a", Label = "Reply" }) };
            app.State["edges"] = replacement;
            app.Tick();
            Assert.AreSame(replacement[0], control.GetRenderData(100)[1].Points[0].Source);
        }
        document.Root.EdgeBind = "";
        Assert.ThrowsExactly<ArgumentException>(document.Validate);
    }
}
