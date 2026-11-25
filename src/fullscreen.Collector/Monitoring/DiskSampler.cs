using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace fullscreen.Monitoring;

internal sealed class DiskSampler(string sysRoot = "/sys", string procRoot = "/proc") : IDisposable {
    private readonly Dictionary<string, ulong[]> _previous = [];
    private readonly Dictionary<string, (bool Read, bool Write)> _macTimes = [];
    private long _last;
    private NativeCounterSet? _windows;
    private long _layoutTime;
    private long _temperatureTime;
    private readonly Dictionary<string, double?> _temperatures = [];
    private (string? Device, DiskVolume Volume)[] _volumes = [];
    private static readonly string[] Counters = ["Disk Read Bytes/sec", "Disk Write Bytes/sec", "Disk Reads/sec", "Disk Writes/sec", "% Idle Time", "Avg. Disk sec/Read", "Avg. Disk sec/Write", "Current Disk Queue Length"];
    internal void Sample(CollectorFrame frame, double? elapsed = null) {
        long now = Stopwatch.GetTimestamp();
        double seconds = elapsed ?? (_last == 0 ? 0 : (double)(now - _last) / Stopwatch.Frequency);
        _last = now;
        var rows = new List<DiskRow>();
        try {
            if (Platform.IsLinux()) {
                rows.AddRange(Linux(seconds));
            }
            else if (Platform.IsWindows()) {
                rows.AddRange(Windows());
            }
            else if (Platform.IsMacOS()) { rows.AddRange(Mac(seconds)); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _previous.Clear(); }
        if (rows.Count == 0) { _previous.Clear(); }
        if (_layoutTime == 0 || (double)(now - _layoutTime) / Stopwatch.Frequency >= 5) {
            _layoutTime = now;
            _volumes = DiscoverVolumes();
        }
        frame.Disks = DiskLayout.Combine(rows, _volumes);
    }
    private (string? Device, DiskVolume Volume)[] DiscoverVolumes() {
        var result = new List<(string? Device, DiskVolume Volume)>();
        try {
            if (Platform.IsLinux()) {
                foreach (var mount in DiskLayout.LinuxMounts(ResourceSampler.Read(Path.Combine(procRoot, "self/mountinfo")), sysRoot)) {
                    try {
                        var volume = new DriveInfo(mount.Path);
                        if (volume.TotalSize <= 0) { continue; }
                        result.Add((mount.Device.Length > 0 ? mount.Device : null, new(mount.Key, mount.Path, mount.FileSystem, (ulong)volume.TotalSize, (ulong)volume.AvailableFreeSpace)));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
                result.AddRange(DiskLayout.LinuxSwaps(ResourceSampler.Read(Path.Combine(procRoot, "swaps")), sysRoot));
            }
            else {
                foreach (var volume in DriveInfo.GetDrives()) {
                    try {
                        if (volume.DriveType is not (DriveType.Fixed or DriveType.Removable) || !volume.IsReady || volume.TotalSize <= 0) { continue; }
                        int? disk = Platform.IsWindows() ? DiskLayout.WindowsDisk(volume.Name) : null;
                        result.Add((disk.HasValue ? "disk:" + disk.Value : null, new(volume.Name, volume.Name, volume.DriveFormat, (ulong)volume.TotalSize, (ulong)volume.AvailableFreeSpace)));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return result.ToArray();
    }
    internal DiskRow[] Linux(double seconds) {
        var rows = new List<DiskRow>();
        var active = new HashSet<string>();
        foreach (string device in Directory.EnumerateDirectories(Path.Combine(sysRoot, "block"))) {
            string name = Path.GetFileName(device);
            if (name.StartsWith("loop", StringComparison.Ordinal) || name.StartsWith("ram", StringComparison.Ordinal)) { continue; }
            if (ResourceSampler.Read(Path.Combine(device, "device/type")) == "5") { continue; }
            ulong[]? ticks = Parse(ResourceSampler.Read(Path.Combine(device, "stat")));
            if (ticks is null) { continue; }
            string model = ResourceSampler.Read(Path.Combine(device, "device/model"));
            double? temperature = null;
            string hwmon = Path.Combine(device, "device/hwmon");
            if (Directory.Exists(hwmon)) {
                foreach (string monitor in Directory.EnumerateDirectories(hwmon)) {
                    double? reading = ResourceSampler.Number(ResourceSampler.Read(Path.Combine(monitor, "temp1_input"))) / 1000;
                    if (reading is >= -20 and <= 150) { temperature = reading; break; }
                }
            }
            bool stacked = Directory.Exists(Path.Combine(device, "slaves")) && Directory.EnumerateFileSystemEntries(Path.Combine(device, "slaves")).Any();
            ulong? total = ulong.TryParse(ResourceSampler.Read(Path.Combine(device, "size")), out ulong sectors) && sectors <= ulong.MaxValue / 512 ? sectors * 512 : null;
            rows.Add(Delta(name, model.Length > 0 ? name + " · " + model : name, ticks, seconds) with { Temperature = temperature, Total = total, Overview = !stacked });
            active.Add(name);
        }
        foreach (string id in _previous.Keys.Where(id => !active.Contains(id)).ToArray()) { _previous.Remove(id); }
        return rows.ToArray();
    }
    internal static ulong[]? Parse(string text) {
        string[] fields = ResourceSampler.Fields(text);
        if (fields.Length < 11) { return null; }
        var result = new ulong[11];
        for (int index = 0; index < result.Length; index++) {
            if (!ulong.TryParse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture, out result[index])) { return null; }
        }
        return result;
    }
    internal DiskRow Delta(string id, string name, ulong[] current, double seconds) {
        _previous.TryGetValue(id, out var previous);
        _previous[id] = current;
        const string scope = "Device accounting; stacked devices overlap; busy time is not a saturation measure on parallel storage";
        if (previous is null || seconds <= 0 || current.Where((value, index) => index != 8 && value < previous[index]).Any()) {
            return new(id, name, "Device", Queue: current[8], Status: "Warming up or counters reset; " + scope);
        }
        double Change(int index) => current[index] - previous[index];
        double reads = Change(0), writes = Change(4);
        return new(id, name, "Device", ReadBytes: Change(2) * 512 / seconds, WriteBytes: Change(6) * 512 / seconds,
            ReadIops: reads / seconds, WriteIops: writes / seconds, Busy: Compat.Clamp(Change(9) / (seconds * 10), 0, 100),
            ReadLatency: reads > 0 ? Change(3) / reads : null, WriteLatency: writes > 0 ? Change(7) / writes : null,
            Queue: Change(10) / (seconds * 1000), Status: scope + "; queue is interval average");
    }
    private DiskRow[] Windows() {
        _windows ??= new(Counters.Select(counter => "\\PhysicalDisk(*)\\" + counter));
        var values = _windows.Sample();
        var names = values.Values.SelectMany(data => data.Keys).Where(id => id != "_Total").Distinct().OrderBy(id => id, StringComparer.Ordinal);
        double? Get(int index, string id) => values.TryGetValue("\\PhysicalDisk(*)\\" + Counters[index], out var data) && data.TryGetValue(id, out double value) ? value : null;
        long now = Stopwatch.GetTimestamp();
        bool refresh = _temperatureTime == 0 || (double)(now - _temperatureTime) / Stopwatch.Frequency >= 5;
        if (refresh) { _temperatures.Clear(); _temperatureTime = now; }
        return names.Select(id => {
            bool physical = int.TryParse(id.Split(' ')[0], out int number);
            if (refresh && physical) { _temperatures[id] = DiskLayout.WindowsTemperature(number); }
            return new DiskRow(physical ? "disk:" + number : "pdh:" + id, "Disk " + id, "Device", ReadBytes: Get(0, id), WriteBytes: Get(1, id),
            ReadIops: Get(2, id), WriteIops: Get(3, id), Busy: Get(4, id) is { } idle ? Compat.Clamp(100 - idle, 0, 100) : null,
            ReadLatency: Get(2, id) > 0 ? Get(5, id) * 1000 : null, WriteLatency: Get(3, id) > 0 ? Get(6, id) * 1000 : null,
            Queue: Get(7, id), Temperature: _temperatures.TryGetValue(id, out var temperature) ? temperature : null,
            Status: "Windows PhysicalDisk PDH; queue is instantaneous; missing/first-sample counters unknown");
        }).ToArray();
    }
    private DiskRow[] Mac(double seconds) {
        try {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-a -r -d 1 -w 0 -c IOBlockStorageDriver") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null) { return []; }
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(1500)) { process.Kill(); return []; }
            if (!output.Wait(500) || !errors.Wait(500) || process.ExitCode != 0) { return []; }
            return ParseMac(output.Result, seconds);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or AggregateException) { return []; }
    }
    internal DiskRow[] ParseMac(string xml, double seconds) {
        try {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
            var rows = new List<DiskRow>();
            var active = new HashSet<string>();
            foreach (var entry in XDocument.Load(reader).Descendants("dict")) {
                XElement? Property(string key) => entry.Elements("key").FirstOrDefault(element => element.Value == key)?.ElementsAfterSelf().FirstOrDefault();
                if (Property("Statistics") is not { } stats || Property("IORegistryEntryID")?.Value is not { } id) { continue; }
                ulong? Get(string key) => ulong.TryParse(stats.Elements("key").FirstOrDefault(element => element.Value == key)?.ElementsAfterSelf().FirstOrDefault()?.Value, out ulong value) ? value : null;
                if (Get("Operations (Read)") is not { } reads || Get("Operations (Write)") is not { } writes
                    || Get("Bytes (Read)") is not { } readBytes || Get("Bytes (Write)") is not { } writeBytes) { continue; }
                // IORegistry's IOBlockStorageDriver time counters are nanoseconds.
                ulong[] ticks = [reads, 0, readBytes, Get("Total Time (Read)") ?? 0, writes, 0, writeBytes, Get("Total Time (Write)") ?? 0, 0, 0, 0];
                _macTimes.TryGetValue(id, out var previousTimes);
                var row = Delta("ioreg:" + id, "Storage " + id, ticks, seconds);
                _macTimes[id] = (Get("Total Time (Read)").HasValue, Get("Total Time (Write)").HasValue);
                rows.Add(row with {
                    ReadBytes = row.ReadBytes / 512,
                    WriteBytes = row.WriteBytes / 512,
                    ReadLatency = previousTimes.Read && Get("Total Time (Read)").HasValue ? row.ReadLatency / 1_000_000 : null,
                    WriteLatency = previousTimes.Write && Get("Total Time (Write)").HasValue ? row.WriteLatency / 1_000_000 : null,
                    Busy = null,
                    Queue = null,
                    Status = "IOBlockStorageDriver; latency is service time per operation; busy/queue unavailable; registry identity is not a mount mapping"
                });
                active.Add("ioreg:" + id);
            }
            foreach (string id in _previous.Keys.Where(id => !active.Contains(id)).ToArray()) { _previous.Remove(id); }
            foreach (string id in _macTimes.Keys.Where(id => !active.Contains("ioreg:" + id)).ToArray()) { _macTimes.Remove(id); }
            return rows.ToArray();
        }
        catch (XmlException) { _previous.Clear(); _macTimes.Clear(); return []; }
    }
    public void Dispose() => _windows?.Dispose();
}
