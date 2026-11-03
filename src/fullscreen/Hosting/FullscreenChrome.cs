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
    private Visual? _layer;
    internal Rectangle CloseBounds => _close.Bounds;

    internal FullscreenChrome(IFullscreenApp app, Themes.ThemePalette palette) {
        _app = app;
        _content = app.Root;
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
    }

    // Modal windows live in the app window layer (our parent) and scope hit-testing
    // to themselves; preview pointer presses there so the close target stays live.
    protected override void OnAttachedToApp(TerminalApp app) {
        base.OnAttachedToApp(app);
        _layer = Parent;
        if (_layer is not null) {
            _layer.PointerPressedRouted += OnLayerPointerPressed;
        }
    }

    protected override void OnDetachedFromApp(TerminalApp app) {
        if (_layer is not null) {
            _layer.PointerPressedRouted -= OnLayerPointerPressed;
            _layer = null;
        }
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

    protected override int ChildrenCount => 2;

    protected override Visual GetChild(int index) =>
        index == 0 ? _content : _close;
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _content.Measure(new(constraints.MaxWidth, constraints.MaxHeight));
        _close.Measure(new(3, 1));
        return SizeHints.Flex(new(0, 0), new(100, 40), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    protected override void ArrangeCore(in Rectangle finalRect) {
        _content.Arrange(finalRect);
        int width = Math.Min(3, finalRect.Width);
        _close.Arrange(new(finalRect.Right - width, finalRect.Y, width, Math.Min(1, finalRect.Height)));
    }
}
