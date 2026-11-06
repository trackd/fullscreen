using System.Linq.Expressions;
using System.Reflection;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Charts;
/// <summary>Painted overlay bounds for XenoAtom 3.10's viewport-sized hosts and nonvisual F12 overlay.</summary>
internal static class TerminalOverlayBounds {
    private static readonly Func<TerminalApp, Rectangle> ReadDebugBounds = CreateReader();
    private static readonly Func<TerminalApp, bool> ReadVisible = CreateVisibleReader();
    internal static Rectangle Debug(TerminalApp app) =>
        ReadDebugBounds(app);
    internal static bool DebugVisible(TerminalApp app) =>
        ReadVisible(app);
    internal static Rectangle Window(Visual visual) {
        if (visual is Popup { Content: { } popup }) {
            return popup.Bounds;
        }
        // XenoAtom 3.10's internal TooltipWindow spans the viewport, but paints
        // only its content plus the TooltipStyle padding and one-cell border.
        if (visual is ContentVisual { Content: { } content }
            && visual.GetType().FullName == "XenoAtom.Terminal.UI.Controls.TooltipWindow") {
            var padding = visual.GetStyle<TooltipStyle>().Padding;
            var bounds = content.Bounds;
            return ChartClipping.Intersect(visual.Bounds, new(
                bounds.X - padding.Left - 1, bounds.Y - padding.Top - 1,
                bounds.Width + padding.Horizontal + 2, bounds.Height + padding.Vertical + 2));
        }
        return visual.Bounds;
    }
    private static Func<TerminalApp, bool> CreateVisibleReader() {
        var visible = typeof(TerminalApp).GetField("_debugOverlayVisible", BindingFlags.Instance | BindingFlags.NonPublic);
        if (visible is null) {
            return _ => false;
        }

        var app = Expression.Parameter(typeof(TerminalApp), "app");
        return Expression.Lambda<Func<TerminalApp, bool>>(Expression.Field(app, visible), app).Compile();
    }

    private static Func<TerminalApp, Rectangle> CreateReader() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var visible = typeof(TerminalApp).GetField("_debugOverlayVisible", flags);
        var metrics = typeof(TerminalApp).GetField("_debugOverlayMetrics", flags);
        var bounds = metrics?.FieldType.GetProperty("OverlayRect", flags);
        if (visible is null || metrics is null || bounds is null) {
            return _ => default;
        }

        var app = Expression.Parameter(typeof(TerminalApp), "app");
        var metricsValue = Expression.Field(app, metrics);
        var region = Expression.Property(metricsValue, bounds);
        var active = Expression.AndAlso(
            Expression.Field(app, visible),
            Expression.NotEqual(metricsValue, Expression.Constant(null, metrics.FieldType))
        );
        return Expression.Lambda<Func<TerminalApp, Rectangle>>(Expression.Condition(active, region, Expression.Default(typeof(Rectangle))), app).Compile();
    }
}
