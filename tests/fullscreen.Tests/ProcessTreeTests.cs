using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class ProcessTreeTests {
    private static ProcessRow[] Family() =>
        [
        new(1, "parent", 1, 100, 1, ParentId: 0, UserName: "root", StartTimeTicks: 10),
        new(2, "other-root", 8, 200, 1, ParentId: 0, StartTimeTicks: 10),
        new(3, "child", 100, 300, 1, ParentId: 1, UserName: "alice", StartTimeTicks: 20),
        new(4, "grandchild", 5, 400, 1, ParentId: 3, UserName: "alice", StartTimeTicks: 30),
        new(5, "second-child", 2, 500, 1, ParentId: 1, StartTimeTicks: 20)
    ];
    private static int[] Ids(ProcessView view) =>
        [.. view.Rows.ToArray().Select(row => row.Id)];

    private static readonly int[] expected = [2, 1, 3, 4, 5];
    private static readonly int[] expectedArray = [2, 1];

    [TestMethod]
    public void TreeSortsSiblingsAndCollapsesWholeBranch() {
        var rows = Family();
        var view = new ProcessView { Tree = true, Sort = ProcessSort.Cpu };
        view.Update(rows);
        CollectionAssert.AreEqual(expected, Ids(view));
        Assert.AreEqual(2, view.DepthAt(3));
        view.SelectedId = 1;
        Assert.IsTrue(view.ToggleSelected());
        view.Update(rows);
        CollectionAssert.AreEqual(expectedArray, Ids(view));
        Assert.AreEqual(1, view.SelectedId);
        Assert.IsTrue(view.IsCollapsedAt(1));
        Assert.IsTrue(view.CollapseSelected(false));
        view.Update(rows);
        Assert.AreEqual(5, view.Count);
    }

    private static readonly int[] expectedArray0 = [1, 3, 4];
    private static readonly int[] expectedArray1 = [1, 2];
    private static readonly int[] expectedArray2 = [1, 3, 4];

    [TestMethod]
    public void FilterRetainsAncestorsAndTemporarilyOpensCollapsedBranches() {
        var view = new ProcessView { Tree = true, Sort = ProcessSort.Id, Ascending = true };
        var rows = Family();
        view.Update(rows);
        view.SelectedId = 1;
        view.ToggleSelected();
        view.Update(rows);
        view.Filter = "grandchild";
        view.Update(rows);
        CollectionAssert.AreEqual(expectedArray0, Ids(view));
        Assert.IsFalse(view.IsCollapsedAt(0));
        view.Filter = "";
        view.Update(rows);
        CollectionAssert.AreEqual(expectedArray1, Ids(view));
        view.Filter = "ALICE";
        view.Update(rows);
        CollectionAssert.AreEqual(expectedArray2, Ids(view));
    }

    private static readonly int[] expectedArray3 = [1, 2, 3, 4, 5, 6];

    [TestMethod]
    public void MissingParentsCyclesAndRecycledParentIdsRemainVisible() {
        var view = new ProcessView { Tree = true, Sort = ProcessSort.Id, Ascending = true };
        view.Update([
            new(1, "cycle-a", 0, 0, 1, ParentId: 2), new(2, "cycle-b", 0, 0, 1, ParentId: 1),
            new(3, "orphan", 0, 0, 1, ParentId: 999), new(4, "self", 0, 0, 1, ParentId: 4),
            new(5, "recycled-parent", 0, 0, 1, StartTimeTicks: 100), new(6, "older-child", 0, 0, 1, ParentId: 5, StartTimeTicks: 50)
        ]);
        Assert.AreEqual(6, view.Count);
        CollectionAssert.AreEquivalent(expectedArray3, Ids(view));
        int child = Array.IndexOf(Ids(view), 6);
        Assert.AreEqual(0, view.DepthAt(child));
    }
    [TestMethod]
    public void CollapseStateDoesNotSurviveProcessIdReuse() {
        var view = new ProcessView { Tree = true, Sort = ProcessSort.Id, Ascending = true };
        var rows = Family();
        view.Update(rows);
        view.SelectedId = 1;
        view.ToggleSelected();
        rows[0] = rows[0] with {
            StartTimeTicks = 11
        };
        view.Update(rows);
        Assert.AreEqual(5, view.Count);
        Assert.IsFalse(view.IsCollapsedAt(0));
    }
    [TestMethod]
    public void DeepTreeIsIterativeAndReusesProjectionBuffers() {
        var rows = Enumerable.Range(1, 10000).Select(i => new ProcessRow(i, "process", 0, 1, 1, ParentId: i - 1)).ToArray();
        var view = new ProcessView { Tree = true, Sort = ProcessSort.Id, Ascending = true };
        view.Update(rows);
        view.Update(rows);
        long before = GC.GetAllocatedBytesForCurrentThread();
        view.Update(rows);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.AreEqual(10000, view.Count);
        Assert.AreEqual(9999, view.DepthAt(9999));
    }
    [TestMethod]
    public void ColumnSelectionPreservesOrderAndRejectsAnEmptyTable() {
        var columns = new ProcessColumns();
        columns.Configure([ProcessColumn.UserName, ProcessColumn.Id, ProcessColumn.Name], [ProcessColumn.Id], false);
        CollectionAssert.AreEqual(new[] { ProcessColumn.UserName, ProcessColumn.Name }, columns.Selected.ToArray());
        Assert.Throws<ArgumentException>(() => columns.Configure([ProcessColumn.Id, ProcessColumn.Id], null, false));
        Assert.Throws<ArgumentException>(() => columns.Configure([ProcessColumn.Id], [ProcessColumn.Id], false));
        columns.Configure([ProcessColumn.Id], null, true);
        Assert.IsTrue(columns.Contains(ProcessColumn.Name));
        Assert.IsFalse(columns.Toggle(ProcessColumn.Name, true));
        Assert.IsTrue(columns.Toggle(ProcessColumn.UserName, true));
        Assert.IsTrue(columns.Contains(ProcessColumn.UserName));
    }
    [TestMethod]
    public void EffectiveUidIsDistinctFromRealUid() {
        Assert.IsTrue(ProcessOwnerReader.TryReadEffectiveUid("Name:\tprocess\nUid:\t1000\t2000\t3000\t4000\n"u8.ToArray(), out uint effective));
        Assert.AreEqual(2000u, effective);
        Assert.IsTrue(ProcessOwnerReader.TryReadEffectiveUid("Uid: 1000 2000 3000 4000"u8.ToArray(), out effective));
        Assert.AreEqual(2000u, effective);
        Assert.IsFalse(ProcessOwnerReader.TryReadEffectiveUid("Uid:\t1000\n"u8.ToArray(), out _));
    }
}
