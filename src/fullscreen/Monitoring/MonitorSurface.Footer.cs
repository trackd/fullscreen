using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private int FooterHelpWidth => Bounds.Width >= 40 ? 8 : 3;
    private int FooterFilterX => FooterHelpWidth + 2;

    private void RenderCompactFooter(CellBuffer buffer) {
        if (Minimal && !_editingFilter && ActionMessage.Length == 0 && SourceError is null) {
            return;
        }

        int y = Bounds.Height - 1;
        if (y < 0 || Bounds.Width < 5) {
            return;
        }

        var background = Color.Mix(Palette.Background, Palette.Accent, 0.16f);
        var footer = Body.WithBackground(background);
        var muted = Muted.WithBackground(background);
        var accent = Accent.WithBackground(background);
        Fill(buffer, 0, y, Bounds.Width, 1, footer);
        int filterX = FooterFilterX;
        double milliseconds = SampleMilliseconds;
        string timingLabel = "ms collect";
        if (IsRemote) {
            milliseconds = LatencyMilliseconds;
            timingLabel = "ms remote";
        }

        string timing = milliseconds.ToString("F1", CultureInfo.InvariantCulture) + timingLabel;
        int timingX = Math.Max(filterX, Bounds.Width - timing.Length - 1);
        int filterWidth = Math.Min(Math.Max(12, Bounds.Width / 3), Math.Max(0, timingX - filterX - 2));
        string filterText = Model.Filter;
        if (filterText.Length == 0) {
            string itemName = View switch {
                MonitorView.Users => "users",
                MonitorView.Services => "services",
                MonitorView.Network => "interfaces",
                MonitorView.Gpu => "devices",
                _ => "processes"
            };
            filterText = "all " + itemName;
        }

        string filter = (_editingFilter ? "Filter › " : "/ ") + filterText;
        Write(buffer, filterX, y, filter, _editingFilter ? accent : footer, filterWidth);
        string status = "LIVE";
        if (SourceError is not null) {
            status = SourceError;
        }
        else if (ActionMessage.Length > 0) {
            status = ActionMessage;
        }
        else if (Paused) {
            status = "PAUSED";
        }
        else if (View == MonitorView.Users) {
            status = Frame.UserStatus;
        }
        else if (ProcessTable) {
            status = $"{Model.Count} processes";
            if (Model.Tree) {
                status += " · tree";
            }
        }

        Write(
            buffer,
            filterX + filterWidth + 1,
            y,
            status,
            SourceError is null ? muted : Hot.WithBackground(background),
            Math.Max(0, timingX - filterX - filterWidth - 2)
        );
        Write(buffer, timingX, y, timing, muted, Math.Max(0, Bounds.Width - timingX - 1));
        Write(buffer, 1, y, FooterHelpWidth == 8 ? "[? Help]" : "[?]", accent.WithTextStyle(TextStyle.Bold), FooterHelpWidth);
    }

    internal void ShowHelp() {
        Dialog? dialog = null;
        string text = "Views: 1 processes · 2 services · 3 network · 4 GPU · 5/F8 users · 6 dashboard\n" +
            "↑↓ / PgUp / PgDn: select or scroll · Enter/A: actions · D: session details\n" +
            "/: edit filter · Esc: clear filter · P: pause/resume · Q: quit\n" +
            "T: process tree · ←/→: collapse/expand selected · right-click: expand/collapse all\n" +
            "C: columns · F6: sort · R: reverse sort · E: exclude process name · U: restore\n" +
            "L: include user processes · G: change graph type · click chart: inspect\n" +
            "120s history · newest at right · network receive: green, send: purple\n" +
            "F4: minimal view · F12: diagnostics · ?: this help\n\n" +
            "Chart: " + GraphLabel;
        dialog = new Dialog {
            Title = new TextBlock("Monitor keys and legend"),
            IsModal = true,
            Width = Math.Max(20, Math.Min(96, Bounds.Width - 4)),
            Content = new VStack(new Paragraph(text) { Wrap = true }, new Button("Close").Click(() => dialog!.Close()))
        };
        dialog.Show();
    }
}
