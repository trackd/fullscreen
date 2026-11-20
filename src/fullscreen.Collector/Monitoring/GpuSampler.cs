using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Samples graphics-device and per-process GPU counters where supported.</summary>
public sealed class GpuSampler : IDisposable {
    private NvmlSampler? _nvml;
    private WindowsGpuSampler? _windows;
    private LinuxGpuSampler? _linux;
    private (bool Devices, bool Processes) _mode;
    internal CollectorCapabilities ProcessCapabilities =>
        (_windows?.Capabilities ?? CollectorCapabilityDetection.Current()) & CollectorCapabilities.ProcessGraphics;
    internal CollectorCapabilities DeviceCapabilities { get; private set; }
    /// <summary>Updates graphics-device and optional per-process GPU counters in a frame.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    /// <param name="devices">Whether device-wide GPU counters are collected. Defaults to true.</param>
    /// <param name="processes">Whether per-process GPU counters are collected. Defaults to true.</param>
    public void Sample(CollectorFrame frame, bool devices = true, bool processes = true) {
        frame.GpuCount = 0;
        frame.GpuStatus = "";
        DeviceCapabilities = CollectorCapabilities.None;
        if (devices) {
            (_nvml ??= new()).Sample(frame);
            DeviceCapabilities |= _nvml.Capabilities;
        }
        else {
            _nvml?.Dispose();
            _nvml = null;
        }

        bool wddm = (CollectorCapabilityDetection.Current() & CollectorCapabilities.ProcessGraphics) != 0;
        if (Platform.IsWindows() && wddm && (processes || devices && frame.GpuCount == 0)) {
            if (_mode != (devices, processes)) {
                _windows?.Dispose();
                _windows = null;
                _mode = (devices, processes);
            }

            (_windows ??= new(processes, devices)).Sample(processes);
            if (devices) {
                DeviceCapabilities |= _windows.Capabilities & CollectorCapabilities.DeviceGraphics;
            }
        }
        else {
            _windows?.Dispose();
            _windows = null;
        }

        if (devices && Platform.IsLinux()) {
            (_linux ??= new()).Sample(frame);
            DeviceCapabilities |= _linux.Capabilities;
        }
        else {
            _linux = null;
        }

        if (devices && frame.GpuCount == 0 && _windows is { } windows && WindowsDevice(windows.Utilization, windows.UsedMemory) is { } deviceRow) {
            frame.Gpus[frame.GpuCount++] = deviceRow;
        }

        if (_linux is null || !frame.GpuStatus.StartsWith("Intel", StringComparison.Ordinal)) {
            frame.GpuStatus = frame.GpuCount > 0 ? "GPU telemetry available" : "Unavailable · driver/provider not exposed";
        }
    }

    internal static GpuRow? WindowsDevice(double? utilization, ulong? memory) =>
        utilization.HasValue || memory.HasValue
            ? new("WDDM adapters · busiest engine", "Windows PDH", utilization, memory, null, null) : null;

    internal static CollectorCapabilities SampleDrm(CollectorFrame frame, string root) {
        var capabilities = CollectorCapabilities.None;
        try {
            foreach (string card in Directory.EnumerateDirectories(root, "card*")) {
                if (!int.TryParse(Path.GetFileName(card)[4..], out _)) {
                    continue;
                }

                string device = Path.Combine(card, "device");
                bool intel = ReadLabel(Path.Combine(device, "vendor")) == "0x8086";
                bool busyCounter = ExposesCounter(Path.Combine(device, "gpu_busy_percent"));
                if (!busyCounter && !intel) {
                    continue;
                }

                if (busyCounter) {
                    capabilities |= CollectorCapabilities.GpuUtilization;
                }
                if (ExposesCounter(Path.Combine(device, "mem_info_vram_used"))) {
                    capabilities |= CollectorCapabilities.GpuUsedMemory;
                }
                if (ExposesCounter(Path.Combine(device, "mem_info_vram_total"))) {
                    capabilities |= CollectorCapabilities.GpuTotalMemory;
                }
                double? busy = ReadNumber(Path.Combine(device, "gpu_busy_percent"));
                ulong? used = (ulong?)ReadNumber(Path.Combine(device, "mem_info_vram_used"));
                ulong? total = (ulong?)ReadNumber(Path.Combine(device, "mem_info_vram_total"));
                frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                frame.Gpus[frame.GpuCount++] = new(
                    Path.GetFileName(card) + (intel ? " Intel GPU " + ReadLabel(Path.Combine(device, "device")) : ""),
                    intel ? "DRM/Intel clients" : "DRM/sysfs", busy, used, total, null);
            }
        }
        catch (DirectoryNotFoundException) {
            // A missing DRM provider must not override capabilities from NVML.
        }
        catch (IOException) {
            capabilities |= CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuTotalMemory;
        }
        catch (UnauthorizedAccessException) {
            capabilities |= CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory | CollectorCapabilities.GpuTotalMemory;
        }
        return capabilities;
    }

    private static bool ExposesCounter(string path) {
        try {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) {
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return true; // An inaccessible provider is not evidence that the metric is unsupported.
        }
    }

    private static string? ReadLabel(string path) {
        try {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    private static double? ReadNumber(string path) {
        try {
            return double.TryParse(
                File.ReadAllText(path).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value
            ) && Compat.IsFinite(value) && value >= 0 ? value : null;
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
