using System.Text;
using fullscreen.Files;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using System.Reflection;
using System.Diagnostics;

namespace fullscreen.Tests;

[TestClass]
public sealed class FileManagerTests {
    private static readonly string[] expected = ["a", "c"];
    private static readonly string[] expectedArray = ["bad"];

    [TestMethod]
    public async Task MultiItemOperationsContinuePastFailures() {
        var done = new List<string>();
        var (failed, error) = await FileOperations.EachAsync(["a", "bad", "c"], source => {
            if (source == "bad") {
                throw new IOException("denied");
            }

            done.Add(source);
            return Task.CompletedTask;
        }, CancellationToken.None);

        CollectionAssert.AreEqual(expected, done);
        CollectionAssert.AreEqual(expectedArray, failed);
        Assert.AreEqual("1 of 3 failed · bad: denied", error);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task FrameworkInputNavigatesPreviewsFiltersAndConfirmsFileOperations() {
        using var fixture = new Fixture();
        string target = fixture.Directory("folder");
        fixture.File("a.txt", "contents");
        fixture.File("b.md", "# Markdown");
        using var files = new FileManagerApp(fixture.Root);
        var surface = (FileManagerSurface)files.Root;
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 32));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(surface, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            files.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        void Send(TerminalEvent ev, Func<bool>? condition = null) {
            backend.PushEvent(ev);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return condition?.Invoke() ?? true; }, 5000));
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(surface);
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.Preview is not null && !surface.Busy; }, 5000));
            Send(new TerminalKeyEvent { Key = TerminalKey.Down }, () => surface.Model.Selected?.Name == "a.txt");
            Send(new TerminalTextEvent { Text = " " }, () => surface.Model.Marked.Count == 1);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return surface.Preview?.Kind == PreviewKind.Markdown; }, 5000));
            Assert.Contains("TextMate", backend.GetOutText(), "A non-PowerShell host must explain the optional preview dependency.");
            string currentPath = surface.Model.CurrentPath, selectedPath = surface.Model.Selected!.Value.FullPath;
            int previewWidth = surface.EnumerateVisualsDepthFirst().OfType<XenoAtom.Terminal.UI.Controls.ScrollViewer>().First().Bounds.Width;
            Send(new TerminalKeyEvent { Key = TerminalKey.Right }, () => !surface.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Right }, () => surface.ParentPaneCollapsed);
            Assert.IsGreaterThan(previewWidth, surface.EnumerateVisualsDepthFirst().OfType<XenoAtom.Terminal.UI.Controls.ScrollViewer>().First().Bounds.Width);
            Send(new TerminalKeyEvent { Key = TerminalKey.Left }, () => !surface.ParentPaneCollapsed);
            Send(new TerminalKeyEvent { Key = TerminalKey.Right }, () => surface.ParentPaneCollapsed);
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Left }, () => !surface.ParentPaneCollapsed);
            Assert.AreEqual(currentPath, surface.Model.CurrentPath);
            Assert.AreEqual(selectedPath, surface.Model.Selected!.Value.FullPath);
            Assert.HasCount(1, surface.Model.Marked);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 1, Y = 3 }, () => surface.ParentPaneCollapsed);
            Send(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 1, Y = 3 });
            Send(new TerminalKeyEvent { Key = TerminalKey.Left }, () => !surface.ParentPaneCollapsed);
            Send(new TerminalTextEvent { Text = "y" });
            Send(new TerminalTextEvent { Text = "/" });
            Send(new TerminalPasteEvent { Text = "b.md" }, () => surface.Model.Filter == "b.md");
            Assert.HasCount(1, surface.Model.Entries);
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape });
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => surface.Model.Filter.Length == 0 && surface.Model.Marked.Count == 0);
            Send(new TerminalKeyEvent { Key = TerminalKey.Home }, () => surface.Model.Selected?.Name == "folder");
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => surface.Model.CurrentPath == target && !surface.Busy);
            Send(new TerminalTextEvent { Text = "p" }, () => backend.GetOutText().Contains("Confirm file operation", StringComparison.Ordinal));
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => System.IO.File.Exists(Path.Combine(target, "a.txt")) && !surface.Busy);
            Assert.AreEqual("contents", System.IO.File.ReadAllText(Path.Combine(target, "a.txt")));
            Send(new TerminalTextEvent { Text = "n" }, () => backend.GetOutText().Contains("Create directory", StringComparison.Ordinal));
            Send(new TerminalTextEvent { Text = "made" });
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => System.IO.Directory.Exists(Path.Combine(target, "made")) && !surface.Busy);
            Send(new TerminalTextEvent { Text = "q" }, () => surface.ExitRequested);
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    internal sealed class Fixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullscreen-test-" + Guid.NewGuid().ToString("N"));
        public Fixture() {
            System.IO.Directory.CreateDirectory(Root);
        }
        public string File(string name, string contents = "hello") {
            string path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, contents);
            return path;
        }
        public string Directory(string name) {
            string path = Path.Combine(Root, name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
        public void Dispose() {
            string full = Path.GetFullPath(Root), temp = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("fullscreen-test-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Unexpected fixture path.");
            }

            System.IO.Directory.Delete(full, recursive: true);
        }
    }
    [TestMethod]
    public void DirectoriesSortFirstAndSelectionSurvivesFilteringAndRefresh() {
        using var fixture = new Fixture();
        fixture.Directory("z-folder");
        string file = fixture.File("hello.txt");
        fixture.File(".hidden");
        var model = new FileManagerModel();
        model.Apply(FileReader.ReadDirectory(fixture.Root, CancellationToken.None));
        Assert.HasCount(2, model.Entries);
        Assert.IsTrue(model.Entries[0].IsDirectory);
        model.Move(1, 10);
        Assert.AreEqual(file, model.Selected!.Value.FullPath);
        model.ToggleMark();
        model.Filter = "HELLO";
        model.Rebuild();
        Assert.HasCount(1, model.Entries);
        model.Filter = "";
        model.ShowHidden = true;
        model.Rebuild();
        Assert.HasCount(3, model.Entries);
        model.Apply(FileReader.ReadDirectory(fixture.Root, CancellationToken.None));
        Assert.AreEqual(file, model.Selected!.Value.FullPath);
        CollectionAssert.AreEqual(new[] { file }, model.OperationPaths());
    }
    [TestMethod]
    public void HistoryAndMarkedPathsSurviveDirectoryNavigation() {
        using var fixture = new Fixture();
        string child = fixture.Directory("child"), file = fixture.File("x");
        var model = new FileManagerModel();
        model.Apply(FileReader.ReadDirectory(fixture.Root, CancellationToken.None));
        model.Move(1, 10);
        model.ToggleMark();
        model.Apply(FileReader.ReadDirectory(child, CancellationToken.None));
        Assert.AreEqual(fixture.Root, model.History(-1));
        model.Apply(FileReader.ReadDirectory(fixture.Root, CancellationToken.None), false);
        Assert.AreEqual(file, model.Selected!.Value.FullPath);
        Assert.AreEqual(child, model.History(1));
    }
    [TestMethod]
    public async Task CopiesStageWithoutOverwritingAndCancelWithoutLeavingPartialFiles() {
        using var fixture = new Fixture();
        string source = fixture.File("source.txt", new string('x', 100000));
        string target = Path.Combine(fixture.Root, "copy.txt");
        await FileOperations.CopyAsync(source, target, CancellationToken.None);
        Assert.AreEqual(await System.IO.File.ReadAllTextAsync(source, TestContext.CancellationToken), await System.IO.File.ReadAllTextAsync(target, TestContext.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => FileOperations.CopyAsync(source, target, CancellationToken.None));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => FileOperations.CopyAsync(source, Path.Combine(fixture.Root, "cancelled.txt"), stop.Token));
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(fixture.Root, "cancelled.txt")));
        Assert.IsEmpty(System.IO.Directory.GetFileSystemEntries(fixture.Root, ".fullscreen-copy-*"));
    }
    [TestMethod]
    public async Task RecursiveCopyMoveRenameAndMkdirRespectBoundaries() {
        using var fixture = new Fixture();
        string source = fixture.Directory("source");
        System.IO.File.WriteAllText(Path.Combine(source, "nested.txt"), "data");
        await Assert.ThrowsAsync<IOException>(() => FileOperations.CopyAsync(source, Path.Combine(source, "inside"), CancellationToken.None));
        string copy = Path.Combine(fixture.Root, "copy");
        await FileOperations.CopyAsync(source, copy, CancellationToken.None);
        Assert.AreEqual("data", System.IO.File.ReadAllText(Path.Combine(copy, "nested.txt")));
        string moved = Path.Combine(fixture.Root, "moved");
        FileOperations.Move(copy, moved);
        Assert.IsFalse(System.IO.Directory.Exists(copy));
        string renamed = FileOperations.Rename(moved, "renamed");
        Assert.IsTrue(System.IO.Directory.Exists(renamed));
        FileOperations.CreateDirectory(fixture.Root, "new");
        Assert.Throws<IOException>(() => FileOperations.CreateDirectory(fixture.Root, "new"));
        Assert.Throws<ArgumentException>(() => FileOperations.ChildPath(fixture.Root, "../outside"));
        Assert.Throws<ArgumentException>(() => FileOperations.Rename(source, ".."));
        string file = fixture.File("case.txt", "x");
        foreach (string path in new[] { file, renamed }) {
            string upper = Path.GetFileName(path).ToUpperInvariant();
            FileOperations.Rename(path, upper);
            CollectionAssert.Contains(System.IO.Directory.GetFileSystemEntries(fixture.Root).Select(Path.GetFileName).ToArray(), upper, "Case-only rename must apply.");
        }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CaseOnlyRenameNeverReplacesADistinctExistingEntry(bool directory) {
        using var fixture = new Fixture();
        string source = directory ? fixture.Directory("case") : fixture.File("case", "source");
        string destination = Path.Combine(fixture.Root, "CASE");
        if (Path.Exists(destination)) {
            Assert.AreEqual(destination, FileOperations.Rename(source, "CASE"));
            CollectionAssert.Contains(System.IO.Directory.GetFileSystemEntries(fixture.Root).Select(Path.GetFileName).ToArray(), "CASE");
        }
        else {
            if (directory) { System.IO.Directory.CreateDirectory(destination); }
            else { System.IO.File.WriteAllText(destination, "destination"); }
            Assert.ThrowsExactly<IOException>(() => FileOperations.Rename(source, "CASE"));
            Assert.IsTrue(Path.Exists(source));
            Assert.IsTrue(Path.Exists(destination));
            if (!directory) {
                Assert.AreEqual("source", System.IO.File.ReadAllText(source));
                Assert.AreEqual("destination", System.IO.File.ReadAllText(destination));
            }
        }
    }
    [TestMethod]
    public async Task PreviewHandlesMarkdownBinaryUtf16ControlCodesAndSizeLimits() {
        using var fixture = new Fixture();
        fixture.File("readme.md", "# Hello\n\n**Markdown**\u001b[31m");
        string binary = fixture.File("binary");
        System.IO.File.WriteAllBytes(binary, [1, 0, 255]);
        string unicode = fixture.File("unicode.txt");
        System.IO.File.WriteAllText(unicode, "hello 世界", Encoding.Unicode);
        fixture.File("large.txt", new string('x', FileReader.MaxTextBytes + 500));
        var snapshot = FileReader.ReadDirectory(fixture.Root, CancellationToken.None);
        var md = await FileReader.ReadPreviewAsync(snapshot.Entries.Single(e => e.Name == "readme.md"), false, CancellationToken.None);
        Assert.AreEqual(PreviewKind.Markdown, md.Kind);
        Assert.DoesNotContain('\u001b', md.Text);
        Assert.AreEqual(PreviewKind.Binary, (await FileReader.ReadPreviewAsync(snapshot.Entries.Single(e => e.FullPath == binary), false, CancellationToken.None)).Kind);
        Assert.AreEqual("hello 世界", (await FileReader.ReadPreviewAsync(snapshot.Entries.Single(e => e.FullPath == unicode), false, CancellationToken.None)).Text);
        Assert.Contains("limited to 64 KiB", (await FileReader.ReadPreviewAsync(snapshot.Entries.Single(e => e.Name == "large.txt"), false, CancellationToken.None)).Text);
    }
    [TestMethod]
    public void MinimalDirectoryDoesNotLoadHiddenParentOrPreviewAndRestoresThemOnDemand() {
        using var fixture = new Fixture();
        fixture.File("one.txt", "preview text");
        using var app = new FileManagerApp(fixture.Root, minimal: true);
        var surface = (FileManagerSurface)app.Root;
        VisualSnapshotRenderer.Render(surface, 120, 30);
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.Model.CurrentPath == fixture.Root; }, 5000));
        Assert.IsNull(surface.Preview);
        Assert.IsFalse(surface.PreviewVisible);
        surface.Minimal = false;
        VisualSnapshotRenderer.Render(surface, 120, 30);
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.Preview is not null; }, 5000));
        Assert.AreEqual("preview text", surface.Preview!.Text);
        surface.Minimal = true;
        app.Tick();
        Assert.IsNull(surface.Preview);
    }
    [TestMethod]
    public async Task WorkerLoadsDirectoryAndLatestPreviewAndShutsDown() {
        using var fixture = new Fixture();
        fixture.File("one.txt", "preview text");
        using var app = new FileManagerApp(fixture.Root);
        var surface = (FileManagerSurface)app.Root;
        VisualSnapshotRenderer.Render(surface, 120, 30);
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.Preview is not null; }, 5000));
        Assert.AreEqual("preview text", surface.Preview!.Text);
        Assert.AreEqual(fixture.Root, app.CurrentPath);
        fixture.File("two.txt", "another file");
        Assert.IsTrue(SpinWait.SpinUntil(() => { app.Tick(); return surface.Model.Entries.Count == 2; }, 5000));
        await Task.CompletedTask;
    }
    [TestMethod]
    public void RichAndPlainPreviewsRenderInWideAndCompactLayouts() {
        foreach (var kind in new[] { PreviewKind.Markdown, PreviewKind.Text, PreviewKind.Directory }) {
            foreach (var size in new[] { (150, 36), (80, 24), (35, 12), (20, 5) }) {
                var surface = new FileManagerSurface();
                surface.SetPreview(new("readme.md", kind, "# Heading\n\nA **beautiful** preview\n\n- One\n- Two\n"));
                Assert.AreEqual(size.Item1, VisualSnapshotRenderer.Render(surface, size.Item1, size.Item2).Width);
            }
        }
    }

    public TestContext TestContext {
        get;
        set;
    }
}
