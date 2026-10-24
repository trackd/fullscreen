using System.Text;

namespace fullscreen.Monitoring;
/// <summary>Versioned wire format. Only populated rows cross PSRP; no TUI dependencies or deserialized CLR types.</summary>
public static class FrameCodec {
    private const int Magic = 0x46534D37;
    /// <summary>Serializes the active portions of a collector frame.</summary>
    /// <param name="frame">The reusable collector frame to encode or populate.</param>
    /// <returns>The encoded frame bytes.</returns>
    public static byte[] Encode(CollectorFrame frame) {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(frame.MachineName);
        writer.Write(frame.Platform);
        writer.Write(frame.CapturedAt.UtcDateTime.Ticks);
        Write(writer, frame.CpuPercent);
        Write(writer, frame.TotalMemory);
        Write(writer, frame.AvailableMemory);
        writer.Write(frame.SampleMilliseconds);
        writer.Write(frame.SampleAllocation);
        Write(writer, frame.ReceiveBytesPerSecond);
        Write(writer, frame.SendBytesPerSecond);
        writer.Write(frame.GpuStatus);
        writer.Write(frame.NetworkStatus);
        writer.Write(frame.ProcessCount);
        for (int i = 0; i < frame.ProcessCount; i++) {
            var row = frame.Processes[i];
            writer.Write(row.Id);
            writer.Write(row.Name);
            writer.Write(row.CpuPercent);
            writer.Write(row.WorkingSet);
            writer.Write(row.Threads);
            Write(writer, row.GpuPercent);
            Write(writer, row.GpuMemory);
            writer.Write(row.ParentId ?? -1);
            writer.Write(row.UserName ?? "");
            writer.Write(row.StartTimeTicks);
            writer.Write(row.CpuTimeTicks);
            writer.Write(row.Status ?? "");
            Write(writer, row.DiskBytesPerSecond);
            Write(writer, row.NetworkBytesPerSecond);
        }

        writer.Write(frame.NetworkCount);
        for (int i = 0; i < frame.NetworkCount; i++) {
            var row = frame.Networks[i];
            writer.Write(row.Id);
            writer.Write(row.Name);
            Write(writer, row.ReceiveBytesPerSecond);
            Write(writer, row.SendBytesPerSecond);
            writer.Write(row.ReceivedBytes);
            writer.Write(row.SentBytes);
            writer.Write(row.Errors);
            writer.Write(row.Speed);
        }

        writer.Write(frame.GpuCount);
        for (int i = 0; i < frame.GpuCount; i++) {
            var row = frame.Gpus[i];
            writer.Write(row.Name);
            writer.Write(row.Provider);
            Write(writer, row.Utilization);
            Write(writer, row.UsedMemory);
            Write(writer, row.TotalMemory);
            Write(writer, row.Temperature);
        }

        writer.Write(frame.ServiceStatus);
        writer.Write(frame.ServiceCount);
        for (int i = 0; i < frame.ServiceCount; i++) {
            var row = frame.Services[i];
            writer.Write(row.Name);
            writer.Write(row.Description);
            writer.Write(row.State);
            writer.Write(row.Detail);
            writer.Write(row.ProcessId ?? -1);
            writer.Write(row.UserName ?? "");
        }

        writer.Write(frame.UserStatus);
        writer.Write(frame.UserCount);
        for (int i = 0; i < frame.UserCount; i++) {
            var row = frame.Users[i];
            writer.Write(row.UserName);
            writer.Write(row.Session);
            writer.Write(row.State);
            writer.Write(row.Host);
            writer.Write(row.LoginTime);
            writer.Write(row.SessionId ?? -1);
            WriteTime(writer, row.LogonTime);
            writer.Write(row.IdleTime?.Ticks ?? -1);
            WriteTime(writer, row.LastInputTime);
            WriteTime(writer, row.ConnectTime);
            WriteTime(writer, row.DisconnectTime);
            writer.Write(row.ClientName);
            writer.Write(row.Protocol);
        }

        return stream.ToArray();
    }

