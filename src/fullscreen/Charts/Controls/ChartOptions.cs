using System.Globalization;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>Selects how numeric axis values are transformed and labeled.</summary>
public enum ChartScale {
    /// <summary>Uses untransformed numeric coordinates.</summary>
    Linear,
    /// <summary>Transforms positive coordinates using a base-10 logarithm.</summary>
    Logarithmic,
    /// <summary>Interprets coordinates as UTC seconds since year 1 and formats time labels.</summary>
    Time,
    /// <summary>Labels coordinates using the corresponding category names.</summary>
    Category
}

/// <summary>Selects the stroke pattern used to draw a chart series.</summary>
public enum ChartLineStyle {
    /// <summary>Draws series lines with a solid stroke.</summary>
    Solid,
    /// <summary>Draws series lines with a dashed stroke.</summary>
    Dashed,
    /// <summary>Draws series lines with a dotted stroke.</summary>
    Dotted
}

/// <summary>Selects the symbol drawn at a chart data point.</summary>
public enum ChartMarker {
    /// <summary>Draws circle point markers.</summary>
    Circle,
    /// <summary>Draws diamond point markers.</summary>
    Diamond,
    /// <summary>Draws no point markers.</summary>
    None
}

/// <summary>Selects the calculation applied to each group of chart input objects.</summary>
public enum ChartAggregate {
    /// <summary>Keeps individual object values without grouping calculations.</summary>
    None,
    /// <summary>Counts the original objects in each group.</summary>
    Count,
    /// <summary>Sums the numeric values in each group.</summary>
    Sum,
    /// <summary>Calculates the mean numeric value of each group.</summary>
    Average,
    /// <summary>Uses the smallest numeric value in each group.</summary>
    Minimum,
    /// <summary>Uses the largest numeric value in each group.</summary>
    Maximum
}

/// <summary>Axis presentation. Dates use UTC seconds since year 1, matching the object adapter.</summary>
public sealed record ChartAxis {
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; init; } = "";
    /// <summary>Gets or sets the suffix appended to formatted axis labels.</summary>
    public string Unit { get; init; } = "";
    /// <summary>Gets or sets the numeric or date format string used for axis labels.</summary>
    public string Format { get; init; } = "G4";
    /// <summary>Gets or sets the axis transformation and label interpretation.</summary>
    public ChartScale Scale { get; init; }
    /// <summary>Gets or sets the requested number of axis ticks; supported values are 2 through
    /// 20.</summary>
    public int TickCount { get; init; } = 4;

    /// <summary>Formats an axis value using its scale, format string, and unit.</summary>
    /// <param name="value">The axis coordinate to format; time coordinates use UTC seconds since year
    /// 1.</param>
    /// <returns>The formatted axis label.</returns>
    public string FormatValue(double value) {
        string text;
        if (Scale == ChartScale.Time) {
            long ticks = checked((long)(value * TimeSpan.TicksPerSecond));
            var timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
            string timeFormat = Format;
            if (timeFormat == "G4") {
                timeFormat = "HH:mm:ss";
            }

            text = timestamp.ToString(timeFormat, CultureInfo.InvariantCulture);
        }
        else {
            text = value.ToString(Format, CultureInfo.InvariantCulture);
        }

        return Unit.Length == 0 ? text : text + " " + Unit;
    }

    internal double Transform(double value) {
        if (Scale != ChartScale.Logarithmic) {
            return value;
        }

        if (value > 0) {
            return Math.Log10(value);
        }

        return double.NaN;
    }

    internal double Inverse(double value) =>
        Scale == ChartScale.Logarithmic ? Math.Pow(10, value) : value;
}

/// <summary>Describes a selected chart point and its associated source object.</summary>
/// <param name="SeriesIndex">The zero-based selected series index.</param>
/// <param name="SeriesName">The display name of the selected series.</param>
/// <param name="PointIndex">The zero-based point index within its series.</param>
/// <param name="X">The selected point's numeric X coordinate.</param>
/// <param name="Y">The numeric Y coordinate.</param>
/// <param name="Label">The human-readable label.</param>
/// <param name="Source">The original object associated with the point, or null when no source is attached.</param>
public sealed record ChartSelection(
    int SeriesIndex,
    string SeriesName,
    int PointIndex,
    double X,
    double Y,
    string Label,
    object? Source
);
/// <summary>Stores the inclusive X-coordinate limits of a chart selection.</summary>
/// <param name="MinimumX">The inclusive lower X-coordinate limit.</param>
/// <param name="MaximumX">The inclusive upper X-coordinate limit.</param>
public sealed record ChartSelectedRange(double MinimumX, double MaximumX);
/// <summary>Associates an aggregation key with the original objects represented by that group.</summary>
/// <param name="Key">The value that identifies this aggregation group.</param>
/// <param name="Items">The original input objects represented by this group, without mutation.</param>
public sealed record ChartGroup(object? Key, IReadOnlyList<object> Items);
/// <summary>A labeled horizontal or vertical reference line.</summary>
/// <param name="Value">The axis coordinate at which to draw the reference line.</param>
/// <param name="Label">The human-readable label. Defaults to &quot;&quot;.</param>
/// <param name="Vertical">True for a vertical line at an X coordinate; false for a horizontal line at a Y coordinate.</param>
/// <param name="Color">The display color, or null to use the palette default. Defaults to null.</param>
public sealed record ChartThreshold(double Value, string Label = "", bool Vertical = false, Color? Color = null);
/// <summary>A label anchored to a numeric data coordinate.</summary>
/// <param name="X">The numeric X coordinate anchoring the annotation.</param>
/// <param name="Y">The numeric Y coordinate.</param>
/// <param name="Text">The source text to parse or display.</param>
/// <param name="Color">The display color, or null to use the palette default. Defaults to null.</param>
public sealed record ChartAnnotation(double X, double Y, string Text, Color? Color = null);
/// <summary>Selects a series role in a column/line combination.</summary>
public enum ChartSeriesRole {
    /// <summary>Uses columns for the first series and lines for subsequent series.</summary>
    Auto,
    /// <summary>Draws grouped columns.</summary>
    Column,
    /// <summary>Draws a line.</summary>
    Line
}

