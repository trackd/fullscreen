using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class MonitorUserFilteringTests {
    [TestMethod]
    [DataRow("HOST\\Ada", "Active", 1, true)]
    [DataRow("HOST\\Ada", "Disconnected", 2, true)]
    [DataRow("HOST\\Ada", "active", 1, true)]
    [DataRow("HOST\\Ada", "Connected", 1, true)]
    [DataRow("HOST\\Ada", "Listen", 1, true)]
    [DataRow("HOST\\Ada", "Idle", 1, true)]
    [DataRow("HOST\\Ada", "Down", 1, true)]
    [DataRow("HOST\\Ada", "Active", 0, false)]
    [DataRow("HOST\\Ada", "Active", -1, false)]
    [DataRow("", "Active", 1, false)]
    [DataRow(" ", "Active", 1, false)]
    public void WindowsSessionsRequireNamedWtsUsersRegardlessOfState(string account, string state, int id, bool expected) {
        var session = new UserSessionRow(account, "Console", state, "", "", id);
        Assert.AreEqual(expected, session.IsNamedWindowsSession);
        var frame = new CollectorFrame { Platform = "Windows", UserCount = 1, ProcessCount = 1 };
        frame.Users[0] = session;
        frame.Processes[0] = new(1, "worker", 10, 1024, 1, UserName: account);
        var view = new UserView();
        view.Update(frame);
        Assert.HasCount(expected ? 1 : 0, view.Rows);
    }

    [TestMethod]
    public void WindowsProcessOwnershipCannotCreateLoginsAndDisconnectedUsersRemainVisible() {
        var frame = new CollectorFrame { Platform = "Windows", UserCount = 2, ProcessCount = 6 };
        frame.Users[0] = new("HOST\\Ada", "Console", "Active", "", "", 1);
        frame.Users[1] = new("HOST\\Bob", "RDP", "Disconnected", "", "", 2);
        frame.Processes[0] = new(1, "editor", 10, 1024, 1, UserName: "host\\ada");
        frame.Processes[1] = new(2, "system", 90, 8192, 1, UserName: "NT AUTHORITY\\SYSTEM");
        frame.Processes[2] = new(3, "service", 90, 8192, 1, UserName: "HOST\\ServiceAccount");
        frame.Processes[3] = new(4, "font", 90, 8192, 1, UserName: "Font Driver Host\\UMFD-1");
        frame.Processes[4] = new(5, "window", 90, 8192, 1, UserName: "Window Manager\\DWM-1");
        frame.Processes[5] = new(6, "service", 90, 8192, 1, UserName: "NT SERVICE\\Example");
        var view = new UserView();
        view.Update(frame);
        CollectionAssert.AreEqual(new[] { "HOST\\Ada", "HOST\\Bob" }, view.Rows.Select(row => row.UserName).ToArray());
        Assert.AreEqual(0, view.Rows[1].ProcessCount);
        view.SelectedKey = view.Rows[0].Key;
        frame.UserCount = 1;
        frame.Users[0] = frame.Users[1];
        view.Update(frame);
        Assert.AreEqual("HOST\\Bob", Assert.ContainsSingle(view.Rows).UserName);
        Assert.AreEqual("user:HOST\\Bob", view.SelectedKey);
        frame.UserCount = 0;
        view.Update(frame);
        Assert.IsEmpty(view.Rows);
    }

    [TestMethod]
    public void UnixOwnershipRemainsCaseSensitiveWithoutWtsSessionIds() {
        var frame = new CollectorFrame { Platform = "Linux", UserCount = 1, ProcessCount = 2 };
        frame.Users[0] = new("ada", "pts/1", "Logged in", "", "");
        frame.Processes[0] = new(1, "editor", 10, 1024, 1, UserName: "ada");
        frame.Processes[1] = new(2, "other", 1, 512, 1, UserName: "Ada");
        var view = new UserView();
        view.Update(frame);
        Assert.HasCount(2, view.Rows);
        Assert.HasCount(1, view.Rows.Single(row => row.UserName == "ada").Logins);
        Assert.IsEmpty(view.Rows.Single(row => row.UserName == "Ada").Logins);
    }

    [TestMethod]
    [DataRow(ProcessSort.Cpu, false)]
    [DataRow(ProcessSort.Cpu, true)]
    [DataRow(ProcessSort.Memory, false)]
    [DataRow(ProcessSort.Memory, true)]
    [DataRow(ProcessSort.Threads, false)]
    [DataRow(ProcessSort.Threads, true)]
    [DataRow(ProcessSort.Gpu, false)]
    [DataRow(ProcessSort.Gpu, true)]
    [DataRow(ProcessSort.GpuMemory, false)]
    [DataRow(ProcessSort.GpuMemory, true)]
    [DataRow(ProcessSort.CpuTime, false)]
    [DataRow(ProcessSort.CpuTime, true)]
    [DataRow(ProcessSort.Disk, false)]
    [DataRow(ProcessSort.Disk, true)]
    [DataRow(ProcessSort.Network, false)]
    [DataRow(ProcessSort.Network, true)]
    public void GroupsSortByAggregateAndKeepSortedChildrenContiguous(ProcessSort sort, bool ascending) {
        var frame = new CollectorFrame { Platform = "Windows", UserCount = 2, ProcessCount = 3 };
        frame.Users[0] = new("HOST\\Ada", "Console", "Active", "", "", 1);
        frame.Users[1] = new("HOST\\Bob", "RDP", "Disconnected", "", "", 2);
        frame.Processes[0] = Process(1, "HOST\\Ada", 6);
        frame.Processes[1] = Process(2, "HOST\\Ada", 7);
        frame.Processes[2] = Process(3, "HOST\\Bob", 10);
        var view = new UserView { Sort = sort, Ascending = ascending };
        view.Update(frame);
        foreach (var row in view.Rows.ToArray()) {
            view.SelectedKey = row.Key;
            view.Expand(true);
        }
        view.SelectedKey = "user:HOST\\Bob";
        view.Update(frame);
        string expectedFirst = ascending ? "HOST\\Bob" : "HOST\\Ada";
        Assert.AreEqual(expectedFirst, view.Rows[0].UserName);
        var adaProcesses = view.Rows.Where(row => row.UserName == "HOST\\Ada" && row.Process.HasValue).ToArray();
        Assert.AreEqual(ascending ? 1 : 2, adaProcesses[0].Process!.Value.Id);
        CollectionAssert.AreEqual(ascending ? new[] { "HOST\\Bob", "HOST\\Bob", "HOST\\Ada", "HOST\\Ada", "HOST\\Ada" }
            : ["HOST\\Ada", "HOST\\Ada", "HOST\\Ada", "HOST\\Bob", "HOST\\Bob"], view.Rows.Select(row => row.UserName).ToArray());
        Assert.AreEqual("user:HOST\\Bob", view.SelectedKey);
        frame.UserCount = 0;
        view.Update(frame);
        Assert.IsEmpty(view.Rows);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingGroupCountersStayLastInEitherDirection(bool ascending) {
        var frame = new CollectorFrame { Platform = "Windows", UserCount = 2, ProcessCount = 2 };
        frame.Users[0] = new("HOST\\Ada", "Console", "Active", "", "", 1);
        frame.Users[1] = new("HOST\\Bob", "RDP", "Disconnected", "", "", 2);
        frame.Processes[0] = new(1, "unknown", 1, 1024, 1, UserName: "HOST\\Ada");
        frame.Processes[1] = Process(2, "HOST\\Bob", 0);
        var view = new UserView { Sort = ProcessSort.Gpu, Ascending = ascending };
        view.Update(frame);
        Assert.AreEqual("HOST\\Bob", view.Rows[0].UserName);
        Assert.AreEqual("HOST\\Ada", view.Rows[1].UserName);
    }
    private static ProcessRow Process(int id, string account, int value) =>
        new(id, "worker" + id, value, value * 1024, value, GpuPercent: value, GpuMemory: (ulong)value,
            UserName: account, CpuTimeTicks: value, DiskBytesPerSecond: value, NetworkBytesPerSecond: value);
}
