using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private Dialog? _monitorDialog;
    private bool _monitorDialogEditable;
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

    private void ShowMonitorDialog(Dialog dialog, bool editable = false) {
        _monitorDialog = dialog;
        _monitorDialogEditable = editable;
        dialog.SetStyle(Palette.Controls);
        dialog.KeyDownRouted += (_, e) => {
            if (e.Key == TerminalKey.Escape || e.Char == TerminalChar.CtrlQ || (!editable && e.Char is 'q' or 'Q')) {
                e.Handled = true;
                dialog.Close();
            }
        };
        if (!editable) {
            dialog.TextInputRouted += (_, e) => {
                if (e.Text is "q" or "Q") {
                    e.Handled = true;
                    dialog.Close();
                }
            };
            foreach (char key in new[] { 'q', 'Q' }) {
                dialog.AddCommand(new Command {
                    Id = "Fullscreen.Monitor.Dismiss." + key,
                    LabelMarkup = "Close dialog",
                    Gesture = new KeyGesture(key),
                    Execute = _ => dialog.Close()
                });
            }
        }
        dialog.Show();
    }

    internal string HelpText(int width, int height) {
        string[] bindings = [
            "1: processes", "2: services", "3: network", "4: GPU", "5 / F8: users", "6: dashboard",
            "↑/↓: select or scroll", "PgUp/PgDn: select or scroll a page", "Home/End: first/last row",
            "Enter: toggle user group / process tree, or actions", "A: process, service or session actions",
            "D: process or user session details", "Space: mark account sessions · M: mark/unmark all · X: logoff picker",
            "/: edit filter", "Esc: clear filter / close dialog",
            "Ctrl+U: clear search text", "P: pause/resume", "Q / Ctrl+Q: quit (close dialogs first)",
            "T: process tree", "←/→: process columns (Shift in tree) / user branches",
            "Right-click: menus / expand or collapse all",
            "C: columns", "F6: sort", "R: reverse sort", "E: exclude process name", "U: restore excluded processes",
            "L: include user processes", "G: change graph type", "Click chart: inspect",
            "F4: minimal view", "F12: diagnostics", "? / F1: help"
        ];
        string legend = "120s history · newest at right · network receive: green, send: purple\nChart: " + GraphLabel;
        int contentWidth = Math.Max(1, width - 6);
        string detailed = string.Join("\n", bindings) + "\n\n" + legend;
        int lines = detailed.Split('\n').Sum(line => Math.Max(1, (line.Length + contentWidth - 1) / contentWidth));
        if (lines <= height - 8) {
            return detailed;
        }

        return "Views: 1 processes · 2 services · 3 network · 4 GPU · 5/F8 users · 6 dashboard\n" +
            "↑↓ / PgUp/PgDn / Home/End: select or scroll · Enter: toggle branch/actions · A: actions · D: details\n" +
            "Users: Space marks account · M marks/unmarks all · X selects sessions for confirmed logoff\n" +
            "/: edit filter · Esc: clear filter/close dialog · Ctrl+U: clear search · P: pause/resume · Q/Ctrl+Q: quit (close dialogs first)\n" +
            "T: tree · ←/→: process columns (Shift in tree) / user branches · right-click: menus\n" +
            "C: columns · F6: sort · R: reverse · E: exclude process name · U: restore\n" +
            "L: include user processes · G: graph type · click chart: inspect\n" +
            "F4: minimal view · F12: diagnostics · ?/F1: help\n\n" + legend;
    }

    internal void ShowHelp() {
        int width = Math.Max(20, Math.Min(96, Bounds.Width - 4));
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Monitor keys and legend"),
            IsModal = true,
            Width = width,
            MaxHeight = Math.Max(1, Bounds.Height - 2),
            Content = new VStack(
                new ScrollViewer(new Paragraph(HelpText(width, Bounds.Height)) { Wrap = true }) {
                    MaxHeight = Math.Max(1, Bounds.Height - 8)
                },
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        ShowMonitorDialog(dialog);
    }
}
