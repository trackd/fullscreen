namespace fullscreen.Monitoring;

// Optional system data, not a dependency on lspci or a downloaded hardware database.
internal sealed class LinuxGpuNames {
    private static readonly string[] Databases = ["/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids"];
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    internal string Resolve(string? vendor, string? device, string driver) {
        if (driver is "hyperv_drm" or "hyperv_fb" || vendor == "0x1414" && device == "0x5353") {
            return "Microsoft Hyper-V Video";
        }
        string vendorId = (vendor ?? "").Replace("0x", "").Trim().ToLowerInvariant();
        string deviceId = (device ?? "").Replace("0x", "").Trim().ToLowerInvariant();
        string key = vendorId + ":" + deviceId + ":" + driver;
        if (_names.TryGetValue(key, out string? cached)) {
            return cached;
        }
        foreach (string path in Databases) {
            try {
                if (FindModel(File.ReadLines(path), vendorId, deviceId) is { } model) {
                    return _names[key] = Friendly(vendorId, model, driver);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return _names[key] = Friendly(vendorId, null, driver);
    }
    internal static string? FindModel(IEnumerable<string> lines, string vendor, string device) {
        if (vendor.Length != 4 || device.Length != 4) {
            return null;
        }
        bool selected = false;
        foreach (string line in lines) {
            if (line.Length == 0 || line[0] == '#') {
                continue;
            }
            if (line[0] != '\t') {
                if (selected) {
                    break;
                }
                selected = line.Length > 4 && string.Equals(line.Substring(0, 4), vendor, StringComparison.OrdinalIgnoreCase)
                    && char.IsWhiteSpace(line[4]);
            }
            else if (selected && line.Length > 5 && line[1] != '\t' && char.IsWhiteSpace(line[5])
                && string.Equals(line.Substring(1, 4), device, StringComparison.OrdinalIgnoreCase)) {
                return line.Substring(6).Trim();
            }
        }
        return null;
    }
    internal static string Friendly(string vendor, string? model, string driver) {
        if (driver is "hyperv_drm" or "hyperv_fb") {
            return "Microsoft Hyper-V Video";
        }
        string manufacturer = vendor switch {
            "8086" => "Intel",
            "1002" => "AMD",
            "10de" => "NVIDIA",
            "15ad" => "VMware",
            "1af4" => "Virtio",
            "1414" => "Microsoft",
            _ => driver switch {
                "i915" or "xe" => "Intel",
                "amdgpu" => "AMD",
                "vmwgfx" => "VMware",
                "nouveau" or "nvidia" => "NVIDIA",
                "virtio_gpu" => "Virtio",
                _ => ""
            }
        };
        if (string.IsNullOrWhiteSpace(model)) {
            return manufacturer.Length > 0 ? manufacturer + " graphics" : "Graphics adapter";
        }
        int start = model!.IndexOf('['), end = model.LastIndexOf(']');
        if (start >= 0 && end > start + 1) {
            model = model.Substring(start + 1, end - start - 1);
        }
        return manufacturer.Length == 0 || model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase) ? model : manufacturer + " " + model;
    }
}
