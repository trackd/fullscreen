namespace fullscreen.Charts;

internal readonly record struct DiagramPoint(double X, double Y);
internal readonly record struct DiagramNode(double X, double Y, double Width, double Height, ChartNodeShape Shape);

/// <summary>Cold, deterministic topology layout. Redraw only maps retained nodes and routes to the viewport.</summary>
internal sealed class ChartDiagramLayout {
    internal DiagramNode[] Nodes { get; }
    internal DiagramPoint[][] Routes { get; }

    internal ChartDiagramLayout(ChartTopology graph, GraphKind kind) {
        int count = graph.Nodes.Length;
        Nodes = new DiagramNode[count];
        Routes = new DiagramPoint[graph.Edges.Length][];
        if (count == 0) {
            return;
        }
        var order = Enumerable.Range(0, count).OrderBy(index => graph.Nodes[index].NodeId, StringComparer.Ordinal).ToArray();
        var positions = new DiagramPoint[count];
        int[] ranks = kind == GraphKind.FlowChart ? FlowRanks(graph) : new int[count];
        if (kind == GraphKind.FlowChart) {
            foreach (var row in order.GroupBy(index => ranks[index])) {
                int column = 0;
                foreach (int index in row) {
                    positions[index] = new(column++ * 4, row.Key * 4);
                }
            }
            int widest = order.GroupBy(index => ranks[index]).Max(row => row.Count());
            foreach (var row in order.GroupBy(index => ranks[index])) {
                double offset = (widest - row.Count()) * 2;
                foreach (int index in row) {
                    positions[index] = positions[index] with { X = positions[index].X + offset };
                }
            }
        }
        else {
            NetworkPositions(graph, order, positions);
        }

        for (int index = 0; index < count; index++) {
            ChartNodeShape shape = graph.Nodes[index].Shape == ChartNodeShape.Auto
                ? kind == GraphKind.FlowChart ? ChartNodeShape.Process : ChartNodeShape.Terminal : graph.Nodes[index].Shape;
            Nodes[index] = new(positions[index].X, positions[index].Y, kind == GraphKind.NetworkDiagram ? 6 : 3,
                kind == GraphKind.NetworkDiagram ? 4 : 2, shape);
        }
        double left = Nodes.Min(node => node.X - node.Width / 2) - .5, right = Nodes.Max(node => node.X + node.Width / 2) + .5;
        double top = Nodes.Min(node => node.Y - node.Height / 2) - .5, bottom = Nodes.Max(node => node.Y + node.Height / 2) + .5;
        double routingLeft = left;
        var parallel = new Dictionary<(int, int), int>();
        for (int index = 0; index < Routes.Length; index++) {
            var (from, to) = graph.Connections[index];
            var a = positions[from];
            var b = positions[to];
            parallel.TryGetValue((from, to), out int number);
            parallel[(from, to)] = number + 1;
            DiagramPoint[] route;
            if (from == to) {
                double extra = .5 + number * .3;
                double halfWidth = Nodes[from].Width / 2, halfHeight = Nodes[from].Height / 2;
                route = [new(a.X + halfWidth, a.Y), new(a.X + halfWidth + .5 + extra, a.Y),
                    new(a.X + halfWidth + .5 + extra, a.Y - halfHeight - .5 - extra),
                    new(a.X, a.Y - halfHeight - .5 - extra), new(a.X, a.Y - halfHeight)];
            }
            else if (kind == GraphKind.FlowChart && ranks[to] == ranks[from] + 1) {
                double middle = (a.Y + b.Y) / 2 + Math.Min(.6, number * .2);
                route = [new(a.X, a.Y + 1), new(a.X, middle), new(b.X, middle), new(b.X, b.Y - 1)];
            }
            else if (kind == GraphKind.FlowChart) {
                // Non-adjacent and cyclic connections travel outside the node columns.
                double lane = routingLeft - .5 - index * .15;
                double departure = a.Y + 1.5 + (index % 3) * .1, arrival = b.Y - 1.5 - (index % 3) * .1;
                route = [new(a.X, a.Y + 1), new(a.X, departure), new(lane, departure),
                    new(lane, arrival), new(b.X, arrival), new(b.X, b.Y - 1)];
            }
            else {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length = Math.Max(.001, Math.Sqrt(dx * dx + dy * dy));
                double offset = number * .4;
                var midpoint = new DiagramPoint((a.X + b.X) / 2 - dy / length * offset, (a.Y + b.Y) / 2 + dx / length * offset);
                route = [Boundary(Nodes[from], midpoint), midpoint, Boundary(Nodes[to], midpoint)];
            }
            Routes[index] = route;
            foreach (var point in route) {
                left = Math.Min(left, point.X - .3);
                right = Math.Max(right, point.X + .3);
                top = Math.Min(top, point.Y - .3);
                bottom = Math.Max(bottom, point.Y + .3);
            }
        }
        double width = right - left, height = bottom - top;
        for (int index = 0; index < count; index++) {
            var node = Nodes[index];
            Nodes[index] = node with { X = (node.X - left) / width, Y = (node.Y - top) / height, Width = node.Width / width, Height = node.Height / height };
        }
        foreach (var route in Routes) {
            for (int index = 0; index < route.Length; index++) {
                route[index] = new((route[index].X - left) / width, (route[index].Y - top) / height);
            }
        }
    }

