namespace fullscreen.Charts;
/// <summary>Shape used by chart drawing and interaction.</summary>
public enum GraphKind {
    /// <summary>Connects adjacent numeric points with line segments, leaving gaps for missing values.</summary>
    Line,
    /// <summary>Fills the area between a series and its baseline.</summary>
    Area,
    /// <summary>Draws vertical bars for individual values.</summary>
    Bars,
    /// <summary>Connects values with horizontal and vertical step segments.</summary>
    Step,
    /// <summary>Draws independent markers at explicit X and Y coordinates.</summary>
    Scatter,
    /// <summary>Draws a stem from the baseline to each data point.</summary>
    Stem,
    /// <summary>Groups values into bins and displays their frequencies.</summary>
    Histogram,
    /// <summary>Summarizes a distribution using quartiles and whiskers.</summary>
    BoxPlot,
    /// <summary>Draws horizontal bars for individual values.</summary>
    HorizontalBars,
    /// <summary>Stacks series values within each bar category.</summary>
    StackedBars,
    /// <summary>Maps numeric values to colored cells arranged by series and position.</summary>
    Heatmap,
    /// <summary>Draws values with associated lower and upper uncertainty bounds.</summary>
    ErrorBars,
    /// <summary>Combines grouped columns and lines on shared axes; by default the first series is columns.</summary>
    ColumnLine,
    /// <summary>Shows nonnegative row values as proportional slices of one pie.</summary>
    Pie,
    /// <summary>Shows row values on concentric semicircular load gauges, defaulting to 0 through 100.</summary>
    Gauge,
    /// <summary>Shows row values on concentric circular bars, defaulting to 0 through 100.</summary>
    RadialBars,
    /// <summary>Compares series on category spokes using one shared numeric radial scale.</summary>
    Radar,
    /// <summary>Draws ordered signed increments as floating bars with cumulative connectors, starting at zero.</summary>
    Waterfall,
    /// <summary>Stacks nonnegative category compositions to 100 percent with widths proportional to category totals.</summary>
    Marimekko,
    /// <summary>Draws ordered closing prices using fixed-amount reversals and shoulder/waist thickness changes.</summary>
    Kagi,
    /// <summary>Shows hierarchical profiling frames with inclusive widths computed from exclusive self weights.</summary>
    Flamegraph,
    /// <summary>Shows absolute task start/end intervals, optional progress fractions and zero-duration milestones.</summary>
    Gantt,
    /// <summary>Draws directed process/decision topology with ranked layout and explicit cycle routing.</summary>
    FlowChart,
    /// <summary>Draws arbitrary directed or undirected node/edge topology using a deterministic bounded spring layout.</summary>
    NetworkDiagram,
    /// <summary>Stacks aligned nonnegative series as filled areas; missing components leave whole-stack gaps.</summary>
    StackedArea,
    /// <summary>Stacks aligned nonnegative series as percentages totaling 100; zero totals and missing components leave gaps.</summary>
    PercentStackedArea
}
