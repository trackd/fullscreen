namespace fullscreen.Charts;

/// <summary>A profiling frame with a unique ID, optional root parent, display name and exclusive self weight.</summary>
/// <param name="FrameId">Unique nonempty frame identifier, compared ordinally.</param>
/// <param name="ParentId">Parent frame identifier; null or empty denotes a root.</param>
/// <param name="Name">Nonempty displayed function/frame name; equal sibling names are aggregated.</param>
/// <param name="Weight">Finite nonnegative exclusive time or sample count, excluding descendants.</param>
/// <param name="Source">Optional original object retained for inspection.</param>
public sealed record ChartFrame(string FrameId, string? ParentId, string Name, double Weight, object? Source = null);

internal sealed class ChartFlameTree {
    internal sealed class Node(string id, string name, int parent, int depth) {
        internal string Id { get; } = id;
        internal string Name { get; } = name;
        internal int Parent { get; } = parent;
        internal int Depth { get; } = depth;
        internal double Self;
        internal double Inclusive;
        internal byte Shade;
        internal readonly List<int> Children = [];
        internal readonly List<int> Sources = [];
    }
    internal Node[] Nodes { get; }
    internal double Total { get; }
    internal int Depth { get; }
    internal ChartDataSeries[] Data { get; }
    internal bool[] Matches { get; }
    private string _search = "";
    internal double MatchPercent { get; private set; }

    internal void Search(string term) {
        if (_search == term) {
            return;
        }
        _search = term;
        double matched = 0;
        for (int index = 0; index < Nodes.Length; index++) {
            var node = Nodes[index];
            Matches[index] = term.Length > 0 && node.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            if (!Matches[index]) {
                continue;
            }
            int parent = node.Parent;
            while (parent >= 0 && !Matches[parent]) {
                parent = Nodes[parent].Parent;
            }
            if (parent < 0 && Total > 0) {
                matched += node.Inclusive / Total;
            }
        }
        MatchPercent = Math.Min(100, matched * 100);
    }

    internal ChartFlameTree(IReadOnlyList<ChartFrame> frames) {
        if (frames.Count > 1_000_000) {
            throw new ArgumentException("Flamegraph supports at most 1000000 input frames.");
        }
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < frames.Count; index++) {
            var frame = frames[index];
            if (frame is null || string.IsNullOrWhiteSpace(frame.FrameId) || string.IsNullOrWhiteSpace(frame.Name)
                || frame.FrameId.Length > 256 || frame.Name.Length > 1024
                || frame.FrameId.Any(char.IsControl) || frame.Name.Any(char.IsControl) || !double.IsFinite(frame.Weight) || frame.Weight < 0) {
                throw new ArgumentException("Flamegraph requires nonempty IDs (up to 256 characters), printable names (up to 1024 characters) and finite nonnegative self weights.");
            }
            if (!ids.TryAdd(frame.FrameId, index)) {
                throw new ArgumentException($"Duplicate Flamegraph frame ID '{frame.FrameId}'.");
            }
        }
        var parents = new int[frames.Count];
        var children = new List<int>[frames.Count + 1];
        for (int index = 0; index < children.Length; index++) {
            children[index] = [];
        }
        for (int index = 0; index < frames.Count; index++) {
            string? parent = frames[index].ParentId;
            if (string.IsNullOrEmpty(parent)) {
                parents[index] = -1;
            }
            else if (!ids.TryGetValue(parent, out parents[index])) {
                throw new ArgumentException($"Unknown Flamegraph parent ID '{parent}'.");
            }
            children[parents[index] + 1].Add(index);
        }
        foreach (var siblings in children) {
            siblings.Sort((left, right) => StringComparer.Ordinal.Compare(frames[left].Name, frames[right].Name));
        }
        var order = new List<int>(frames.Count);
        var depths = new int[frames.Count];
        var queue = new Queue<int>(children[0]);
        while (queue.TryDequeue(out int index)) {
            int depth = parents[index] < 0 ? 0 : depths[parents[index]] + 1;
            if (depth >= 1024) {
                throw new ArgumentException("Flamegraph depth must not exceed 1024 frames.");
            }
            depths[index] = depth;
            order.Add(index);
            foreach (int child in children[index + 1]) {
                queue.Enqueue(child);
            }
        }
        if (order.Count != frames.Count) {
            throw new ArgumentException("Flamegraph parent references contain a cycle.");
        }
        var nodes = new List<Node>();
        var prefixes = new Dictionary<(int Parent, string Name), int>();
        var mapped = new int[frames.Count];
        foreach (int index in order) {
            var frame = frames[index];
            int parent = parents[index] < 0 ? -1 : mapped[parents[index]];
            if (!prefixes.TryGetValue((parent, frame.Name), out int target)) {
                target = nodes.Count;
                prefixes.Add((parent, frame.Name), target);
                nodes.Add(new(frame.FrameId, frame.Name, parent, depths[index]));
                byte shade = (byte)(2 + ChartFlame.StableColor(frame.Name) % 4);
                if (parent >= 0 && nodes[parent].Shade == shade) {
                    shade = (byte)(2 + (shade - 1) % 4);
                }
                nodes[target].Shade = shade;
                if (parent >= 0) {
                    nodes[parent].Children.Add(target);
                }
            }
            mapped[index] = target;
            nodes[target].Self = Add(nodes[target].Self, frame.Weight);
            nodes[target].Sources.Add(index);
        }
        double total = 0;
        int maximumDepth = 0;
        foreach (var node in nodes) {
            node.Children.Sort((left, right) => StringComparer.Ordinal.Compare(nodes[left].Name, nodes[right].Name));
        }
        byte rootShade = 0;
        foreach (var node in nodes) {
            if (node.Parent < 0) {
                if (node.Shade == rootShade) {
                    node.Shade = (byte)(2 + (node.Shade - 1) % 4);
                }
                rootShade = node.Shade;
            }
            byte previous = 0;
            foreach (int child in node.Children) {
                byte shade = (byte)(2 + ChartFlame.StableColor(nodes[child].Name) % 4);
                while (shade == node.Shade || shade == previous) {
                    shade = (byte)(2 + (shade - 1) % 4);
                }
                nodes[child].Shade = shade;
                previous = shade;
            }
        }
        for (int index = nodes.Count - 1; index >= 0; index--) {
            var node = nodes[index];
            node.Inclusive = Add(node.Inclusive, node.Self);
            maximumDepth = Math.Max(maximumDepth, node.Depth);
            if (node.Parent >= 0) {
                nodes[node.Parent].Inclusive = Add(nodes[node.Parent].Inclusive, node.Inclusive);
            }
            else {
                total = Add(total, node.Inclusive);
            }
        }
        Nodes = [.. nodes];
        Matches = new bool[nodes.Count];
        Total = total;
        Depth = nodes.Count == 0 ? 0 : maximumDepth + 1;
        var points = new ChartPoint[nodes.Count];
        for (int index = 0; index < nodes.Count; index++) {
            var node = nodes[index];
            int original = node.Sources[0];
            points[index] = new(original, index, node.Inclusive, node.Name) {
                Source = frames[original].Source ?? frames[original],
                SourceIndices = [.. node.Sources]
            };
        }
        Data = [new("Inclusive weight", points) { FlameTree = this }];
    }

    private static double Add(double left, double right) {
        double sum = left + right;
        if (!double.IsFinite(sum)) {
            throw new ArgumentException("Flamegraph inclusive totals must fit in finite double values.");
        }
        return sum;
    }
}
