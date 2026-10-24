using fullscreen.Themes;
using fullscreen.Charts;
using fullscreen.Custom;
using fullscreen.Files;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ChartTests {
    [TestMethod]
    public void MonitorGraphChoiceReachesEveryViewWithNativeFallback() {
        foreach (var kind in MonitorSurface.TimeGraphTypes) {
            foreach (var view in new[] { MonitorView.Dashboard, MonitorView.Processes, MonitorView.Network, MonitorView.Gpu }) {
                var surface = new MonitorSurface { View = view, GraphType = kind };
                surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch, CpuPercent = 50 });
                VisualSnapshotRenderer.Render(surface, 150, 36);
                var plots = surface.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.Bounds.Width > 0).ToArray();
                Assert.IsNotEmpty(plots);
                Assert.IsTrue(plots.All(plot => plot.GraphKind == kind), $"{view} / {kind}");
                Assert.IsTrue(plots.All(chart => !chart.UseSixel), "Snapshot terminals must retain the shared chart's terminal-cell fallback.");
            }
        }

        Assert.Throws<ArgumentException>(() => new MonitorSurface { GraphType = GraphKind.Histogram });
        Assert.Throws<ArgumentException>(() => TuiDocument.FromJson("""{"Root":{"Id":"monitor","Type":"PerformanceMonitor","GraphKind":"BoxPlot"}}"""));
        TuiDocument.FromJson("""{"Root":{"Id":"monitor","Type":"PerformanceMonitor","GraphKind":"Step"}}""");
    }
    [TestMethod]
    public void NativeChartAdapterPreservesTimeSpikesAndMissingBucketsWithoutAllocations() {
        var time = DateTimeOffset.UnixEpoch;
        var history = new TimeSeries();
        history.Add(10, time);
        history.Add(100, time.AddMilliseconds(100));
        history.Add(10, time.AddMilliseconds(200));
        history.Add(null, time.AddSeconds(30));
        history.Add(20, time.AddSeconds(60));
        history.Add(30, time.AddSeconds(120));
        double[] buckets = new double[240];
        BuiltInTrend.Sample(history, time.AddSeconds(120), buckets);
        Assert.AreEqual(100d, buckets[0], "Compressed spikes must retain their peak.");
        Assert.IsTrue(double.IsNaN(buckets[60]), "Unavailable data must stay empty.");
        Assert.IsTrue(double.IsNaN(buckets[180]), "Long collection gaps must stay empty.");
        Assert.AreEqual(30d, buckets[^1]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        BuiltInTrend.Sample(history, time.AddSeconds(120), buckets);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        long version = history.Version;
        history.Add(40, time.AddSeconds(120));
        Assert.IsGreaterThan(version, history.Version);
        BuiltInTrend.Sample(history, time.AddSeconds(120), buckets);
        Assert.AreEqual(40d, buckets[^1], "Same-timestamp corrections must reach the native chart.");
    }
    [TestMethod]
    public void DashboardUsesOneNumericChartPerLaneAndPickerHidesCharts() {
        var surface = new MonitorSurface { View = MonitorView.Dashboard, MinHeight = 36, MaxHeight = 36 };
        for (int i = 0; i < 120; i++) {
            surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i), CpuPercent = 50, TotalMemory = 1000, AvailableMemory = 500, ReceiveBytesPerSecond = 100, SendBytesPerSecond = 20 });
        }

        VisualSnapshotRenderer.Render(surface, 150, 36);
        var visuals = surface.EnumerateVisualsDepthFirst().ToArray();
        Assert.AreEqual(5, visuals.OfType<Chart>().Count(chart => chart.Bounds.Width > 0), string.Join("; ", visuals.Select(chart => $"{chart.GetType().Name}: {chart.Bounds}")));
        Assert.IsTrue(visuals.OfType<Chart>().Where(chart => chart.Bounds.Width > 0).All(chart => !chart.UseSixel), "A snapshot must use one terminal-cell representation per history lane.");
        var picker = new MonitorSurface { View = MonitorView.Processes };
        picker.OpenColumns();
        VisualSnapshotRenderer.Render(picker, 150, 36);
        Assert.IsTrue(picker.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().All(chart => chart.Bounds.Width == 0), "Native children must not paint over the column chooser.");
    }
    [TestMethod]
    public void HistoryIsBoundedAndHonorsTimeAndMissingSamples() {
        var history = new TimeSeries(3);
        var time = DateTimeOffset.UnixEpoch;
        history.Add(10, time);
        history.Add(20, time.AddSeconds(1));
        history.Add(null, time.AddSeconds(2));
        history.Add(50, time.AddSeconds(3));
        Assert.AreEqual(3, history.Count);
        Assert.AreEqual(20d, history.ValueAt(0));
        Assert.IsNull(history.ValueAt(1));
        history.Add(40, time.AddSeconds(3));
        Assert.AreEqual(3, history.Count);
        Assert.AreEqual(40d, history.Maximum);
        history.Add(100, time);
        Assert.AreEqual(40d, history.Maximum);
        history.Add(5, time.AddMinutes(3));
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual(5d, history.Average);
        Assert.AreEqual(0, history.Nearest(time.AddMinutes(3)));
    }
    [TestMethod]
    public void ChartIsAvailableInNativeDefinitionsAndDesignerPalette() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"chart","Type":"Chart","Text":"CPU %","Bind":"cpu","Height":8},"State":{"cpu":42}}""");
        using var app = new CustomTuiApp(document);
        var chart = (HistoryChart)app.Controls["chart"];
        Assert.AreEqual(42d, chart.History.ValueAt(0));
        ThemeCatalog.Load("Monokai").Apply(app.Root);
        Assert.AreEqual("Monokai", chart.Palette.Name);
        VisualSnapshotRenderer.Render(app.Root, 100, 25);
        Assert.Contains("Chart", TuiDocument.ControlTypes);
    }
    [TestMethod]
    public void BoundNumericAndBooleanStateAcceptsPowerShellWrappers() {
        var document = TuiDocument.FromJson("""{"Root":{"Id":"root","Type":"VStack","Children":[{"Id":"chart","Type":"Chart","Bind":"load"},{"Id":"bar","Type":"Progress","Bind":"load"},{"Id":"check","Type":"CheckBox","Text":"On","Bind":"on"}]}}""");
        using var app = new CustomTuiApp(document, new System.Collections.Hashtable { ["load"] = new System.Management.Automation.PSObject(0.5), ["on"] = new System.Management.Automation.PSObject(true) });
        Assert.AreEqual(0.5d, ((HistoryChart)app.Controls["chart"]).History.ValueAt(0));
    }
    [TestMethod]
    public void DashboardAndHeadersRenderDenseHistoryAtNarrowAndWideSizes() {
        foreach (var view in new[] { MonitorView.Dashboard, MonitorView.Processes, MonitorView.Network }) {
            var surface = new MonitorSurface { View = view };
            for (int i = 0; i < 120; i++) {
                surface.SetFrame(new CollectorFrame { CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(i), CpuPercent = 50 + 45 * Math.Sin(i / 5.0), TotalMemory = 1000, AvailableMemory = 500, ReceiveBytesPerSecond = i * 100, SendBytesPerSecond = i * 20 });
            }

            foreach (var size in new[] { (160, 48), (80, 24), (35, 12) }) {
                Assert.AreEqual(size.Item1, VisualSnapshotRenderer.Render(surface, size.Item1, size.Item2).Width);
            }
        }
    }
    [TestMethod]
    public void FileTabsKeepTheirOwnFiltersSelectionsAndMarks() {
        var surface = new FileManagerSurface();
        surface.Model.Apply(new("/fixtures", [new("/fixtures/a", "a", false, false, 1, default, false), new("/fixtures/b", "b", false, false, 2, default, false)], []));
        surface.Model.Select(1, 10);
        surface.Model.ToggleMark();
        surface.ToggleBookmark();
        surface.NewTab();
        Assert.AreEqual(2, surface.TabCount);
        Assert.AreEqual(1, surface.ActiveTab);
        Assert.IsEmpty(surface.Model.Marked);
        surface.Model.Filter = "a";
        surface.Model.Rebuild();
        surface.SwitchTab(0);
        Assert.AreEqual("", surface.Model.Filter);
        Assert.AreEqual("b", surface.Model.Selected!.Value.Name);
        Assert.HasCount(1, surface.Model.Marked);
        surface.SwitchTab(1);
        Assert.AreEqual("a", surface.Model.Filter);
        surface.CloseTab();
        Assert.AreEqual(1, surface.TabCount);
        Assert.HasCount(1, surface.Bookmarks);
    }
}
