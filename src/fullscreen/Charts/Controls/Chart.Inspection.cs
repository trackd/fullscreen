using System.Globalization;
using fullscreen.Files;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Charts;

public partial class Chart {
    private bool _showTooltips = true;
    /// <summary>Gets or sets whether hovering shows point details after 200 milliseconds. Defaults to true.</summary>
    public bool ShowTooltips {
        get => _showTooltips;
        set {
            _showTooltips = value;
            _inspection.IsVisible = value && Interactive;
            if (!value) {
                ClearInspection();
            }
        }
    }

    private sealed class InspectionAnchor(Chart owner) : Visual {
        protected override void OnDetachedFromApp(TerminalApp app) {
            // Close the popup while its parent TooltipHost is still attached.
            owner.ClearInspection();
            base.OnDetachedFromApp(app);
        }
    }

    private readonly TooltipHost _inspection = new() {
        ShowDelayMilliseconds = 200,
        HorizontalAlignment = Align.Stretch,
        VerticalAlignment = Align.Stretch
    };
    private ChartSelection? _hoverPoint;
    private readonly record struct InspectionKey(long Data, GraphKind Kind, double? Minimum, double? Maximum, ChartViewKey View);
    private InspectionKey InspectionVersion => new(DataVersion, GraphKind, Minimum, Maximum, ViewVersion);
    private InspectionKey _hoverVersion;
    private Rectangle _hoverBounds;

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _inspection : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        _inspection.Arrange(PlotBounds);
        if (_hoverBounds != PlotBounds) {
            ClearInspection();
        }
    }

    private void MeasureInspection(in LayoutConstraints constraints) {
        bool enabled = Interactive && ShowTooltips;
        for (Visual? visual = this; visual is not null; visual = visual.Parent) {
            enabled &= visual.IsEnabled && visual.IsVisible;
        }
        _inspection.IsVisible = enabled;
        if (_hoverPoint is not null && (_hoverVersion != InspectionVersion || !enabled)) {
            ClearInspection();
        }
        _inspection.Measure(new Size(constraints.MaxWidth, constraints.MaxHeight));
    }

    private void ClearInspection() {
        _hoverPoint = null;
        _inspection.TooltipContent = null;
    }

    private void InspectPointer(int x, int y) {
        if (!ShowTooltips && GraphKind != GraphKind.Flamegraph) {
            return;
        }
        var point = Interactive && !_dragStart.HasValue && PlotBounds.Contains(x, y) ? PickPoint(x, y) : null;
        if (GraphKind == GraphKind.Flamegraph) {
            FlameHover = point;
        }
        if (!ShowTooltips) {
            ClearInspection();
            return;
        }
        if (point is null) {
            ClearInspection();
            return;
        }

        if (point == _hoverPoint && _hoverVersion == InspectionVersion && _hoverBounds == PlotBounds) {
            return;
        }
        // Reattach to reschedule the native delay when the target changes without leaving the plot.
        DetachChild(_inspection);
        AttachChild(_inspection);
        _hoverPoint = point;
        _hoverVersion = InspectionVersion;
        _hoverBounds = PlotBounds;
        _inspection.TooltipContent = new Paragraph(InspectionText(point)) {
            Wrap = true,
            MaxWidth = Math.Max(1, Math.Min(72, Bounds.Width - 2))
        };
        _inspection.OffsetX = Math.Max(0, x - PlotBounds.X);
        _inspection.OffsetY = y - PlotBounds.Bottom + 1;
    }

    private string InspectionText(ChartSelection point) {
        string text;
        if (ChartTopology.Supports(GraphKind) || GraphKind is GraphKind.Flamegraph or GraphKind.Gantt) {
            text = point.Label + "\n" + point.SeriesName;
        }
        else if (GraphKind is GraphKind.Histogram or GraphKind.BoxPlot) {
            var data = GetRenderData(Math.Max(1, Bounds.Width));
            var range = RenderRange(data);
            int seriesIndex = Array.FindIndex(data, series => series.SourceIndex == point.SeriesIndex);
            var distribution = GetDistributions(data, range, PlotBounds.Width)[seriesIndex];
            if (GraphKind == GraphKind.Histogram) {
                double value = YAxis.Transform(point.Y);
                int bin = Math.Clamp((int)(ChartMath.Fraction(value, range.MinX, range.MaxX) * distribution.Bins.Length), 0, distribution.Bins.Length - 1);
                double low = range.MinX + (range.MaxX - range.MinX) * bin / distribution.Bins.Length;
                double high = range.MinX + (range.MaxX - range.MinX) * (bin + 1) / distribution.Bins.Length;
                text = FormattableString.Invariant($"{point.SeriesName}\nObservation: {point.Y:G6}\nBin containing observation: {YAxis.Inverse(low):G6} .. {YAxis.Inverse(high):G6}\nCount: {distribution.Bins[bin]}");
            }
            else {
                var sorted = distribution.Sorted;
                var summary = ChartStatistics.SummarizeSorted(sorted);
                text = FormattableString.Invariant($"{point.SeriesName}\nCount: {sorted.Length}\nMinimum: {YAxis.Inverse(sorted[0]):G6}\nMaximum: {YAxis.Inverse(sorted[^1]):G6}\nMedian: {YAxis.Inverse(summary.Median):G6}\nQ1: {YAxis.Inverse(summary.FirstQuartile):G6}\nQ3: {YAxis.Inverse(summary.ThirdQuartile):G6}");
            }
        }
        else {
            text = point.Label + "\n" + (point.SeriesName.Length == 0 ? "Value" : point.SeriesName) + ": " + point.Y.ToString("G6", CultureInfo.InvariantCulture);
            if (XAxis.Scale != ChartScale.Category) {
                text += "\nX: " + XAxis.FormatValue(point.X);
            }
        }
        return FileReader.Sanitize(text, true);
    }
}
