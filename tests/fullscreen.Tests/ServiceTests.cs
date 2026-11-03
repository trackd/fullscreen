using fullscreen.Monitoring;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class ServiceTests {
    [TestMethod]
    public void WindowsServiceTableIgnoresLegacyExitDetails() {
        var frame = new CollectorFrame { Platform = "Windows", ServiceCount = 1 };
        frame.Services[0] = new("ALG", "Application Layer Gateway Service", "stopped", "error 1077", null);
        var surface = new MonitorSurface { View = MonitorView.Services, Frame = frame };
        string output = string.Join('\n', VisualSnapshotRenderer.Render(surface, 150, 30).ToMarkupLines());
        Assert.Contains("DISPLAY NAME", output);
        Assert.Contains("Application Layer Gateway Service", output);
        Assert.DoesNotContain("1077", output);
        Assert.DoesNotContain("DETAIL", output);
    }
    [TestMethod]
    [DataRow(1U, "stopped")]
    [DataRow(4U, "running")]
    [DataRow(7U, "paused")]
    public void WindowsServiceRowsShowDisplayNamesInsteadOfExitErrors(uint state, string expectedState) {
        var row = ServiceSampler.WindowsRow("ALG", "Application Layer Gateway Service", state, state == 4 ? 42U : 0U, null);
        Assert.AreEqual(expectedState, row.State);
        Assert.AreEqual("Application Layer Gateway Service", ServiceView.Text(row, ServiceColumn.Description));
        Assert.AreEqual("", row.Detail);
        var frame = new CollectorFrame { Platform = "Windows", ServiceCount = 1 };
        frame.Services[0] = row;
        var surface = new MonitorSurface { View = MonitorView.Services, Frame = frame };
        string output = string.Join('\n', VisualSnapshotRenderer.Render(surface, 150, 30).ToMarkupLines());
        Assert.Contains("DISPLAY NAME", output);
        Assert.Contains("Application Layer Gateway Service", output);
        Assert.DoesNotContain("DETAIL", output);
        Assert.DoesNotContain("1077", output);
        surface.Services.Filter = "Layer Gateway";
        surface.Services.Update(frame.Services.AsSpan(0, 1));
        Assert.AreEqual(1, surface.Services.Count);
    }
    [TestMethod]
    public void AccountsSurviveTransportAndSupportFilteringAndSorting() {
        var frame = new CollectorFrame { ServiceCount = 2 };
        frame.Services[0] = new("one", "", "stopped", "", null, @"DOMAIN\zoe");
        frame.Services[1] = new("two", "", "running", "", 42, @"DOMAIN\amy");
        var copy = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), copy);
        Assert.AreEqual(@"DOMAIN\zoe", copy.Services[0].UserName);
        var view = new ServiceView { Sort = ServiceColumn.UserName };
        view.Update(copy.Services.AsSpan(0, 2));
        Assert.AreEqual("two", view.Rows[0].Name);
        view.Filter = "zoe";
        view.Update(copy.Services.AsSpan(0, 2));
        Assert.AreEqual(1, view.Count);
        Assert.AreEqual("one", view.Rows[0].Name);
    }
    [TestMethod]
    public void ServiceSortingFiltersAndSelectionAreIndependentFromProcesses() {
        ServiceRow[] rows = [new("zulu", "Worker", "active", "running", 100), new("alpha", "Cache", "inactive", "dead", null), new("beta", "Worker", "failed", "exit 1", 20)];
        var view = new ServiceView { Sort = ServiceColumn.ProcessId, Ascending = true };
        view.Update(rows);
        Assert.AreEqual("beta", view.Rows[0].Name);
        Assert.AreEqual("alpha", view.Rows[^1].Name);
        view.Ascending = false;
        view.Update(rows);
        Assert.AreEqual("zulu", view.Rows[0].Name);
        Assert.AreEqual("alpha", view.Rows[^1].Name);
        view.ColumnFilters[ServiceColumn.State] = "active";
        view.Update(rows);
        Assert.AreEqual(1, view.Count);
        Assert.AreEqual("zulu", view.Rows[0].Name);
        view.ColumnFilters.Clear();
        view.ColumnFilters[ServiceColumn.Description] = "~work";
        view.ColumnFilters[ServiceColumn.ProcessId] = ">50";
        view.Update(rows);
        Assert.AreEqual(1, view.Count);
        Assert.AreEqual("zulu", view.SelectedName);
        view.ColumnFilters.Clear();
        view.Sort = ServiceColumn.Name;
        view.Ascending = true;
        view.Update(rows);
        view.Move(int.MaxValue, 1);
        Assert.AreEqual("zulu", view.SelectedName);
        Assert.AreEqual(2, view.Offset);
    }
    [TestMethod]
    public void UnixParsersPreserveNativeStateAndLaunchdExitStatus() {
        var frame = new CollectorFrame();
        ServiceSampler.ParseSystemd("sshd.service loaded active running OpenSSH server\nmissing.service not-found inactive dead Missing unit\n", frame);
        Assert.AreEqual(2, frame.ServiceCount);
        Assert.AreEqual("OpenSSH server", frame.Services[0].Description);
        Assert.AreEqual("not-found/dead", frame.Services[1].Detail);
        frame.ServiceCount = 0;
        ServiceSampler.ParseLaunchd("PID Status Label\n42 0 com.example.running\n- -9 com.example.idle\n", frame);
        Assert.AreEqual(2, frame.ServiceCount);
        Assert.AreEqual(42, frame.Services[0].ProcessId);
        Assert.AreEqual("not running", frame.Services[1].State);
        Assert.AreEqual("last exit -9", frame.Services[1].Detail);
    }
    [TestMethod]
    public void ServicesSurviveRemoteCodecAndSnapshotCopy() {
        var source = new CollectorFrame { ServiceCount = 1, ServiceStatus = "fixture" };
        source.Services[0] = new("service", "Service description", "running", "exit 0", 42);
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(source), decoded);
        var copy = new CollectorFrame();
        copy.CopyFrom(decoded);
        Assert.AreEqual(source.Services[0], copy.Services[0]);
        Assert.AreEqual("fixture", copy.ServiceStatus);
        Assert.AreEqual(1, copy.ServiceCount);
    }
    [TestMethod]
    public void ServicesViewOnlyRequestsServicesAndRendersAtAllSupportedSizes() {
        var surface = new MonitorSurface { View = MonitorView.Services };
        surface.Frame.ServiceCount = 1;
        surface.Frame.Services[0] = new("example", "Example service", "active", "loaded/running", null);
        foreach (bool minimal in new[] { false, true }) {
            foreach (var size in new[] { (150, 36), (80, 24), (35, 12) }) {
                surface.Minimal = minimal;
                VisualSnapshotRenderer.Render(surface, size.Item1, size.Item2);
                Assert.AreEqual(CollectorMetrics.Services, surface.RequiredMetrics);
                surface.OpenColumns();
                Assert.IsFalse(surface.ColumnsOpen);
            }
        }
    }
    [TestMethod]
    public void WindowsServiceInventoryMatchesTheLocalServiceManager() {
        if (!OperatingSystem.IsWindows()) {
            Assert.Inconclusive("Windows-only live service inventory");
            return;
        }
        var frame = new CollectorFrame();
        new ServiceSampler().Sample(frame);
        Assert.IsGreaterThan(0, frame.ServiceCount, frame.ServiceStatus);
        Assert.IsTrue(frame.Services.Take(frame.ServiceCount).Any(row => row.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(frame.ServiceStatus.StartsWith("Windows Service Control Manager", StringComparison.Ordinal));
    }
}
