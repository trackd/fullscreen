using System.Text;
using fullscreen.Logs;

namespace fullscreen.Tests;

[TestClass]
public sealed class LogTests {
    [TestMethod]
    public void ParsesJsonTextAndMalformedLinesWithoutTerminalControls() {
        var now = DateTimeOffset.UtcNow;
        var json = LogParser.Parse(1, "{\"@t\":\"2026-01-01T12:00:00Z\",\"@l\":\"Error\",\"@m\":\"broken\\u001b[31m\"}", "test", now);
        Assert.AreEqual(LogLevel.Error, json.Level);
        Assert.AreEqual(2026, json.Timestamp.Year);
        Assert.DoesNotContain('\u001b', json.Message);
        var text = LogParser.Parse(2, "[2025-12-01T10:20:00Z] WARN timeout", "test", now);
        Assert.AreEqual(LogLevel.Warning, text.Level);
        Assert.AreEqual(2025, text.Timestamp.Year);
        var malformed = LogParser.Parse(3, "{unclosed", "test", now);
        Assert.AreEqual(LogLevel.Unknown, malformed.Level);
        Assert.AreEqual("{unclosed", malformed.Message);
        Assert.AreEqual(LogLevel.Information, LogParser.Parse(4, "\uFEFF{\"level\":\"info\",\"message\":\"hi\"}", "test", now).Level);
        var spaced = LogParser.Parse(5, "2026-10-24 12:34:56 ERROR failed", "test", now);
        Assert.AreEqual(12, spaced.Timestamp.Hour);
        Assert.AreEqual(34, spaced.Timestamp.Minute);
    }
    [TestMethod]
    public void InitialReadIsBoundedAndRetainsNewestRowsAcrossFiles() {
        using var files = new LogFixture();
        string first = files.Write("a.log", string.Join('\n', Enumerable.Range(0, 50).Select(i => $"INFO entry {i}")) + "\n");
        string second = files.Write("b.log", "{\"level\":\"error\",\"message\":\"failed\"}\n");
        using var app = new LogViewerApp([first, second], maximumCount: 8);
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return app.Completed; }, 5000));
        app.Tick();
        Assert.HasCount(8, app.Model.Rows);
        app.SetMinimumLevel(LogLevel.Error);
        Assert.HasCount(1, app.Model.Visible);
        Assert.AreEqual("failed", app.Model.Current!.Cells[3]);
    }
    [TestMethod]
    public void FollowHandlesPartialUtf8TruncationAndReplacement() {
        using var files = new LogFixture();
        string path = files.Write("follow.log", "INFO start\n");
        using var app = new LogViewerApp([path], follow: true, pollMilliseconds: 50);
        Wait(() => app.Model.Rows.Count == 1);
        byte[] emoji = Encoding.UTF8.GetBytes("INFO smile 😀\n");
        Append(emoji.AsSpan(0, emoji.Length - 3));
        Thread.Sleep(120);
        app.Tick();
        Assert.HasCount(1, app.Model.Rows);
        Append(emoji.AsSpan(emoji.Length - 3));
        Wait(() => app.Model.Rows.Count == 2);
        Assert.AreEqual("INFO smile 😀", app.Model.Rows[1].Cells[3]);
        File.WriteAllText(path, "ERROR rewritten\n", new UTF8Encoding(false));
        Wait(() => app.Model.Rows.Count == 3);
        Assert.AreEqual(LogLevel.Error, ((LogEntry)app.Model.Rows[2].Original).Level);
        string old = path + ".old";
        File.Move(path, old);
        File.WriteAllText(path, "WARN replacement\n", new UTF8Encoding(false));
        Wait(() => app.Model.Rows.Count == 4);
        Assert.AreEqual(LogLevel.Warning, ((LogEntry)app.Model.Rows[3].Original).Level);
        void Wait(Func<bool> condition) =>
            Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return condition(); }, 5000));
        void Append(ReadOnlySpan<byte> bytes) {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            stream.Write(bytes);
        }
    }
    [TestMethod]
    public void FollowDetectsReplacementWithSamePrefixAndLongerThanPreviousPosition() {
        using var files = new LogFixture();
        string prefix = string.Concat(Enumerable.Range(0, 40).Select(i => $"INFO shared {i:D4}\n"));
        string suffix = string.Concat(Enumerable.Range(0, 300).Select(i => $"INFO original {i:D4}\n"));
        string path = files.Write("same-prefix.log", prefix + suffix);
        using var app = new LogViewerApp([path], follow: true, pollMilliseconds: 50);
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return app.Model.Rows.Count >= 340; }, 5000));

        string old = path + ".old";
        File.Move(path, old);
        string replacement = prefix + "ERROR replacement-first\n" + string.Concat(Enumerable.Range(0, 400).Select(i => $"INFO replacement {i:D4}\n"));
        File.WriteAllText(path, replacement, new UTF8Encoding(false));
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return app.Model.Rows.Any(row => row.Cells[3].Contains("ERROR replacement-first", StringComparison.Ordinal)); }, 5000),
            "Rotation with an unchanged header must restart at the beginning of the replacement file.");
    }
    [TestMethod]
    public void OversizedLinesAreTruncatedAndDisposalUnblocksProducer() {
        using var files = new LogFixture();
        string path = files.Write("large.log", new string('x', LogReader.MaximumLineLength + 200) + "\n");
        using (var app = new LogViewerApp([path])) {
            Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return app.Model.Rows.Count == 1; }, 5000));
            Assert.Contains("[line truncated]", ((LogEntry)app.Model.Rows[0].Original).Raw);
        }
        var reader = new LogReader([.. Enumerable.Repeat(path, 8)], false, 100, 50);
        Assert.IsTrue(SpinWait.SpinUntil(() => reader.Batches.Count == 4, 5000));
        Assert.IsTrue(Task.Run(reader.Dispose, TestContext.CancellationToken).Wait(TimeSpan.FromSeconds(3), TestContext.CancellationToken), "Cancellation must unblock a full producer channel.");
    }
    private sealed class LogFixture : IDisposable {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Fullscreen-Logs-" + Guid.NewGuid().ToString("N"));
        public LogFixture() =>
            Directory.CreateDirectory(_root);
        public string Write(string name, string content) {
            string path = Path.Combine(_root, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() {
            string resolved = Path.GetFullPath(_root);
            if (!Path.GetDirectoryName(resolved)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Path.GetFileName(resolved).StartsWith("Fullscreen-Logs-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Unsafe fixture cleanup");
            }

            Directory.Delete(resolved, true);
        }
    }

    public TestContext TestContext {
        get;
        set;
    }
}
