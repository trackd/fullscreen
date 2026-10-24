namespace fullscreen.Monitoring;

internal readonly record struct WindowsThreadIdentity(int Id, int ProcessId, long CreationTime);
/// <summary>Only kernel byte counters and thread identities; no event objects or payload copies.</summary>
internal sealed class EtwIoCounters(bool disk, bool network) {
    internal static readonly Guid TcpProvider = new("9a280ac0-c8e0-11d1-84e2-00c04fb998a2");
    internal static readonly Guid UdpProvider = new("bf3a50c5-a9c9-4988-a005-2df0b7c80f80");
    internal static readonly Guid DiskProvider = new("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c");
    internal static readonly Guid ThreadProvider = new("3d6fa8d1-fe05-11d0-9dda-00c04fd7ba7c");
    private const int MaxThreads = 262144;
    private readonly object _gate = new();
    private readonly Dictionary<int, Bytes> _bytes = new(1024);
    private readonly Dictionary<int, Thread> _threads = new(4096);
    private readonly HashSet<int> _active = [];
    private readonly List<int> _dead = [];
    private ulong _lost;
    private bool _diskIncomplete, _networkIncomplete, _faulted;
    internal bool Faulted {
        get {
            lock (_gate) {
                return _faulted;
            }
        }
    }

    private struct Bytes {
        internal long Birth, Disk, Network;
    }

    private readonly record struct Thread(int ProcessId, long ProcessBirth, long Birth, long End = 0);
    internal void Fault() {
        lock (_gate) {
            _faulted = true;
        }
    }

    // Timestamp is FILETIME after ProcessTrace's default conversion. All supported
    // network schemas start with PID and size; completion disk schemas need TID mapping.
    internal void Accept(
        Guid provider,
        byte opcode,
        byte version,
        ushort flags,
        uint headerPid,
        uint headerTid,
        long timestamp,
        ByteWindow payload
    ) {
        bool net = provider == TcpProvider || provider == UdpProvider;
        bool storage = provider == DiskProvider;
        if (!(net && network || storage && disk || provider == ThreadProvider && disk)) {
            return;
        }

        lock (_gate) {
            if (net) {
                // Count send/receive only: no retransmit, connect, disconnect or copy events.
                if (opcode is not (10 or 11 or 26 or 27)) {
                    return;
                }

                if (version != 2 || payload.Length < (opcode >= 26 ? 44 : 20)) {
                    _networkIncomplete = true;
                    return;
                }

                Add(ByteNumbers.UInt32(payload), ByteNumbers.UInt32(payload[4..]), false, timestamp);
            }
            else if (storage) {
                if (opcode is not (10 or 11)) {
                    return;
                }

                int pointerSize = (flags & 0x20) != 0 ? 4 : 8;
                int issuingThreadOffset = 32 + 2 * pointerSize;
                if (version is not (2 or 3) || payload.Length < issuingThreadOffset) {
                    _diskIncomplete = true;
                    return;
                }

                uint pid = headerPid;
                if (pid == uint.MaxValue) {
                    uint tid = headerTid;
                    if (payload.Length >= issuingThreadOffset + 4) {
                        tid = ByteNumbers.UInt32(payload[issuingThreadOffset..]);
                    }

                    if (tid > int.MaxValue || !_threads.TryGetValue((int)tid, out var thread) || timestamp < thread.Birth
                        || !_bytes.TryGetValue(thread.ProcessId, out var process)
                        || process.Birth != thread.ProcessBirth) {
                        _diskIncomplete = true;
                        return;
                    }

                    pid = (uint)thread.ProcessId;
                }

                Add(pid, ByteNumbers.UInt32(payload[8..]), true, timestamp);
            }
            else {
                if (opcode is not (1 or 2 or 3 or 4)) {
                    return;
                }

                if (version is not (2 or 3) || payload.Length < 8) {
                    _diskIncomplete = true;
                    return;
                }

                uint pid = ByteNumbers.UInt32(payload), tid = ByteNumbers.UInt32(payload[4..]);
                if (pid > int.MaxValue || tid > int.MaxValue || !_bytes.TryGetValue((int)pid, out var process)
                    || timestamp < process.Birth) {
                    return;
                }

                if (_threads.TryGetValue((int)tid, out var current) && timestamp < current.Birth) {
                    return;
                }

                if (opcode == 2) {
                    if (_threads.TryGetValue((int)tid, out var existing) && existing.ProcessId == pid
                        && timestamp >= existing.Birth) {
                        _threads[(int)tid] = existing with {
                            End = timestamp
                        };
                    }
                }
                else if (opcode is 3 or 4 && _threads.TryGetValue((int)tid, out var existing) && existing.ProcessId == pid
                    && existing.ProcessBirth == process.Birth) {
                }
                else if (_threads.Count < MaxThreads || _threads.ContainsKey((int)tid)) {
                    _threads[(int)tid] = new((int)pid, process.Birth, timestamp);
                }
                else {
                    _diskIncomplete = true;
                }
            }
        }
    }

