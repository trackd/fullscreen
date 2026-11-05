using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;

namespace fullscreen.Charts;

public partial class Chart {
    /// <summary>Gets the separately defined relationship nodes. NodeId values must be unique.</summary>
    [Bindable]
    public BindableList<ChartNode> Nodes { get; }

    /// <summary>Gets connections referencing existing Nodes. Original edge identity is preserved.</summary>
    [Bindable]
    public BindableList<ChartEdge> Edges { get; }

    private int _nodeVersion = -1, _edgeVersion = -1;
    private ChartTopology? _topology;

    private ChartSelection? PickDiagram(ChartDataSeries[] data, int x, int y) {
        var bounds = PlotBounds;
        if (!bounds.Contains(x, y)) {
            return null;
        }
        x -= bounds.X;
        y -= bounds.Y;
        _pickGeometry.Build(data, RenderRange(data), GraphKind, bounds.Width, bounds.Height);
        // Nodes are painted after edges; inspect only the visible topmost geometry.
        for (int index = _pickGeometry.Primitives.Count - 1; index >= 0; index--) {
            var shape = _pickGeometry.Primitives[index];
            bool hit = false;
            if (shape.Kind is ChartPrimitiveKind.Rectangle or ChartPrimitiveKind.Text) {
                int right = shape.Kind == ChartPrimitiveKind.Text ? Math.Min(shape.X1, shape.X0 + shape.Text!.Length - 1) : shape.X1;
                hit = x >= shape.X0 && x <= right && y >= shape.Y0 && y <= shape.Y1;
            }
            else {
                int steps = Math.Max(1, Math.Max(Math.Abs(shape.X1 - shape.X0), Math.Abs(shape.Y1 - shape.Y0)));
                for (int step = 0; step <= steps && !hit; step++) {
                    hit = x == (int)Math.Round(shape.X0 + (shape.X1 - shape.X0) * step / (double)steps)
                        && y == (int)Math.Round(shape.Y0 + (shape.Y1 - shape.Y0) * step / (double)steps);
                }
            }
            if (hit && shape.Point >= 0) {
                return DiagramSelection(data, shape.Series, shape.Point);
            }
        }
        return null;
    }

    private ChartSelection DiagramSelection(ChartDataSeries[] data, int series, int position) {
        var point = data[series].Points[position];
        if (_gridSelectionData == data && _gridSelectionVersion == DataVersion && _gridSelection is { } current
            && current.SeriesIndex == series && current.PointIndex == point.Index) {
            return current;
        }
        _gridSelectionData = data;
        _gridSelectionVersion = DataVersion;
        var graph = data[0].Topology!;
        string details = series == 0 ? $"Node {graph.Nodes[position].NodeId}"
            : $"{graph.Edges[position].SourceId} {(graph.Edges[position].Directed ? "->" : "--")} {graph.Edges[position].TargetId}";
        return _gridSelection = new(series, details, point.Index, point.X, point.Y!.Value, point.Label, point.Source);
    }

    private void NavigateDiagram(XenoAtom.Terminal.TerminalKey key) {
        var data = GetRenderData(Math.Max(1, Bounds.Width));
        int count = data[0].Count + data[1].Count;
        if (count == 0) {
            return;
        }
        int target = key switch {
            XenoAtom.Terminal.TerminalKey.Home => 0,
            XenoAtom.Terminal.TerminalKey.End => count - 1,
            XenoAtom.Terminal.TerminalKey.Left => Math.Clamp(SelectedIndex - 1, 0, count - 1),
            _ => Math.Clamp(SelectedIndex + 1, 0, count - 1)
        };
        int series = target < data[0].Count ? 0 : 1;
        Select(DiagramSelection(data, series, series == 0 ? target : target - data[0].Count));
    }
}
