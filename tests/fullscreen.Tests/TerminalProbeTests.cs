using fullscreen.Charts;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TerminalProbeTests {
    internal sealed class Transport(string response) : ITerminalQueryTransport {
        private readonly Queue<ConsoleKeyInfo> _reply = new(response.Select(character =>
            new ConsoleKeyInfo(character, (ConsoleKey)0, false, false, false)));
        private bool _written;
        public bool CanQuery { get; set; } = true;
        public bool PendingInput { get; set; }
        public bool FailWrite { get; set; }
        public bool AdvanceOnRead { get; set; } = true;
        public bool ModeActive { get; private set; }
        public bool WriteSawMode { get; private set; }
        public int ModeEntries { get; private set; }
        public int ModeExits { get; private set; }
        public int Reads { get; private set; }
        public int Remaining => _reply.Count;
        public long ElapsedMilliseconds { get; private set; }
        public bool KeyAvailable => PendingInput || _written && _reply.Count > 0;
        public Action? OnWrite { get; set; }
        public List<string> Queries { get; } = [];
        public List<ConsoleKeyInfo> Preserved { get; } = [];
        public IDisposable EnterQueryMode() {
            ModeEntries++;
            ModeActive = true;
            return new Mode(this);
        }
        public ConsoleKeyInfo Read() {
            Reads++;
            if (AdvanceOnRead) { ElapsedMilliseconds++; }
            return _reply.Dequeue();
        }
        public void Write(string query) {
            WriteSawMode = ModeActive;
            Queries.Add(query);
            if (FailWrite) { throw new IOException("fixture write failure"); }
            if (!_written && query == TerminalFeatureProbe.DeviceQuery && response.Length > 0 && response[0] is '\x1b' or '\u009b') {
                int offset = Math.Max(response.LastIndexOf("\x1b[?", StringComparison.Ordinal), response.LastIndexOf("\u009b?", StringComparison.Ordinal));
                int end = offset < 0 ? -1 : response.IndexOf('c', offset);
                string? attributes = end < 0 ? null : response[offset..(end + 1)];
                if (attributes is not null && attributes.TrimEnd('c').Split(';').Skip(1).Any(flag => flag is "3" or "4")) {
                    var pending = _reply.ToArray();
                    _reply.Clear();
                    foreach (char character in attributes) {
                        _reply.Enqueue(new(character, (ConsoleKey)0, false, false, false));
                    }
                    foreach (var key in pending) { _reply.Enqueue(key); }
                }
            }
            _written = true;
            OnWrite?.Invoke();
        }
        public void Wait() { ElapsedMilliseconds++; }
        public void PreserveInput(IReadOnlyList<ConsoleKeyInfo> keys) { Preserved.AddRange(keys); }
        private sealed class Mode(Transport owner) : IDisposable {
            public void Dispose() {
                owner.ModeActive = false;
                owner.ModeExits++;
            }
        }
    }

    internal static TerminalProbeResult Observe(TerminalInstance terminal, string response) {
        var result = TerminalFeatureProbe.Read(new Transport(response), terminal.Size);
        TerminalFeatureProbe.Record(terminal, result);
        return result;
    }

    [TestMethod]
    [DataRow("\x1b[?65;1;3;4;22c", true, true)]
    [DataRow("\x1b[?62;4c", true, false)]
    [DataRow("\x1b[?63;3c", false, true)]
    [DataRow("\x1b[?4c", false, false)]
    [DataRow("\x1b[?3c", false, false)]
    [DataRow("\x1b[4;4;800t\x1b[?62;1c", false, false)]
    [DataRow("\u009b?65;3;4c", true, true)]
    public void Da1FlagsSelectOnlyAdvertisedProtocols(string response, bool sixel, bool regis) {
        var transport = new Transport(response);
        var result = TerminalFeatureProbe.Read(transport, new(80, 24));
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(sixel, result.Sixel);
        Assert.AreEqual(regis, result.Regis);
        if (!sixel && !regis) {
            CollectionAssert.AreEqual(new[] { TerminalFeatureProbe.DeviceQuery }, transport.Queries,
                "A negative DA1 must not issue geometry queries that ConPTY can leave behind.");
        }
    }

    [TestMethod]
    [DataRow("\x1b[6;23;10t\x1b[?65;4c", 10, 23)]
    [DataRow("\x1b[4;920;1300t\x1b[8;40;130t\x1b[?65;4c", 10, 23)]
    [DataRow("\x1b[4;480;640t\x1b[?65;4c", 8, 20)]
    public void PixelReportsDetermineCellGeometry(string response, int width, int height) {
        var result = TerminalFeatureProbe.Read(new Transport(response), new(80, 24));
        Assert.AreEqual(width, result.Metrics!.Value.CellPixelWidth);
        Assert.AreEqual(height, result.Metrics.Value.CellPixelHeight);
    }

    [TestMethod]
    public void ProbeStopsAtFenceAndRestoresMode() {
        var transport = new Transport("\x1b[6;12;4t\x1b[?65;4cxyz");
        var result = TerminalFeatureProbe.Read(transport, new(20, 10));
        Assert.IsTrue(result.Sixel);
        CollectionAssert.AreEqual(new[] { TerminalFeatureProbe.DeviceQuery, TerminalFeatureProbe.Query }, transport.Queries);
        Assert.IsTrue(transport.WriteSawMode);
        Assert.IsFalse(transport.ModeActive);
        Assert.AreEqual(1, transport.ModeEntries);
        Assert.AreEqual(1, transport.ModeExits);
        Assert.AreEqual(3, transport.Remaining, "Input following the DA fence must stay unread.");
        Assert.HasCount(0, transport.Preserved);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void PendingOrUnavailableInputDoesNotQuery(bool pending, bool unavailable) {
        var transport = new Transport("\x1b[?65;4c") { PendingInput = pending, CanQuery = !unavailable };
        var result = TerminalFeatureProbe.Read(transport, new(80, 24));
        Assert.IsFalse(result.Attempted);
        Assert.AreEqual(0, transport.Reads);
        Assert.AreEqual(0, transport.ModeEntries);
        Assert.HasCount(0, transport.Queries);
    }

    [TestMethod]
    public void UnfencedRepliesNeverEnableGraphicsOrRetry() {
        var transport = new Transport("\x1b[6;20;10t");
        var cache = new TerminalProbeCache();
        var first = cache.Get(new(80, 24), () => TerminalFeatureProbe.Read(transport, new(80, 24)));
        Assert.IsTrue(first.Attempted);
        Assert.IsFalse(first.Completed);
        Assert.IsFalse(first.Sixel);
        Assert.IsNull(first.Metrics, "A partial response is not complete negotiation evidence.");
        var again = cache.Get(new(100, 40), () => throw new InvalidOperationException("Retried an unfenced query."));
        Assert.AreSame(first, again);
        Assert.HasCount(1, transport.Queries);
        Assert.AreEqual(1, transport.ModeExits);
        Assert.IsGreaterThanOrEqualTo(TerminalFeatureProbe.TimeoutMilliseconds, transport.ElapsedMilliseconds);
    }

    [TestMethod]
    public void CompletedEvidenceIsReusedAndSkippedInputCanBeRetried() {
        var cache = new TerminalProbeCache();
        int calls = 0;
        var size = new TerminalSize(80, 24);
        var skipped = cache.Get(size, () => { calls++; return TerminalProbeResult.Unknown; });
        Assert.IsFalse(skipped.Attempted);
        var completed = cache.Get(size, () => {
            calls++;
            return TerminalFeatureProbe.Read(new Transport("\x1b[?65;4c"), size);
        });
        Assert.AreSame(completed, cache.Get(size, () => throw new InvalidOperationException("Repeated query.")));
        cache.Get(new(100, 40), () => { calls++; return completed; });
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    [DataRow("\x1b[?65;4;badc")]
    [DataRow("\x1b[?65;4294967296c")]
    [DataRow("\x1b[6;0;10t\x1b[?65;4c")]
    public void MalformedReportsDoNotInventGeometryOrReplayDigits(string response) {
        var transport = new Transport(response);
        var result = TerminalFeatureProbe.Read(transport, new(80, 24));
        Assert.IsNull(result.Metrics);
        Assert.HasCount(0, transport.Preserved);
        if (!result.Completed) { Assert.IsFalse(result.Sixel); }
    }

    [TestMethod]
    public void UserKeysDuringProbeArePreservedAndResponseSizeIsBounded() {
        var typed = new Transport("x\x1b[?65;4c");
        Assert.IsFalse(TerminalFeatureProbe.Read(typed, new(80, 24)).Completed);
        Assert.AreEqual(1, typed.Reads);
        Assert.AreEqual('x', typed.Preserved.Single().KeyChar);
        var oversized = new Transport("\x1b[?" + new string('1', 5000)) { AdvanceOnRead = false };
        Assert.IsFalse(TerminalFeatureProbe.Read(oversized, new(80, 24)).Completed);
        Assert.AreEqual(4097, oversized.Reads);
        Assert.AreEqual(1, oversized.ModeExits);
    }

    [TestMethod]
    public void CancellationAndWriteFailureRestoreMode() {
        using var stop = new CancellationTokenSource();
        var canceled = new Transport("") { OnWrite = stop.Cancel };
        Assert.ThrowsExactly<OperationCanceledException>(() => TerminalFeatureProbe.Read(canceled, new(80, 24), stop.Token));
        Assert.AreEqual(1, canceled.ModeExits);
        var failure = new Transport("") { FailWrite = true };
        Assert.IsFalse(TerminalFeatureProbe.Read(failure, new(80, 24)).Completed);
        Assert.AreEqual(1, failure.ModeExits);
    }

    [TestMethod]
    public void InputQueueCompatibilityPreservesTextAndShortcuts() {
        Assert.IsTrue(TerminalProbeInput.CanPreserve, "Recheck the pinned XenoAtom input queue contract on upgrades.");
        using var backend = new InMemoryTerminalBackend();
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        TerminalProbeInput.Preserve(session.Instance, [
            new('x', ConsoleKey.X, false, false, false),
            new('\x03', ConsoleKey.C, false, false, true),
            new('\0', ConsoleKey.LeftArrow, false, true, false)
        ]);
        Assert.IsTrue(session.Instance.TryReadEvent(out var first));
        Assert.AreEqual("x", Assert.IsInstanceOfType<TerminalTextEvent>(first).Text);
        Assert.IsTrue(session.Instance.TryReadEvent(out var second));
        var control = Assert.IsInstanceOfType<TerminalKeyEvent>(second);
        Assert.AreEqual('c', control.Char);
        Assert.AreEqual(TerminalModifiers.Ctrl, control.Modifiers);
        Assert.IsTrue(session.Instance.TryReadEvent(out var third));
        var arrow = Assert.IsInstanceOfType<TerminalKeyEvent>(third);
        Assert.AreEqual(TerminalKey.Left, arrow.Key);
        Assert.AreEqual(TerminalModifiers.Alt, arrow.Modifiers);
    }

    [TestMethod]
    public void FactoryPrefersVtEvidenceAndFallsBackToXenoAtom() {
        using var backend = new InMemoryTerminalBackend(new TerminalSize(80, 24));
        var options = new TerminalOptions { ImplicitStartInput = true };
        options.Graphics.PreferredProtocol = TerminalGraphicsProtocol.Sixel;
        options.Graphics.ForcedPixelMetrics = new(800, 480, 10, 20, 80, 24);
        using var session = Terminal.Open(backend, options, force: true);
        using var fallback = Assert.IsInstanceOfType<SixelChartPresenter>(ChartPresenters.Create(session.Instance));
        Assert.AreEqual(options.Graphics.ForcedPixelMetrics, fallback.Capabilities.PixelMetrics);
        TerminalFeatureProbe.Record(session.Instance, TerminalProbeResult.Incomplete);
        using var incomplete = Assert.IsInstanceOfType<SixelChartPresenter>(ChartPresenters.Create(session.Instance));
        Assert.IsTrue(incomplete.CanPresent(TerminalGraphicsCapabilities.None));
        var positive = Observe(session.Instance, "\x1b[6;12;4t\x1b[?65;3;4c");
        using var sixel = Assert.IsInstanceOfType<SixelChartPresenter>(ChartPresenters.Create(session.Instance));
        Assert.IsTrue(sixel.CanPresent(TerminalGraphicsCapabilities.None));
        Assert.AreEqual(positive.Metrics, sixel.Capabilities.PixelMetrics);
        using var regis = Assert.IsInstanceOfType<RegisChartPresenterPrototype>(ChartPresenters.Create(session.Instance, ChartRenderer.Regis));
        Assert.IsTrue(regis.CanPresent(TerminalGraphicsCapabilities.None));
        Observe(session.Instance, "\x1b[?65;4c");
        Assert.IsNull(ChartPresenters.Create(session.Instance, ChartRenderer.Regis), "Sixel does not imply ReGIS.");
        Observe(session.Instance, "\x1b[?65;1c");
        Assert.IsNull(ChartPresenters.Create(session.Instance));
        Assert.IsNull(ChartPresenters.Create(session.Instance, ChartRenderer.Regis));
    }
}
