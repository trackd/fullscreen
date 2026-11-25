namespace fullscreen.Monitoring;
/// <summary>Owns independent collectors for remote callers without PowerShell session variables.</summary>
public static class CollectorSessions {
    internal static byte[]? CollectorImage { get; private set; }
    /// <summary>Retains the collector image for GPU helpers when remoting loads the assembly in memory.</summary>
    /// <param name="image">The collector assembly bytes deployed to this endpoint.</param>
    public static void SetCollectorImage(byte[] image) {
        if (image is null || image.Length == 0 || image.Length > 4 * 1024 * 1024) {
            throw new ArgumentException("Invalid collector image.", nameof(image));
        }
        CollectorImage = (byte[])image.Clone();
    }
    /// <summary>Reads a verified process command line on the collector host.</summary>
    /// <param name="handle">The opaque identifier of an open collector.</param>
    /// <param name="processId">The selected process identifier.</param>
    /// <param name="birth">The selected process start identity.</param>
    /// <returns>The bounded command line.</returns>
    public static string ReadProcessCommandLine(string handle, int processId, long birth) {
        _ = Get(handle);
        return ProcessOwnerReader.ReadCommandLine(processId, birth);
    }

    /// <summary>Revalidates a named session&apos;s identity before logging it off.</summary>
    /// <param name="sessionId">The Windows terminal-services session identifier.</param>
    /// <param name="userName">The account name associated with the session or process.</param>
    /// <param name="logonTime">The expected Windows logon FILETIME used to reject a reused session
    /// identifier.</param>
    public static void LogoffSession(
        int sessionId,
        string userName,
        long logonTime
    ) =>
        UserSessionSampler.LogoffSession(sessionId, userName, logonTime);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Session> Sessions = new(StringComparer.Ordinal);
    private sealed class Session(bool useSudo) : IDisposable {
        private readonly object _gate = new();
        private readonly PerformanceCollector _collector = new(useSudo);
        public bool NeedsSudoAuthentication {
            get {
                lock (_gate) {
                    return _collector.NeedsSudoAuthentication;
                }
            }
        }
        public bool AuthenticateSudo(System.Security.SecureString password) {
            lock (_gate) {
                return _collector.AuthenticateSudo(password, CancellationToken.None);
            }
        }
        private bool _disposed;
        public string Capture(int metrics) {
            lock (_gate) {
                if (_disposed) {
                    throw new ObjectDisposedException(nameof(Session));
                }

                return _collector.CaptureBase64(metrics);
            }
        }

        public void InvalidateServices() {
            lock (_gate) {
                if (!_disposed) {
                    _collector.InvalidateServices();
                    _collector.InvalidateUsers();
                }
            }
        }

        public void Dispose() {
            lock (_gate) {
                if (!_disposed) {
                    _disposed = true;
                    _collector.Dispose();
                }
            }
        }
    }

    /// <summary>Creates a collector associated with an opaque handle.</summary>
    /// <param name="handle">The opaque identifier of a reusable collector.</param>
    public static void Open(string handle) => Open(handle, false);
    /// <summary>Creates a collector with an explicit Linux sudo policy.</summary>
    /// <param name="handle">The collector handle.</param>
    /// <param name="useSudo">Whether device readers may use sudo.</param>
    public static void Open(string handle, bool useSudo) {
        if (!Guid.TryParse(handle, out _)) {
            throw new ArgumentException("Expected a collector session handle.", nameof(handle));
        }

        lock (Gate) {
            if (!Sessions.ContainsKey(handle)) {
                Sessions.Add(handle, new Session(useSudo));
            }
        }
    }

    /// <summary>Checks whether an opaque collector handle is active.</summary>
    /// <param name="handle">The opaque identifier of a reusable collector.</param>
    /// <returns>True when the handle identifies an active collector.</returns>
    public static bool IsOpen(string handle) {
        lock (Gate) {
            return Sessions.ContainsKey(handle);
        }
    }

    /// <summary>Captures selected metrics and serializes the frame for remoting.</summary>
    /// <param name="handle">The opaque identifier of a reusable collector.</param>
    /// <param name="metrics">The numeric CollectorMetrics flags requested for this capture.</param>
    /// <returns>The Base64-encoded frame.</returns>
    public static string CaptureBase64(string handle, int metrics) =>
        Get(handle).Capture(metrics);
    /// <summary>Checks whether sudo requested authentication for a device reader.</summary>
    /// <param name="handle">The collector handle.</param>
    /// <returns>Whether authentication can help.</returns>
    public static bool NeedsSudoAuthentication(string handle) => Get(handle).NeedsSudoAuthentication;
    /// <summary>Authenticates sudo on the collector host.</summary>
    /// <param name="handle">The collector handle.</param>
    /// <param name="password">The password received through the protected remoting channel.</param>
    /// <returns>Whether authentication succeeded.</returns>
    public static bool AuthenticateSudo(string handle, System.Security.SecureString password) => Get(handle).AuthenticateSudo(password);
    /// <summary>Requests refresh of cached service metadata for a collector.</summary>
    /// <param name="handle">The opaque identifier of a reusable collector.</param>
    public static void InvalidateServices(string handle) =>
        Get(handle).InvalidateServices();
    /// <summary>Disposes the collector associated with a handle.</summary>
    /// <param name="handle">The opaque identifier of a reusable collector.</param>
    public static void Close(string handle) {
        Session? session;
        lock (Gate) {
            if (!Sessions.TryGetValue(handle, out session)) {
                return;
            }

            Sessions.Remove(handle);
        }

        session.Dispose();
    }

    private static Session Get(string handle) {
        lock (Gate) {
            return Sessions.TryGetValue(handle, out var session) ? session : throw new InvalidOperationException("The collector session is closed or was not opened.");
        }
    }
}
