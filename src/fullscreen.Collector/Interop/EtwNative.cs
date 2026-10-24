using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;
// Windows SDK evntrace.h / evntcons.h. TRACEHANDLE is always 64 bits, not HANDLE.
// The consumer is used only in Windows x64/arm64 processes.
internal static unsafe partial class EtwNative {
    [StructLayout(LayoutKind.Sequential)]
    internal struct WnodeHeader {
        public uint BufferSize, ProviderId;
        public ulong HistoricalContext;
        public long Timestamp;
        public Guid Guid;
        public uint ClientContext, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TraceProperties {
        public WnodeHeader Wnode;
        public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public nint LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventDescriptor {
        public ushort Id;
        public byte Version, Channel, Level, Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventHeader {
        public ushort Size, HeaderType, Flags, EventProperty;
        public uint ThreadId, ProcessId;
        public long Timestamp;
        public Guid ProviderId;
        public EventDescriptor Descriptor;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventRecord {
        public EventHeader Header;
        public uint BufferContext;
        public ushort ExtendedDataCount, UserDataLength;
        public nint ExtendedData;
        public byte* UserData;
        public nint UserContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventTraceHeader {
        public ushort Size, FieldTypeFlags;
        public uint Version, ThreadId, ProcessId;
        public long Timestamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventTrace {
        public EventTraceHeader Header;
        public uint InstanceId, ParentInstanceId;
        public Guid ParentGuid;
        public nint MofData;
        public uint MofLength, ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TimeZoneInformation {
        public int Bias;
        public fixed char StandardName[32];
        public SystemTime StandardDate;
        public int StandardBias;
        public fixed char DaylightName[32];
        public SystemTime DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LogfileHeader {
        public uint BufferSize, Version, ProviderVersion, NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
        public Guid LogInstanceGuid;
        public nint LoggerName, LogFileName;
        public TimeZoneInformation TimeZone;
        public long BootTime, PerfFreq, StartTime;
        public uint ReservedFlags, BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TraceLogfile {
        public char* LogFileName;
        public char* LoggerName;
        public long CurrentTime;
        public uint BuffersRead, ProcessTraceMode;
        public EventTrace CurrentEvent;
        public LogfileHeader Header;
        public nint BufferCallback;
        public uint BufferSize, Filled, EventsLost;
        public nint EventRecordCallback;
        public uint IsKernelTrace;
        public nint Context;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void EventCallback(EventRecord* record);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint StartTrace(out ulong handle, string name, TraceProperties* properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", ExactSpelling = true)]
    internal static extern uint ControlTrace(ulong handle, char* name, TraceProperties* properties, uint control);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "QueryAllTracesW", ExactSpelling = true)]
    internal static extern uint QueryAllTraces(TraceProperties** properties, uint count, out uint loggers);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true, ExactSpelling = true)]
    internal static extern ulong OpenTrace(ref TraceLogfile logfile);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", ExactSpelling = true)]
    internal static extern uint ProcessTrace(ulong* handles, uint count, long* start, long* end);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", ExactSpelling = true)]
    internal static extern uint CloseTrace(ulong handle);
}
