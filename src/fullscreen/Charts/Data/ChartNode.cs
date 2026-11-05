using System.Globalization;
using System.Management.Automation;

namespace fullscreen.Charts;

/// <summary>Presentation shape of a relationship-diagram node.</summary>
public enum ChartNodeShape {
    /// <summary>Process rectangle in flow charts, ellipse in network diagrams.</summary>
    Auto,
    /// <summary>Rectangular processing step.</summary>
    Process,
    /// <summary>Diamond-shaped decision.</summary>
    Decision,
    /// <summary>Elliptical start, end or network endpoint.</summary>
    Terminal
}

/// <summary>A uniquely identified diagram node, independent of its connections.</summary>
/// <param name="NodeId">Unique ordinal identifier.</param>
/// <param name="Name">Printable display label.</param>
/// <param name="Shape">Optional shape; Auto depends on the diagram kind.</param>
/// <param name="Source">Original node object for inspection.</param>
public sealed record ChartNode(string NodeId, string Name, ChartNodeShape Shape = ChartNodeShape.Auto, object? Source = null);

/// <summary>A connection between existing nodes; parallel edges and self-loops remain distinct.</summary>
/// <param name="SourceId">Origin node identifier.</param>
/// <param name="TargetId">Destination node identifier.</param>
/// <param name="Label">Optional printable connection label.</param>
/// <param name="Directed">Whether to draw a destination arrow; flow charts require directed edges.</param>
/// <param name="Source">Original edge object for inspection.</param>
public sealed record ChartEdge(string SourceId, string TargetId, string Label = "", bool Directed = true, object? Source = null);

internal sealed class ChartTopology {
    internal ChartNode[] Nodes { get; }
    internal ChartEdge[] Edges { get; }
    internal (int From, int To)[] Connections { get; }
    internal ChartDataSeries[] Data { get; }
    private readonly Dictionary<GraphKind, ChartDiagramLayout> _layouts = [];
    internal static bool Supports(GraphKind kind) => kind is GraphKind.FlowChart or GraphKind.NetworkDiagram;

    internal ChartTopology(IReadOnlyList<ChartNode> nodes, IReadOnlyList<ChartEdge> edges) {
        if (nodes.Count > 256 || edges.Count > 4096) {
            throw new ArgumentException("Relationship diagrams support at most 256 nodes and 4096 edges; reduce the graph before rendering.");
        }
        Nodes = [.. nodes];
        Edges = [.. edges];
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < Nodes.Length; index++) {
            var node = Nodes[index];
            if (node is null || !Printable(node.NodeId, 256) || !Printable(node.Name, 1024) || !Enum.IsDefined(node.Shape)) {
                throw new ArgumentException("Diagram nodes require printable nonempty NodeId/Name (up to 256/1024 characters) and a defined Shape.");
            }
            if (!ids.TryAdd(node.NodeId, index)) {
                throw new ArgumentException($"Duplicate diagram NodeId '{node.NodeId}'.");
            }
        }
        Connections = new (int, int)[Edges.Length];
        for (int index = 0; index < Edges.Length; index++) {
            var edge = Edges[index];
            if (edge is null || edge.Label is null || edge.Label.Length > 1024 || edge.Label.Any(char.IsControl)
                || edge.SourceId is null || edge.TargetId is null
                || !ids.TryGetValue(edge.SourceId, out int from) || !ids.TryGetValue(edge.TargetId, out int to)) {
                throw new ArgumentException($"Diagram edge {index + 1} requires existing SourceId/TargetId and a printable label of at most 1024 characters.");
            }
            Connections[index] = (from, to);
        }
        var nodePoints = Nodes.Select((node, index) => new ChartPoint(index, index, 1, node.Name) { Source = node.Source ?? node }).ToArray();
        var edgePoints = Edges.Select((edge, index) => new ChartPoint(Nodes.Length + index, index, 1, edge.Label) { Source = edge.Source ?? edge }).ToArray();
        Data = [new("Nodes", nodePoints) { Topology = this }, new("Edges", edgePoints) { Topology = this }];
    }

    internal ChartDiagramLayout Layout(GraphKind kind) {
        if (!Supports(kind)) {
            throw new ArgumentException("A topology requires FlowChart or NetworkDiagram.");
        }
        if (kind == GraphKind.FlowChart) {
            foreach (var edge in Edges) {
                if (!edge.Directed) {
                    throw new ArgumentException("FlowChart requires directed edges. Use NetworkDiagram for undirected connections.");
                }
            }
        }
        if (!_layouts.TryGetValue(kind, out var layout)) {
            layout = new(this, kind);
            _layouts.Add(kind, layout);
        }
        return layout;
    }

    internal static void ValidateOptions(double? minimum, double? maximum, double? minimumX, double? maximumX,
        ChartScale x, ChartScale y, int thresholds, int annotations, ChartSelectedRange? selectedRange) {
        if (minimum is not null || maximum is not null || minimumX is not null || maximumX is not null
            || x != ChartScale.Linear || y != ChartScale.Linear || thresholds != 0 || annotations != 0 || selectedRange is not null) {
            throw new ArgumentException("Relationship diagrams use intrinsic topology layout, not numeric axes, bounds, ranges or coordinate overlays.");
        }
    }

    internal static ChartTopology FromObjects(IReadOnlyList<object> nodes, IReadOnlyList<object> edges) {
        if (nodes.Count > 256 || edges.Count > 4096) {
            throw new ArgumentException("Relationship diagrams support at most 256 nodes and 4096 edges.");
        }
        object? Value(PSObject row, string property) {
            object? value = row.BaseObject is System.Collections.IDictionary dictionary ? dictionary[property] : row.Properties[property]?.Value;
            return value is PSObject wrapped ? wrapped.BaseObject : value;
        }
        string Text(PSObject row, string property) => Convert.ToString(Value(row, property), CultureInfo.InvariantCulture) ?? "";
        var projectedNodes = new ChartNode[nodes.Count];
        for (int index = 0; index < nodes.Count; index++) {
            var original = nodes[index] ?? throw new ArgumentException("Diagram node records cannot be null.");
            var row = PSObject.AsPSObject(original);
            string id = Text(row, "NodeId"), name = Text(row, "Name");
            string shapeText = Text(row, "Shape");
            ChartNodeShape shape = ChartNodeShape.Auto;
            if (shapeText.Length > 0 && (!Enum.TryParse(shapeText, true, out shape) || !Enum.IsDefined(shape))) {
                throw new ArgumentException($"Diagram node '{id}' has an unknown Shape '{shapeText}'.");
            }
            projectedNodes[index] = new(id, name.Length == 0 ? id : name, shape, original);
        }
        var projectedEdges = new ChartEdge[edges.Count];
        for (int index = 0; index < edges.Count; index++) {
            var original = edges[index] ?? throw new ArgumentException("Diagram edge records cannot be null.");
            var row = PSObject.AsPSObject(original);
            object? directed = Value(row, "Directed");
            if (directed is not null and not bool) {
                throw new ArgumentException("Diagram Directed must be a Boolean or omitted (default true).");
            }
            projectedEdges[index] = new(Text(row, "SourceId"), Text(row, "TargetId"), Text(row, "Label"), directed is not false, original);
        }
        return new(projectedNodes, projectedEdges);
    }

    private static bool Printable(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
}
