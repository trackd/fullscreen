using System.Diagnostics;
using System.Reflection;
using System.Text;
using fullscreen.Files;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class CommanderTests {
    [TestMethod]
    [DoNotParallelize]
    public async Task ViewerOpensWhileTheOtherPaneIsRefreshing() {
        using var fixture = new Fixture();
        string left = fixture.Child("left"), right = fixture.Child("right");
        File.WriteAllText(Path.Combine(left, "demo.ps1"), "Get-Process\n");
        using var files = new CommanderApp(left, right);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 32));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(files.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            files.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(files.Surface.Active);
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return !files.Busy; }, 5000));
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Down });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return files.Surface.Active.Model.Selected?.Name == "demo.ps1"; }, 5000));
            files.Surface.Other.Busy = true; // Hold an unrelated pane refresh across F3 delivery.
            backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F3 });
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return terminal.FocusedElement is XenoAtom.Terminal.UI.Controls.ScrollViewer; }, 3000), "Other-pane refresh must not swallow an active-pane viewer command.");
            Assert.IsTrue(files.Surface.Other.Busy);
        }
        finally {
            files.Surface.Other.Busy = false;
            typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        }
    }
    [TestMethod]
    [DoNotParallelize]
    public async Task TabAndFunctionKeysUseTheOppositePaneAndConfirmTransfers() {
        using var fixture = new Fixture();
        string left = fixture.Child("left"), right = fixture.Child("right");
        string source = Path.Combine(left, "demo.ps1");
        File.WriteAllText(source, "Get-Process\n");
        using var files = new CommanderApp(left, right);
        using var backend = new InMemoryTerminalBackend(new TerminalSize(120, 32));
        using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
        await using var terminal = new TerminalApp(files.Root, session.Instance, new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen, EnableMouse = true });
        long timestamp = Stopwatch.GetTimestamp();
        void Tick() {
            files.Tick();
            timestamp += Stopwatch.Frequency / 100;
            typeof(TerminalApp).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, [timestamp]);
        }
        void Send(TerminalEvent ev, Func<bool>? observed = null) {
            backend.PushEvent(ev);
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return observed?.Invoke() ?? true; }, 5000), $"Commander input was not delivered. Active={files.Surface.ActivePane}, activeFocus={files.Surface.Active.HasFocus}, otherFocus={files.Surface.Other.HasFocus}. {backend.GetOutText()[^Math.Min(400, backend.GetOutText().Length)..]}");
        }
        typeof(TerminalApp).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null);
        terminal.Focus(files.Surface.Active);
        try {
            Assert.IsTrue(SpinWait.SpinUntil(() => { Tick(); return !files.Surface.Active.Busy && !files.Surface.Other.Busy; }, 5000));
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => files.Surface.ActivePane == 1);
            Assert.AreEqual(right, files.CurrentPath);
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab }, () => files.Surface.ActivePane == 0);
            Send(new TerminalKeyEvent { Key = TerminalKey.Down }, () => files.Surface.Active.Model.Selected?.Name == "demo.ps1");
            Send(new TerminalKeyEvent { Key = TerminalKey.F5 }, () => backend.GetOutText().Contains("Confirm file operation", StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(Path.Combine(right, "demo.ps1")), "Copy must wait for confirmation.");
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => File.Exists(Path.Combine(right, "demo.ps1")) && !files.Surface.Active.Busy);
            Assert.AreEqual("Get-Process\n", File.ReadAllText(Path.Combine(right, "demo.ps1")));
            Assert.IsTrue(File.Exists(source));
            Send(new TerminalKeyEvent { Key = TerminalKey.F3 }, () => backend.GetOutText().Contains("demo.ps1", StringComparison.Ordinal) && !files.Surface.Active.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Escape }, () => files.Surface.Active.HasFocus);
            Send(new TerminalKeyEvent { Key = TerminalKey.Delete }, () => terminal.FocusedElement is XenoAtom.Terminal.UI.Controls.Button);
            Assert.IsTrue(File.Exists(source), "Delete must wait for confirmation.");
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => files.Surface.Active.HasFocus);
            Assert.IsTrue(File.Exists(source), "Enter on the default Cancel button must preserve the file.");
            Send(new TerminalKeyEvent { Key = TerminalKey.Delete }, () => terminal.FocusedElement is XenoAtom.Terminal.UI.Controls.Button);
            Send(new TerminalKeyEvent { Key = TerminalKey.Tab });
            Send(new TerminalKeyEvent { Key = TerminalKey.Enter }, () => !File.Exists(source));
            Assert.IsTrue(File.Exists(Path.Combine(right, "demo.ps1")), "Delete must preserve the other pane's copy.");
            Send(new TerminalKeyEvent { Key = TerminalKey.F10 }, () => !files.Tick());
        }
        finally { typeof(TerminalApp).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(terminal, null); }
    }
    [TestMethod]
    public void TrashRetainsTheEntireDirectoryAndCanBeRestoredWithoutOverwrite() {
        using var fixture = new Fixture();
        string original = fixture.Child("keep");
        File.WriteAllText(Path.Combine(original, "data.txt"), "keep me");
        string stored = FileOperations.Trash(original);
        Assert.IsFalse(Directory.Exists(original));
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(stored, "data.txt")));
        Assert.IsTrue(stored.StartsWith(Path.Combine(fixture.Root, ".fullscreen-trash"), StringComparison.Ordinal));
        FileOperations.Move(stored, original);
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(original, "data.txt")));
        Assert.Throws<IOException>(() => FileOperations.Trash(Path.Combine(fixture.Root, ".fullscreen-trash")));
    }
    [TestMethod]
    public void ParentNavigationEntryCannotBeMarkedOrUsedAsAnOperationSource() {
        var model = new FileManagerModel { Descending = true };
        model.Apply(new("/parent/child", [new("/parent", "..", true, false, 0, default, false), new("/parent/child/a", "a", false, false, 1, default, false)], []));
        Assert.AreEqual("..", model.Selected!.Value.Name);
        model.ToggleMark();
        Assert.IsEmpty(model.OperationPaths());
        model.ToggleAll();
        Assert.HasCount(1, model.Marked);
        model.ToggleAll();
        Assert.IsEmpty(model.Marked);
    }
    [TestMethod]
    public void CommanderRendersBothPanesInNormalAndMinimalLayouts() {
        var left = new FileManagerSurface();
        var right = new FileManagerSurface();
        var surface = new CommanderSurface(left, right);
        left.Model.Apply(new("left", [new("left/a", "a.cs", false, false, 50, default, false)], []));
        right.Model.Apply(new("right", [new("right/b", "b.ps1", false, false, 100, default, false)], []));
        foreach (bool minimal in new[] { false, true }) {
            surface.Minimal = minimal;
            foreach (var size in new[] { (150, 40), (80, 24), (40, 10), (20, 5) }) {
                Assert.AreEqual(size.Item1, VisualSnapshotRenderer.Render(surface, size.Item1, size.Item2).Width);
            }
        }
        VisualSnapshotRenderer.Render(SyntaxPreview.Code("var a = 1;\n```\n", "a.cs"), 80, 20);
    }
    private sealed class Fixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-Commander-" + Guid.NewGuid().ToString("N"));
        public Fixture() =>
            Directory.CreateDirectory(Root);
        public string Child(string name) {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }
        public void Dispose() {
            string full = Path.GetFullPath(Root);
            if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) || !Path.GetFileName(full).StartsWith("Fullscreen-Commander-", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Unsafe fixture path.");
            }

            Directory.Delete(full, true);
        }
    }
}