    private void Add(uint pid, uint size, bool diskEvent, long timestamp) {
        if (pid > int.MaxValue || !_bytes.TryGetValue((int)pid, out var value) || timestamp < value.Birth) {
            return;
        }

        if (diskEvent) {
            value.Disk = checked(value.Disk + size);
        }
        else {
            value.Network = checked(value.Network + size);
        }

        _bytes[(int)pid] = value;
    }

    internal void Sample(
        ProcessRow[] rows,
        WindowsThreadIdentity[] threads,
        double seconds,
        ulong lost,
        long now,
        int rowCount = -1,
        int threadCount = -1
    ) {
        if (rowCount < 0) {
            rowCount = rows.Length;
        }

        if (threadCount < 0) {
            threadCount = threads.Length;
        }

        lock (_gate) {
            bool complete = !_faulted && _lost == lost && seconds > 0 && Compat.IsFinite(seconds);
            if (_lost != lost) {
                _threads.Clear(); // Repair thread identities from the latest OS snapshot below.
            }

            _lost = lost;
            _active.Clear();
            for (int i = 0; i < rowCount; i++) {
                var row = rows[i];
                _active.Add(row.Id);
                bool known = _bytes.TryGetValue(row.Id, out var value) && value.Birth == row.StartTimeTicks && value.Birth > 0;
                rows[i] = row with {
                    DiskBytesPerSecond = disk && known && complete && !_diskIncomplete ? value.Disk / seconds : null,
                    NetworkBytesPerSecond = network && known && complete && !_networkIncomplete ? value.Network / seconds : null
                };
                _bytes[row.Id] = new Bytes {
                    Birth = row.StartTimeTicks
                };
            }

            _dead.Clear();
            foreach (int id in _bytes.Keys) {
                if (!_active.Contains(id)) {
                    _dead.Add(id);
                }
            }

            foreach (int id in _dead) {
                _bytes.Remove(id);
            }

            _dead.Clear();
            foreach (var pair in _threads) {
                if (!_bytes.TryGetValue(pair.Value.ProcessId, out var process)
                    || process.Birth != pair.Value.ProcessBirth
                    || pair.Value.End != 0 && pair.Value.End < now - 10 * TimeSpan.TicksPerSecond) {
                    _dead.Add(pair.Key);
                }
            }

            foreach (int id in _dead) {
                _threads.Remove(id);
            }

            _diskIncomplete = _networkIncomplete = false;
            // Seed existing threads, including after event loss. Birth dates reject delayed events and TID reuse.
            for (int index = 0; index < threadCount; index++) {
                var thread = threads[index];
                if (!_bytes.TryGetValue(thread.ProcessId, out var process) || thread.CreationTime < process.Birth) {
                    continue;
                }

                if (_threads.Count >= MaxThreads && !_threads.ContainsKey(thread.Id)) {
                    _diskIncomplete = true;
                    continue;
                }

                _threads[thread.Id] = new(thread.ProcessId, process.Birth, thread.CreationTime);
            }
        }
    }
}
