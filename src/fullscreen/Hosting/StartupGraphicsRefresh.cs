using System.Diagnostics;
using XenoAtom.Terminal.UI;

namespace fullscreen.Hosting;

/// <summary>Restores graphics after the Windows console host's initial cell repaint.</summary>
internal sealed class StartupGraphicsRefresh {
    private long? _firstFrame;
    private bool _completed;

    internal void Update(TerminalApp app, long timestamp) {
        if (_completed || !app.IsGraphicsPresentationEnabled) {
            return;
        }

        if (_firstFrame is not { } firstFrame) {
            _firstFrame = timestamp;
            return;
        }

        // ConPTY may replay its text buffer after forwarding the first Sixel,
        // clearing an image that it does not retain in that buffer. Resend once
        // after startup has settled; subsequent static frames remain cached.
        if (timestamp - firstFrame >= Stopwatch.Frequency / 5) {
            _completed = true;
            app.RequestFullRender();
        }
    }
}
