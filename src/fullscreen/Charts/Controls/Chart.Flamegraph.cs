using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;

namespace fullscreen.Charts;

public partial class Chart {
    /// <summary>Gets profiling frames. IDs are unique; Weight is exclusive self time/sample count.</summary>
    [Bindable]
    public BindableList<ChartFrame> Frames { get; }

    /// <summary>Gets or sets whether flamegraph roots appear at the top rather than the bottom.</summary>
    [Bindable]
    public partial bool FlameReverse { get; set; }

    /// <summary>Gets or sets terminal rows per flamegraph depth, from 1 through 8; default 2.</summary>
    [Bindable]
    public partial int FlameFrameRows { get; set; }

    /// <summary>Gets or sets the original representative frame ID to focus; empty shows all roots.</summary>
    [Bindable]
    public partial string FlameFocusId { get; set; }

    /// <summary>Gets or sets a case-insensitive function-name substring to highlight.</summary>
    [Bindable]
    public partial string FlameSearch { get; set; }

    /// <summary>Gets the percentage of the whole profile matching search, without counting nested matches twice.</summary>
    public double FlameMatchPercent {
        get {
            if (GraphKind != GraphKind.Flamegraph) {
                return 0;
            }
            var tree = GetFlameTree();
            tree.Search(FlameSearch);
            return tree.MatchPercent;
        }
    }

    /// <summary>Requests a host search editor when the user presses / in a flamegraph.</summary>
    public event Action? FlameSearchRequested;

    /// <summary>Gets frame details at the current pointer without changing source selection.</summary>
    [Bindable]
    public partial ChartSelection? FlameHover { get; set; }

    private int _frameVersion = -1;
    private ChartFlameTree? _flameTree;
    internal static Color FlameColor(Themes.ThemePalette palette, byte shade) => shade switch {
        2 => palette.ChartYellow,
        3 => Color.Mix(palette.ChartYellow, palette.ChartRed, .35f),
        4 => Color.Mix(palette.ChartYellow, palette.ChartRed, .7f),
        5 => palette.ChartRed,
        _ => palette.ChartBlue
    };
    private Color FlameColor(byte shade) => FlameColor(Palette, shade);

    internal ChartFlameTree GetFlameTree() {
        var data = GetData(Math.Max(1, Bounds.Width));
        if (data.Length != 1 || data[0].FlameTree is not { } tree) {
            throw new ArgumentException("Flamegraph requires FrameId/ParentId/Name rows with exclusive self weights, not ordinary numeric series.");
        }
        return tree;
    }

    internal int FlameFocus(ChartFlameTree tree) {
        if (string.IsNullOrEmpty(FlameFocusId)) {
            return -1;
        }
        for (int index = 0; index < tree.Nodes.Length; index++) {
            if (tree.Nodes[index].Id == FlameFocusId) {
                return index;
            }
        }
        throw new ArgumentException($"Unknown Flamegraph focus frame ID '{FlameFocusId}'.");
    }

    /// <summary>Focuses the currently selected flamegraph frame without changing its original source.</summary>
    public void FocusSelectedFrame() {
        if (GraphKind != GraphKind.Flamegraph || Selection is null) {
            return;
        }
        var tree = GetFlameTree();
        for (int index = 0; index < tree.Data[0].Count; index++) {
            if (tree.Data[0].Points[index].Index == Selection.PointIndex) {
                FlameFocusId = tree.Nodes[index].Id;
                return;
            }
        }
    }

    /// <summary>Moves flamegraph focus to its parent, or to all roots at the top level.</summary>
    public void FlameParent() {
        if (GraphKind != GraphKind.Flamegraph) {
            return;
        }
        var tree = GetFlameTree();
        int focus = FlameFocus(tree);
        FlameFocusId = focus < 0 || tree.Nodes[focus].Parent < 0 ? "" : tree.Nodes[tree.Nodes[focus].Parent].Id;
    }

    private void ValidateFlameOptions() {
        if (GraphKind != GraphKind.Flamegraph) {
            return;
        }
        if (Minimum is not null || Maximum is not null || MinimumX is not null || MaximumX is not null
            || XAxis.Scale == ChartScale.Logarithmic || YAxis.Scale == ChartScale.Logarithmic
            || Thresholds.Count != 0 || Annotations.Count != 0 || SelectedRange is not null) {
            throw new ArgumentException("Flamegraph uses inclusive widths/depth rows; numeric bounds, logarithmic axes, overlays and selected coordinate ranges are not supported.");
        }
        if (FlameSearch is null || FlameSearch.Length > 1024) {
            throw new ArgumentException("Flamegraph search must contain at most 1024 characters.");
        }
        if (FlameFrameRows is < 1 or > 8) {
            throw new ArgumentException("FlameFrameRows must be between 1 and 8 terminal rows.");
        }
        var tree = GetFlameTree();
        tree.Search(FlameSearch);
        _ = FlameFocus(tree);
    }
}
