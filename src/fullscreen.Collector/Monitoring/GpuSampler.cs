using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Samples graphics-device and per-process GPU counters where supported.</summary>
public sealed class GpuSampler : IDisposable {
    private readonly SudoCollector? _sudo;
    private readonly bool _ownsSudo;
    /// <summary>Creates a GPU sampler without privilege escalation.</summary>
    public GpuSampler() { }
    /// <summary>Creates a GPU sampler with optional non-interactive sudo fallback.</summary>
    /// <param name="useSudo">Whether Linux device readers may use sudo.</param>
    public GpuSampler(bool useSudo) {
        _sudo = useSudo ? new() : null;
        _ownsSudo = useSudo;
    }
    internal GpuSampler(SudoCollector? sudo) => _sudo = sudo;
    private NvmlSampler? _nvml;
    private WindowsGpuSampler? _windows;
    private LinuxGpuSampler? _linux;
    private AppleGpuSampler? _apple;
    private AmdWindowsGpuSampler? _amd;
    private IntelWindowsGpuSensors? _intel;
    private readonly LinuxGpuNames _linuxNames = new();
    private (bool Devices, bool Processes) _mode;
    internal CollectorCapabilities ProcessCapabilities => Platform.IsMacOS() ? CollectorCapabilities.ProcessGraphics
        : Platform.IsLinux() ? (_linux?.ProcessCapabilities ?? CollectorCapabilities.None) | (_nvml?.ProcessTelemetryAvailable == true ? CollectorCapabilities.ProcessGraphics : CollectorCapabilities.None)
        : (_windows?.Capabilities ?? CollectorCapabilityDetection.Current()) & CollectorCapabilities.ProcessGraphics;
    internal CollectorCapabilities DeviceCapabilities { get; private set; }
    /// <summary>Updates graphics-device and optional per-process GPU counters in a frame.</summary>
    /// <param name="frame">The reusable metric snapshot to read or fill.</param>
    /// <param name="devices">Whether device-wide GPU counters are collected. Defaults to true.</param>
    /// <param name="processes">Whether per-process GPU counters are collected. Defaults to true.</param>
    public void Sample(CollectorFrame frame, bool devices = true, bool processes = true) {
        frame.GpuCount = 0;
        frame.GpuStatus = "";
        DeviceCapabilities = CollectorCapabilities.None;
        if (devices || processes && Platform.IsLinux()) {
            (_nvml ??= new()).Sample(frame, devices, processes);
            if (devices) { DeviceCapabilities |= _nvml.Capabilities; }
        }
        else {
            _nvml?.Dispose();
            _nvml = null;
        }

        bool wddm = (CollectorCapabilityDetection.Current() & CollectorCapabilities.ProcessGraphics) != 0;
        if (Platform.IsWindows() && wddm && (processes || devices)) {
            if (_mode != (devices, processes)) {
                _windows?.Dispose();
                _windows = null;
                _mode = (devices, processes);
            }

            (_windows ??= new(processes, devices)).Sample(processes);
            if (devices) {
                var adapters = new CollectorFrame();
                _windows.AppendDevices(adapters);
                MergeWindowsDevices(frame, adapters);
                DeviceCapabilities |= (_amd ??= new()).Sample(frame);
                DeviceCapabilities |= (_intel ??= new()).Sample(frame);
                DeviceCapabilities |= _windows.Capabilities & CollectorCapabilities.DeviceGraphics;
            }
        }

        else {
            _windows?.Dispose();
            _windows = null;
        }

        if (Platform.IsMacOS() && (devices || processes)) {
            (_apple ??= new()).Sample(frame, devices, processes);
            if (devices) { DeviceCapabilities |= _apple.Capabilities; }
        }

        if ((devices || processes) && Platform.IsLinux()) {
            int count = frame.GpuCount;
            (_linux ??= new(allowSudo: _sudo is not null, sudo: _sudo)).Sample(frame, includeDevices: devices, includeProcesses: processes);
            if (devices) {
                for (int index = 0; index < frame.GpuCount; index++) {
                    var row = frame.Gpus[index];
                    string card = row.Name.Split(' ')[0];
                    if (!row.Provider.StartsWith("DRM/", StringComparison.Ordinal) || !card.StartsWith("card", StringComparison.Ordinal)
                        || !int.TryParse(card.Substring(4), out _)) {
                        continue;
                    }
                    string device = Path.Combine("/sys/class/drm", card, "device");
                    string driver = row.Provider.Substring(4).Split(' ')[0];
                    frame.Gpus[index] = row with { Name = _linuxNames.Resolve(ReadLabel(Path.Combine(device, "vendor")), ReadLabel(Path.Combine(device, "device")), driver) };
                }
            }
            if (devices) {
                LinuxHyperVDisplay.Append(frame);
                DeviceCapabilities |= _linux.Capabilities;
            }
            else {
                frame.GpuCount = count;
            }
        }
        else {
            _linux?.Dispose();
            _linux = null;
        }

        if (devices && frame.GpuCount == 0 && _windows is { } windows && WindowsDevice(windows.Utilization, windows.UsedMemory) is { } deviceRow) {
            frame.Gpus[frame.GpuCount++] = deviceRow;
        }

        if (_linux is null && _apple is null) {
            frame.GpuStatus = frame.GpuCount == 0 ? "Unavailable · driver/provider not exposed"
                : frame.Gpus.Take(frame.GpuCount).Any(gpu => gpu.Utilization.HasValue || gpu.UsedMemory.HasValue)
                    ? "GPU telemetry available"
                    : string.Join(" | ", frame.Gpus.Take(frame.GpuCount).Select(gpu => gpu.Status ?? "Activity counters unavailable"));
        }
    }

