using System.Runtime.CompilerServices;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;
/// <summary>Creates chart graphics presenters for an existing XenoAtom terminal session.</summary>
public static class ChartPresenters {
    private static readonly ConditionalWeakTable<TerminalInstance, ITerminalGraphicsPresenter> Presenters = [];
    /// <summary>Probes VT graphics support before input starts, with XenoAtom detection as a fallback.</summary>
    /// <remarks>Call before running the TUI. A complete DA1 response takes precedence over XenoAtom detection.
    /// Pass the result to TerminalAppOptions.GraphicsPresenter and dispose it with the application.</remarks>
    /// <param name="terminal">The terminal instance receiving graphics output.</param>
    /// <param name="renderer">The chart rendering mode selected by the host. Defaults to ChartRenderer.Auto.</param>
    /// <param name="metrics">The terminal pixel and cell dimensions, or null to use detected metrics. Defaults to
    /// null.</param>
    /// <returns>The presenter, or null when terminal-cell rendering is used.</returns>
    /// <param name="cancellationToken">Cancels the startup query before the TUI begins processing input.</param>
    public static ITerminalGraphicsPresenter? Create(
        TerminalInstance terminal,
        ChartRenderer renderer = ChartRenderer.Auto,
        TerminalPixelMetrics? metrics = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(terminal);
        if (!Enum.IsDefined(renderer)) {
            throw new ArgumentOutOfRangeException(nameof(renderer));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var evidence = renderer == ChartRenderer.Native
            ? TerminalProbeResult.Unknown
            : TerminalFeatureProbe.Get(terminal, cancellationToken);
        var capabilities = evidence.Completed ? evidence.Capabilities : terminal.Graphics.Capabilities;
        metrics = FullscreenHost.ResolveChartMetrics(
            capabilities,
            metrics ?? evidence.Metrics ?? terminal.Graphics.Capabilities.PixelMetrics,
            terminal.Size);
        ITerminalGraphicsPresenter? presenter = renderer switch {
            ChartRenderer.Native => null,
            // ChartRenderer.Drcs => new DrcsChartPresenter(capabilities),
            // ChartRenderer.DrcsUniversal => new DrcsChartPresenter(capabilities, DrcsFontScope.Universal),
            ChartRenderer.Regis when evidence.Regis => new RegisChartPresenterPrototype(terminal, evidence),
            ChartRenderer.Auto or ChartRenderer.Sixel when SixelChartPresenter.SupportsSixel(capabilities)
                && metrics is { CellPixelWidth: > 0, CellPixelHeight: > 0 } value => new SixelChartPresenter(capabilities, value),
            _ => null
        };
        if (presenter is not null) {
            Register(terminal, presenter);
        }
        else {
            Presenters.Remove(terminal);
        }

        return presenter;
    }

    internal static void Register(TerminalInstance terminal, ITerminalGraphicsPresenter presenter) {
        Presenters.Remove(terminal);
        Presenters.Add(terminal, presenter);
    }

    internal static bool Supports(
        TerminalApp? app,
        GraphKind kind
    ) =>
        app is { IsGraphicsPresentationEnabled: true }
        && Presenters.TryGetValue(app.Terminal, out var presenter)
        && (presenter is SixelChartPresenter || presenter is DrcsChartPresenter && kind <= GraphKind.BoxPlot
        || presenter is RegisChartPresenterPrototype && RegisChartPresenterPrototype.Supports(kind));
}
