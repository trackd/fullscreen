using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace fullscreen.Hosting;
/// <summary>Defines the visual root and lifecycle hooks used by the fullscreen host.</summary>
public interface IFullscreenApp : IDisposable {
    /// <summary>Gets the root visual rendered by the fullscreen host.</summary>
    Visual Root { get; }

    /// <summary>Updates application state on the terminal UI thread.</summary>
    /// <returns>True while the application should continue running.</returns>
    bool Tick();
    /// <summary>Requests application cancellation or shutdown, retaining application-specific confirmation
    /// behavior.</summary>
    void RequestClose() =>
        Root.App?.Stop();
    /// <summary>Changes whether the application uses a reduced-chrome layout.</summary>
    /// <param name="minimal">Whether reduced-chrome layout is enabled.</param>
    void SetMinimal(bool minimal) {
        if (Root is Themes.ThemedSurface surface) {
            surface.Minimal = minimal;
        }
    }

    /// <summary>Applies the supplied palette to the application&apos;s root visual.</summary>
    /// <param name="theme">The palette used by the application.</param>
    void ApplyTheme(Themes.ThemePalette theme) =>
        theme.Apply(Root);
}

/// <summary>Runs fullscreen applications with shared input, theme, and graphics handling.</summary>
public static class FullscreenHost {
    private static int s_running;
    /// <summary>Runs an application in the terminal until it closes or cancellation is requested.</summary>
    /// <param name="app">The application whose root and lifecycle are hosted.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <param name="theme">The palette used by the application. Defaults to null.</param>
    /// <param name="minimal">Whether reduced-chrome layout is enabled. Defaults to false.</param>
    /// <param name="chartRenderer">The preferred chart graphics renderer. Defaults to
    /// Charts.ChartRenderer.Auto.</param>
    public static void Run(
        IFullscreenApp app,
        CancellationToken cancellationToken,
        Themes.ThemePalette? theme = null,
        bool minimal = false,
        Charts.ChartRenderer chartRenderer = Charts.ChartRenderer.Auto
    ) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) {
            throw new InvalidOperationException("Fullscreen requires an interactive terminal with VT input/output.");
        }

        if (Interlocked.CompareExchange(ref s_running, 1, 0) != 0) {
            throw new InvalidOperationException("A fullscreen app is already running in this process.");
        }

        try {
            app.ApplyTheme(theme ?? Themes.ThemePalette.Default);
            app.SetMinimal(minimal);
            var root = new FullscreenChrome(app, theme ?? Themes.ThemePalette.Default);
            app.Root.KeyDown((_, e) => {
                if (!e.Handled && e.Key == TerminalKey.F4) {
                    minimal = !minimal;
                    app.SetMinimal(minimal);
                    e.Handled = true;
                }
            });
            var terminalOptions = CreateTerminalOptions();
            using var inputMode = WindowsConsoleInputMode.Capture();
            using var session = Terminal.Open(options: terminalOptions);
            using ITerminalGraphicsPresenter? graphics = Charts.ChartPresenters.Create(
                session.Instance, chartRenderer, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var monitor in app.Root.EnumerateVisualsDepthFirst().OfType<Monitoring.MonitorSurface>()) {
                monitor.Renderer = chartRenderer;
            }

            Files.ImagePreview.SixelPresenter = graphics is Charts.SixelChartPresenter;
            var startupRefresh = OperatingSystem.IsWindows() && graphics is Charts.SixelChartPresenter
                ? new StartupGraphicsRefresh()
                : null;
            // Run keeps XenoAtom's app-wide F12 diagnostics gesture. It runs before
            // control input, including focused embedded components and modal dialogs.
            session.Instance.Run(root, context => {
                if (graphics is Charts.SixelChartPresenter { HasPresented: true }) {
                    startupRefresh?.Update(context.App, context.Timestamp);
                }
                Charts.ChartAdapters.Synchronize(app.Root);
                return cancellationToken.IsCancellationRequested || !app.Tick() ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
            }, new TerminalRunOptions { GraphicsPresenter = graphics });
        }
        finally {
            Files.ImagePreview.SixelPresenter = false;
            Volatile.Write(ref s_running, 0);
        }
    }

    internal static TerminalOptions CreateTerminalOptions() {
        // Window/cell-size replies must enter the VT decoder. Native Windows key
        // records otherwise expose reply digits as shortcuts and leave metrics unknown.
        var options = new TerminalOptions {
            WindowsVtInputDecoder = TerminalWindowsVtInputDecoderMode.Enabled
        };
        // The owned query batch includes pixel reports. Do not launch a second
        // upstream query after a missing/late response.
        options.Graphics.DisableProbing = true;
        // Passive XenoAtom detection remains available when our VT probe is inconclusive.
        options.Graphics.AllowHeuristicEnablement = true;
        return options;
    }

    internal static TerminalPixelMetrics? ResolveChartMetrics(
        TerminalGraphicsCapabilities capabilities,
        TerminalPixelMetrics? queried,
        TerminalSize size
    ) {
        if (!Charts.SixelChartPresenter.SupportsSixel(capabilities)) {
            return null;
        }

        if (queried is { CellPixelWidth: > 0, CellPixelHeight: > 0 }) {
            return queried;
        }

        if (capabilities.PixelMetrics is { CellPixelWidth: > 0, CellPixelHeight: > 0 } detected) {
            return detected;
        }

        // A Sixel-capable terminal can omit pixel-metric replies. Keep graphics
        // available with the same conservative 8x16 cell fallback as upstream.
        int columns = Math.Clamp(size.Columns, 1, 2000), rows = Math.Clamp(size.Rows, 1, 1000);
        return new TerminalPixelMetrics(columns * 8, rows * 16, 8, 16, columns, rows);
    }
}
