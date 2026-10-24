using System.Globalization;
using System.Diagnostics;
using fullscreen.Custom;
using fullscreen.Development;
using fullscreen.Files;

namespace fullscreen.Tests;

[TestClass]
public sealed class ReviewRegressionTests {
    [TestMethod]
    public void DeveloperIdentityCacheDropsItemsNoLongerRetainedByAnyPage() {
        using var app = new DeveloperApp(new ChangingBackend(), refreshSeconds: 3600);
        for (int cycle = 0; cycle < 100; cycle++) {
            Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return !app.Busy; }, 3000));
            if (cycle < 99) {
                app.SwitchView(0);
            }
        }
        var ids = (System.Collections.IDictionary)typeof(DeveloperApp).GetField("_ids", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(app)!;
        Assert.HasCount(1, ids, "Evicted rows must not retain lifetime identity entries.");
    }
    private sealed class ChangingBackend : IDeveloperBackend {
        private int _generation;
        public string Title => "Changing";
        public string[] Tabs => ["Rows"];
        public Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) {
            string key = Interlocked.Increment(ref _generation).ToString(CultureInfo.InvariantCulture);
            return Task.FromResult(new DeveloperPage(["Name"], [new DeveloperItem(key, [key])], "Ready"));
        }
        public Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) =>
            Task.FromResult("");
        public DeveloperOperation[] Operations(int view, DeveloperItem? item) =>
            [];
    }
    [TestMethod]
    public void PermanentDeleteIsRestrictedToConfirmedChildrenAndHonorsCancellation() {
        using var fixture = new Fixture();
        string selected = Path.Combine(fixture.Root, "selected"), sibling = Path.Combine(fixture.Root, "keep.txt");
        Directory.CreateDirectory(selected);
        File.WriteAllText(Path.Combine(selected, "nested.txt"), "delete");
        File.WriteAllText(sibling, "keep");
        Assert.Throws<IOException>(() => FileOperations.Delete(fixture.Root, fixture.Root, default));
        Assert.Throws<IOException>(() => FileOperations.Delete(sibling, selected, default));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileOperations.Delete(selected, fixture.Root, stop.Token));
        Assert.IsTrue(Directory.Exists(selected));
        FileOperations.Delete(selected, fixture.Root, default);
        Assert.IsFalse(Directory.Exists(selected));
        Assert.AreEqual("keep", File.ReadAllText(sibling));
        if (!OperatingSystem.IsWindows()) {
            string link = Path.Combine(fixture.Root, "link");
            Directory.CreateSymbolicLink(link, fixture.Root);
            FileOperations.Delete(link, fixture.Root, default);
            Assert.IsTrue(File.Exists(sibling));
            Assert.IsFalse(Directory.Exists(link));
        }
    }

    [TestMethod]
    public async Task UnixCopiesPreservePrivateAndExecutableModes() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        using var fixture = new Fixture();
        string source = Path.Combine(fixture.Root, "source"), target = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(source);
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string executable = Path.Combine(source, "run"), secret = Path.Combine(source, "secret");
        File.WriteAllText(executable, "#!/bin/sh\n");
        File.WriteAllText(secret, "private");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await FileOperations.CopyAsync(source, target, default);
        Assert.AreEqual(File.GetUnixFileMode(source), File.GetUnixFileMode(target));
        Assert.AreEqual(File.GetUnixFileMode(executable), File.GetUnixFileMode(Path.Combine(target, "run")));
        Assert.AreEqual(File.GetUnixFileMode(secret), File.GetUnixFileMode(Path.Combine(target, "secret")));
    }

    [TestMethod]
    public void ControlCapabilitiesRejectIgnoredOptionsButAcceptSerializedDefaults() {
        foreach (var type in TuiDocument.ControlTypes) {
            var node = new TuiNode { Id = "control", Type = type };
            var document = new TuiDocument { Root = node };
            if (type == "SearchReplacePopup") {
                node.Target = "logs";
                document.Root = new() {
                    Id = "root",
                    Type = "VStack",
                    Children = [node, new() { Id = "logs", Type = "LogControl" }]
                };
            }
            document.Validate();
        }

        foreach (var option in new[] { "\"Columns\":[\"Ignored\"]", "\"RefreshInterval\":100", "\"Close\":true" }) {
            Assert.Throws<ArgumentException>(() => TuiDocument.FromJson("{\"Root\":{\"Id\":\"text\",\"Type\":\"Text\"," + option + "}}"));
        }

        Assert.IsFalse(TuiControlCatalog.Describe("Border").Properties.Contains("Spacing"));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task CommandDeadlineIncludesDescendantHeldPipes() {
        using var fixture = new Fixture();
        string executable = Environment.GetEnvironmentVariable("FULLSCREEN_PWSH") ?? (OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        string script = Path.Combine(fixture.Root, "parent.ps1"), childId = Path.Combine(fixture.Root, "child.id");
        await File.WriteAllTextAsync(script, """
            param([string]$ChildIdPath)
            $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            foreach ($argument in @('-NoProfile','-Command','Start-Sleep -Seconds 10')) { $start.ArgumentList.Add($argument) }
            $child = [Diagnostics.Process]::Start($start)
            [IO.File]::WriteAllText($ChildIdPath, [string]$child.Id)
            $child.Dispose()
            """, TestContext.CancellationToken);
        var watch = Stopwatch.StartNew();
        try {
            await Assert.ThrowsAsync<TimeoutException>(() => new CommandRunner().RunAsync(new(executable, fixture.Root, ["-NoProfile", "-File", script, childId], TimeoutSeconds: 2), default));
            Assert.IsLessThan(7.0, watch.Elapsed.TotalSeconds);
        }
        finally {
            if (File.Exists(childId) && int.TryParse(await File.ReadAllTextAsync(childId, TestContext.CancellationToken), out int id)) {
                try {
                    using var child = Process.GetProcessById(id);
                    if (!child.HasExited) {
                        child.Kill(true);
                    }
                }
                catch (ArgumentException) { }
            }
        }
    }

    private sealed class Fixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-Review-" + Guid.NewGuid().ToString("N"));
        public Fixture() =>
            Directory.CreateDirectory(Root);
        public void Dispose() {
            string full = Path.GetFullPath(Root);
            if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) || !Path.GetFileName(full).StartsWith("Fullscreen-Review-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Unsafe fixture path.");
            }

            if (!OperatingSystem.IsWindows()) {
                foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(full, true);
        }
    }

    public TestContext TestContext {
        get;
        set;
    }
}
