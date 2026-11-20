namespace fullscreen.Custom;

public sealed partial class CustomTuiApp {
    private readonly List<(TuiNode Node, Charts.Chart Chart)> _rangeBindings = [];
    private bool _applyingRange;

    private void ApplyRanges() {
        foreach (var (node, chart) in _rangeBindings) {
            object? value = State[node.RangeBind];
            Charts.ChartSelectedRange? range = null;
            if (value is not null) {
                range = value as Charts.ChartSelectedRange;
                if (range is null) {
                    object? minimum = NativeTuiControls.Member(value, "MinimumX"), maximum = NativeTuiControls.Member(value, "MaximumX");
                    if (minimum is null || maximum is null) {
                        throw new ArgumentException($"Chart '{node.Id}': RangeBind requires MinimumX and MaximumX, or null.");
                    }
                    double minimumX = Number(minimum), maximumX = Number(maximum);
                    if (chart.SelectedRange is { } selected && selected.MinimumX == minimumX && selected.MaximumX == maximumX) {
                        continue;
                    }
                    range = new(minimumX, maximumX);
                }
            }
            if (chart.SelectedRange == range) {
                continue;
            }
            _applyingRange = true;
            try {
                chart.SelectRange(range);
            }
            finally {
                _applyingRange = false;
            }
        }
    }
}
