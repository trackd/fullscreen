using System.Text.RegularExpressions;
using fullscreen.Charts;
using fullscreen.Monitoring;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed partial class StorageLayoutTests {
    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Markup();
    [GeneratedRegex("Recent IOPS")]
    private static partial Regex Trends();
    [TestMethod]
    public void MountsMapPartitionsAndExcludePseudoFilesystemsAndBindDuplicates() {
        string root = Path.Combine(Path.GetTempPath(), "fullscreen-storage-" + Guid.NewGuid().ToString("N"));
        void Write(string relative, string value) {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        try {
            Write("block/sda/dev", "8:0");
            Write("block/sda/sda1/dev", "8:1");
            Write("block/sda/sda1/partition", "1");
            Write("block/sda/sda2/dev", "8:2");
            Write("block/sda/sda2/partition", "2");
            string text = "36 35 8:1 / / rw - ext4 /dev/sda1 rw\n37 35 8:1 / /bind rw - ext4 /dev/sda1 rw\n38 35 0:12 / /sys/kernel/tracing rw - tracefs tracefs rw\n39 35 8:2 / /mnt/my\\040data rw - ext4 /dev/sda2 rw\n40 35 0:15 / /run/rpc_pipefs rw - rpc_pipefs sunrpc rw";
            var mounts = DiskLayout.LinuxMounts(text, root);
            Assert.HasCount(2, mounts);
            Assert.IsTrue(mounts.All(mount => mount.Device == "sda"));
            Assert.AreEqual("/", mounts[0].Path);
            Assert.AreEqual("/mnt/my data", mounts[1].Path);
            var swap = DiskLayout.LinuxSwaps("Filename Type Size Used Priority\n/dev/sda2 partition 100 20 -2\n/swapfile file 100 10 -2", root);
            Assert.HasCount(1, swap);
            Assert.AreEqual("sda", swap[0].Device);
            Assert.AreEqual(80UL * 1024, swap[0].Volume.Available);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CombinedDriveRetainsIoAndShowsFilesystemCapacityExactlyOnce() {
        DiskRow[] devices = [new("sda", "Drive A", "Device", ReadIops: 12, WriteIops: 8), new("sdb", "Drive B", "Device", Total: 1024)];
        var rows = DiskLayout.Combine(devices, [
            ("sda", new DiskVolume("root", "/", "ext4", 100, 40)),
            ("sda", new DiskVolume("root", "/bind", "ext4", 100, 40)),
            ("sda", new DiskVolume("data", "/data", "ext4", 50, 10))
        ]);
        Assert.HasCount(2, rows);
        Assert.AreEqual(150UL, rows[0].Total);
        Assert.AreEqual(50UL, rows[0].Free);
        Assert.AreEqual(12d, rows[0].ReadIops);
        Assert.HasCount(2, rows[0].Volumes);
        Assert.AreEqual(1024UL, rows[1].Total);
        Assert.IsNull(rows[1].Free);
        rows = DiskLayout.Combine(devices, [(null, new DiskVolume("pool", "/pool", "zfs", 200, 80))]);
        Assert.HasCount(3, rows);
        Assert.AreEqual("Filesystem", rows[2].Kind);
        Assert.IsFalse(rows[2].Overview);
        Assert.IsNull(rows[2].ReadIops, "Unmapped capacity must not claim a device's I/O.");
    }

    [TestMethod]
    public void StorageOverviewExcludesStackedLayersAndDoesNotTurnUnknownIntoZero() {
        DiskRow[] rows = [
            new("sda", "Drive A", "Device", ReadBytes: 100, WriteBytes: 200, ReadIops: 2, WriteIops: 3),
            new("dm0", "Logical", "Device", ReadBytes: 100, WriteBytes: 200, ReadIops: 2, WriteIops: 3) { Overview = false },
            new("fs", "/", "Filesystem", Total: 100, Free: 20)
        ];
        Assert.AreEqual((100d, 200d, 2d, 3d), MonitorSurface.StorageOverview(rows));
        rows[0] = rows[0] with { ReadIops = null };
        Assert.IsNull(MonitorSurface.StorageOverview(rows).Reads);
        rows[0] = rows[0] with { ReadIops = 0 };
        Assert.AreEqual(0d, MonitorSurface.StorageOverview(rows).Reads);
        Assert.IsNull(MonitorSurface.StorageOverview([]).Reads);
    }

    [TestMethod]
    public void CombinedVolumeMetadataSurvivesTransportAndVersionTwelveStillDecodes() {
        var frame = new CollectorFrame {
            Disks = [new("drive", "Drive", "Device", Total: 100, Free: 40) {
            Overview = false, Volumes = [new("root", "/", "ext4", 100, 40)]
        }]
        };
        var decoded = new CollectorFrame();
        FrameCodec.Decode(FrameCodec.Encode(frame), decoded);
        CollectionAssert.AreEqual(frame.Disks[0].Volumes, decoded.Disks[0].Volumes);
        Assert.IsFalse(decoded.Disks[0].Overview);
        frame.Disks = [new("legacy", "v12", "Device", ReadIops: 3)];
        byte[] bytes = FrameCodec.Encode(frame);
        // v13 appends one overview byte and an empty volume count for this single row.
        bytes = bytes[..^5];
        BitConverter.GetBytes(0x46534D3C).CopyTo(bytes, 0);
        FrameCodec.Decode(bytes, decoded);
        Assert.AreEqual(3d, decoded.Disks[0].ReadIops);
        Assert.HasCount(0, decoded.Disks[0].Volumes);
        Assert.IsTrue(decoded.Disks[0].Overview);
    }

    [TestMethod]
    public void DashboardDetailsAreInsidePanelsAndDisksHaveOverviewAndEveryDriveTrend() {
        var frame = new CollectorFrame {
            CpuPercent = 10,
            TotalMemory = 4096,
            AvailableMemory = 1024,
            CpuDetails = new("Intel(R) Core(TM) fixture CPU", [new(0, 10), new(1, 20)], [new("Package", 65)], PhysicalCores: 2),
            MemoryDetails = new(Free: 512, Cached: 1024),
            Disks = [
                new("a", "Drive A", "Device", Total: 4096, Free: 1024, ReadBytes: 10, WriteBytes: 20, ReadIops: 2, WriteIops: 3) { Volumes = [new("root", "/", "ext4", 4096, 1024)] },
                new("b", "Drive B", "Device", Total: 8192, Free: 2048, ReadBytes: 20, WriteBytes: 30, ReadIops: 4, WriteIops: 5)
            ]
        };
        var dashboard = new MonitorSurface { View = MonitorView.Dashboard, MinHeight = 50, MaxHeight = 50 };
        dashboard.SetFrame(frame);
        string[] lines = VisualSnapshotRenderer.Render(dashboard, 130, 50).ToMarkupLines().Select(line => Markup().Replace(line, "")).ToArray();
        int sensorLine = Array.FindIndex(lines, line => line.Contains("65.0°C", StringComparison.Ordinal));
        Assert.IsGreaterThan(2, sensorLine);
        Assert.AreEqual('│', lines[sensorLine][1], "CPU sensor text must be inside the CPU block.");
        Assert.AreEqual('│', lines[sensorLine][63], "The CPU block must enclose the sensor text on its right edge.");
        Assert.IsTrue(lines.Any(line => line.Contains("CPU 0", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("2 cores / 2 threads", StringComparison.Ordinal)));
        var disks = new MonitorSurface { View = MonitorView.Disks, MinHeight = 50, MaxHeight = 50 };
        disks.SetFrame(frame);
        string storage = string.Join('\n', VisualSnapshotRenderer.Render(disks, 130, 50).ToMarkupLines());
        Assert.Contains("STORAGE · read / write", storage);
        Assert.Contains("OPERATIONS · read / write IOPS", storage);
        Assert.Contains("1.0 KiB free / 4.0 KiB capacity", storage);
        Assert.Contains("2.0 KiB free / 8.0 KiB capacity", storage);
        Assert.AreEqual(2, Trends().Count(storage));
        Assert.HasCount(6, disks.EnumerateVisualsDepthFirst().OfType<BuiltInTrend>().Where(plot => plot.IsVisible && plot.Bounds.Width > 0).ToArray());
        var processes = new MonitorSurface { MinHeight = 44, MaxHeight = 44 };
        processes.SetFrame(frame);
        string processView = string.Join('\n', VisualSnapshotRenderer.Render(processes, 130, 44).ToMarkupLines());
        Assert.Contains("DISK IOPS 14.0", processView);
        Assert.Contains("CPU", processView);
        Assert.Contains("MEMORY", processView);
        Assert.Contains("GPU", processView);
        Assert.Contains("NETWORK", processView);
    }
}