    private static DiagramPoint Boundary(DiagramNode node, DiagramPoint toward) {
        double dx = toward.X - node.X, dy = toward.Y - node.Y;
        double x = Math.Abs(dx) / (node.Width / 2), y = Math.Abs(dy) / (node.Height / 2);
        double divisor = node.Shape == ChartNodeShape.Decision ? x + y
            : node.Shape == ChartNodeShape.Terminal ? Math.Sqrt(x * x + y * y) : Math.Max(x, y);
        return divisor == 0 ? new(node.X, node.Y) : new(node.X + dx / divisor, node.Y + dy / divisor);
    }

    private static int[] FlowRanks(ChartTopology graph) {
        int count = graph.Nodes.Length;
        var children = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        foreach (var (from, to) in graph.Connections) {
            children[from].Add(to);
        }
        int next = 0, componentCount = 0;
        var index = Enumerable.Repeat(-1, count).ToArray();
        var low = new int[count];
        var components = new int[count];
        var stack = new Stack<int>();
        var active = new bool[count];
        void Visit(int node) {
            index[node] = low[node] = next++;
            stack.Push(node);
            active[node] = true;
            foreach (int child in children[node]) {
                if (index[child] < 0) {
                    Visit(child);
                    low[node] = Math.Min(low[node], low[child]);
                }
                else if (active[child]) {
                    low[node] = Math.Min(low[node], index[child]);
                }
            }
            if (low[node] != index[node]) {
                return;
            }
            int member;
            do {
                member = stack.Pop();
                active[member] = false;
                components[member] = componentCount;
            } while (member != node);
            componentCount++;
        }
        for (int node = 0; node < count; node++) {
            if (index[node] < 0) {
                Visit(node);
            }
        }
        var links = Enumerable.Range(0, componentCount).Select(_ => new HashSet<int>()).ToArray();
        var incoming = new int[componentCount];
        foreach (var (from, to) in graph.Connections) {
            int a = components[from], b = components[to];
            if (a != b && links[a].Add(b)) {
                incoming[b]++;
            }
        }
        var rank = new int[componentCount];
        var queue = new Queue<int>(Enumerable.Range(0, componentCount).Where(component => incoming[component] == 0));
        while (queue.TryDequeue(out int component)) {
            foreach (int target in links[component]) {
                rank[target] = Math.Max(rank[target], rank[component] + 1);
                if (--incoming[target] == 0) {
                    queue.Enqueue(target);
                }
            }
        }
        return [.. components.Select(component => rank[component])];
    }

