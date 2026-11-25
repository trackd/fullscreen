using System.Globalization;
using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private readonly Dictionary<string, TimeSeries> _diskIops = [];
    private readonly TimeSeries _storageRead = new(), _storageWrite = new(), _storageReads = new(), _storageWrites = new(), _storageIops = new();
    internal static (double? Read, double? Write, double? Reads, double? Writes) StorageOverview(IEnumerable<DiskRow> disks) {
        DiskRow[] rows = disks.Where(disk => disk.Kind == "Device" && disk.Overview).ToArray();
        double? Sum(Func<DiskRow, double?> select) {
            return rows.Length > 0 && rows.All(row => select(row).HasValue) ? rows.Sum(row => select(row)!.Value) : null;
        }

        return (Sum(row => row.ReadBytes), Sum(row => row.WriteBytes), Sum(row => row.ReadIops), Sum(row => row.WriteIops));
    }
    private (int Top, int Width, int Height) StorageGraphLayout(int width, int height) =>
        (Minimal ? 0 : 2, Math.Max(12, (width - 3) / 2), width >= 76 && height >= 25 ? Math.Clamp(height / 3, 8, 14) : 0);
    private int DiskRowsTop => StorageGraphLayout(Bounds.Width, Bounds.Height).Top + StorageGraphLayout(Bounds.Width, Bounds.Height).Height + 2;
    private int DiskPage => Math.Max(1, (Bounds.Height - DiskRowsTop - 2) / 5);
    private readonly Dictionary<string, (TimeSeries Receive, TimeSeries Send)> _interfaceHistory = [];
    private string? _interfaceId;
    internal NetworkRow? GraphInterface => Frame.Networks.Take(Frame.NetworkCount).Where(row => row.Id == _interfaceId).Select(row => (NetworkRow?)row).FirstOrDefault();
    private TimeSeries NetworkGraphReceive => GraphInterface is { } network && _interfaceHistory.TryGetValue(network.Id, out (TimeSeries Receive, TimeSeries Send) history) ? history.Receive : _receiveHistory;
    private TimeSeries NetworkGraphSend => GraphInterface is { } network && _interfaceHistory.TryGetValue(network.Id, out (TimeSeries Receive, TimeSeries Send) history) ? history.Send : _sendHistory;
    private void CycleInterface(int direction) {
        string?[] ids = [null, .. Frame.Networks.Take(Frame.NetworkCount).Select(row => row.Id)];
        int index = Math.Max(0, Array.IndexOf(ids, _interfaceId));
        _interfaceId = ids[(index + direction + ids.Length) % ids.Length];
    }
    private void ShowNetworkDetails() {
        NetworkRow? selected = GraphInterface;
        ShowResourceDialog(selected?.Name ?? "Network interfaces", selected is { } network
            ? $"{network.Name}\n{network.Addresses}\nLink {ResourceNumber(network.Speed / 1_000_000d)} Mbit/s\nReceived {ResourceBytes(network.ReceivedBytes)} · sent {ResourceBytes(network.SentBytes)}\nErrors {network.Errors}\n[ / ] selects the graph interface"
            : string.Join("\n\n", Frame.Networks.Take(Frame.NetworkCount).Select(row => row.Name + " · " + row.Addresses)) + "\n[ / ] selects the graph interface");
    }
    private string? _diskSelectedId;
    private int _diskMode;
    private TextBlock? _processLiveText;
    private (int Id, long Birth)? _detailProcess;
    private readonly Queue<double?> _processCpu = new(), _processRam = new();
    internal static string ResourceNumber(double? value, string format = "0.0") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";
    internal static string ResourceBytes(double? bytes) {
        if (!bytes.HasValue) { return "—"; }
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes.Value;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
    private static void Remember(Queue<double?> history, double? value) {
        history.Enqueue(value);
        while (history.Count > 64) { history.Dequeue(); }
    }
    internal static string ResourceSparkline(IEnumerable<double?> values) {
        double?[] samples = [.. values];
        double peak = Math.Max(1, samples.Max() ?? 1);
        const string levels = "▁▂▃▄▅▆▇█";
        return new([.. samples.Select(value => value.HasValue ? levels[(int)Math.Clamp(Math.Round(value.Value / peak * 7), 0, 7)] : '·')]);
    }
    private void UpdateResourceHistory(CollectorFrame frame) {
        DateTimeOffset timestamp = frame.CapturedAt == default ? DateTimeOffset.UtcNow : frame.CapturedAt;
        (double? Read, double? Write, double? Reads, double? Writes) overview = StorageOverview(frame.Disks);
        _storageRead.Add(overview.Read, timestamp);
        _storageWrite.Add(overview.Write, timestamp);
        _storageReads.Add(overview.Reads, timestamp);
        _storageWrites.Add(overview.Writes, timestamp);
        _storageIops.Add(overview.Reads + overview.Writes, timestamp);
        foreach (NetworkRow network in frame.Networks.Take(frame.NetworkCount)) {
            if (!_interfaceHistory.TryGetValue(network.Id, out (TimeSeries Receive, TimeSeries Send) history)) { _interfaceHistory[network.Id] = history = (new(), new()); }
            history.Receive.Add(network.ReceiveBytesPerSecond, timestamp);
            history.Send.Add(network.SendBytesPerSecond, timestamp);
        }
        foreach (string id in _interfaceHistory.Keys.Where(id => !frame.Networks.Take(frame.NetworkCount).Any(network => network.Id == id)).ToArray()) { _interfaceHistory.Remove(id); }
        foreach (DiskRow disk in frame.Disks) {
            if (disk.Kind != "Device") { continue; }
            if (!_diskIops.TryGetValue(disk.Id, out TimeSeries? history)) { _diskIops[disk.Id] = history = new(); }
            history.Add(disk.ReadIops + disk.WriteIops, timestamp);
        }
        if (ProcessTable || View == MonitorView.Disks) {
            foreach (string id in _diskIops.Keys.Where(id => !frame.Disks.Any(disk => disk.Id == id)).ToArray()) { _diskIops.Remove(id); }
        }
        if (_detailProcess is not { } identity || _processLiveText?.App is null) { return; }
        ProcessRow? process = frame.Processes.Take(frame.ProcessCount).Where(row => row.Id == identity.Id && row.StartTimeTicks == identity.Birth).Select(row => (ProcessRow?)row).FirstOrDefault();
        if (process is not { } row) { _processLiveText.Text = "Process exited or identity changed; history stopped."; return; }
        Remember(_processCpu, row.CpuPercent);
        Remember(_processRam, row.WorkingSet);
        _processLiveText.Text = $"CPU {ResourceNumber(row.CpuPercent)}%  {ResourceSparkline(_processCpu)}\nRAM {ResourceBytes(row.WorkingSet)}  {ResourceSparkline(_processRam)}\nLast 64 samples · CPU is share of machine capacity\nDisk read {ResourceBytes(row.DiskReadBytesPerSecond)}/s · write {ResourceBytes(row.DiskWriteBytesPerSecond)}/s";
    }
    private DiskRow[] VisibleDisks => [.. Frame.Disks.Where(disk => Model.Filter.Length == 0 || disk.Name.Contains(Model.Filter, StringComparison.OrdinalIgnoreCase) || disk.FileSystem.Contains(Model.Filter, StringComparison.OrdinalIgnoreCase)
        || disk.Volumes.Any(volume => volume.Mount.Contains(Model.Filter, StringComparison.OrdinalIgnoreCase)))];
    internal DiskRow? SelectedDisk => VisibleDisks.FirstOrDefault(disk => disk.Id == _diskSelectedId) ?? VisibleDisks.FirstOrDefault();
    private void MoveDisk(int delta) {
        DiskRow[] rows = VisibleDisks;
        int index = Math.Max(0, Array.FindIndex(rows, disk => disk.Id == _diskSelectedId));
        if (rows.Length > 0) { _diskSelectedId = rows[(int)Math.Clamp((long)index + delta, 0, rows.Length - 1)].Id; }
    }
    private void SelectDiskAt(int y) {
        DiskRow[] rows = VisibleDisks;
        int selected = Math.Max(0, Array.FindIndex(rows, disk => disk.Id == _diskSelectedId));
        int index = Math.Max(0, selected - DiskPage + 1) + ((y - DiskRowsTop) / 5);
        if (y >= DiskRowsTop && y < DiskRowsTop + (DiskPage * 5) && index >= 0 && index < rows.Length) { _diskSelectedId = rows[index].Id; }
    }
    private bool DiskKey(KeyEventArgs e) {
        DiskRow[] rows = VisibleDisks;
        int? delta = e.Key switch { TerminalKey.Up => -1, TerminalKey.Down => 1, TerminalKey.PageUp => -DiskPage, TerminalKey.PageDown => DiskPage, TerminalKey.Home => -int.MaxValue, TerminalKey.End => int.MaxValue, _ => null };
        if (delta.HasValue && rows.Length > 0) { MoveDisk(delta.Value); }
        else if (e.Key is TerminalKey.Left or TerminalKey.Right) { _diskMode = 1 - _diskMode; }
        else { return false; }
        e.Handled = true;
        Refresh();
        return true;
    }
    private void ShowDiskDetails() {
        if (SelectedDisk is not { } disk) { return; }
        string temperature = disk.Temperature.HasValue ? "\nTemperature " + ResourceNumber(disk.Temperature) + " °C" : "";
        string volumes = string.Join("\n", disk.Volumes.Select(volume => volume.Mount + " · " + volume.FileSystem + " · " + ResourceBytes(volume.Available) + " free / " + ResourceBytes(volume.Total)));
        ShowResourceDialog(disk.Name, $"{disk.Id}\n{disk.Kind} · {disk.FileSystem}\nTotal {ResourceBytes(disk.Total)} · available {ResourceBytes(disk.Free)}\n{volumes}\nRead {ResourceBytes(disk.ReadBytes)}/s · write {ResourceBytes(disk.WriteBytes)}/s\nRead IOPS {ResourceNumber(disk.ReadIops)} · write IOPS {ResourceNumber(disk.WriteIops)}\nRead latency {ResourceNumber(disk.ReadLatency)} ms · write latency {ResourceNumber(disk.WriteLatency)} ms\nBusy {ResourceNumber(disk.Busy)}% · queue {ResourceNumber(disk.Queue)}\n{disk.Status}{temperature}");
    }
    private void RenderDisks(CellBuffer buffer) {
        (int Top, int Width, int Height) = StorageGraphLayout(Bounds.Width, Bounds.Height);
        (double? Read, double? Write, double? Reads, double? Writes) overview = StorageOverview(Frame.Disks);
        if (Height > 0) {
            GraphPanel(buffer, 1, Top, Width, Height, "STORAGE · read / write", _storageRead, 0, overview.Read, Green, true,
                secondary: _storageWrite, primaryLabel: "READ", secondaryLabel: "WRITE");
            GraphPanel(buffer, Width + 2, Top, Bounds.Width - Width - 3, Height, "OPERATIONS · read / write IOPS", _storageReads, 0, overview.Reads, Green, false,
                secondary: _storageWrites, scalar: true, primaryLabel: "READ", secondaryLabel: "WRITE");
        }
        DiskRow[] rows = VisibleDisks;
        int selected = Math.Max(0, Array.FindIndex(rows, disk => disk.Id == _diskSelectedId));
        int offset = Math.Max(0, selected - DiskPage + 1);
        Write(buffer, 1, DiskRowsTop - 1, _editingFilter ? "FILTER › " + Model.Filter : "DRIVES · ↑↓ select · ←→ activity / latency · D details", Accent, Bounds.Width - 2);
        if (rows.Length == 0) {
            Write(buffer, 2, DiskRowsTop + 1, "No drives available or matching this filter", Muted, Bounds.Width - 4);
        }
        for (int index = offset; index < Math.Min(rows.Length, offset + DiskPage); index++) {
            DiskRow disk = rows[index];
            int y = DiskRowsTop + ((index - offset) * 5);
            int half = Math.Max(20, Bounds.Width / 2);
            Box(buffer, 1, y, Bounds.Width - 2, 5, disk.Name, index == selected ? Accent : Muted);
            string mounts = disk.Volumes.Length > 0 ? string.Join(" · ", disk.Volumes.Select(volume => volume.Mount + " (" + volume.FileSystem + ")"))
                : disk.FileSystem.Length > 0 ? disk.FileSystem : "No mounted filesystem";
            Write(buffer, 3, y + 1, mounts, Body, half - 5);
            string capacity = disk.Total.HasValue ? ResourceBytes(disk.Free) + " free / " + ResourceBytes(disk.Total) + " capacity" : "Capacity unavailable";
            if (!disk.Free.HasValue && disk.Total.HasValue) { capacity = ResourceBytes(disk.Total) + " · no filesystem free-space reading"; }
            Write(buffer, half, y + 1, capacity, Green, Bounds.Width - half - 3);
            string io = _diskMode == 0 ? "Read " + ResourceBytes(disk.ReadBytes) + "/s · write " + ResourceBytes(disk.WriteBytes) + "/s"
                : "Latency · read " + ResourceNumber(disk.ReadLatency) + " ms · write " + ResourceNumber(disk.WriteLatency) + " ms";
            Write(buffer, 3, y + 2, io, Body, half - 5);
            string operations = _diskMode == 0 ? "IOPS · read " + ResourceNumber(disk.ReadIops) + " · write " + ResourceNumber(disk.WriteIops)
                : "Busy " + ResourceNumber(disk.Busy) + "% · queue " + ResourceNumber(disk.Queue) + (disk.Temperature.HasValue ? " · " + ResourceNumber(disk.Temperature) + " °C" : "");
            Write(buffer, half, y + 2, operations, Body, Bounds.Width - half - 3);
            if (_diskIops.TryGetValue(disk.Id, out TimeSeries? history)) {
                Write(buffer, 3, y + 3, "Recent IOPS", Purple, 12);
                InspectPlot(buffer, 16, y + 3, Math.Max(1, half - 21), 1, history, Purple);
            }
            if (disk.Total > 0 && disk.Free.HasValue) {
                double used = Math.Clamp((1 - ((double)disk.Free.Value / disk.Total.Value)) * 100, 0, 100);
                Write(buffer, half, y + 3, "Used " + ResourceNumber(used, "0") + "%", Muted, 10);
                int barWidth = Math.Max(1, Math.Min(35, Bounds.Width - half - 14));
                int filled = (int)Math.Round(used / 100 * barWidth);
                Write(buffer, half + 10, y + 3, new string('█', filled), used > 85 ? Hot : Green, barWidth);
                Write(buffer, half + 10 + filled, y + 3, new string('░', barWidth - filled), Muted, barWidth - filled);
            }
        }
        int contributors = Frame.Disks.Count(disk => disk.Kind == "Device" && disk.Overview);
        Write(buffer, 1, Bounds.Height - 2, $"{rows.Length} drives · overview: {contributors} devices · capacity refreshes every 5s · D: filesystem details", Muted, Bounds.Width - 2);
        RenderCompactFooter(buffer);
    }
    private void ShowResourceDialog(string title, string details) {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock(title),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            Height = Math.Max(1, Bounds.Height - 2),
            Content = new VStack(new Button("Close").Click(() => dialog!.Close()), new ScrollViewer(DetailLines(details)))
        };
        ShowMonitorDialog(dialog);
    }
    private void ShowResourceDetails() {
        var text = new System.Text.StringBuilder();
        if (Frame.CpuDetails is { } cpu) {
            text.AppendLine(cpu.Name).AppendLine(CultureInfo.InvariantCulture, $"{cpu.Cores.Length} logical processors · physical cores {cpu.PhysicalCores?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Uptime {ResourceNumber(cpu.UptimeSeconds / 3600)} hours · load {ResourceNumber(cpu.Load1)} / {ResourceNumber(cpu.Load5)} / {ResourceNumber(cpu.Load15)}");
            foreach (TemperatureReading sensor in cpu.Temperatures) { text.AppendLine(sensor.Name + " · " + ResourceNumber(sensor.Celsius) + " °C"); }
            foreach (CpuCore core in cpu.Cores) { text.AppendLine(CultureInfo.InvariantCulture, $"CPU {core.Id}: {ResourceNumber(core.Usage)}% · user {ResourceNumber(core.User)} · system {ResourceNumber(core.System)} · wait {ResourceNumber(core.Wait)} · steal {ResourceNumber(core.Steal)} · {ResourceNumber(core.Megahertz)} MHz"); }
        }
        if (Frame.MemoryDetails is { } memory) {
            text.AppendLine(CultureInfo.InvariantCulture, $"\nRAM total {ResourceBytes(Frame.TotalMemory)} · available {ResourceBytes(Frame.AvailableMemory)}\nFree {ResourceBytes(memory.Free)} · cached {ResourceBytes(memory.Cached)}\nSwap {ResourceBytes(memory.SwapUsed)} / {ResourceBytes(memory.SwapTotal)}\nCommit {ResourceBytes(memory.Committed)} / {ResourceBytes(memory.CommitLimit)}\nCompressed {ResourceBytes(memory.Compressed)} · wired {ResourceBytes(memory.Wired)}\nMemory pressure (10s stalled time): some {ResourceNumber(memory.PressureSome10)}% · full {ResourceNumber(memory.PressureFull10)}%\n{memory.Scope}");
        }
        ShowResourceDialog("CPU and memory details", text.ToString());
    }
    private void ResourceLoadBar(CellBuffer buffer, int x, int y, double percent, int width, Style? tint = null) {
        const string fractions = " ▏▎▍▌▋▊▉█";
        double units = Math.Clamp(percent, 0, 100) * width / 100;
        Style style = tint ?? (percent > 85 ? Hot : Accent);
        if (Panel.TryGetBackground(out Color background)) {
            style = style.WithBackground(Color.Mix(background, style.TryGetForeground(out Color foreground) ? foreground : Palette.Accent, .12f));
        }
        for (int index = 0; index < width; index++) {
            int fraction = (int)Math.Clamp(Math.Round((units - index) * 8), 0, 8);
            Cell(buffer, x + index, y, fractions[fraction], style);
        }
    }
    private int ResourceFooterRows(int width, int upper) {
        if (upper < 9 || Frame.CpuDetails is null && Frame.MemoryDetails is null) {
            return 0;
        }
        int columns = Math.Max(1, (width - 6) / 17);
        int coreRows = ((Frame.CpuDetails?.Cores.Length ?? 0) + columns - 1) / columns;
        return Math.Min(Math.Max(7, coreRows + 4), Math.Min(10, upper - 5));
    }
    private void RenderResourceSummary(CellBuffer buffer, int width, int upper, int top) {
        int footer = ResourceFooterRows(width, upper), start = top + upper - footer;
        int gridHeight = footer - 2;
        if (Frame.CpuDetails is { } cpu) {
            string identity = (cpu.PhysicalCores.HasValue ? cpu.PhysicalCores.Value.ToString(CultureInfo.InvariantCulture) + " cores / " : "") + cpu.Cores.Length.ToString(CultureInfo.InvariantCulture) + " threads";
            Write(buffer, 18, top + 1, identity, Muted, width - 21);
            int gridWidth = width - 4;
            Box(buffer, 3, start, gridWidth, gridHeight, "LOGICAL PROCESSORS", Accent);
            int columns = Math.Max(1, (gridWidth - 2) / 17), cellWidth = (gridWidth - 2) / columns;
            int capacity = columns * Math.Max(0, gridHeight - 2);
            for (int column = 1; column < columns; column++) {
                int divider = 3 + (column * cellWidth);
                Cell(buffer, divider, start, '┬', Muted);
                Cell(buffer, divider, start + gridHeight - 1, '┴', Muted);
                for (int row = 1; row < gridHeight - 1; row++) { Cell(buffer, divider, start + row, '│', Muted); }
            }
            // Keep the heading uninterrupted by the column junctions.
            Write(buffer, 5, start, "LOGICAL PROCESSORS", Accent, gridWidth - 4);
            foreach ((CpuCore? core, int index) in cpu.Cores.Take(capacity).Select((core, index) => (core, index))) {
                int x = 4 + (index % columns * cellWidth), y = start + 1 + (index / columns);
                Write(buffer, x, y, $"CPU {core.Id,-2} {ResourceNumber(core.Usage, "0"),3}%", Accent, cellWidth - 2);
                if (core.Usage.HasValue && cellWidth >= 17) { ResourceLoadBar(buffer, x + 12, y, core.Usage.Value, cellWidth - 14); }
            }
            string temperatures = string.Join(" · ", cpu.Temperatures.Take(2).Select(sensor => sensor.Name.Split('·').Last().Trim().Replace("Package id", "Package", StringComparison.Ordinal) + " " + ResourceNumber(sensor.Celsius) + "°C"));
            string details = cpu.Cores.Length > capacity ? "D: all cores" : "D: details";
            Write(buffer, 3, top + upper - 2, temperatures.Length > 0 ? temperatures + " · " + details : details, Muted, width - 4);
        }
        if (Frame.MemoryDetails is { } memory) {
            int x = width + 4, gridWidth = Bounds.Width - x - 3;
            Box(buffer, x, start, gridWidth, gridHeight, "MEMORY BREAKDOWN", Green);
            (string Label, string Value)[] entries = [
                ("Used", ResourceBytes(Frame.TotalMemory.HasValue && Frame.AvailableMemory.HasValue ? Frame.TotalMemory - Frame.AvailableMemory : null) + " / " + ResourceBytes(Frame.TotalMemory)),
                ("Available", ResourceBytes(Frame.AvailableMemory)),
                ("Free", memory.Free.HasValue ? ResourceBytes(memory.Free) : ""),
                ("Cache", ResourceBytes(memory.Cached)),
                ("Swap", ResourceBytes(memory.SwapUsed) + " / " + ResourceBytes(memory.SwapTotal)),
                ("Commit", ResourceBytes(memory.Committed) + " / " + ResourceBytes(memory.CommitLimit))
            ];
            if (gridHeight < 8) {
                entries = [
                    ("Available", ResourceBytes(Frame.AvailableMemory) + (memory.Free.HasValue ? " / " + ResourceBytes(memory.Free) + " free" : "")),
                    ("Cache", ResourceBytes(memory.Cached)),
                    ("Swap", ResourceBytes(memory.SwapUsed) + " / " + ResourceBytes(memory.SwapTotal)),
                    ("Commit", ResourceBytes(memory.Committed) + " / " + ResourceBytes(memory.CommitLimit))
                ];
                if (gridHeight == 3) {
                    entries = [("Available", ResourceBytes(Frame.AvailableMemory) + " · Cache " + ResourceBytes(memory.Cached))];
                }
            }
            if (gridWidth >= 48 && gridHeight >= 7) {
                static double? Ratio(ulong? used, ulong? total) {
                    return used.HasValue && total is > 0 ? Math.Clamp(100d * used.Value / total.Value, 0, 100) : null;
                }

                ulong? ramUsed = Frame.TotalMemory.HasValue && Frame.AvailableMemory.HasValue ? Frame.TotalMemory - Math.Min(Frame.TotalMemory.Value, Frame.AvailableMemory.Value) : null;
                (string Label, string Value, double? Percent, Style Tint)[] meters = [
                    ("RAM used", ResourceBytes(ramUsed) + " / " + ResourceBytes(Frame.TotalMemory), Ratio(ramUsed, Frame.TotalMemory), Green),
                    ("Available", ResourceBytes(Frame.AvailableMemory) + (memory.Free.HasValue ? " · " + ResourceBytes(memory.Free) + " free" : ""), null, Green),
                    ("Cache", ResourceBytes(memory.Cached), Ratio(memory.Cached, Frame.TotalMemory), Accent),
                    ("Swap", ResourceBytes(memory.SwapUsed) + " / " + ResourceBytes(memory.SwapTotal), Ratio(memory.SwapUsed, memory.SwapTotal), Purple),
                    ("Commit", ResourceBytes(memory.Committed) + " / " + ResourceBytes(memory.CommitLimit), Ratio(memory.Committed, memory.CommitLimit), Green)
                ];
                int barWidth = Math.Clamp(gridWidth - 40, 6, 16), barX = x + gridWidth - barWidth - 7;
                for (int row = 0; row < Math.Min(meters.Length, gridHeight - 2); row++) {
                    (string? Label, string? Value, double? Percent, Style Tint) = meters[row];
                    int y = start + 1 + row;
                    Write(buffer, x + 2, y, Label, Muted, 10);
                    Write(buffer, x + 13, y, Value, Tint, Percent.HasValue ? barX - x - 14 : gridWidth - 15);
                    if (Percent is { } percent) {
                        ResourceLoadBar(buffer, barX, y, percent, barWidth, percent > 90 ? Hot : Tint);
                        Write(buffer, barX + barWidth + 1, y, percent.ToString("0", CultureInfo.InvariantCulture).PadLeft(3) + "%", Tint, 4);
                    }
                }
                Write(buffer, x, top + upper - 2, "D: memory details", Muted, gridWidth);
                return;
            }
            int divider = x + Math.Min(12, gridWidth / 3);
            Cell(buffer, divider, start + gridHeight - 1, '┴', Muted);
            for (int row = 1; row < gridHeight - 1; row++) { Cell(buffer, divider, start + row, '│', Muted); }
            int index = 0;
            foreach ((string? Label, string? Value) in entries.Where(entry => entry.Value.Length > 0).Take(Math.Max(0, gridHeight - 2))) {
                int y = start + 1 + index++;
                Write(buffer, x + 2, y, Label, Muted, divider - x - 3);
                Write(buffer, divider + 2, y, Value, Green, x + gridWidth - divider - 4);
            }
        }
    }
}
