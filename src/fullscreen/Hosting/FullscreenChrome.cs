using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Hosting;
/// <summary>One shared close target over the existing first row; consumes no content rows.</summary>
internal sealed class FullscreenChrome : Visual {
    private readonly IFullscreenApp _app;
    private readonly Visual _content;
    private readonly Button _close;
    private readonly VStack _diagnostics;
    private readonly TextBlock[] _diagnosticLines;
    private Visual? _layer;
    internal Rectangle CloseBounds => _close.Bounds;
    internal Rectangle DiagnosticsBounds => _diagnostics.IsVisible ? _diagnostics.Bounds : default;

    internal void UpdateDiagnostics(Charts.ChartRenderer requested, ITerminalGraphicsPresenter? graphics) {
        if (App is not { } app) {
            return;
        }
        bool visible = Charts.TerminalOverlayBounds.DebugVisible(app);
        _diagnostics.IsVisible = visible;
        if (!visible) {
            return;
        }
        var capabilities = graphics?.Capabilities ?? app.Terminal.Graphics.Capabilities;
        var pixels = graphics is Charts.SixelChartPresenter sixel ? sixel.PixelMetrics : capabilities.PixelMetrics;
        string geometry = pixels is { } size
            ? $"cell {size.CellPixelWidth}x{size.CellPixelHeight}px; surface {size.WindowPixelWidth}x{size.WindowPixelHeight}px"
            : "pixel geometry unavailable";
        string actual = graphics switch {
            null => "Native",
            Charts.SixelChartPresenter => "Sixel",
            Charts.RegisChartPresenterPrototype => "Regis",
            _ => graphics.GetType().Name
        };
        _diagnosticLines[0].Text = $"Terminal: {app.Terminal.Size.Columns}x{app.Terminal.Size.Rows} cells; TERM={Safe(Environment.GetEnvironmentVariable("TERM"))}; program={Safe(Environment.GetEnvironmentVariable("TERM_PROGRAM"))}";
        _diagnosticLines[1].Text = $"Graphics: {requested} -> {actual}; {capabilities.SupportState}; source={Safe(capabilities.DetectionSource)}";
        _diagnosticLines[2].Text = $"Geometry: {geometry}" + (graphics is Charts.SixelChartPresenter { UsesFallbackMetrics: true } ? " (8x16 fallback)" : "");
    }

    private static string Safe(string? value) =>
        string.IsNullOrEmpty(value) ? "unknown" : string.Create(Math.Min(80, value.Length), value, static (output, source) => {
            for (int index = 0; index < output.Length; index++) {
                output[index] = char.IsControl(source[index]) ? '?' : source[index];
            }
        });

    internal FullscreenChrome(IFullscreenApp app, Themes.ThemePalette palette) {
        _app = app;
        _content = app.Root;
        _diagnosticLines = [new(""), new(""), new("")];
        foreach (var line in _diagnosticLines) {
            line.Wrap = false;
            line.IsHitTestVisible = false;
            line.SetStyle(new TextBlockStyle { Foreground = palette.Foreground, Background = palette.Background, FillBackground = true });
        }
        _diagnostics = new VStack(_diagnosticLines) { IsVisible = false, IsHitTestVisible = false };
        _close = new Button("×") {
            MinHeight = 1,
            MaxHeight = 1,
            MinWidth = 3,
            MaxWidth = 3
        }.Click(app.RequestClose);
        _close.SetStyle(ButtonStyle.Default with {
            Padding = new(1, 0, 1, 0),
            ShowBorder = false,
            Normal = palette.AccentStyle,
            Hovered = palette.Hot,
            Pressed = palette.Selected,
            Focused = palette.Hot | TextStyle.Underline
        });
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        AttachChild(_content);
        AttachChild(_close);
        AttachChild(_diagnostics);
    }

    // Modal windows live in the app window layer (our parent) and scope hit-testing
    // to themselves; preview pointer presses there so the close target stays live.
    protected override void OnAttachedToApp(TerminalApp app) {
        base.OnAttachedToApp(app);
        _layer = Parent;
        _layer?.PointerPressedRouted += OnLayerPointerPressed;
    }

    protected override void OnDetachedFromApp(TerminalApp app) {
        _layer?.PointerPressedRouted -= OnLayerPointerPressed;
        _layer = null;
        base.OnDetachedFromApp(app);
    }

    private void OnLayerPointerPressed(object? sender, PointerEventArgs e) {
        if (e.Handled
            || e.RoutingPhase != RoutingPhase.Preview
            || e.RawEvent?.Button != XenoAtom.Terminal.TerminalMouseButton.Left
            || ReferenceEquals(e.OriginalSource, _close)
            || !_close.IsVisible
            || !_close.Bounds.Contains(e.UiX, e.UiY)) {
            return;
        }
        e.Handled = true;
        _app.RequestClose();
    }

    protected override int ChildrenCount => 3;

    protected override Visual GetChild(int index) =>
        index switch {
            0 => _content,
            1 => _close,
            2 => _diagnostics,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _content.Measure(new(constraints.MaxWidth, constraints.MaxHeight));
        _close.Measure(new(3, 1));
        _diagnostics.Measure(new(constraints.MaxWidth, 3));
        return SizeHints.Flex(new(0, 0), new(100, 40), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    protected override void ArrangeCore(in Rectangle finalRect) {
        _content.Arrange(finalRect);
        int width = Math.Min(3, finalRect.Width);
        _close.Arrange(new(finalRect.Right - width, finalRect.Y, width, Math.Min(1, finalRect.Height)));
        int height = Math.Min(3, finalRect.Height);
        _diagnostics.Arrange(new(finalRect.X, finalRect.Bottom - height, finalRect.Width, height));
    }
}
