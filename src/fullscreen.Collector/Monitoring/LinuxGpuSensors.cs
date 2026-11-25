namespace fullscreen.Monitoring;

// Use only sensors associated with this GPU. Never attribute host RAPL or CPU sensors to a passed-through device.
internal sealed class LinuxGpuSensors {
    private readonly Dictionary<string, double> _energy = new(StringComparer.Ordinal);
    internal GpuRow Sample(GpuRow row, string device, double seconds) {
        string card = Path.GetDirectoryName(device)!;
        double? clock = Read(Path.Combine(card, "gt/gt0/rps_act_freq_mhz"))
            ?? Read(Path.Combine(card, "gt_act_freq_mhz")) ?? Read(Path.Combine(device, "tile0/gt0/freq0/act_freq"));
        double? power = null, memoryClock = null;
        try {
            foreach (string monitor in Directory.EnumerateDirectories(Path.Combine(device, "hwmon"))) {
                clock ??= Read(Path.Combine(monitor, "freq1_input")) / 1e6;
                string label = ResourceSampler.Read(Path.Combine(monitor, "freq2_label"));
                if (label.IndexOf("mem", StringComparison.OrdinalIgnoreCase) >= 0) {
                    memoryClock ??= Read(Path.Combine(monitor, "freq2_input")) / 1e6;
                }
                power = Read(Path.Combine(monitor, "power1_average")) / 1e6
                    ?? Read(Path.Combine(monitor, "power1_input")) / 1e6;
                string energyPath = Path.Combine(monitor, "energy1_input");
                if (power is null && Read(energyPath) is { } energy) {
                    if (_energy.TryGetValue(energyPath, out double before) && seconds > 0 && energy >= before) {
                        power = (energy - before) / seconds / 1e6;
                    }
                    _energy[energyPath] = energy;
                }
                else {
                    _energy.Remove(energyPath);
                }
                if (power.HasValue) {
                    break;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return row with {
            ClockMHz = clock,
            MemoryClockMHz = memoryClock,
            PowerWatts = power,
            PowerScope = power.HasValue ? "GPU/SoC hwmon" : null
        };
    }
    private static double? Read(string path) => GpuSampler.ReadNumber(path);
}
