using System.Globalization;
using fullscreen.Files;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;

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
    internal bool FollowLiveHover { get; init; }
    private int _hoverX, _hoverY;
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
        if (_hoverPoint is not null) {
            if (!enabled || _hoverBounds != PlotBounds) {
                ClearInspection();
            }
            else if (_hoverVersion != InspectionVersion) {
                if (FollowLiveHover && _inspection.IsHovered) {
                    InspectPointer(_hoverX, _hoverY, refresh: true);
                }
                else {
                    ClearInspection();
                }
            }
        }
        _inspection.Measure(new Size(constraints.MaxWidth, constraints.MaxHeight));
    }

    private void ClearInspection() {
        _hoverPoint = null;
        _inspection.TooltipContent = null;
    }

    private void InspectPointer(int x, int y, bool refresh = false) {
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
        _hoverX = x;
        _hoverY = y;
        _hoverPoint = point;
        _hoverVersion = InspectionVersion;
        _hoverBounds = PlotBounds;
        if (refresh && _inspection.TooltipContent is Paragraph tooltip) {
            tooltip.Text = InspectionText(point);
            PositionInspection(tooltip, x, y);
            return;
        }
        // Reattach to reschedule the native delay when the pointer moves to another target.
        DetachChild(_inspection);
        AttachChild(_inspection);
        // Detaching the inspection anchor clears its previous tooltip.
        _hoverPoint = point;
        var content = new Paragraph(InspectionText(point)) { Wrap = true };
        PositionInspection(content, x, y);
    }

    private void PositionInspection(Paragraph content, int x, int y) {
        var style = _inspection.GetStyle<TooltipStyle>();
        int frameWidth = style.Padding.Horizontal + 2, frameHeight = style.Padding.Vertical + 2;
        var bounds = PlotBounds;
        if (bounds.Width <= frameWidth || bounds.Height <= frameHeight) {
            ClearInspection();
            return;
        }
        int width = Math.Min(72, Math.Min(bounds.Width, style.MaxWidth ?? bounds.Width) - frameWidth);
        content.MaxWidth = Math.Max(1, width);
        content.MaxHeight = bounds.Height - frameHeight;
        content.Measure(new LayoutConstraints(0, content.MaxWidth, 0, content.MaxHeight));
        int popupWidth = content.DesiredSize.Width + frameWidth;
        int popupHeight = content.DesiredSize.Height + frameHeight;
        // Anchor to the measured popup position; native viewport clamping alone
        // cannot keep a tooltip inside a chart embedded in a larger terminal.
        _inspection.Content!.Margin = new Thickness(
            Math.Clamp(x + 1, bounds.X, bounds.Right - popupWidth) - bounds.X,
            Math.Clamp(y + 1, bounds.Y, bounds.Bottom - popupHeight) - bounds.Y, 0, 0);
        _inspection.OffsetX = 0;
        _inspection.OffsetY = 0;
        _inspection.TooltipContent = content;
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
            if (seriesIndex < 0) {
                seriesIndex = point.SeriesIndex;
            }
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
