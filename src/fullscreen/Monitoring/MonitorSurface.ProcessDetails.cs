using System.Globalization;
using fullscreen.Files;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private long _processDetailRequest;
    private TextBlock? _commandLineText;
    /// <summary>Raised when the selected process needs an on-demand command-line lookup.</summary>
    public event Action<long, ProcessRow>? ProcessDetailRequested;

    private void ShowSelectedProcessDetails() {
        foreach (var row in Model.Rows) {
            if (row.Id == Model.SelectedId) {
                ShowProcessDetails(row);
                return;
            }
        }
    }

    internal void ShowProcessDetails(ProcessRow process) {
        long request = ++_processDetailRequest;
        static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Unavailable";
        string details = $"Name · {process.Name}\nPID · {process.Id}\nParent PID · {process.ParentId}\n" +
            $"User · {process.UserName ?? "Unavailable"}\nState · {process.Status ?? "Unavailable"}\n" +
            $"CPU · {Number(process.CpuPercent)}%\nRSS · {process.WorkingSet:N0} bytes\n" +
            $"Network · {Number(process.NetworkBytesPerSecond)} bytes/s\nDisk · {Number(process.DiskBytesPerSecond)} bytes/s\n" +
            $"GPU · {Number(process.GpuPercent)}%\nThreads · {process.Threads}";
        _commandLineText = new TextBlock("Loading command line…") { Wrap = true };
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Process details · " + process.Name + " · PID " + process.Id),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            Height = Math.Max(1, Bounds.Height - 2),
            Content = new VStack(new Button("Close").Click(() => dialog!.Close()),
                new ScrollViewer(new VStack(
                    new Border(new TextBlock(FileReader.Sanitize(details, true)) { Wrap = true }),
                    new TextBlock("COMMAND LINE · arguments are displayed only, never executed"),
                    new Border(_commandLineText))))
        };
        ShowMonitorDialog(dialog);
        if (ProcessDetailRequested is null) {
            CompleteProcessDetails(request, "Unavailable: this source does not support command-line inspection.");
        }
        else {
            ProcessDetailRequested.Invoke(request, process);
        }
    }

    internal void CompleteProcessDetails(long request, string text) {
        if (request != _processDetailRequest || _commandLineText is null) {
            return;
        }
        const int limit = 32768;
        if (text.Length > limit) {
            text = text[..limit] + "\n[Truncated at 32768 characters.]";
        }
        _commandLineText.Text = FileReader.Sanitize(string.IsNullOrWhiteSpace(text)
            ? "Unavailable: command line is empty or access is denied." : text, true);
    }
}
