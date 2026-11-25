namespace fullscreen.Monitoring;

// Older guests may expose Hyper-V video only as a framebuffer, without a DRM card.
internal static class LinuxHyperVDisplay {
    internal static void Append(CollectorFrame frame, string root = "/sys/class/graphics") {
        if (frame.Gpus.Take(frame.GpuCount).Any(gpu => gpu.Provider.StartsWith("DRM/hyperv_drm", StringComparison.Ordinal)
            || gpu.Name == "Microsoft Hyper-V Video")) {
            return;
        }
        try {
            foreach (string entry in Directory.EnumerateDirectories(root, "fb*")) {
                if (Read(Path.Combine(entry, "name")) != "hyperv_fb") {
                    continue;
                }
                string? instance = Read(Path.Combine(entry, "device", "device_id"));
                const string status = "Hyper-V synthetic framebuffer; guest acceleration counters are not exposed";
                frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount + 1);
                frame.Gpus[frame.GpuCount] = new("Microsoft Hyper-V Video", "Hyper-V framebuffer", null, null, null, null) {
                    Id = instance is not null ? "vmbus:" + instance.Trim('{', '}') : "framebuffer:" + Path.GetFileName(entry),
                    Status = status
                };
                frame.GpuStatus = frame.GpuCount == 0 ? status : frame.GpuStatus + " | " + status;
                frame.GpuCount++;
                break; // Multiple framebuffers can represent the same synthetic display device.
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static string? Read(string path) {
        try {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            return null;
        }
    }
}
