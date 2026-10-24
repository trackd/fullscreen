using System.ComponentModel;
using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

public sealed partial class ProcessSampler {
    private byte[] _nativeBuffer = new byte[256 * 1024];
    private unsafe void SampleWindows(double seconds) {
        // SYSTEM_PROCESS_INFORMATION, Windows x64/arm64 ABI. No process handles or thread objects.
        // Keep this buffer pinned only while the OS pointers into it are being consumed.
        for (int attempt = 0; attempt < 8; attempt++) {
            fixed (byte* start = _nativeBuffer) {
                int status = NtQuerySystemInformation(5, start, (uint)_nativeBuffer.Length, out uint required);
                if (status == unchecked((int)0xC0000004)) {
                    Array.Resize(ref _nativeBuffer, checked((int)Math.Max(required + 65536L, _nativeBuffer.Length * 2L)));
                    continue;
                }

                if (status < 0) {
                    throw new Win32Exception($"NtQuerySystemInformation failed (0x{status:X8}).");
                }

                if (required > _nativeBuffer.Length) {
                    throw new InvalidDataException("Invalid native snapshot size.");
                }

                int offset = 0;
                while (offset <= (int)required - 152) {
                    var record = new ByteWindow(start + offset, (int)required - offset);
                    uint next = ByteNumbers.UInt32(record);
                    int threads = checked((int)ByteNumbers.UInt32(record[4..]));
                    long birth = ByteNumbers.Int64(record[32..]);
                    long cpu = ByteNumbers.Int64(record[40..]) + ByteNumbers.Int64(record[48..]);
                    long nativeId = ByteNumbers.Int64(record[80..]);
                    if (nativeId is > 0 and <= int.MaxValue) {
                        int id = (int)nativeId;
                        string name;
                        if (_previous.TryGetValue(id, out var previous) && previous.StartTicks == birth) {
                            name = previous.Name;
                        }
                        else {
                            ushort length = ByteNumbers.UInt16(record[56..]);
                            nuint pointer = (nuint)ByteNumbers.UInt64(record[64..]);
                            nuint begin = (nuint)start;
                            if ((length & 1) != 0 || pointer < begin || pointer - begin > required
                                || length > required - (pointer - begin)) {
                                name = "System";
                            }
                            else {
                                name = new string((char*)pointer, 0, length / 2);
                            }
                        }

                        long rss = checked((long)ByteNumbers.UInt64(record[144..]));
                        long parent = ByteNumbers.Int64(record[88..]);
                        int recordLength = next == 0 ? record.Length : checked((int)next);
                        if (_collectDisk && threads > 0 && threads <= (recordLength - 256) / 80) {
                            for (int index = 0; index < threads; index++) {
                                var thread = record.Slice(256 + index * 80, 80);
                                long tid = ByteNumbers.Int64(thread[48..]);
                                if (tid is > 0 and <= int.MaxValue) {
                                    AddThreadIdentity((int)tid, id, ByteNumbers.Int64(thread[16..]));
                                }
                            }
                        }

                        Add(
                            id,
                            name,
                            cpu,
                            birth,
                            rss,
                            threads,
                            seconds,
                            parent is >= 0 and <= int.MaxValue ? (int)parent : null,
                            status: ReadWindowsStatus(record[..recordLength], threads)
                        );
                    }

                    if (next == 0) {
                        break;
                    }

                    if (next < 152 || next > record.Length) {
                        throw new InvalidDataException("Invalid native process record offset.");
                    }

                    offset += checked((int)next);
                }

                return;
            }
        }

        throw new IOException("Process list changed too quickly to capture after eight buffer growth attempts.");
    }

    internal static string? ReadWindowsStatus(ByteWindow record, int threads) {
        if (threads <= 0 || threads > (record.Length - 256) / 80) {
            return null;
        }

        for (int i = 0; i < threads; i++) {
            var thread = record[(256 + i * 80)..];
            if (ByteNumbers.UInt32(thread[68..]) != 5 || ByteNumbers.UInt32(thread[72..]) != 5) {
                return "Active";
            }
        }

        return "Suspended";
    }

    private void AddThreadIdentity(int id, int processId, long creationTime) {
        if (_threadCount == _threadIdentities.Length) {
            Array.Resize(ref _threadIdentities, _threadIdentities.Length * 2);
        }

        _threadIdentities[_threadCount++] = new(id, processId, creationTime);
    }

    /// <summary>Reads process status from a Windows process-information record.</summary>
    /// <param name="record">The Windows process-information record bytes.</param>
    /// <param name="threads">The process thread count.</param>
    /// <returns>The display status, or null when unavailable.</returns>
    public static string? ReadWindowsStatus(
        byte[] record,
        int threads
    ) =>
        ReadWindowsStatus(new ByteWindow(record), threads);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static unsafe extern int NtQuerySystemInformation(
        int informationClass,
        void* buffer,
        uint length,
        out uint required
    );
}
