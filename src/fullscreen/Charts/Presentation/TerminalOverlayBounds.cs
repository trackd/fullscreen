using System.Linq.Expressions;
using System.Reflection;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Charts;
/// <summary>Compatibility for XenoAtom 3.10, whose F12 overlay is not a visual or public clip region.</summary>
internal static class TerminalOverlayBounds {
    private static readonly Func<TerminalApp, Rectangle> ReadDebugBounds = CreateReader();
    private static readonly Func<TerminalApp, bool> ReadVisible = CreateVisibleReader();
    internal static Rectangle Debug(TerminalApp app) =>
        ReadDebugBounds(app);
    internal static bool DebugVisible(TerminalApp app) =>
        ReadVisible(app);
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
