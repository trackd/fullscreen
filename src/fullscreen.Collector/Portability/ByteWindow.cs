using System.Text;

namespace fullscreen.Monitoring;
// Bounded byte access for procfs and native event payloads. This is deliberately
// byte-only: no replacement Span library, pinning ownership, or external runtime dependency.
internal readonly unsafe ref struct ByteWindow {
    private readonly byte[]? _array;
    private readonly byte* _pointer;
    private readonly int _offset;
    public int Length { get; }
    public bool IsEmpty => Length == 0;

    public ByteWindow(byte[] array) : this(array, 0, array.Length) {
    }

    public static implicit operator ByteWindow(byte[] array) =>
        new(array);
    public ByteWindow(byte[] array, int offset, int length) {
        if (offset < 0 || length < 0 || offset > array.Length - length) {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset and length must describe a range within the array.");
        }

        _array = array;
        _offset = offset;
        Length = length;
        _pointer = null;
    }

    public ByteWindow(void* pointer, int length) {
        if (length < 0 || pointer == null && length != 0) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _array = null;
        _offset = 0;
        _pointer = (byte*)pointer;
        Length = length;
    }

    public byte this[int index] {
        get {
            if ((uint)index >= (uint)Length) {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _array is null ? _pointer[index] : _array[_offset + index];
        }
    }

    public ByteWindow this[Range range] {
        get {
            var (offset, length) = range.GetOffsetAndLength(Length);
            return Slice(offset, length);
        }
    }

    public ByteWindow Slice(int offset) =>
        Slice(offset, Length - offset);
    public ByteWindow Slice(int offset, int length) {
        if (offset < 0 || length < 0 || offset > Length - length) {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset and length must describe a range within the window.");
        }

        return _array is null ? new(_pointer + offset, length) : new(_array, _offset + offset, length);
    }

    public int IndexOf(byte value) {
        for (int i = 0; i < Length; i++) {
            if (this[i] == value) {
                return i;
            }
        }

        return -1;
    }

    public int LastIndexOf(byte value) {
        for (int i = Length - 1; i >= 0; i--) {
            if (this[i] == value) {
                return i;
            }
        }

        return -1;
    }

    public bool StartsWith(string ascii) {
        if (Length < ascii.Length) {
            return false;
        }

        for (int i = 0; i < ascii.Length; i++) {
            if (this[i] != ascii[i]) {
                return false;
            }
        }

        return true;
    }

    public ByteWindow TrimStart(byte value) {
        int start = 0;
        while (start < Length && this[start] == value) {
            start++;
        }

        return Slice(start);
    }

    public ByteWindow Trim(byte value) {
        var trimmed = TrimStart(value);
        int end = trimmed.Length;
        while (end > 0 && trimmed[end - 1] == value) {
            end--;
        }

        return trimmed.Slice(0, end);
    }

    public SplitEnumerator Split(byte separator) =>
        new(this, separator);
    public string Utf8() =>
        _array is null ? Encoding.UTF8.GetString(_pointer, Length) : Encoding.UTF8.GetString(_array, _offset, Length);
    public ref struct SplitEnumerator {
        private readonly ByteWindow _data;
        private readonly byte _separator;
        private int _next;
        public ByteWindow Current { get; private set; }

        public SplitEnumerator(ByteWindow data, byte separator) {
            _data = data;
            _separator = separator;
            _next = 0;
            Current = default;
        }

        public readonly SplitEnumerator GetEnumerator() =>
            this;
        public bool MoveNext() {
            if (_next > _data.Length) {
                return false;
            }

            int end = _next;
            while (end < _data.Length && _data[end] != _separator) {
                end++;
            }

            Current = _data.Slice(_next, end - _next);
            _next = end + 1;
            return true;
        }
    }
}

internal static class ByteNumbers {
    internal static bool TryParse(ByteWindow bytes, out ulong value, out int used) {
        value = 0;
        used = 0;
        while (used < bytes.Length && bytes[used] >= '0' && bytes[used] <= '9') {
            uint digit = (uint)(bytes[used] - '0');
            if (value > (ulong.MaxValue - digit) / 10) {
                return false;
            }

            value = value * 10 + digit;
            used++;
        }

        return used > 0;
    }

    internal static bool TryParse(ByteWindow bytes, out uint value, out int used) {
        bool valid = TryParse(bytes, out ulong number, out used);
        value = (uint)number;
        return valid && number <= uint.MaxValue;
    }

    internal static bool TryParse(ByteWindow bytes, out long value, out int used) {
        bool negative = bytes.Length > 0 && bytes[0] == '-';
        bool valid = TryParse(negative ? bytes.Slice(1) : bytes, out ulong number, out used);
        value = 0;
        if (!valid || number > (negative ? 1ul << 63 : long.MaxValue)) {
            return false;
        }

        value = negative ? unchecked(-(long)number) : (long)number;
        if (negative) {
            used++;
        }

        return true;
    }

    internal static uint UInt32(ByteWindow bytes) {
        if (bytes.Length < 4) {
            throw new ArgumentException("Truncated integer.");
        }

        return (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
    }

    internal static ushort UInt16(ByteWindow bytes) {
        if (bytes.Length < 2) {
            throw new ArgumentException("Truncated integer.");
        }

        return (ushort)(bytes[0] | bytes[1] << 8);
    }

    internal static ulong UInt64(ByteWindow bytes) =>
        UInt32(bytes) | (ulong)UInt32(bytes.Slice(4)) << 32;
    internal static long Int64(ByteWindow bytes) =>
        unchecked((long)UInt64(bytes));
}
