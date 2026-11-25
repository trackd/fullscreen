using fullscreen.Monitoring;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class GpuCounterTests {
    [TestMethod]
    public void EngineClassesKeepIndependentRatesAndDoNotInventEncodeDecode() {
        var activity = GpuActivity.FromEngines(new Dictionary<string, double> {
            ["rcs0"] = 12,
            ["ccs0"] = 34,
            ["bcs0"] = 56,
            ["vcs0"] = 78,
            ["vcs1"] = 20,
            ["vecs0"] = 90
        });
        Assert.AreEqual(new GpuActivity(Render: 12, Compute: 34, Copy: 56, Video: 78, VideoEnhance: 90), activity);
        Assert.IsNull(activity.Encode);
        Assert.IsNull(activity.Decode);
        Assert.AreEqual(CollectorCapabilities.GpuVideo, GpuActivity.EngineCapability("enc"), "AMD enc can be unified video.");
        Assert.AreEqual(default, GpuActivity.FromEngines(new Dictionary<string, double>()));
    }

    [TestMethod]
    public void RichGpuCountersAndPowerScopeRoundTripThroughRemoteFrames() {
        var frame = new CollectorFrame { GpuCount = 1, Capabilities = CollectorCapabilities.DeviceGraphics };
        frame.Gpus[0] = new("GPU", "DRM/i915 + PMU (sudo)", 78, null, null, 45) {
            Activity = new(12, 34, 56, 78, 9, 10, 11, 13),
            ClockMHz = 800,
            PowerWatts = 12.5,
            PowerScope = "GPU/SoC hwmon"
        };
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        Assert.AreEqual(frame.Gpus[0], decoded.Gpus[0]);
        Assert.AreEqual(frame.Capabilities, decoded.Capabilities);
    }

    [TestMethod]
    public void LegacyGpuFramesDecodeWithNewCountersUnknown() {
        var frame = new CollectorFrame { GpuCount = 1 };
        frame.Gpus[0] = new("legacy", "provider", 42, 1024, 2048, 123.456789);
        // Frozen v8 bytes: independent of the current encoder and future extension fields.
        byte[] legacy = Convert.FromBase64String("OE1TRgAA/////wAAAAAAAAAAAAAAAAAA+P//////////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAPj/AAAAAAAA+P8YV2FpdGluZyBmb3IgR1BVIHByb3ZpZGVyFldhaXRpbmcgZm9yIGludGVyZmFjZXMAAAAAAAAAAAEAAAAGbGVnYWN5CHByb3ZpZGVyAAAAAAAARUAABAAAAAAAAAAIAAAAAAAACwvuBzzdXkAbV2FpdGluZyBmb3Igc2VydmljZSBtYW5hZ2VyAAAAACFOb3QgcmVxdWVzdGVkIGJ5IHRoZSBjdXJyZW50IHZpZXcAAAAA");
        var decoded = new CollectorFrame();
        FrameCodec.Decode(legacy, decoded);
        Assert.AreEqual(frame.Gpus[0], decoded.Gpus[0]);
        Assert.AreEqual(default, decoded.Gpus[0].Activity);
    }

    [TestMethod]
    public void LocalGpuSensorsConvertUnitsAndKeepZeroActualClock() {
        using var fixture = new Sensors();
        fixture.Write("gt/gt0/rps_act_freq_mhz", "0");
        fixture.Write("device/hwmon/hwmon0/freq1_input", "900000000");
        fixture.Write("device/hwmon/hwmon0/power1_average", "45000000");
        var row = new LinuxGpuSensors().Sample(new GpuRow(), fixture.Device, 1);
        Assert.AreEqual(0d, row.ClockMHz);
        Assert.AreEqual(45d, row.PowerWatts);
        Assert.AreEqual("GPU/SoC hwmon", row.PowerScope);
    }

    [TestMethod]
    public void EnergyRatesWarmUpAndRebaselineAfterMissingOrResetSamples() {
        using var fixture = new Sensors();
        var sampler = new LinuxGpuSensors();
        const string energy = "device/hwmon/hwmon0/energy1_input";
        fixture.Write(energy, "1000000");
        Assert.IsNull(sampler.Sample(new(), fixture.Device, 1).PowerWatts);
        fixture.Write(energy, "3000000");
        Assert.AreEqual(1d, sampler.Sample(new(), fixture.Device, 2).PowerWatts);
        File.Delete(Path.Combine(fixture.Card, energy));
        Assert.IsNull(sampler.Sample(new(), fixture.Device, 1).PowerWatts);
        fixture.Write(energy, "9000000");
        Assert.IsNull(sampler.Sample(new(), fixture.Device, 1).PowerWatts);
        fixture.Write(energy, "1000000");
        Assert.IsNull(sampler.Sample(new(), fixture.Device, 1).PowerWatts);
        fixture.Write(energy, "2000000");
        Assert.AreEqual(1d, sampler.Sample(new(), fixture.Device, 1).PowerWatts);
    }

    [TestMethod]
    public async Task GpuColumnsScrollToNewCountersAndFilterTheirNumericValues() {
        var frame = new CollectorFrame { GpuCount = 1, Capabilities = CollectorCapabilities.DeviceGraphics };
        frame.Gpus[0] = new("counter-fixture", "provider", 78, null, null, null) {
            Activity = new(12, 34, 56, 78, 9, 10, 11, 13),
            ClockMHz = 800,
            PowerWatts = 12.5
        };
        var surface = new MonitorSurface { View = MonitorView.Gpu, Frame = frame };
        await using var ui = new DesignerUxTests.UiSession(surface, () => { }, 80, 32);
        ui.App.Focus(surface);
        for (int index = 1; index <= 8; index++) {
            int expected = index;
            ui.Key(TerminalKey.Right, () => surface.FirstGpuColumn == expected);
        }
        Assert.Contains("ClockMHz", ui.Output);
        Assert.Contains("PowerW", ui.Output);
        surface.SetGpuFilter(GpuColumn.Compute, ">40");
        ui.Tick();
        int VisibleCount() => (int)typeof(MonitorSurface).GetProperty("DetailCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
        Assert.AreEqual(0, VisibleCount());
        surface.SetGpuFilter(GpuColumn.Compute, ">30");
        surface.ConfigureDetailColumns(null, null, [GpuColumn.Device, GpuColumn.Compute, GpuColumn.Power], null);
        ui.Tick();
        // Scroll back so the filtered device identity is visible again.
        for (int index = surface.FirstGpuColumn; index > 0; index--) {
            int expected = index - 1;
            ui.Key(TerminalKey.Left, () => surface.FirstGpuColumn == expected);
        }
        Assert.AreEqual(1, VisibleCount());
        var snapshot = new MonitorSurface { View = MonitorView.Gpu, Frame = frame };
        snapshot.ConfigureDetailColumns(null, null, [GpuColumn.Device, GpuColumn.Compute, GpuColumn.Power], null);
        string restored = string.Join('\n', VisualSnapshotRenderer.Render(snapshot, 80, 32).ToMarkupLines());
        Assert.Contains("counter-fixture", restored);
        Assert.Contains("34.0", restored);
        Assert.Contains("12.5", restored);
        Assert.IsFalse(new MonitorSurface { Frame = new CollectorFrame { Capabilities = CollectorCapabilities.None } }.IsColumnVisible(GpuColumn.Compute));
    }

    private sealed class Sensors : IDisposable {
        internal string Card { get; } = Path.Combine(Path.GetTempPath(), "Fullscreen-GpuSensors-" + Guid.NewGuid().ToString("N"));
        internal string Device => Path.Combine(Card, "device");
        internal Sensors() => Directory.CreateDirectory(Device);
        internal void Write(string path, string value) {
            string file = Path.Combine(Card, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, value);
        }
        public void Dispose() => Directory.Delete(Card, true);
    }
}
