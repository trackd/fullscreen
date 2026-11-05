namespace fullscreen.Charts;

/// <summary>Partitions hierarchical inclusive widths; depth and source mapping are shared by drawing and picking.</summary>
internal sealed class ChartFlame {
    private double[] _left = [];
    private double[] _end = [];

    internal void Build(List<ChartPrimitive> shapes, ChartFlameTree tree, int width, int height, bool reverse, int focus, int frameHeight) {
        if (tree.Total == 0 || tree.Nodes.Length == 0) {
            shapes.Add(new(ChartPrimitiveKind.Text, 0, height / 2, width - 1, height / 2, Text: "No positive frame weights"));
            return;
        }
        if (_left.Length < tree.Nodes.Length) {
            _left = new double[tree.Nodes.Length];
            _end = new double[tree.Nodes.Length];
        }
        Array.Fill(_left, double.NaN, 0, tree.Nodes.Length);
        double total = focus >= 0 && focus < tree.Nodes.Length ? tree.Nodes[focus].Inclusive : tree.Total;
        if (total == 0) {
            shapes.Add(new(ChartPrimitiveKind.Text, 0, height / 2, width - 1, height / 2, Text: "No positive frame weights"));
            return;
        }
        int firstDepth = focus >= 0 && focus < tree.Nodes.Length ? tree.Nodes[focus].Depth : 0;
        double roots = 0;
        for (int index = 0; index < tree.Nodes.Length; index++) {
            var node = tree.Nodes[index];
            if (focus == index) {
                _left[index] = 0;
                _end[index] = node.Self / total;
            }
            else if (focus < 0 && node.Parent < 0) {
                _left[index] = roots;
                roots += node.Inclusive / total;
                _end[index] = _left[index] + node.Self / total;
            }
            if (!double.IsFinite(_left[index]) || node.Inclusive == 0) {
                continue;
            }
            foreach (int child in node.Children) {
                _left[child] = _end[index];
                _end[index] += tree.Nodes[child].Inclusive / total;
                _end[child] = _left[child] + tree.Nodes[child].Self / total;
            }
        }
        frameHeight = Math.Clamp(frameHeight, 1, height);
        int rows = Math.Max(1, height / frameHeight);
        for (int index = 0; index < tree.Nodes.Length; index++) {
            var node = tree.Nodes[index];
            if (!double.IsFinite(_left[index]) || node.Inclusive == 0) {
                continue;
            }
            int depth = node.Depth - firstDepth;
            if (depth < 0 || depth >= rows) {
                continue;
            }
            int left = Math.Clamp((int)Math.Round(_left[index] * width), 0, width);
            int right = Math.Clamp((int)Math.Round((_left[index] + node.Inclusive / total) * width), 0, width);
            int start = depth * frameHeight;
            int end = start + frameHeight;
            int top = reverse ? start : height - end;
            int bottom = reverse ? end - 1 : height - start - 1;
            if (right <= left) {
                continue;
            }
            byte shade = tree.Matches[index] ? (byte)6 : node.Shade;
            shapes.Add(new(ChartPrimitiveKind.Rectangle, left, top, right - 1, bottom, Shade: shade, Point: index));
            if (right - left >= 3) {
                int length = Math.Min(node.Name.Length, right - left - 2);
                // Substrings are avoided on redraw; drawing supplies the span width.
                shapes.Add(new(ChartPrimitiveKind.Text, left + 1, (top + bottom) / 2, left + length, (top + bottom) / 2,
                    Shade: shade, Text: node.Name, Point: index));
            }
        }
    }

    internal static uint StableColor(string name) {
        uint hash = 2166136261;
        foreach (char value in name) {
            hash = unchecked((hash ^ value) * 16777619);
        }
        return hash;
    }
}
