using fullscreen.Development;

namespace fullscreen.Tests;

public sealed partial class DeveloperTests {
    private static DeveloperOperation Action(GitBackend backend, int view, DeveloperItem? item, string label) =>
        backend.Operations(view, item).Single(action => action.Label == label);
    private static async Task Seed(GitFixture repo) {
        await repo.Init();
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "base\n");
        await repo.Git("add", "base.data");
        await repo.Git("commit", "-m", "base");
    }
    [TestMethod]
    public void WorktreeParserPreservesSpacesNewlinesLocksAndDetachedState() {
        var trees = GitBackend.ParseWorktrees("worktree C:/main dir\0HEAD aabb\0branch refs/heads/main\0\0worktree C:/linked\nname\0HEAD ccdd\0detached\0locked offline\nreason\0\0worktree C:/gone\0HEAD eeff\0branch refs/heads/topic\0prunable gitdir missing\0\0");
        Assert.HasCount(3, trees);
        Assert.IsTrue(trees[0].Main);
        Assert.AreEqual("C:/linked\nname", trees[1].Path);
        Assert.AreEqual("offline\nreason", trees[1].Locked);
        Assert.IsTrue(trees[1].Detached);
        Assert.AreEqual("gitdir missing", trees[2].Prunable);
    }
    [TestMethod]
    public async Task WorktreesSupportCreateLockMoveOpenAndCleanRemoval() {
        using var repo = new GitFixture();
        await Seed(repo);
        File.AppendAllText(Path.Combine(repo.Root, ".git", "info", "exclude"), "\nworktrees/\n");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(5, 0, default);
        string linked = Path.Combine(repo.Root, "worktrees", "feature-one");
        await Action(backend, 5, null, "Add worktree on new branch…").Execute(linked, default);
        page = await backend.LoadAsync(5, 0, default);
        Assert.HasCount(2, page.Items);
        var item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        Assert.AreEqual("feature-one", ((GitWorktree)item.State!).Branch);
        await Action(backend, 5, item, "Lock worktree…").Execute("keep offline", default);
        page = await backend.LoadAsync(5, 0, default);
        item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        Assert.AreEqual("keep offline", ((GitWorktree)item.State!).Locked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 5, item, "Remove clean worktree").Execute("", default));
        await Action(backend, 5, item, "Unlock worktree").Execute("", default);
        page = await backend.LoadAsync(5, 0, default);
        item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        string moved = Path.Combine(repo.Root, "worktrees", "moved dir");
        await Action(backend, 5, item, "Move worktree…").Execute(moved, default);
        page = await backend.LoadAsync(5, 0, default);
        item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        Assert.IsTrue(Directory.Exists(moved));
        Assert.IsFalse(Directory.Exists(linked));
        File.WriteAllText(Path.Combine(moved, "untracked.data"), "keep me");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 5, item, "Remove clean worktree").Execute("", default));
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(moved, "untracked.data")));
        File.Delete(Path.Combine(moved, "untracked.data"));
        var open = Action(backend, 5, item, "Open worktree");
        Assert.IsTrue(open.ReadOnly);
        Assert.AreEqual(0, open.TargetView);
        await open.Execute("", default);
        var changes = await backend.LoadAsync(0, 0, default);
        Assert.Contains("feature-one", changes.Summary);
        Assert.Contains("moved dir", backend.Title);
        page = await backend.LoadAsync(5, 0, default);
        var main = page.Items.Single(row => row.State is GitWorktree tree && tree.Main);
        Assert.IsFalse(backend.Operations(5, main).Any(action => action.Label == "Remove clean worktree"));
        await Action(backend, 5, main, "Open worktree").Execute("", default);
        page = await backend.LoadAsync(5, 0, default);
        item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        await Action(backend, 5, item, "Remove clean worktree").Execute("", default);
        Assert.IsFalse(Directory.Exists(moved));
        Assert.HasCount(1, (await backend.LoadAsync(5, 0, default)).Items);
        Assert.IsTrue((await backend.LoadAsync(3, 0, default)).Items.Any(row => row.Key == "feature-one"));
        var branches = await backend.LoadAsync(3, 0, default);
        var existing = branches.Items.Single(row => row.Key == "feature-one");
        await Action(backend, 3, existing, "Add worktree for branch…").Execute(moved, default);
        page = await backend.LoadAsync(5, 0, default);
        item = page.Items.Single(row => row.State is GitWorktree tree && !tree.Main);
        await Action(backend, 5, item, "Remove clean worktree").Execute("", default);
        string detached = Path.Combine(repo.Root, "worktrees", "detached");
        await backend.LoadAsync(5, 0, default);
        await Action(backend, 5, null, "Add detached worktree…").Execute(detached, default);
        page = await backend.LoadAsync(5, 0, default);
        Assert.IsTrue(page.Items.Any(row => row.State is GitWorktree tree && tree.Detached));
        await Assert.ThrowsAsync<ArgumentException>(() => Action(backend, 5, null, "Add detached worktree…").Execute(Path.Combine(repo.Root, ".git", "nested-worktree"), default));
    }
    [TestMethod]
    public async Task BranchesRenameDeleteCompareAndRejectStaleReferences() {
        using var repo = new GitFixture();
        await Seed(repo);
        await repo.Git("branch", "topic");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(3, 0, default);
        var topic = page.Items.Single(row => row.Key == "topic");
        Assert.Contains("DIFF SUMMARY", await Action(backend, 3, topic, "Compare with current HEAD").Execute("", default));
        await Action(backend, 3, topic, "Rename branch…").Execute("renamed-topic", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 3, topic, "Delete merged branch").Execute("", default));
        page = await backend.LoadAsync(3, 0, default);
        topic = page.Items.Single(row => row.Key == "renamed-topic");
        await Action(backend, 3, topic, "Delete merged branch").Execute("", default);
        Assert.IsFalse((await backend.LoadAsync(3, 0, default)).Items.Any(row => row.Key == "renamed-topic"));
    }
    [TestMethod]
    public async Task RemoteBranchesSupportTrackingAndUpstreamChanges() {
        using var repo = new GitFixture();
        await Seed(repo);
        await repo.Git("remote", "add", "origin", ".");
        await repo.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(3, 0, default);
        var remote = page.Items.Single(row => row.State is GitBranch branch && branch.Remote);
        await Action(backend, 3, remote, "Create tracking branch…").Execute("tracked", default);
        page = await backend.LoadAsync(3, 0, default);
        var tracked = page.Items.Single(row => row.Key == "tracked");
        Assert.AreEqual("origin/main", ((GitBranch)tracked.State!).Upstream);
        await Action(backend, 3, tracked, "Unset upstream").Execute("", default);
        page = await backend.LoadAsync(3, 0, default);
        tracked = page.Items.Single(row => row.Key == "tracked");
        await Action(backend, 3, tracked, "Set upstream…").Execute("origin/main", default);
        Assert.AreEqual("origin/main", ((GitBranch)(await backend.LoadAsync(3, 0, default)).Items.Single(row => row.Key == "tracked").State!).Upstream);
        File.AppendAllText(Path.Combine(repo.Root, "base.data"), "ahead\n");
        await repo.Git("commit", "-am", "ahead");
        page = await backend.LoadAsync(3, 0, default);
        Assert.Contains("ahead 1", ((GitBranch)page.Items.Single(row => row.Key == "tracked").State!).Tracking);
    }
    [TestMethod]
    public async Task StashesSaveApplyPopAndRefuseShiftedEntries() {
        using var repo = new GitFixture();
        await Seed(repo);
        File.AppendAllText(Path.Combine(repo.Root, "base.data"), "saved\n");
        File.WriteAllText(Path.Combine(repo.Root, "untracked.data"), "untracked");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        await backend.LoadAsync(0, 0, default);
        await Action(backend, 0, null, "Stash changes…").Execute("first stash", default);
        Assert.AreEqual("", (await repo.Git("status", "--porcelain=v1")).Trim(), "Saving a stash should leave this fixture clean.");
        var page = await backend.LoadAsync(6, 0, default);
        var first = page.Items.Single();
        Assert.Contains("+saved", await backend.DetailAsync(6, first, default));
        await Action(backend, 6, first, "Apply stash").Execute("", default);
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, "untracked.data")));
        await backend.LoadAsync(0, 0, default);
        await Action(backend, 0, null, "Stash changes…").Execute("second stash", default);
        await backend.LoadAsync(6, 0, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 6, first, "Drop stash").Execute("", default));
        page = await backend.LoadAsync(6, 0, default);
        var latest = page.Items.First();
        await Action(backend, 6, latest, "Pop stash").Execute("", default);
        Assert.HasCount(1, (await backend.LoadAsync(6, 0, default)).Items);
    }
    [TestMethod]
    public async Task TagsAndStageAllOperateOnTheSelectedCommitAndPreserveFiles() {
        using var repo = new GitFixture();
        await Seed(repo);
        var backend = new GitBackend(repo.Root, new CommandRunner());
        await backend.LoadAsync(7, 0, default);
        await Action(backend, 7, null, "Create annotated tag…").Execute("v1.0-test", default);
        var tags = await backend.LoadAsync(7, 0, default);
        Assert.HasCount(1, tags.Items);
        Assert.AreEqual("Annotated", tags.Items[0].Cells[1]);
        Assert.Contains("v1.0-test", await backend.DetailAsync(7, tags.Items[0], default));
        await Action(backend, 7, tags.Items[0], "Delete local tag").Execute("", default);
        Assert.IsEmpty((await backend.LoadAsync(7, 0, default)).Items);
        File.AppendAllText(Path.Combine(repo.Root, "base.data"), "extra\n");
        File.WriteAllText(Path.Combine(repo.Root, "new.data"), "new");
        await backend.LoadAsync(0, 0, default);
        await Action(backend, 0, null, "Stage all changes").Execute("", default);
        Assert.HasCount(2, (await backend.LoadAsync(1, 0, default)).Items);
        await Action(backend, 1, null, "Unstage all changes").Execute("", default);
        Assert.IsEmpty((await backend.LoadAsync(1, 0, default)).Items);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(repo.Root, "new.data")));
    }
    [TestMethod]
    public async Task MergeConflictsExposeAbortAndPreserveBothBranches() {
        using var repo = new GitFixture();
        await Seed(repo);
        await repo.Git("switch", "-c", "topic");
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "topic\n");
        await repo.Git("commit", "-am", "topic change");
        await repo.Git("switch", "main");
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "main\n");
        await repo.Git("commit", "-am", "main change");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(3, 0, default);
        var topic = page.Items.Single(row => row.Key == "topic");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 3, topic, "Merge into current branch").Execute("", default));
        await backend.LoadAsync(0, 0, default);
        Assert.IsTrue(backend.Operations(0, null).Any(action => action.Label == "Continue merge"));
        await Action(backend, 0, null, "Abort merge").Execute("", default);
        Assert.AreEqual("main\n", File.ReadAllText(Path.Combine(repo.Root, "base.data")));
        Assert.HasCount(2, (await backend.LoadAsync(3, 0, default)).Items);
        page = await backend.LoadAsync(3, 0, default);
        topic = page.Items.Single(row => row.Key == "topic");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 3, topic, "Merge into current branch").Execute("", default));
        Assert.IsTrue(backend.Operations(3, null).Any(action => action.Label == "Continue merge"), "Conflict recovery should be available without waiting for a refresh.");
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "resolved\n");
        await repo.Git("add", "base.data");
        await Action(backend, 3, null, "Continue merge").Execute("", default);
        await backend.LoadAsync(0, 0, default);
        Assert.IsFalse(backend.Operations(0, null).Any(action => action.Label == "Abort merge"));
    }
    [TestMethod]
    public async Task HistoryCanCherryPickAndRevertACommit() {
        using var repo = new GitFixture();
        await Seed(repo);
        await repo.Git("switch", "-c", "topic");
        File.WriteAllText(Path.Combine(repo.Root, "topic.data"), "topic");
        await repo.Git("add", "topic.data");
        await repo.Git("commit", "-m", "topic");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var history = await backend.LoadAsync(2, 0, default);
        var commit = history.Items[0];
        await repo.Git("switch", "main");
        await backend.LoadAsync(2, 0, default);
        await Action(backend, 2, commit, "Cherry-pick commit").Execute("", default);
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, "topic.data")));
        history = await backend.LoadAsync(2, 0, default);
        await Action(backend, 2, history.Items[0], "Revert commit").Execute("", default);
        Assert.IsFalse(File.Exists(Path.Combine(repo.Root, "topic.data")));
    }
    [TestMethod]
    public async Task RebaseCanCompleteOrAbortAConflictingReplay() {
        using var repo = new GitFixture();
        await Seed(repo);
        await repo.Git("switch", "-c", "topic");
        File.WriteAllText(Path.Combine(repo.Root, "topic.data"), "topic\n");
        await repo.Git("add", "topic.data");
        await repo.Git("commit", "-m", "topic file");
        await repo.Git("switch", "main");
        File.WriteAllText(Path.Combine(repo.Root, "main.data"), "main\n");
        await repo.Git("add", "main.data");
        await repo.Git("commit", "-m", "main file");
        await repo.Git("switch", "topic");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        var page = await backend.LoadAsync(3, 0, default);
        await Action(backend, 3, page.Items.Single(row => row.Key == "main"), "Rebase current branch onto selected").Execute("", default);
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, "main.data")));
        Assert.IsTrue(File.Exists(Path.Combine(repo.Root, "topic.data")));
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "topic edit\n");
        await repo.Git("commit", "-am", "topic base edit");
        await repo.Git("switch", "main");
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "main edit\n");
        await repo.Git("commit", "-am", "main base edit");
        await repo.Git("switch", "topic");
        page = await backend.LoadAsync(3, 0, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 3, page.Items.Single(row => row.Key == "main"), "Rebase current branch onto selected").Execute("", default));
        await Action(backend, 3, null, "Abort rebase").Execute("", default);
        Assert.AreEqual("topic edit\n", File.ReadAllText(Path.Combine(repo.Root, "base.data")));
        page = await backend.LoadAsync(3, 0, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(backend, 3, page.Items.Single(row => row.Key == "main"), "Rebase current branch onto selected").Execute("", default));
        File.WriteAllText(Path.Combine(repo.Root, "base.data"), "resolved\n");
        await repo.Git("add", "base.data");
        await Action(backend, 3, null, "Continue rebase").Execute("", default);
        Assert.AreEqual("topic", (await repo.Git("branch", "--show-current")).Trim());
    }
    [TestMethod]
    public async Task TagsPushOnlyTheSelectedTagToALocalRemote() {
        using var repo = new GitFixture();
        using var remote = new GitFixture();
        await Seed(repo);
        await repo.Git("init", "--bare", remote.Root);
        await repo.Git("--git-dir", remote.Root, "config", "core.hooksPath", Path.Combine(remote.Root, "no-hooks"));
        await repo.Git("remote", "add", "origin", remote.Root);
        await repo.Git("tag", "--annotate", "--message", "extra", "v-extra");
        var backend = new GitBackend(repo.Root, new CommandRunner());
        await backend.LoadAsync(7, 0, default);
        await Action(backend, 7, null, "Create annotated tag…").Execute("v-selected", default);
        var tags = await backend.LoadAsync(7, 0, default);
        var selected = tags.Items.Single(item => item.Key == "v-selected");
        await Action(backend, 7, selected, "Push selected tag…").Execute("origin", default);
        string refs = await repo.Git("--git-dir", remote.Root, "tag", "--list");
        Assert.AreEqual("v-selected", refs.Trim());
    }
}
