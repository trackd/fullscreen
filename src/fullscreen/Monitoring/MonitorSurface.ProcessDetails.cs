using System.Globalization;
using fullscreen.Files;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private long _processDetailRequest;
    private TextBlock? _commandLineText;
    private static VStack DetailLines(string text) => new(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
        .Select(line => new TextBlock(FileReader.Sanitize(line.Length == 0 ? " " : line)) { Wrap = true }).ToArray());
    private void ShowGpuDetails() {
        var text = new System.Text.StringBuilder();
        foreach (var gpu in Frame.Gpus.Take(Frame.GpuCount)) {
            text.AppendLine(CultureInfo.InvariantCulture, $"{gpu.Name}\nAdapter · {gpu.Id ?? "Unavailable"}\nProvider · {gpu.Provider}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Memory scope · {gpu.MemoryKind ?? "Unavailable"}\nPower scope · {gpu.PowerScope ?? "Unavailable"}");
            text.AppendLine(gpu.Status ?? "No provider-specific availability detail");
            text.AppendLine(CultureInfo.InvariantCulture, $"Memory clock · {ResourceNumber(gpu.MemoryClockMHz)} MHz\nPCIe RX · {ResourceBytes(gpu.PcieReceiveBytes)}/s\nPCIe TX · {ResourceBytes(gpu.PcieSendBytes)}/s\nPerformance state · {(gpu.PerformanceState.HasValue ? "P" + gpu.PerformanceState.Value.ToString(CultureInfo.InvariantCulture) : "Unavailable")}");
            text.AppendLine();
        }
        if (Frame.GpuCount == 0) { text.Append(Frame.GpuStatus); }
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("GPU details"),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            Height = Math.Max(1, Bounds.Height - 2),
            Content = new VStack(new Button("Close").Click(() => dialog!.Close()),
                new ScrollViewer(DetailLines(text.ToString())))
        };
        ShowMonitorDialog(dialog);
    }
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
            $"Disk read · {ResourceBytes(process.DiskReadBytesPerSecond)}/s\nDisk write · {ResourceBytes(process.DiskWriteBytesPerSecond)}/s\n" +
            $"GPU · {Number(process.GpuPercent)}%\nThreads · {process.Threads}\n" + FormatGpuProcessDetails(process);
        _commandLineText = new TextBlock("Loading command line…") { Wrap = true };
        _detailProcess = (process.Id, process.StartTimeTicks);
        _processCpu.Clear();
        _processRam.Clear();
        _processLiveText = new TextBlock("Waiting for next CPU/memory sample…") { Wrap = true };
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Process details · " + process.Name + " · PID " + process.Id),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            Height = Math.Max(1, Bounds.Height - 2),
            Content = new VStack(new Button("Close").Click(() => dialog!.Close()),
                new ScrollViewer(new VStack(
                    new Border(DetailLines(details)),
                    new Border(_processLiveText),
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

    internal static string FormatGpuProcessDetails(ProcessRow process) {
        static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Unavailable";
        static string Bytes(ulong? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "Unavailable";
        var text = new System.Text.StringBuilder("\nGPU DETAILS\n");
        if (process.GpuDetails is not { Length: > 0 } details) { return text.Append("No attributable GPU counters").ToString(); }
        foreach (var detail in details) {
            text.AppendLine(CultureInfo.InvariantCulture, $"Adapter · {detail.AdapterId} · {detail.Provider}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Usage · {Number(detail.Utilization)}% · memory {Bytes(detail.Memory)} bytes");
            text.AppendLine(CultureInfo.InvariantCulture, $"Dedicated · {Bytes(detail.DedicatedMemory)} · shared {Bytes(detail.SharedMemory)} bytes · {detail.MemoryKind}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Render {Number(detail.Activity.Render)}% · compute {Number(detail.Activity.Compute)}% · copy {Number(detail.Activity.Copy)}%");
            text.AppendLine(CultureInfo.InvariantCulture, $"Video {Number(detail.Activity.Video)}% · video processing {Number(detail.Activity.VideoEnhance)}% · encode {Number(detail.Activity.Encode)}% · decode {Number(detail.Activity.Decode)}%");
            text.AppendLine(CultureInfo.InvariantCulture, $"SM {Number(detail.SmActivity)}% · memory activity {Number(detail.Activity.Memory)}%\n{detail.Status}");
        }
        return text.ToString();
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
