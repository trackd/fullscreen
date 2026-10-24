using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Charts;
/// <summary>Reusable XenoAtom visual and native-definition control for scalar live metrics.</summary>
public sealed partial class HistoryChart : Themes.ThemedSurface {
    private readonly BuiltInTrend _trend = new();
    /// <summary>Initializes a new HistoryChart instance.</summary>
    public HistoryChart() {
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        AttachChild(_trend);
    }

    [Bindable]
    private partial int Revision { get; set; }
    /// <summary>Gets the bounded timestamped sample buffer.</summary>
    public TimeSeries History { get; } = new();
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; set; } = "History";
    /// <summary>Gets or sets the upper chart scale limit.</summary>
    public double Maximum { get; set; } = 100;
    /// <summary>Gets or sets the visual representation used to draw the data.</summary>
    public GraphKind GraphKind {
        get => _trend.GraphKind;
        set => _trend.GraphKind = value;
    }
    private bool Distribution => _trend.UseSixel && GraphKind is GraphKind.Histogram or GraphKind.BoxPlot;

    /// <summary>Appends a timestamped sample and refreshes the displayed history.</summary>
    /// <param name="value">The nonnegative sample value, or null for a gap.</param>
    /// <param name="timestamp">The sample timestamp.</param>
    public void Add(double? value, DateTimeOffset timestamp) {
        History.Add(value, timestamp);
        Revision++;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _trend : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = Revision;
        _trend.PixelInk = Palette.Accent;
        _trend.PixelBackground = Palette.Background;
        _trend.Update(
            History,
            History.Latest,
            Maximum > 0 ? Maximum : History.Scale
        );
        _trend.Measure(new Size(constraints.MaxWidth, Math.Max(0, constraints.MaxHeight - (Distribution ? 2 : 1))));
        return SizeHints.Flex(new(12, 4), new(48, 10), new(int.MaxValue, 1000), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        _trend.Arrange(new(
            finalRect.X,
            finalRect.Y + 1,
            finalRect.Width,
            Math.Max(0, finalRect.Height - (Distribution ? 2 : 1))
        ));
    /// <inheritdoc/>
    protected override void RenderOverride(CellBuffer buffer) {
        _ = Revision;
        if (Bounds.Width < 1 || Bounds.Height < 2) {
            return;
        }

        Span<char> label = stackalloc char[160];
        if (label.TryWrite(
            CultureInfo.InvariantCulture,
            $"{Title} · {(History.Count == 0 ? null : History.ValueAt(History.Count - 1)):F1} · 120s",
            out int length
        )) {
            buffer.WriteText(Bounds.X, Bounds.Y, label[..Math.Min(length, Bounds.Width)], Palette.AccentStyle);
        }

        if (Distribution && label.TryWrite(
            CultureInfo.InvariantCulture,
            $"values 0–{(Maximum > 0 ? Maximum : History.Scale):G4} · {(GraphKind == GraphKind.Histogram ? "frequency, auto-scaled" : "quartiles · median · 1.5 IQR whiskers")}",
            out length
        )) {
            buffer.WriteText(Bounds.X, Bounds.Bottom - 1, label[..Math.Min(length, Bounds.Width)], Palette.Muted);
        }
    }
}
