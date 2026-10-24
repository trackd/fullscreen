using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;
/// <summary>A real-time system logger with no files, symbol loading or general-purpose event decoding.</summary>
internal sealed unsafe class WindowsKernelTrace : IDisposable {
    private const string Prefix = "Fullscreen-IO-";
    private static int s_sequence;
    private readonly Controller _controller;
    private readonly EtwNative.EventCallback _eventCallback = OnEvent;
    private readonly Consumer? _consumer;
    private GCHandle _context;
    private readonly Task? _reader;
    private readonly EventHandler _onExit;
    private int _running;
    private int _disposed;
    internal string Name { get; }
    internal bool Healthy => Volatile.Read(ref _running) != 0;

    internal WindowsKernelTrace(EtwIoCounters counters, bool disk, bool network) {
        if (!Platform.IsWindows() || !Environment.Is64BitProcess) {
            throw new PlatformNotSupportedException();
        }

        Name = $"{Prefix}{Compat.ProcessId}-{Interlocked.Increment(ref s_sequence)}";
        StopOrphans();
        _controller = Controller.Start(Name, disk, network);
        // .NET runs no finalizers at exit, so an undisposed session would outlive pwsh.
        _onExit = (_, _) => Dispose();
        AppDomain.CurrentDomain.ProcessExit += _onExit;
        try {
            _context = GCHandle.Alloc(counters);
            var logfile = new EtwNative.TraceLogfile {
                LoggerName = _controller.LoggerName,
                // ProcessTrace converts QPC to FILETIME. Do not set RAW_TIMESTAMP.
                ProcessTraceMode = 0x10000100, // EVENT_RECORD | REAL_TIME
                EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_eventCallback),
                Context = GCHandle.ToIntPtr(_context)
            };
            ulong handle = EtwNative.OpenTrace(ref logfile);
            if (handle == ulong.MaxValue) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _consumer = new Consumer(handle);
            Volatile.Write(ref _running, 1);
            // Root callbacks/context until ProcessTrace has actually returned.
            _reader = Task.Factory.StartNew(Read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch {
            Dispose();
            throw;
        }
    }

    private void Read() {
        try {
            ulong handle = _consumer!.TraceHandle;
            _ = EtwNative.ProcessTrace(&handle, 1, null, null);
        }
        finally {
            Volatile.Write(ref _running, 0);
        }
    }

    private static void OnEvent(EtwNative.EventRecord* record) {
        if (record == null || record->UserContext == 0) {
            return;
        }

        var counters = (EtwIoCounters)GCHandle.FromIntPtr(record->UserContext).Target!;
        try {
            counters.Accept(
                record->Header.ProviderId,
                record->Header.Descriptor.Opcode,
                record->Header.Descriptor.Version,
                record->Header.Flags,
                record->Header.ProcessId,
                record->Header.ThreadId,
                record->Header.Timestamp,
                record->UserData == null ? default : new ByteWindow(record->UserData, record->UserDataLength)
            );
        }
        catch (Exception) {
            counters.Fault();
        } // Exceptions must never cross the native callback boundary.
    }

    internal bool TryGetLoss(out ulong lost) =>
        _controller.TryGetLoss(out lost);
    /// <summary>System loggers outlive a crashed process and Windows allows only a few; stop ours whose owner is gone.</summary>
    private static void StopOrphans() {
        const int Count = 64, NameChars = 1024;
        uint each = (uint)(sizeof(EtwNative.TraceProperties) + 2 * NameChars * sizeof(char));
        var block = (byte*)NativeAllocation.AllocZeroed(each * Count);
        try {
            var sessions = stackalloc EtwNative.TraceProperties*[Count];
            for (int i = 0; i < Count; i++) {
                var properties = (EtwNative.TraceProperties*)(block + i * each);
                properties->Wnode.BufferSize = each;
                properties->LoggerNameOffset = (uint)sizeof(EtwNative.TraceProperties);
                properties->LogFileNameOffset = (uint)(sizeof(EtwNative.TraceProperties) + NameChars * sizeof(char));
                sessions[i] = properties;
            }

            uint status = EtwNative.QueryAllTraces(sessions, Count, out uint found);
            if (status is not (0 or 234)) { // ERROR_MORE_DATA still fills the first Count sessions.
                return;
            }

            for (int i = 0; i < Math.Min(found, Count); i++) {
                char* name = (char*)((byte*)sessions[i] + sessions[i]->LoggerNameOffset);
                string text = new(name);
                if (!text.StartsWith(Prefix, StringComparison.Ordinal)) {
                    continue;
                }

                string owner = text.Substring(Prefix.Length, Math.Max(0, text.IndexOf('-', Prefix.Length) - Prefix.Length));
                if (int.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && !Running(pid)) {
                    _ = EtwNative.ControlTrace(0, name, sessions[i], 1);
                }
            }
        }
        finally {
            NativeAllocation.Free(block);
        }
    }

    private static bool Running(int pid) {
        try {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        if (_onExit is not null) {
            AppDomain.CurrentDomain.ProcessExit -= _onExit;
        }

        try {
            _controller.Stop();
            _consumer?.Dispose(); // CloseTrace wakes ProcessTrace, including when Stop failed.
            _reader?.GetAwaiter().GetResult();
        }
        finally {
            if (_context.IsAllocated) {
                _context.Free();
            }

            _controller.Dispose();
            GC.KeepAlive(_eventCallback);
        }
    }

    private sealed class Consumer : SafeHandle {
        internal Consumer(ulong value) : base(new IntPtr(-1), true) =>
            SetHandle(unchecked((nint)value));
        public override bool IsInvalid => handle == new IntPtr(-1);
        internal ulong TraceHandle => unchecked((ulong)handle);

        protected override bool ReleaseHandle() {
            uint status = EtwNative.CloseTrace(TraceHandle);
            return status is 0 or 7007; // ERROR_CTX_CLOSE_PENDING: ProcessTrace still draining.
        }
    }

    private sealed class Controller : SafeHandle {
        private EtwNative.TraceProperties* _properties;
        private readonly uint _size;
        private bool _stopped;
        private Controller(ulong value, EtwNative.TraceProperties* properties, uint size) : base(IntPtr.Zero, true) {
            SetHandle(unchecked((nint)value));
            _properties = properties;
            _size = size;
        }

        public override bool IsInvalid => handle == IntPtr.Zero;
        internal char* LoggerName => (char*)((byte*)_properties + sizeof(EtwNative.TraceProperties));

        internal static Controller Start(string name, bool disk, bool network) {
            uint size = checked((uint)(sizeof(EtwNative.TraceProperties) + (name.Length + 1) * sizeof(char)));
            var properties = (EtwNative.TraceProperties*)NativeAllocation.AllocZeroed(size);
            try {
                properties->Wnode.BufferSize = size;
                properties->Wnode.Guid = Guid.NewGuid(); // Never use the global kernel logger GUID/name.
                properties->Wnode.ClientContext = 1; // QPC clock
                properties->Wnode.Flags = 0x20000; // WNODE_FLAG_TRACED_GUID
                properties->BufferSize = 64;
                properties->MinimumBuffers = 16;
                properties->MaximumBuffers = 256; // At most 16 MiB of event buffers requested.
                properties->LogFileMode = 0x02000100; // SYSTEM_LOGGER | REAL_TIME, no ETL file
                properties->FlushTimer = 1;
                properties->EnableFlags = (disk ? 0x00000102u : 0) | (network ? 0x00010000u : 0); // THREAD | DISK_IO | NETWORK_TCPIP
                properties->LoggerNameOffset = (uint)sizeof(EtwNative.TraceProperties);
                uint status = EtwNative.StartTrace(out ulong handle, name, properties);
                if (status != 0) {
                    throw new Win32Exception((int)status);
                }

                return new Controller(handle, properties, size);
            }
            catch {
                NativeAllocation.Free(properties);
                throw;
            }
        }

        internal bool TryGetLoss(out ulong lost) {
            lost = 0;
            if (_stopped || IsClosed) {
                return false;
            }

            Prepare();
            if (EtwNative.ControlTrace(unchecked((ulong)handle), null, _properties, 0) != 0) {
                return false;
            }

            lost = (ulong)_properties->EventsLost + _properties->RealTimeBuffersLost;
            return true;
        }

        private void Prepare() {
            _properties->Wnode.BufferSize = _size;
            _properties->LoggerNameOffset = (uint)sizeof(EtwNative.TraceProperties);
            _properties->LogFileNameOffset = 0;
        }

        internal void Stop() {
            if (_stopped) {
                return;
            }

            Prepare();
            uint result = EtwNative.ControlTrace(unchecked((ulong)handle), null, _properties, 1);
            _stopped = result is 0 or 4201; // Already stopped is harmless; never stop another named session.
        }

        protected override bool ReleaseHandle() {
            Stop();
            NativeAllocation.Free(_properties);
            _properties = null;
            return _stopped;
        }
    }
}
