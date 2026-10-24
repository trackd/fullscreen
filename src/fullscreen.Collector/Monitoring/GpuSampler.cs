using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Samples graphics-device and per-process GPU counters where supported.</summary>
public sealed class GpuSampler : IDisposable {
    private NvmlSampler? _nvml;
    private WindowsGpuSampler? _windows;
    private (bool Devices, bool Processes) _mode;
    /// <summary>Updates graphics-device and optional per-process GPU counters in a frame.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    /// <param name="devices">Whether device-wide GPU counters are collected. Defaults to true.</param>
    /// <param name="processes">Whether per-process GPU counters are collected. Defaults to true.</param>
    public void Sample(CollectorFrame frame, bool devices = true, bool processes = true) {
        frame.GpuCount = 0;
        if (devices) {
            (_nvml ??= new()).Sample(frame);
        }
        else {
            _nvml?.Dispose();
            _nvml = null;
        }

        if (Platform.IsWindows() && (processes || devices && frame.GpuCount == 0)) {
            if (_mode != (devices, processes)) {
                _windows?.Dispose();
                _windows = null;
                _mode = (devices, processes);
            }

            (_windows ??= new(processes, devices)).Sample(processes);
        }
        else {
            _windows?.Dispose();
            _windows = null;
        }

        if (devices && Platform.IsLinux()) {
            try {
                foreach (string card in Directory.EnumerateDirectories("/sys/class/drm", "card*")) {
                    if (!int.TryParse(Path.GetFileName(card)[4..], out _)) {
                        continue;
                    }

                    string device = Path.Combine(card, "device");
                    if (!File.Exists(Path.Combine(device, "gpu_busy_percent"))) {
                        continue;
                    }

                    double? busy = ReadNumber(Path.Combine(device, "gpu_busy_percent"));
                    ulong? used = (ulong?)ReadNumber(Path.Combine(device, "mem_info_vram_used"));
                    ulong? total = (ulong?)ReadNumber(Path.Combine(device, "mem_info_vram_total"));
                    frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                    frame.Gpus[frame.GpuCount++] = new(Path.GetFileName(card), "DRM/sysfs", busy, used, total, null);
                }
            }
            catch (IOException) {
            }
            catch (UnauthorizedAccessException) {
            }
        }

        if (devices && frame.GpuCount == 0 && _windows?.Utilization is double busyPercent) {
            frame.Gpus[frame.GpuCount++] = new(
                "WDDM adapters · busiest engine",
                "Windows PDH",
                busyPercent,
                _windows.DedicatedMemory,
                null,
                null
            );
        }

        frame.GpuStatus = frame.GpuCount > 0 ? "GPU telemetry available" : "Unavailable · driver/provider not exposed";
    }

    private static double? ReadNumber(string path) {
        try {
            return double.TryParse(
                File.ReadAllText(path).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value
            ) ? value : null;
        }
        catch (IOException) {
            return null;
        }
        catch (UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>Adds sampled GPU counters to active process rows.</summary>
    /// <param name="rows">The source rows or requested terminal row count.</param>
    /// <param name="count">The active entry count.</param>
    public void Enrich(ProcessRow[] rows, int count) {
        for (int i = 0; i < count; i++) {
            var row = rows[i];
            if (_windows is not null) {
                rows[i] = row with {
                    GpuPercent = _windows.ProcessUtilization(row.Id),
                    GpuMemory = _windows.ProcessMemory(row.Id)
                };
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        _windows?.Dispose();
        _nvml?.Dispose();
    }
}
