using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

// English PDH paths avoid localized counter names. Missing/disabled counters remain absent.
internal sealed class NativeCounterSet : IDisposable {
    private nint _query;
    private readonly Dictionary<string, nint> _counters = [];
    internal NativeCounterSet(IEnumerable<string> paths) {
        if (PdhOpenQueryW(null, 0, out _query) != 0) {
            return;
        }
        foreach (string path in paths) {
            if (PdhAddEnglishCounterW(_query, path, 0, out nint counter) == 0) {
                _counters[path] = counter;
            }
        }
    }
    internal unsafe Dictionary<string, Dictionary<string, double>> Sample() {
        var result = new Dictionary<string, Dictionary<string, double>>();
        if (_query == 0 || PdhCollectQueryData(_query) != 0) {
            return result;
        }
        foreach (var counter in _counters) {
            uint size = 0;
            _ = PdhGetFormattedCounterArrayW(counter.Value, 0x8200, ref size, out _, null);
            if (size == 0 || size > 16 * 1024 * 1024) {
                continue;
            }
            var buffer = new byte[size];
            fixed (byte* start = buffer) {
                if (PdhGetFormattedCounterArrayW(counter.Value, 0x8200, ref size, out uint count, start) != 0 || count > size / sizeof(Item)) {
                    continue;
                }
                var values = new Dictionary<string, double>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++) {
                    Item item = ((Item*)start)[i];
                    if (item.Value.Status > 1 || !Compat.IsFinite(item.Value.Number) || item.Value.Number < 0) {
                        continue;
                    }
                    nuint offset = (nuint)item.Name - (nuint)start;
                    if (offset >= size) {
                        continue;
                    }
                    int available = ((int)size - (int)offset) / 2, length = 0;
                    char* text = (char*)item.Name;
                    while (length < available && text[length] != '\0') {
                        length++;
                    }
                    if (length == available) {
                        continue;
                    }
                    values[new string(text, 0, length)] = item.Value.Number;
                }
                result[counter.Key] = values;
            }
        }
        return result;
    }
    public void Dispose() {
        if (_query != 0) {
            _ = PdhCloseQuery(_query);
            _query = 0;
        }
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Value {
        [FieldOffset(0)]
        internal uint Status;
        [FieldOffset(8)]
        internal double Number;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Item {
        internal nint Name;
        internal Value Value;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(nint query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern unsafe int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, byte* buffer);
}
