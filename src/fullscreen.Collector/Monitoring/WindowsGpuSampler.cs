using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace fullscreen.Monitoring;

internal sealed partial class WindowsGpuSampler : IDisposable {
    private readonly SafeQuery? _query;
    private readonly bool _collectProcesses, _collectDevices;
    private readonly nint _engine, _memory, _adapterMemory;
    private byte[] _buffer = new byte[256 * 1024];
    private readonly Dictionary<int, double> _processUse = [];
    private readonly Dictionary<int, ulong> _processMemory = [];
    private readonly Dictionary<ulong, double> _engines = [];
    public double? Utilization { get; private set; }
    public ulong? DedicatedMemory { get; private set; }
    internal CollectorCapabilities Capabilities { get; private set; } =
        CollectorCapabilities.ProcessGraphics | CollectorCapabilities.GpuUtilization | CollectorCapabilities.GpuUsedMemory;
    internal static bool MissingCounter(int status) =>
        status is unchecked((int)0xC0000BB8) or unchecked((int)0xC0000BB9);

    public WindowsGpuSampler(bool processes = true, bool devices = true) {
        _collectProcesses = processes;
        _collectDevices = devices;
        if (PdhOpenQueryW(null, 0, out var query) != 0) {
            query?.Dispose();
            return;
        }

        _query = query;
        int engineStatus = PdhAddEnglishCounterW(query, "\\GPU Engine(*)\\Utilization Percentage", 0, out _engine);
        if (engineStatus != 0) {
            _engine = 0;
            if (MissingCounter(engineStatus)) {
                Capabilities &= ~(CollectorCapabilities.ProcessGpu | CollectorCapabilities.GpuUtilization);
            }
        }

        if (processes) {
            int memoryStatus = PdhAddEnglishCounterW(query, "\\GPU Process Memory(*)\\Dedicated Usage", 0, out _memory);
            if (memoryStatus != 0) {
                _memory = 0;
                if (MissingCounter(memoryStatus)) {
                    Capabilities &= ~CollectorCapabilities.ProcessGpuMemory;
                }
            }
        }

        if (devices) {
            int adapterStatus = PdhAddEnglishCounterW(query, "\\GPU Adapter Memory(*)\\Dedicated Usage", 0, out _adapterMemory);
            if (adapterStatus != 0) {
                _adapterMemory = 0;
                if (MissingCounter(adapterStatus)) {
                    Capabilities &= ~CollectorCapabilities.GpuUsedMemory;
                }
            }
        }

        _ = PdhCollectQueryData(query);
    }

    public double? ProcessUtilization(int id) =>
        _processUse.TryGetValue(id, out double value) ? value : null;
    public ulong? ProcessMemory(int id) =>
        _processMemory.TryGetValue(id, out ulong value) ? value : null;
    public void Sample(bool processes = true) {
        _processUse.Clear();
        _processMemory.Clear();
        _engines.Clear();
        Utilization = null;
        DedicatedMemory = null;
        if (_query is null || PdhCollectQueryData(_query) != 0) {
            return;
        }

        Read(_engine, 0);
        if (processes) {
            Read(_memory, 1);
        }

        Read(_adapterMemory, 2);
        foreach (double value in _engines.Values) {
            Utilization = Compat.Clamp(Math.Max(Utilization ?? 0, value), 0, 100);
        }
    }

    private unsafe void Read(nint counter, int kind) {
        if (counter == 0) {
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++) {
            uint size = (uint)_buffer.Length;
            fixed (byte* pointer = _buffer) {
                int status = PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, out uint count, pointer);
                if (status == unchecked((int)0x800007D2)) {
                    Array.Resize(ref _buffer, Math.Max(checked((int)size + 4096), _buffer.Length * 2));
                    continue;
                }

                if (status != 0 || count > _buffer.Length / sizeof(Item)) {
                    return;
                }

                for (uint i = 0; i < count; i++) {
                    Item item = ((Item*)pointer)[i];
                    if (item.Value.Status > 1 || !Compat.IsFinite(item.Value.Number) || item.Value.Number < 0) {
                        continue;
                    }

                    nuint offset = (nuint)item.Name - (nuint)pointer;
                    if (offset >= (nuint)_buffer.Length) {
                        continue;
                    }

                    char* name = (char*)item.Name;
                    int capacity = (_buffer.Length - (int)offset) / 2, end = 0;
                    while (end < capacity && name[end] != '\0') {
                        end++;
                    }

                    if (end == capacity) {
                        continue;
                    }

                    if (kind == 2) {
                        DedicatedMemory = (DedicatedMemory ?? 0) + (ulong)item.Value.Number;
                        continue;
                    }

                    if (!TryGetPid(name, end, out int pid)) {
                        continue;
                    }

                    if (kind == 1) {
                        _processMemory.TryGetValue(pid, out ulong old);
                        _processMemory[pid] = old + (ulong)item.Value.Number;
                    }
                    else {
                        if (_collectProcesses) {
                            _processUse.TryGetValue(pid, out double old);
                            _processUse[pid] = Compat.Clamp(Math.Max(old, item.Value.Number), 0, 100);
                        }

                        if (!_collectDevices) {
                            continue;
                        }

                        int keyStart = -1;
                        for (int n = 0; n <= end - 6; n++) {
                            if (name[n] == '_' && name[n + 1] == 'l' && name[n + 2] == 'u' && name[n + 3] == 'i'
                                && name[n + 4] == 'd'
                                && name[n + 5] == '_') {
                                keyStart = n;
                                break;
                            }
                        }

                        if (keyStart < 0) {
                            continue;
                        }

                        ulong key = 14695981039346656037;
                        for (int n = keyStart; n < end; n++) {
                            key = (key ^ name[n]) * 1099511628211;
                        }

                        _engines.TryGetValue(key, out double usage);
                        _engines[key] = usage + item.Value.Number;
                    }
                }

                return;
            }
        }
    }

    internal static unsafe bool TryGetPid(char* name, int length, out int pid) {
        pid = 0;
        if (length < 6 || name[0] != 'p' || name[1] != 'i' || name[2] != 'd' || name[3] != '_') {
            return false;
        }

        int i = 4;
        for (; i < length && name[i] != '_'; i++) {
            int digit = name[i] - '0';
            if (digit < 0 || digit > 9 || pid > (int.MaxValue - digit) / 10) {
                return false;
            }

            pid = pid * 10 + digit;
        }

        return i > 4 && i < length;
    }

    public void Dispose() =>
        _query?.Dispose();
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Value {
        [FieldOffset(0)]
        public uint Status;
        [FieldOffset(8)]
        public double Number;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Item {
        public nint Name;
        public Value Value;
    }

    private sealed class SafeQuery : SafeHandleZeroOrMinusOneIsInvalid {
        public SafeQuery() : base(true) {
        }

        protected override bool ReleaseHandle() =>
            PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? source, nuint userData, out SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(SafeQuery query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(SafeQuery query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static unsafe extern int PdhGetFormattedCounterArrayW(
        nint counter,
        uint format,
        ref uint size,
        out uint count,
        void* buffer
    );
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(nint query);
}
