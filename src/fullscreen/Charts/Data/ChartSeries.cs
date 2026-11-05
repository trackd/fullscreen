using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Collections;

namespace fullscreen.Charts;
/// <summary>A numeric series, optionally with explicit X coordinates and point labels.</summary>
public sealed class ChartSeries {
    private Color? _color;
    private bool _visible = true;
    private ChartLineStyle _lineStyle;
    private ChartMarker _marker;
    private ChartSeriesRole _role;
    internal event Action? Changed;
    /// <summary>Gets the display name.</summary>
    public string Name { get; }
    /// <summary>Gets the initial Y values; NaN leaves a gap.</summary>
    public BindableList<double> Values { get; }
    /// <summary>Gets the explicit X coordinates corresponding to numeric values.</summary>
    public BindableList<double> XValues { get; }
    /// <summary>Gets the display labels associated with data points.</summary>
    public BindableList<string> Labels { get; }
    /// <summary>Gets the original source objects associated with data points.</summary>
    public BindableList<object?> Sources { get; }
    /// <summary>Gets the lower bounds corresponding to data points.</summary>
    public BindableList<double> LowerValues { get; }
    /// <summary>Gets the upper bounds corresponding to data points.</summary>
    public BindableList<double> UpperValues { get; }

    /// <summary>Gets or sets the display color, or null to use the palette default.</summary>
    public Color? Color {
        get => _color;
        set {
            if (_color != value) {
                _color = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets whether the series participates in rendering.</summary>
    public bool Visible {
        get => _visible;
        set {
            if (_visible != value) {
                _visible = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the series stroke pattern.</summary>
    public ChartLineStyle LineStyle {
        get => _lineStyle;
        set {
            if (_lineStyle != value) {
                _lineStyle = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the point marker style.</summary>
    public ChartMarker Marker {
        get => _marker;
        set {
            if (_marker != value) {
                _marker = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the role in a ColumnLine chart; ignored by other graph kinds.</summary>
    public ChartSeriesRole Role {
        get => _role;
        set {
            if (!Enum.IsDefined(value)) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_role != value) {
                _role = value;
                Changed?.Invoke();
            }
        }
    }

    internal ChartSeriesVersion Version => new(
        Values.Version,
        XValues.Version,
        Labels.Version,
        Sources.Version,
        LowerValues.Version,
        UpperValues.Version,
        Color,
        Visible,
        LineStyle,
        Marker,
        Role
    );

    /// <summary>Initializes a new ChartSeries instance.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="values">The initial Y values; NaN leaves a gap.</param>
    public ChartSeries(string name, IEnumerable<double> values) {
        Name = name;
        Values = new(this, nameof(Values));
        XValues = new(this, nameof(XValues));
        Labels = new(this, nameof(Labels));
        Sources = new(this, nameof(Sources));
        LowerValues = new(this, nameof(LowerValues));
        UpperValues = new(this, nameof(UpperValues));
        foreach (double value in values) {
            Values.Add(value);
        }
    }
}
