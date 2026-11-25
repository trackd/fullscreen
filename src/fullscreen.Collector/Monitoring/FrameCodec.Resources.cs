namespace fullscreen.Monitoring;

public static partial class FrameCodec {
    private static void WriteResources(BinaryWriter writer, CollectorFrame frame) {
        writer.Write(frame.NetworkCount);
        foreach (var network in frame.Networks.Take(frame.NetworkCount)) { writer.Write(network.Addresses); }
        writer.Write(frame.ProcessCount);
        foreach (var process in frame.Processes.Take(frame.ProcessCount)) { Write(writer, process.DiskReadBytesPerSecond); Write(writer, process.DiskWriteBytesPerSecond); }
        writer.Write(frame.GpuCount);
        foreach (var gpu in frame.Gpus.Take(frame.GpuCount)) {
            Write(writer, gpu.MemoryClockMHz);
            Write(writer, gpu.PcieReceiveBytes);
            Write(writer, gpu.PcieSendBytes);
            writer.Write(gpu.PerformanceState ?? -1);
        }
        writer.Write(frame.CpuDetails is not null);
        if (frame.CpuDetails is { } cpu) {
            writer.Write(cpu.Name);
            Write(writer, cpu.UptimeSeconds);
            Write(writer, cpu.Load1);
            Write(writer, cpu.Load5);
            Write(writer, cpu.Load15);
            writer.Write(cpu.PhysicalCores ?? -1);
            writer.Write(cpu.Cores.Length);
            foreach (var core in cpu.Cores) {
                writer.Write(core.Id);
                Write(writer, core.Usage);
                Write(writer, core.User);
                Write(writer, core.System);
                Write(writer, core.Wait);
                Write(writer, core.Steal);
                Write(writer, core.Megahertz);
            }
            writer.Write(cpu.Temperatures.Length);
            foreach (var sensor in cpu.Temperatures) { writer.Write(sensor.Name); writer.Write(sensor.Celsius); }
        }
        writer.Write(frame.MemoryDetails is not null);
        if (frame.MemoryDetails is { } memory) {
            Write(writer, memory.Free);
            Write(writer, memory.Cached);
            Write(writer, memory.SwapTotal);
            Write(writer, memory.SwapUsed);
            Write(writer, memory.Committed);
            Write(writer, memory.CommitLimit);
            Write(writer, memory.Compressed);
            Write(writer, memory.Wired);
            writer.Write(memory.Scope);
            Write(writer, memory.PressureSome10);
            Write(writer, memory.PressureFull10);
        }
        writer.Write(frame.Disks.Length);
        foreach (var disk in frame.Disks) {
            writer.Write(disk.Id);
            writer.Write(disk.Name);
            writer.Write(disk.Kind);
            writer.Write(disk.FileSystem);
            Write(writer, disk.Total);
            Write(writer, disk.Free);
            Write(writer, disk.ReadBytes);
            Write(writer, disk.WriteBytes);
            Write(writer, disk.ReadIops);
            Write(writer, disk.WriteIops);
            Write(writer, disk.Busy);
            Write(writer, disk.ReadLatency);
            Write(writer, disk.WriteLatency);
            Write(writer, disk.Queue);
            Write(writer, disk.Temperature);
            writer.Write(disk.Status);
        }
        foreach (var disk in frame.Disks) {
            writer.Write(disk.Overview);
            writer.Write(disk.Volumes.Length);
            foreach (var volume in disk.Volumes) {
                writer.Write(volume.Id);
                writer.Write(volume.Mount);
                writer.Write(volume.FileSystem);
                writer.Write(volume.Total);
                writer.Write(volume.Available);
            }
        }
    }
    private static void ReadResources(BinaryReader reader, CollectorFrame frame, int version) {
        if (ReadCount(reader, 100000) != frame.NetworkCount) { throw new InvalidDataException("Network extension count mismatch."); }
        for (int index = 0; index < frame.NetworkCount; index++) { frame.Networks[index] = frame.Networks[index] with { Addresses = reader.ReadString() }; }
        if (ReadCount(reader, 100000) != frame.ProcessCount) { throw new InvalidDataException("Process extension count mismatch."); }
        for (int index = 0; index < frame.ProcessCount; index++) { frame.Processes[index] = frame.Processes[index] with { DiskReadBytesPerSecond = ReadDouble(reader), DiskWriteBytesPerSecond = ReadDouble(reader) }; }
        if (ReadCount(reader, 100000) != frame.GpuCount) { throw new InvalidDataException("GPU extension count mismatch."); }
        for (int index = 0; index < frame.GpuCount; index++) {
            double? clock = ReadDouble(reader), rx = ReadDouble(reader), tx = ReadDouble(reader);
            int state = reader.ReadInt32();
            frame.Gpus[index] = frame.Gpus[index] with { MemoryClockMHz = clock, PcieReceiveBytes = rx, PcieSendBytes = tx, PerformanceState = state < 0 ? null : state };
        }
        if (reader.ReadBoolean()) {
            string name = reader.ReadString();
            double? uptime = ReadDouble(reader), load1 = ReadDouble(reader), load5 = ReadDouble(reader), load15 = ReadDouble(reader);
            int physical = reader.ReadInt32();
            var cores = new CpuCore[ReadCount(reader, 65536)];
            for (int index = 0; index < cores.Length; index++) { cores[index] = new(reader.ReadInt32(), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader)); }
            var sensors = new TemperatureReading[ReadCount(reader, 65536)];
            for (int index = 0; index < sensors.Length; index++) { sensors[index] = new(reader.ReadString(), reader.ReadDouble()); }
            frame.CpuDetails = new(name, cores, sensors, uptime, load1, load5, load15, physical < 0 ? null : physical);
        }
        if (reader.ReadBoolean()) { frame.MemoryDetails = new(ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), ReadUnsigned(reader), reader.ReadString(), ReadDouble(reader), ReadDouble(reader)); }
        var disks = new DiskRow[ReadCount(reader, 100000)];
        for (int index = 0; index < disks.Length; index++) {
            disks[index] = new(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadString(), ReadUnsigned(reader), ReadUnsigned(reader),
                ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), ReadDouble(reader), reader.ReadString());
        }
        frame.Disks = disks;
        if (version >= 0x46534D3D) {
            for (int index = 0; index < disks.Length; index++) {
                bool overview = reader.ReadBoolean();
                int count = ReadCount(reader, 100000);
                DiskVolume[] volumes = count == 0 ? [] : new DiskVolume[count];
                for (int volume = 0; volume < count; volume++) { volumes[volume] = new(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadUInt64(), reader.ReadUInt64()); }
                disks[index] = disks[index] with { Overview = overview, Volumes = volumes };
            }
        }
    }
}