    private static void NetworkPositions(ChartTopology graph, int[] order, DiagramPoint[] points) {
        int count = points.Length;
        double radius = Math.Max(3, Math.Sqrt(count) * 2);
        for (int index = 0; index < count; index++) {
            double angle = 2 * Math.PI * index / count;
            points[order[index]] = new(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }
        var forces = new DiagramPoint[count];
        for (int iteration = 0; iteration < 200; iteration++) {
            for (int index = 0; index < count; index++) {
                forces[index] = new(-points[index].X * .12, -points[index].Y * .12);
            }
            for (int a = 0; a < count; a++) {
                for (int b = a + 1; b < count; b++) {
                    double dx = points[a].X - points[b].X, dy = points[a].Y - points[b].Y;
                    double distance = Math.Max(.01, dx * dx + dy * dy);
                    double force = 12 / distance;
                    forces[a] = new(forces[a].X + dx * force, forces[a].Y + dy * force);
                    forces[b] = new(forces[b].X - dx * force, forces[b].Y - dy * force);
                }
            }
            foreach (var (a, b) in graph.Connections) {
                if (a == b) {
                    continue;
                }
                double dx = points[b].X - points[a].X, dy = points[b].Y - points[a].Y;
                double distance = Math.Max(.01, Math.Sqrt(dx * dx + dy * dy));
                double force = (distance - 5) * .15 / distance;
                forces[a] = new(forces[a].X + dx * force, forces[a].Y + dy * force);
                forces[b] = new(forces[b].X - dx * force, forces[b].Y - dy * force);
            }
            double step = .15 * (1 - iteration / 250d);
            for (int index = 0; index < count; index++) {
                points[index] = new(points[index].X + Math.Clamp(forces[index].X, -2, 2) * step,
                    points[index].Y + Math.Clamp(forces[index].Y, -2, 2) * step);
            }
        }
        // Deterministic packing guarantees distinct node bodies after the bounded solver.
        for (int position = 0; position < order.Length; position++) {
            int node = order[position];
            bool overlap;
            do {
                overlap = false;
                for (int previous = 0; previous < position; previous++) {
                    var other = points[order[previous]];
                    if (Math.Abs(points[node].X - other.X) < 6.5 && Math.Abs(points[node].Y - other.Y) < 4.5) {
                        points[node] = points[node] with { Y = other.Y + 4.5 };
                        overlap = true;
                        break;
                    }
                }
            } while (overlap);
        }
    }
}

internal static class ChartDiagram {
    internal static void Build(List<ChartPrimitive> primitives, ChartTopology graph, GraphKind kind, int width, int height) {
        var layout = graph.Layout(kind);
        int X(double value) => Math.Clamp((int)Math.Round(value * (width - 1)), 0, width - 1);
        int Y(double value) => Math.Clamp((int)Math.Round(value * (height - 1)), 0, height - 1);
        for (int edge = 0; edge < layout.Routes.Length; edge++) {
            var route = layout.Routes[edge];
            for (int index = 1; index < route.Length; index++) {
                primitives.Add(new(ChartPrimitiveKind.Line, X(route[index - 1].X), Y(route[index - 1].Y), X(route[index].X), Y(route[index].Y), 1, Shade: 6, Point: edge));
            }
            if (graph.Edges[edge].Directed && route.Length > 1) {
                var end = route[^1];
                var before = route[^2];
                double dx = X(end.X) - X(before.X), dy = Y(end.Y) - Y(before.Y);
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0) {
                    int size = Math.Clamp(Math.Min(width, height) / 50, 1, 5);
                    int x = X(end.X), y = Y(end.Y);
                    for (int side = -1; side <= 1; side += 2) {
                        int ax = Math.Clamp((int)Math.Round(x - dx / length * size + side * dy / length * size), 0, width - 1);
                        int ay = Math.Clamp((int)Math.Round(y - dy / length * size - side * dx / length * size), 0, height - 1);
                        primitives.Add(new(ChartPrimitiveKind.Line, ax, ay, x, y, 1, Shade: 6, Point: edge));
                    }
                }
            }
            if (graph.Edges[edge].Label.Length > 0 && route.Length > 1) {
                double longest = -1;
                int labelX = -1, labelY = -1, labelEnd = -1;
                for (int segment = 1; segment < route.Length; segment++) {
                    int ax = X(route[segment - 1].X), ay = Y(route[segment - 1].Y);
                    int bx = X(route[segment].X), by = Y(route[segment].Y);
                    int length = Math.Min(width, graph.Edges[edge].Label.Length);
                    int x = Math.Clamp((ax + bx) / 2 - length / 2 + (ax == bx ? length / 2 + 1 : 0), 0, width - length);
                    int y = Math.Clamp((ay + by) / 2 - (ay == by ? 1 : 0), 0, height - 1);
                    int end = x + length - 1;
                    bool clear = true;
                    foreach (var node in layout.Nodes) {
                        if (x <= X(node.X + node.Width / 2) && end >= X(node.X - node.Width / 2)
                            && y >= Y(node.Y - node.Height / 2) && y <= Y(node.Y + node.Height / 2)) {
                            clear = false;
                            break;
                        }
                    }
                    foreach (var existing in primitives) {
                        if (existing.Kind == ChartPrimitiveKind.Text && existing.Y0 == y && x <= existing.X1 && end >= existing.X0) {
                            clear = false;
                            break;
                        }
                    }
                    double distance = Math.Abs(bx - ax) + Math.Abs(by - ay);
                    if (clear && distance > longest) {
                        longest = distance;
                        labelX = x;
                        labelY = y;
                        labelEnd = end;
                    }
                }
                if (labelX >= 0) {
                    primitives.Add(new(ChartPrimitiveKind.Text, labelX, labelY, labelEnd, labelY,
                        1, Text: graph.Edges[edge].Label, Point: edge));
                }
            }
        }
        for (int index = 0; index < layout.Nodes.Length; index++) {
            var node = layout.Nodes[index];
            int left = X(node.X - node.Width / 2), right = X(node.X + node.Width / 2);
            int top = Y(node.Y - node.Height / 2), bottom = Y(node.Y + node.Height / 2);
            double cx = (left + right) / 2d, cy = (top + bottom) / 2d;
            double halfWidth = (right - left) / 2d, halfHeight = (bottom - top) / 2d;
            for (int y = top; y <= bottom; y++) {
                double fraction = halfHeight == 0 ? 0 : Math.Abs(y - cy) / halfHeight;
                double span = node.Shape == ChartNodeShape.Decision ? 1 - fraction
                    : node.Shape == ChartNodeShape.Terminal ? Math.Sqrt(Math.Max(0, 1 - fraction * fraction)) : 1;
                int x0 = Math.Clamp((int)Math.Ceiling(cx - halfWidth * span), left, right);
                int x1 = Math.Clamp((int)Math.Floor(cx + halfWidth * span), left, right);
                if (x1 >= x0) {
                    primitives.Add(new(ChartPrimitiveKind.Rectangle, x0, y, x1, y, Shade: 4, Point: index));
                }
            }
            int labelY = (int)Math.Round(cy);
            double labelFraction = halfHeight == 0 ? 0 : Math.Abs(labelY - cy) / halfHeight;
            double labelSpan = node.Shape == ChartNodeShape.Decision ? 1 - labelFraction
                : node.Shape == ChartNodeShape.Terminal ? Math.Sqrt(Math.Max(0, 1 - labelFraction * labelFraction)) : 1;
            int labelLeft = Math.Clamp((int)Math.Ceiling(cx - halfWidth * labelSpan) + 1, left, right);
            int labelRight = Math.Clamp((int)Math.Floor(cx + halfWidth * labelSpan) - 1, labelLeft, right);
            int visibleLength = Math.Min(labelRight - labelLeft + 1, graph.Nodes[index].Name.Length);
            labelLeft += (labelRight - labelLeft + 1 - visibleLength) / 2;
            labelRight = labelLeft + visibleLength - 1;
            primitives.Add(new(ChartPrimitiveKind.Text, labelLeft, labelY, labelRight, labelY,
                Text: graph.Nodes[index].Name, Shade: 4, Point: index));
        }
    }
}
