using fullscreen.Monitoring;
using System.Buffers.Binary;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class TaskManagerTests {
    [TestMethod]
    public void AscendingSortMeansAscendingForTextIdsAndCounters() {
        ProcessRow[] rows = [new(1, "alpha", 10, 100, 1, ParentId: 3, UserName: "alice", Status: "Active"), new(2, "zulu", 90, 900, 9, ParentId: 9, UserName: "zoe", Status: "Suspended")];
        foreach (var sort in new[] { ProcessSort.Name, ProcessSort.Id, ProcessSort.ParentId, ProcessSort.UserName, ProcessSort.Status, ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Threads }) {
            var view = new ProcessView { Sort = sort, Ascending = true };
            view.Update(rows);
            Assert.AreEqual(1, view.Rows[0].Id, sort.ToString());
            view.Ascending = false;
            view.Update(rows);
            Assert.AreEqual(2, view.Rows[0].Id, sort.ToString());
        }
    }

    private static readonly int[] expected = [1, 2];

    [TestMethod]
    public void ColumnFiltersCombineAndKeepAncestorPathsVisible() {
        ProcessRow[] rows = [
            new(1, "parent", 0, 10, 1, ParentId: 0, UserName: "bob"),
            new(2, "worker", 20, 100, 1, ParentId: 1, UserName: "alice", CpuTimeTicks: 500),
            new(3, "worker", 5, 100, 1, ParentId: 1, UserName: "alice", CpuTimeTicks: 1000)
        ];
        var view = new ProcessView { Tree = true };
        view.Update(rows);
        view.SelectedId = 1;
        view.ToggleSelected();
        view.Update(rows);
        Assert.AreEqual(1, view.Count);
        view.ColumnFilters[ProcessColumn.UserName] = "alice";
        view.ColumnFilters[ProcessColumn.Cpu] = ">10";
        view.TotalMemory = 1000;
        view.ColumnFilters[ProcessColumn.MemoryPercent] = ">9";
        view.Update(rows);
        CollectionAssert.AreEqual(expected, view.Rows.ToArray().Select(row => row.Id).ToArray());
        Assert.IsFalse(view.IsCollapsedAt(0));
        view.Tree = false;
        view.ColumnFilters.Clear();
        view.Sort = ProcessSort.CpuTime;
        view.Update(rows);
        Assert.AreEqual(3, view.Rows[0].Id);
        Assert.IsFalse(new ProcessColumns().Contains(ProcessColumn.CpuTime));
    }
    [TestMethod]
    public void WindowsStatusRequiresEveryThreadToBeSuspended() {
        byte[] record = new byte[256 + 160];
        for (int i = 0; i < 2; i++) {
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(256 + i * 80 + 68), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(256 + i * 80 + 72), 5);
        }
        Assert.AreEqual("Suspended", ProcessSampler.ReadWindowsStatus(record, 2));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(256 + 80 + 68), 2);
        Assert.AreEqual("Active", ProcessSampler.ReadWindowsStatus(record, 2));
        Assert.IsNull(ProcessSampler.ReadWindowsStatus(record[..255], 2));
    }
    [TestMethod]
    public void LinuxDiskCountsStorageBytesRatherThanReadWriteSyscallBytes() {
        Assert.AreEqual(300L, ProcessSampler.TryReadLinuxDisk("rchar: 99999\nwchar: 88888\nread_bytes: 100\nwrite_bytes: 200\ncancelled_write_bytes: 50\n"u8.ToArray()));
        Assert.IsNull(ProcessSampler.TryReadLinuxDisk("read_bytes: 100\n"u8.ToArray()));
        Assert.IsNull(ProcessSampler.TryReadLinuxDisk("read_bytes: -1\nwrite_bytes: 200\n"u8.ToArray()));
    }
    [TestMethod]
    public void DetailColumnsAreIndependentAndParametersMatchTheirTables() {
        var surface = new MonitorSurface();
        surface.ConfigureDetailColumns([NetworkColumn.Interface, NetworkColumn.Send], null, null, [GpuColumn.Provider]);
        Assert.IsFalse(surface.IsColumnVisible(NetworkColumn.Receive));
        Assert.IsTrue(surface.IsColumnVisible(NetworkColumn.Send));
        Assert.IsFalse(surface.IsColumnVisible(GpuColumn.Provider));
        Assert.IsTrue(surface.IsColumnVisible(GpuColumn.Utilization));
        Assert.Throws<ArgumentException>(() => surface.ConfigureDetailColumns([], null, null, null));
        surface.View = MonitorView.Dashboard;
        surface.OpenColumns();
        Assert.IsFalse(surface.ColumnsOpen);
        foreach (var view in new[] { MonitorView.Processes, MonitorView.Network, MonitorView.Gpu }) {
            surface = new MonitorSurface { View = view };
            surface.OpenColumns();
            foreach (var size in new[] { (150, 36), (80, 24), (35, 12) }) {
                VisualSnapshotRenderer.Render(surface, size.Item1, size.Item2);
            }
        }
    }
    [TestMethod]
    public void NewOptionalMetricsStayLastInBothSortDirections() {
        foreach (var sort in new[] { ProcessSort.Disk, ProcessSort.Network, ProcessSort.Status }) {
            foreach (bool ascending in new[] { true, false }) {
                var view = new ProcessView { Sort = sort, Ascending = ascending };
                view.Update([new(1, "unknown", 0, 0, 0), new(2, "known", 0, 0, 0, Status: "Active", DiskBytesPerSecond: 0, NetworkBytesPerSecond: 0)]);
                Assert.AreEqual(2, view.Rows[0].Id);
            }
        }
    }
}
