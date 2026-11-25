using System.Runtime.InteropServices;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class NativeSensorTests {
    [TestMethod]
    public void AmdTemperatureFallbackConvertsMillidegreesAndHonorsQueryStatus() {
        var row = new GpuRow("Radeon", "WDDM", null, null, null, null);
        Assert.AreEqual(54.5d, AmdWindowsGpuSampler.ApplyTemperature(row, 0, 54500).Temperature);
        Assert.AreEqual(0d, AmdWindowsGpuSampler.ApplyTemperature(row, 0, 0).Temperature);
        Assert.AreEqual(row, AmdWindowsGpuSampler.ApplyTemperature(row, -1, 54500));
        Assert.AreEqual(row, AmdWindowsGpuSampler.ApplyTemperature(row, 0, int.MaxValue));
        StringAssert.Contains(AmdWindowsGpuSampler.ApplyTemperature(row, 0, 54500).Provider, "AMD ADL");
    }
    [TestMethod]
    public void StorageTemperatureUsesCompositeAndRejectsInvalidDescriptors() {
        byte[] Buffer(params (ushort Index, short Value)[] sensors) {
            byte[] buffer = new byte[24 + sensors.Length * 16];
            BitConverter.GetBytes(40U).CopyTo(buffer, 0);
            BitConverter.GetBytes((uint)buffer.Length).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)sensors.Length).CopyTo(buffer, 12);
            for (int index = 0; index < sensors.Length; index++) {
                BitConverter.GetBytes(sensors[index].Index).CopyTo(buffer, 24 + index * 16);
                BitConverter.GetBytes(sensors[index].Value).CopyTo(buffer, 26 + index * 16);
            }
            return buffer;
        }
        double? Parse(byte[] bytes) => DiskLayout.StorageTemperature(bytes, (uint)bytes.Length);
        Assert.AreEqual(42d, Parse(Buffer((1, 70), (0, 42))));
        Assert.AreEqual(70d, Parse(Buffer((1, 70), (2, 42))));
        Assert.AreEqual(0d, Parse(Buffer((0, 0))));
        Assert.AreEqual(42d, Parse(Buffer((0, short.MinValue), (1, 42))));
        Assert.IsNull(Parse(Buffer((0, short.MinValue))));
        Assert.IsNull(Parse(Buffer((0, 201))));
        Assert.IsNull(Parse([]));
        byte[] invalid = Buffer((0, 42));
        Assert.IsNull(DiskLayout.StorageTemperature(invalid, 39));
        Assert.IsNull(DiskLayout.StorageTemperature(invalid, 41));
        invalid[12] = 2;
        Assert.IsNull(Parse(invalid));
        invalid = Buffer((0, 42));
        invalid[4] = 23;
        Assert.IsNull(Parse(invalid));
    }
    [TestMethod]
    public void IntelTemperaturePreservesScopeAndNativeLayouts() {
        Assert.AreEqual(36, Marshal.SizeOf<IntelWindowsGpuSensors.InitArgs>());
        Assert.AreEqual(24, Marshal.SizeOf<IntelWindowsGpuSensors.PciAddress>());
        Assert.AreEqual(24, Marshal.SizeOf<IntelWindowsGpuSensors.PciSpeed>());
        Assert.AreEqual(64, Marshal.SizeOf<IntelWindowsGpuSensors.PciProperties>());
        Assert.AreEqual(24, Marshal.SizeOf<IntelWindowsGpuSensors.TemperatureProperties>());
        Assert.AreEqual((nint)16, Marshal.OffsetOf<IntelWindowsGpuSensors.TemperatureProperties>("Maximum"));
        Assert.AreEqual((nint)32, Marshal.OffsetOf<IntelWindowsGpuSensors.PciProperties>("Speed"));
        Assert.AreEqual(51d, IntelWindowsGpuSensors.Temperature([(0, 65), (1, 51), (2, 90)]));
        Assert.AreEqual(65d, IntelWindowsGpuSensors.Temperature([(0, 65), (1, null)]));
        Assert.AreEqual(0d, IntelWindowsGpuSensors.Temperature([(1, 0)]));
        Assert.IsNull(IntelWindowsGpuSensors.Temperature([(2, 90), (4, 50), (1, double.NaN), (0, double.PositiveInfinity)]));
        Assert.IsNull(IntelWindowsGpuSensors.Temperature([]));
    }
    [TestMethod]
    public void LinuxThermalFallbackRequiresCpuIdentityAndAvoidsDuplicateHwmon() {
        string root = Path.Combine(Path.GetTempPath(), "fullscreen-sensors-" + Guid.NewGuid());
        void Write(string path, string value) {
            string file = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, value);
        }
        try {
            Write("thermal/thermal_zone0/type", "acpitz");
            Write("thermal/thermal_zone0/temp", "90000");
            Write("thermal/thermal_zone1/type", "x86_pkg_temp");
            Write("thermal/thermal_zone1/temp", "61000");
            Write("thermal/thermal_zone2/type", "cpu-thermal");
            Write("thermal/thermal_zone2/temp", "999000");
            var sensors = ResourceSampler.Temperatures(Path.Combine(root, "missing"), Path.Combine(root, "thermal"));
            Assert.HasCount(1, sensors);
            Assert.AreEqual(61d, sensors[0].Celsius);
            StringAssert.Contains(sensors[0].Name, "x86_pkg_temp");
            Write("hwmon/hwmon0/name", "k10temp");
            Write("hwmon/hwmon0/temp1_label", "Tdie");
            Write("hwmon/hwmon0/temp1_input", "55000");
            sensors = ResourceSampler.Temperatures(Path.Combine(root, "hwmon"), Path.Combine(root, "thermal"));
            Assert.HasCount(1, sensors);
            Assert.AreEqual("k10temp · Tdie", sensors[0].Name);
            Assert.AreEqual(55d, sensors[0].Celsius);
        }
        finally { Directory.Delete(root, true); }
    }
}