    internal static void MergeWindowsDevices(CollectorFrame frame, CollectorFrame adapters) {
        for (int index = 0; index < adapters.GpuCount; index++) {
            var adapter = adapters.Gpus[index];
            int match = -1;
            for (int row = 0; row < frame.GpuCount; row++) {
                if (adapter.Id is not null && string.Equals(frame.Gpus[row].Id, adapter.Id, StringComparison.OrdinalIgnoreCase)) {
                    match = row;
                    break;
                }
            }
            if (match >= 0) {
                var vendor = frame.Gpus[match];
                frame.Gpus[match] = vendor with {
                    Provider = vendor.Provider + " + WDDM",
                    Activity = adapter.Activity with {
                        Encode = vendor.Activity.Encode ?? adapter.Activity.Encode,
                        Decode = vendor.Activity.Decode ?? adapter.Activity.Decode,
                        Memory = vendor.Activity.Memory
                    },
                    DedicatedMemory = adapter.DedicatedMemory,
                    SharedMemory = adapter.SharedMemory,
                    MemoryKind = "framebuffer usage; split counters from WDDM",
                    Status = adapter.Status + "; " + vendor.Status
                };
            }
            else {
                frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                frame.Gpus[frame.GpuCount++] = adapter;
            }
        }
    }

    internal static GpuRow? WindowsDevice(double? utilization, ulong? memory) =>
        utilization.HasValue || memory.HasValue
            ? new("WDDM adapters · busiest engine", "Windows PDH", utilization, memory, null, null) : null;

    internal static CollectorCapabilities SampleDrm(CollectorFrame frame, string root) {
        var capabilities = CollectorCapabilities.None;
        bool hasNvml = frame.Gpus.Take(frame.GpuCount).Any(row => row.Provider == "NVIDIA NVML");
        try {
            foreach (string card in Directory.EnumerateDirectories(root, "card*")) {
                if (!int.TryParse(Path.GetFileName(card)[4..], out _)) {
                    continue;
                }

                string device = Path.Combine(card, "device");
                string? vendor = ReadLabel(Path.Combine(device, "vendor"));
                bool intel = vendor == "0x8086";
                string driver = (ReadLabel(Path.Combine(device, "uevent")) ?? "").Split('\n')
                    .FirstOrDefault(line => line.StartsWith("DRIVER=", StringComparison.Ordinal))?[7..].Trim() ?? "unknown";
                if (hasNvml && driver == "nvidia") {
                    continue;
                }
                bool hyperv = driver == "hyperv_drm" || vendor == "0x1414" && ReadLabel(Path.Combine(device, "device")) == "0x5353";
                string? pci = (ReadLabel(Path.Combine(device, "uevent")) ?? "").Split('\n')
                    .FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal))?[14..].Trim();
                string? busId = ReadLabel(Path.Combine(device, "device_id"));
                bool busyCounter = ExposesCounter(Path.Combine(device, "gpu_busy_percent"));

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
                double? temperature = ReadTemperature(device);
                if (temperature.HasValue) { capabilities |= CollectorCapabilities.GpuTemperature; }
                frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                frame.Gpus[frame.GpuCount++] = new(
                    Path.GetFileName(card) + (intel ? " Intel GPU " : " " + driver + " GPU ") + ReadLabel(Path.Combine(device, "device")),
                    "DRM/" + driver + (busyCounter ? " sysfs" : ""), busy, used, total, temperature) {
                    Id = pci ?? (busId is not null ? "vmbus:" + busId.Trim('{', '}') : null),
                    DedicatedMemory = used,
                    MemoryKind = used.HasValue ? "VRAM usage" : null,
                    Status = hyperv ? "Hyper-V synthetic display; guest acceleration counters are not exposed" : null
                };
            }
        }
        catch (DirectoryNotFoundException) {
            // A missing DRM provider must not override capabilities from NVML.
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return capabilities;
    }

    private static double? ReadTemperature(string device) {
        try {
            foreach (string monitor in Directory.EnumerateDirectories(Path.Combine(device, "hwmon"))) {
                // Only sensors belonging to this GPU, never an unrelated CPU package sensor.
                if (ReadNumber(Path.Combine(monitor, "temp1_input")) is { } value) {
                    return value / 1000;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
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

    internal static double? ReadNumber(string path) {
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
        if (_apple is not null) { _apple.Enrich(rows, count); return; }
        for (int i = 0; i < count; i++) {
            var row = rows[i];
            if (_windows is not null) {
                rows[i] = GpuProcessAccounting.Apply(row, _windows.ProcessDetails(row.Id));
            }
            else if (_linux is not null) {
                rows[i] = GpuProcessAccounting.Apply(row, _linux.ProcessDetails(row.Id)
                    .Concat(_nvml?.ProcessDetails(row.Id, row.StartTimeTicks) ?? []));
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        _windows?.Dispose();
        _nvml?.Dispose();
        _linux?.Dispose();
        _apple?.Dispose();
        _amd?.Dispose();
        _intel?.Dispose();
        if (_ownsSudo) { _sudo?.Dispose(); }
    }
}