    /// <summary>Decodes transported frame data into reusable destination buffers.</summary>
    /// <param name="bytes">The binary frame produced by Encode.</param>
    /// <param name="frame">The reusable collector frame to encode or populate.</param>
    public static void Decode(byte[] bytes, CollectorFrame frame) {
        if (bytes.Length > 64 * 1024 * 1024) {
            throw new InvalidDataException("Snapshot exceeds 64 MiB.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadInt32() != Magic) {
            throw new InvalidDataException("Unsupported Fullscreen collector protocol.");
        }

        frame.MachineName = reader.ReadString();
        frame.Platform = reader.ReadString();
        frame.CapturedAt = new(reader.ReadInt64(), TimeSpan.Zero);
        frame.CpuPercent = ReadDouble(reader);
        frame.TotalMemory = ReadUnsigned(reader);
        frame.AvailableMemory = ReadUnsigned(reader);
        frame.SampleMilliseconds = reader.ReadDouble();
        frame.SampleAllocation = reader.ReadInt64();
        frame.ReceiveBytesPerSecond = ReadDouble(reader);
        frame.SendBytesPerSecond = ReadDouble(reader);
        frame.GpuStatus = reader.ReadString();
        frame.NetworkStatus = reader.ReadString();
        frame.ProcessCount = ReadCount(reader, 100000);
        frame.Processes = CollectorFrame.EnsureCapacity(frame.Processes, frame.ProcessCount);
        for (int i = 0; i < frame.ProcessCount; i++) {
            int id = reader.ReadInt32();
            string name = reader.ReadString();
            double cpu = reader.ReadDouble();
            long memory = reader.ReadInt64();
            int threads = reader.ReadInt32();
            double? gpu = ReadDouble(reader);
            ulong? gpuMemory = ReadUnsigned(reader);
            int parent = reader.ReadInt32();
            string owner = reader.ReadString();
            long birth = reader.ReadInt64();
            long cpuTime = reader.ReadInt64();
            string status = reader.ReadString();
            double? disk = ReadDouble(reader), network = ReadDouble(reader);
            frame.Processes[i] = new(
                id,
                name,
                cpu,
                memory,
                threads,
                gpu,
                gpuMemory,
                parent < 0 ? null : parent,
                owner.Length == 0 ? null : owner,
                birth,
                cpuTime,
                status.Length == 0 ? null : status,
                disk,
                network
            );
        }

        frame.NetworkCount = ReadCount(reader, 4096);
        frame.Networks = CollectorFrame.EnsureCapacity(frame.Networks, frame.NetworkCount);
        for (int i = 0; i < frame.NetworkCount; i++) {
            frame.Networks[i] = new(
                reader.ReadString(),
                reader.ReadString(),
                ReadDouble(reader),
                ReadDouble(reader),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64()
            );
        }

        frame.GpuCount = ReadCount(reader, 256);
        frame.Gpus = CollectorFrame.EnsureCapacity(frame.Gpus, frame.GpuCount);
        for (int i = 0; i < frame.GpuCount; i++) {
            frame.Gpus[i] = new(
                reader.ReadString(),
                reader.ReadString(),
                ReadDouble(reader),
                ReadUnsigned(reader),
                ReadUnsigned(reader),
                ReadDouble(reader)
            );
        }

        frame.ServiceStatus = reader.ReadString();
        frame.ServiceCount = ReadCount(reader, 100000);
        frame.Services = CollectorFrame.EnsureCapacity(frame.Services, frame.ServiceCount);
        for (int i = 0; i < frame.ServiceCount; i++) {
            string name = reader.ReadString(), description = reader.ReadString(), state = reader.ReadString(), detail = reader.ReadString();
            int processId = reader.ReadInt32();
            string account = reader.ReadString();
            frame.Services[i] = new(
                name,
                description,
                state,
                detail,
                processId < 0 ? null : processId,
                account.Length == 0 ? null : account
            );
        }

        frame.UserStatus = reader.ReadString();
        frame.UserCount = ReadCount(reader, 100000);
        frame.Users = CollectorFrame.EnsureCapacity(frame.Users, frame.UserCount);
        for (int i = 0; i < frame.UserCount; i++) {
            string user = reader.ReadString(), session = reader.ReadString(), state = reader.ReadString(), host = reader.ReadString(), login = reader.ReadString();
            int id = reader.ReadInt32();
            var logon = ReadTime(reader);
            long idle = reader.ReadInt64();
            frame.Users[i] = new(
                user,
                session,
                state,
                host,
                login,
                id < 0 ? null : id,
                logon,
                idle < 0 ? null : TimeSpan.FromTicks(idle),
                ReadTime(reader),
                ReadTime(reader),
                ReadTime(reader),
                reader.ReadString(),
                reader.ReadString()
            );
        }

        if (stream.Position != stream.Length) {
            throw new InvalidDataException("Unexpected trailing snapshot data.");
        }
    }

    private static void WriteTime(
        BinaryWriter writer,
        DateTimeOffset? value
    ) =>
        writer.Write(value?.UtcDateTime.Ticks ?? -1);
    private static DateTimeOffset? ReadTime(BinaryReader reader) {
        long ticks = reader.ReadInt64();
        return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static int ReadCount(BinaryReader reader, int max) {
        int count = reader.ReadInt32();
        if (count < 0 || count > max) {
            throw new InvalidDataException("Invalid snapshot row count.");
        }

        return count;
    }

    private static void Write(BinaryWriter writer, double? value) =>
        writer.Write(value ?? double.NaN);
    private static void Write(BinaryWriter writer, ulong? value) =>
        writer.Write(value ?? ulong.MaxValue);
    private static double? ReadDouble(BinaryReader reader) {
        double value = reader.ReadDouble();
        return Compat.IsFinite(value) ? value : null;
    }

    private static ulong? ReadUnsigned(BinaryReader reader) {
        ulong value = reader.ReadUInt64();
        return value == ulong.MaxValue ? null : value;
    }
}
