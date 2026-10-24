using fullscreen.Development;

namespace fullscreen.Tests;

public sealed partial class DeveloperTests {
    [TestMethod]
    public async Task SelectedHunkStagesAndUnstagesWithoutChangingWorktree() {
        using var repo = new GitFixture();
        await repo.Init();
        string path = Path.Combine(repo.Root, "hunks.data");
        string[] original = [.. Enumerable.Range(1, 40).Select(i => $"line {i}")];
        File.WriteAllText(path, string.Join('\n', original) + "\n");
        await repo.Git("add", ".");
        await repo.Git("commit", "-m", "seed");
        string[] edited = (string[])original.Clone();
        edited[2] = "FIRST";
        edited[35] = "SECOND";
        string worktree = string.Join('\n', edited) + "\n";
        File.WriteAllText(path, worktree);
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(0, 0, default);
        var item = page.Items.Single();
        string detail = await backend.DetailAsync(0, item, default);
        Assert.Contains("HUNK 2 / 2", detail);
        await Action(backend, 0, item, "Stage hunk…").Execute("2", default);
        string staged = await repo.Git("diff", "--cached");
        Assert.Contains("+SECOND", staged);
        Assert.IsFalse(staged.Contains("+FIRST", StringComparison.Ordinal));
        Assert.AreEqual(worktree, File.ReadAllText(path));
        page = await backend.LoadAsync(1, 0, default);
        item = page.Items.Single();
        await backend.DetailAsync(1, item, default);
        await Action(backend, 1, item, "Unstage hunk…").Execute("1", default);
        Assert.AreEqual("", await repo.Git("diff", "--cached"));
        Assert.AreEqual(worktree, File.ReadAllText(path));
    }
    [TestMethod]
    public async Task HunkActionRejectsChangesAfterReview() {
        using var repo = new GitFixture();
        await Seed(repo);
        string path = Path.Combine(repo.Root, "base.data");
        File.WriteAllText(path, "reviewed\n");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(0, 0, default);
        var item = page.Items.Single();
        await backend.DetailAsync(0, item, default);
        var action = Action(backend, 0, item, "Stage hunk…");
        File.WriteAllText(path, "changed after review\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => action.Execute("1", default));
        Assert.AreEqual("", await repo.Git("diff", "--cached"));
    }
    [TestMethod]
    public async Task HunkActionRefusesNonUtf8Patches() {
        using var repo = new GitFixture();
        await repo.Init();
        string path = Path.Combine(repo.Root, "legacy.txt");
        File.WriteAllText(path, "line\n");
        await repo.Git("add", ".");
        await repo.Git("commit", "-m", "seed");
        File.WriteAllBytes(path, [.. "line\ncaf"u8, 0xE9, (byte)'\n']);
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(0, 0, default);
        var item = page.Items.Single();
        await backend.DetailAsync(0, item, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 0, item, "Stage hunk…").Execute("1", default));
        Assert.AreEqual("", await repo.Git("diff", "--cached"));
    }
}
