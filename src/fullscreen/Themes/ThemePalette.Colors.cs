using XenoAtom.Terminal.UI;

namespace fullscreen.Themes;

internal enum ThemeRole {
    Keyword, Type, Function, Property, Parameter, String, Number, Comment, Punctuation,
    ChartBlue, ChartOrange, ChartGreen, ChartPurple, ChartCyan, ChartRed, ChartYellow, ChartPink,
    ChartGrid, ChartAxis, ChartLabel, ChartSequentialLow, ChartSequentialHigh,
    ChartNegative, ChartNeutral, ChartPositive,
    Operator, Variable, Command, DefaultToken
}

public sealed partial class ThemePalette {
    internal static readonly string[] RoleNames = Enum.GetNames<ThemeRole>();
    internal const int SeriesColorCount = 8;
    internal const int GaugeColorCount = 16;
    private readonly Color[] _gaugeColors = new Color[GaugeColorCount];
    private readonly Color[] _colors;
    /// <summary>Gets the syntax keyword color.</summary>
    public Color Keyword => _colors[(int)ThemeRole.Keyword];
    /// <summary>Gets the syntax type-name color.</summary>
    public Color Type => _colors[(int)ThemeRole.Type];
    /// <summary>Gets the syntax function-name color.</summary>
    public Color Function => _colors[(int)ThemeRole.Function];
    /// <summary>Gets the syntax property-name color.</summary>
    public Color Property => _colors[(int)ThemeRole.Property];
    /// <summary>Gets the syntax parameter-name color.</summary>
    public Color Parameter => _colors[(int)ThemeRole.Parameter];
    /// <summary>Gets the syntax string-literal color.</summary>
    public Color StringLiteral => _colors[(int)ThemeRole.String];
    /// <summary>Gets the syntax numeric-literal color.</summary>
    public Color Number => _colors[(int)ThemeRole.Number];
    /// <summary>Gets the syntax comment color.</summary>
    public Color Comment => _colors[(int)ThemeRole.Comment];
    /// <summary>Gets the syntax punctuation color.</summary>
    public Color Punctuation => _colors[(int)ThemeRole.Punctuation];
    /// <summary>Gets the syntax operator color.</summary>
    public Color Operator => _colors[(int)ThemeRole.Operator];
    /// <summary>Gets the syntax variable color.</summary>
    public Color Variable => _colors[(int)ThemeRole.Variable];
    /// <summary>Gets the shell command color.</summary>
    public Color Command => _colors[(int)ThemeRole.Command];
    /// <summary>Gets the color for otherwise unclassified syntax.</summary>
    public Color DefaultToken => _colors[(int)ThemeRole.DefaultToken];
    /// <summary>Gets the blue categorical chart color.</summary>
    public Color ChartBlue => _colors[(int)ThemeRole.ChartBlue];
    /// <summary>Gets the orange categorical chart color.</summary>
    public Color ChartOrange => _colors[(int)ThemeRole.ChartOrange];
    /// <summary>Gets the green categorical chart color.</summary>
    public Color ChartGreen => _colors[(int)ThemeRole.ChartGreen];
    /// <summary>Gets the purple categorical chart color.</summary>
    public Color ChartPurple => _colors[(int)ThemeRole.ChartPurple];
    /// <summary>Gets the cyan categorical chart color.</summary>
    public Color ChartCyan => _colors[(int)ThemeRole.ChartCyan];
    /// <summary>Gets the red categorical chart color.</summary>
    public Color ChartRed => _colors[(int)ThemeRole.ChartRed];
    /// <summary>Gets the yellow categorical chart color.</summary>
    public Color ChartYellow => _colors[(int)ThemeRole.ChartYellow];
    /// <summary>Gets the pink categorical chart color.</summary>
    public Color ChartPink => _colors[(int)ThemeRole.ChartPink];
    /// <summary>Gets the chart grid-line color.</summary>
    public Color ChartGridColor => _colors[(int)ThemeRole.ChartGrid];
    /// <summary>Gets the chart axis color.</summary>
    public Color ChartAxis => _colors[(int)ThemeRole.ChartAxis];
    /// <summary>Gets the chart label color.</summary>
    public Color ChartLabel => _colors[(int)ThemeRole.ChartLabel];
    /// <summary>Gets the low end of the sequential chart ramp.</summary>
    public Color ChartSequentialLow => _colors[(int)ThemeRole.ChartSequentialLow];
    /// <summary>Gets the high end of the sequential chart ramp.</summary>
    public Color ChartSequentialHigh => _colors[(int)ThemeRole.ChartSequentialHigh];
    /// <summary>Gets the negative end of the diverging chart ramp.</summary>
    public Color ChartNegative => _colors[(int)ThemeRole.ChartNegative];
    /// <summary>Gets the midpoint of the diverging chart ramp.</summary>
    public Color ChartNeutral => _colors[(int)ThemeRole.ChartNeutral];
    /// <summary>Gets the positive end of the diverging chart ramp.</summary>
    public Color ChartPositive => _colors[(int)ThemeRole.ChartPositive];

    internal Color RoleColor(int index) => _colors[index];
    internal Color GaugeColor(int index) => _gaugeColors[index];
    internal Color SeriesColor(int index) => ((index % SeriesColorCount + SeriesColorCount) % SeriesColorCount) switch {
        0 => ChartBlue,
        1 => ChartOrange,
        2 => ChartPurple,
        3 => ChartGreen,
        4 => ChartCyan,
        5 => ChartRed,
        6 => ChartYellow,
        _ => ChartPink
    };
    internal Color SequentialColor(float fraction) => Color.Mix(ChartSequentialLow, ChartSequentialHigh, fraction);
    internal Color DivergingColor(float fraction) => fraction <= .5f
        ? Color.Mix(ChartNegative, ChartNeutral, fraction * 2)
        : Color.Mix(ChartNeutral, ChartPositive, (fraction - .5f) * 2);
}
