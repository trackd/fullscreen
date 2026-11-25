using System.Text;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Hosting;

/// <summary>Continues the built-in F12 frame below its existing statistics.</summary>
internal sealed partial class TerminalDiagnosticsOverlay : Visual {
    [Bindable]
    public partial string TerminalDetails { get; set; }
    [Bindable]
    public partial string GraphicsDetails { get; set; }
    [Bindable]
    public partial string GeometryDetails { get; set; }
    [Bindable]
    public partial Rectangle OverlayBounds { get; set; }
    internal string[] Lines => [TerminalDetails, GraphicsDetails, GeometryDetails];

    internal void Refresh() {
        OverlayBounds = CalculateBounds();
    }

    internal Rectangle PaintedBounds => IsVisible && App is { } app && Charts.TerminalOverlayBounds.DebugVisible(app) ? OverlayBounds : default;

    private Rectangle CalculateBounds() {
        if (!IsVisible || App is not { } app || !Charts.TerminalOverlayBounds.DebugVisible(app)) {
            return default;
        }
        var native = Charts.TerminalOverlayBounds.Debug(app);
        int top = native.Bottom - 1;
        int height = Math.Min(5, app.Terminal.Size.Rows - top);
        return native.Width >= 3 && native.Height >= 3 && height >= 3
            ? new(native.X, top, native.Width, height) : default;
    }

    internal TerminalDiagnosticsOverlay() {
        TerminalDetails = "";
        GraphicsDetails = "";
        GeometryDetails = "";
        IsVisible = false;
        IsHitTestVisible = false;
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    protected override SizeHints MeasureCore(in LayoutConstraints constraints) =>
        SizeHints.Flex(new(0, 0), new(0, 0), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);

    protected override void RenderOverride(CellBuffer buffer) {
        var bounds = PaintedBounds;
        if (bounds.Width < 3 || bounds.Height < 3) {
            return;
        }
        var theme = App!.Root.GetTheme();
        var background = theme.ForegroundTextStyle() | TextStyle.Dim;
        if (theme.Background is { } color) {
            background = background.WithBackground(color);
        }
        var border = theme.BorderStyle(focused: true) | TextStyle.Bold;
        for (int y = bounds.Y; y < bounds.Bottom; y++) {
            for (int x = bounds.X; x < bounds.Right; x++) {
                bool edgeX = x == bounds.X || x == bounds.Right - 1;
                bool edgeY = y == bounds.Y || y == bounds.Bottom - 1;
                char glyph = edgeX && edgeY ? '+' : edgeY ? '-' : edgeX ? '|' : ' ';
                buffer.SetCell(x, y, new Rune(glyph), edgeX || edgeY ? border : background);
            }
        }
        buffer.PushClip(new(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2));
        try {
            for (int line = 0; line < Math.Min(3, bounds.Height - 2); line++) {
                string text = line switch {
                    0 => TerminalDetails,
                    1 => GraphicsDetails,
                    _ => GeometryDetails
                };
                buffer.WriteText(bounds.X + 1, bounds.Y + 1 + line, text, background);
            }
        }
        finally {
            buffer.PopClip();
        }
    }
}