/// <summary>Stores display options for an individual chart series.</summary>
/// <param name="Name">The display name. Defaults to &quot;&quot;.</param>
/// <param name="Color">The display color, or null to use the palette default. Defaults to null.</param>
/// <param name="Visible">Whether the series participates in rendering. Defaults to true.</param>
/// <param name="LineStyle">The series stroke pattern. Defaults to ChartLineStyle.Solid.</param>
/// <param name="Marker">The point marker style. Defaults to ChartMarker.Circle.</param>
public sealed record ChartSeriesStyle(
    string Name = "",
    Color? Color = null,
    bool Visible = true,
    ChartLineStyle LineStyle = ChartLineStyle.Solid,
    ChartMarker Marker = ChartMarker.Circle
) {
    /// <summary>Gets or sets the series role in a ColumnLine chart; ignored by other graph kinds.</summary>
    public ChartSeriesRole Role { get; init; }
}
/// <summary>Serializable chart presentation; values and executable formatters stay with their data source.</summary>
public sealed record ChartSettings {
    /// <summary>Gets or sets the visual representation used to draw the data.</summary>
    public GraphKind GraphKind { get; init; }
    /// <summary>Gets or sets the positive fixed Kagi reversal amount in price units; default 1.</summary>
    public double ReversalAmount { get; init; } = 1;
    /// <summary>Gets or sets whether flamegraph roots appear at the top.</summary>
    public bool FlameReverse { get; init; }
    /// <summary>Gets or sets terminal rows per flamegraph depth, from 1 through 8; default 2.</summary>
    public int FlameFrameRows { get; init; } = 2;
    /// <summary>Gets or sets the representative frame ID used as the flamegraph focus; empty shows all roots.</summary>
    public string FlameFocusId { get; init; } = "";
    /// <summary>Gets or sets the first visible Gantt task lane.</summary>
    public int TaskOffset { get; init; }
    /// <summary>Gets or sets the displayed heading.</summary>
    public string Title { get; init; } = "";
    /// <summary>Gets or sets the lower Y-axis limit, or null for automatic scaling.</summary>
    public double? Minimum { get; init; }
    /// <summary>Gets or sets the upper chart scale limit.</summary>
    public double? Maximum { get; init; }
    /// <summary>Gets or sets the lower X-coordinate limit, or null for automatic bounds.</summary>
    public double? MinimumX { get; init; }
    /// <summary>Gets or sets the upper X-coordinate limit, or null for automatic bounds.</summary>
    public double? MaximumX { get; init; }
    /// <summary>Gets or sets whether axis labels and tick marks are displayed.</summary>
    public bool ShowAxes { get; init; }
    /// <summary>Gets or sets whether the series legend is displayed.</summary>
    public bool ShowLegend { get; init; }
    /// <summary>Gets or sets the horizontal axis presentation settings.</summary>
    public ChartAxis XAxis { get; init; } = new();
    /// <summary>Gets or sets the vertical axis presentation settings.</summary>
    public ChartAxis YAxis { get; init; } = new();
    /// <summary>Gets or sets the horizontal and vertical reference lines.</summary>
    public ChartThreshold[] Thresholds { get; init; } = [];
    /// <summary>Gets or sets the labels anchored to data coordinates.</summary>
    public ChartAnnotation[] Annotations { get; init; } = [];
    /// <summary>Gets or sets the chart&apos;s additional numeric series.</summary>
    public ChartSeriesStyle[] Series { get; init; } = [];
    /// <summary>Gets or sets the selected X-coordinate interval, or null when no range is
    /// selected.</summary>
    public ChartSelectedRange? SelectedRange { get; init; }
}
