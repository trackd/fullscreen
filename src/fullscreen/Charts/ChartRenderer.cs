namespace fullscreen.Charts;
/// <summary>Automatic Sixel/native selection, or an explicit chart renderer.</summary>
/// <remarks>DRCS chart modes are disabled; their implementation remains available for reuse.</remarks>
public enum ChartRenderer {
    /// <summary>Selects graphics presentation from detected terminal capabilities.</summary>
    Auto,
    /// <summary>Uses Sixel pixel graphics when supported.</summary>
    Sixel,
    // Drcs = 2,
    // DrcsUniversal = 3,
    /// <summary>Uses terminal-cell chart rendering without an image protocol.</summary>
    Native = 4,
    /// <summary>Experimental Contour ReGIS vector canvas (explicit opt-in).</summary>
    Regis = 5
}
