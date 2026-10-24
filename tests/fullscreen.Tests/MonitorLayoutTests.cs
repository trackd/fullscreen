using fullscreen.Charts;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class MonitorLayoutTests {
    [TestMethod]
    [DataRow(MonitorView.Gpu)]
    [DataRow(MonitorView.Network)]
    public void DetailChartsUseSpareHeightWhileKeepingDeviceRowsVisible(MonitorView view) {
        var surface = new MonitorSurface { View = view, MinHeight = 44, MaxHeight = 44 };
        var frame = new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch, GpuCount = 1, NetworkCount = 1, Gpus = new GpuRow[10] };
        for (int i = 0; i < 10; i++) {
            frame.Gpus[i] = new("fixture" + i, "test", 40, 1024, 2048, 55);
            frame.Networks[i] = new("id" + i, "fixture" + i, 1024, 512, 0, 0, 0, 1000000);
        }
        surface.SetFrame(frame);
        VisualSnapshotRenderer.Render(surface, 140, 44);
        var plots = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.Bounds.Width > 0).ToArray();
        Assert.HasCount(2, plots);
        int sparseHeight = plots[0].Bounds.Height;
        Assert.IsGreaterThan(20, sparseHeight, "A one-row device table should leave most height available for the plots.");
        frame.GpuCount = frame.NetworkCount = 10;
        var crowded = new MonitorSurface { View = view, MinHeight = 44, MaxHeight = 44 };
        crowded.SetFrame(frame);
        string snapshot = string.Join('\n', VisualSnapshotRenderer.Render(crowded, 140, 44).ToMarkupLines());
        int crowdedHeight = crowded.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().First(plot => plot.Bounds.Width > 0).Bounds.Height;
        Assert.IsLessThan(sparseHeight, crowdedHeight, "More devices must reserve space for the table.");
        Assert.Contains("fixture9", snapshot, "Resizing graphs must leave all ten device rows visible.");
    }

    [TestMethod]
    [DataRow(40, false)]
    [DataRow(41, false)]
    [DataRow(40, true)]
    [DataRow(41, true)]
    public void DashboardUsesOddAndEvenHeightsWithoutAnUnusedBottomStrip(int height, bool minimal) {
        var surface = new MonitorSurface { View = MonitorView.Dashboard, Minimal = minimal, MinHeight = height, MaxHeight = height };
        surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch, CpuPercent = 50 });
        VisualSnapshotRenderer.Render(surface, 140, height);
        var plots = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.Bounds.Width > 0).ToArray();
        Assert.HasCount(5, plots);
        Assert.AreEqual(height - (minimal ? 3 : 4), plots.Max(plot => plot.Bounds.Bottom));
    }

    [TestMethod]
    public void ProcessSummariesLeaveMoreSpaceForTheProcessRows() {
        var surface = new MonitorSurface { MinHeight = 44, MaxHeight = 44 };
        surface.Model.Sort = ProcessSort.Id;
        surface.Model.Ascending = true;
        surface.Model.Update(Enumerable.Range(0, 100).Select(i => new ProcessRow(i + 1, $"worker{i:D3}", i, 1024, 1)).ToArray());
        string snapshot = string.Join('\n', VisualSnapshotRenderer.Render(surface, 140, 44).ToMarkupLines());
        Assert.Contains("worker027", snapshot, "Compact summary cards should leave room for at least 28 process rows.");
    }
}
