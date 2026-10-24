using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class MonitorTests {
    [TestMethod]
    public void RepeatedProjectionReusesBuffersAndSortDelegate() {
        var rows = Enumerable.Range(1, 250).Select(i => new ProcessRow(i, "pwsh", i % 10, i, 1)).ToArray();
        var view = new ProcessView { Filter = "pwsh" };
        for (int i = 0; i < 10; i++) {
            view.Update(rows);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        view.Update(rows);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [TestMethod]
    public void FilterSortAndSelectionSurviveRefresh() {
        ProcessRow[] rows = [new(12, "pwsh", 20, 100, 3), new(34, "dotnet", 50, 200, 4), new(56, "PWSH", 1, 300, 5)];
        var view = new ProcessView { Filter = "pwSH", Sort = ProcessSort.Memory };
        view.Update(rows);
        Assert.AreEqual(2, view.Count);
        Assert.AreEqual(56, view.Rows[0].Id);
        view.SelectedId = 12;
        view.Update(rows);
        Assert.AreEqual(12, view.SelectedId);
        view.Filter = "34";
        view.Update(rows);
        Assert.AreEqual(1, view.Count);
        Assert.AreEqual(34, view.SelectedId);
        view.Filter = "absent";
        view.Update(rows);
        Assert.AreEqual(0, view.Count);
        Assert.AreEqual(-1, view.SelectedId);
    }

    [TestMethod]
    public void NavigationClampsAndKeepsSelectionVisible() {
        var view = new ProcessView { Sort = ProcessSort.Id, Ascending = true };
        view.Update(Enumerable.Range(1, 100).Select(i => new ProcessRow(i, "process", 0, 0, 1)).ToArray());
        view.Move(99, 10);
        Assert.AreEqual(100, view.SelectedId);
        Assert.AreEqual(90, view.Offset);
        view.Move(-1000, 10);
        Assert.AreEqual(1, view.SelectedId);
        Assert.AreEqual(0, view.Offset);
    }

    [TestMethod]
    public void LinuxMemoryParserHandlesWhitespaceAndMissingFields() {
        Assert.AreEqual(1234ul * 1024, SystemSampler.ReadKilobytes("MemTotal:       1234 kB\nMemAvailable: 56 kB\n"u8.ToArray(), "MemTotal:"));
        Assert.IsNull(SystemSampler.ReadKilobytes("MemFree: 42 kB"u8.ToArray(), "MemTotal:"));
        Assert.IsNull(SystemSampler.ReadKilobytes("MemTotal: invalid"u8.ToArray(), "MemTotal:"));
    }

    [TestMethod]
    public void LinuxStatParserHandlesNamesContainingParenthesesAndSpaces() {
        Assert.IsTrue(ProcessSampler.TryReadLinuxStat("42 (a tricky ) name) R 1 2 3 4 5 6 7 8 9 10 100 200 13 14 15 16 8 18 900 1000 25 0"u8.ToArray(), out var stat));
        Assert.AreEqual(300L, stat.CpuTicks);
        Assert.AreEqual(900L, stat.StartTicks);
        Assert.AreEqual(25L, stat.ResidentPages);
        Assert.AreEqual(8, stat.Threads);
        Assert.IsFalse(ProcessSampler.TryReadLinuxStat("42 (short) R 1 2"u8.ToArray(), out _));
    }

    [TestMethod]
    public void LiveSamplerFindsCurrentProcessAndReportsCost() {
        using var sampler = new ProcessSampler();
        sampler.Sample();
        sampler.Sample();
        Assert.IsGreaterThan(0, sampler.Count);
        bool found = false;
        foreach (var row in sampler.Rows) {
            if (row.Id != Environment.ProcessId) {
                continue;
            }

            found = true;
            Assert.IsGreaterThan(0L, row.WorkingSet);
            Assert.IsGreaterThan(0, row.Threads);
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) {
                Assert.IsNotNull(row.UserName);
            }

            if (OperatingSystem.IsWindows() && Environment.Is64BitProcess || OperatingSystem.IsLinux()) {
                Assert.IsNotNull(row.ParentId);
            }
        }
        Assert.IsTrue(found);
        Console.WriteLine($"Sampling {sampler.Count} processes: {sampler.SampleMilliseconds:F2} ms, {sampler.AllocatedBytes:N0} bytes");
        var system = new SystemSampler();
        system.Sample();
        system.Sample();
        Assert.IsGreaterThan(0ul, system.TotalMemory!.Value);
        Assert.IsLessThanOrEqualTo(system.TotalMemory.Value, system.AvailableMemory!.Value);
    }

    [TestMethod]
    [DataRow(120, 40)]
    [DataRow(80, 24)]
    [DataRow(35, 12)]
    [DataRow(20, 5)]
    public void AllViewsRenderAtDifferentSizes(int width, int height) {
        foreach (var mode in Enum.GetValues<MonitorView>()) {
            var surface = new MonitorSurface { View = mode, CpuPercent = 27, TotalMemory = 16ul << 30, AvailableMemory = 8ul << 30 };
            surface.Model.Update([new(12, "testç•ŒðŸ‘‹", 150, 500000000, 20)]);
            var buffer = VisualSnapshotRenderer.Render(surface, width, height);
            Assert.AreEqual(width, buffer.Width);
            Assert.IsTrue(buffer.Height > 0 && buffer.Height <= height);
        }
    }
}
