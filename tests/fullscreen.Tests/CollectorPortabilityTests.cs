using System.Reflection;
using System.Runtime.Versioning;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class CollectorPortabilityTests {
    private static readonly string[] expected = ["netstandard"];

    [TestMethod]
    public void OneCollectorTargetsNetStandardAndHasNoExternalAssemblyReferences() {
        var assembly = typeof(PerformanceCollector).Assembly;
        Assert.AreEqual(".NETStandard,Version=v2.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        CollectionAssert.AreEqual(expected, assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray());
        foreach (var type in assembly.GetExportedTypes()) {
            foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) {
                Assert.DoesNotContain("Span`", method.ReturnType.FullName ?? "");
                foreach (var parameter in method.GetParameters()) {
                    Assert.DoesNotContain("Span`", parameter.ParameterType.FullName ?? "");
                }
            }
        }
    }
    [TestMethod]
    public void BoundedByteParsingHandlesUnsignedLimitsAndTruncation() {
        Assert.IsTrue(ByteNumbers.TryParse("18446744073709551615"u8.ToArray(), out ulong maximum, out int used));
        Assert.AreEqual(ulong.MaxValue, maximum);
        Assert.AreEqual(20, used);
        Assert.IsFalse(ByteNumbers.TryParse("18446744073709551616"u8.ToArray(), out ulong _, out _));
        Assert.IsTrue(ByteNumbers.TryParse("-9223372036854775808"u8.ToArray(), out long minimum, out _));
        Assert.AreEqual(long.MinValue, minimum);
        Assert.IsFalse(ByteNumbers.TryParse("9223372036854775808"u8.ToArray(), out long _, out _));
        Assert.IsNull(SystemSampler.ReadKilobytes("MemTotal: 18446744073709551615 kB"u8.ToArray(), "MemTotal:"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ByteWindow(new byte[2], 1, 2));
    }
    [TestMethod]
    public void MacIdentityAcceptsManagedStartTimeWithoutWeakeningPidReuseCheck() {
        const long seconds = 1750000000, micros = 8;
        long exact = DateTimeOffset.FromUnixTimeSeconds(seconds).Ticks + micros * 10;
        long managed = DateTime.UnixEpoch.Ticks + TimeSpan.FromSeconds(seconds + micros / 1000000.0).Ticks;
        Assert.AreNotEqual(exact, managed, "Fixture must reproduce the floating-point timestamp difference.");
        Assert.IsTrue(ProcessOwnerReader.MatchesMacStartTime(managed, seconds, micros));
        Assert.IsTrue(ProcessOwnerReader.MatchesMacStartTime(exact, seconds, micros));
        Assert.IsFalse(ProcessOwnerReader.MatchesMacStartTime(managed + 1000, seconds, micros));
        Assert.IsFalse(ProcessOwnerReader.MatchesMacStartTime(managed, seconds + 1, micros));
    }
    [TestMethod]
    public void CurrentPlatformProvidesCoreMetricsWithoutOptionalProviders() {
        using var collector = new PerformanceCollector();
        collector.Capture(CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Processes | CollectorMetrics.Owners | CollectorMetrics.ProcessDisk);
        Thread.Sleep(30);
        var frame = collector.Capture(CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Processes | CollectorMetrics.Owners | CollectorMetrics.ProcessDisk);
        Assert.IsGreaterThan(0, frame.ProcessCount);
        Assert.IsGreaterThan(0ul, frame.TotalMemory ?? 0);
        var process = frame.Processes.Take(frame.ProcessCount).Single(row => row.Id == Environment.ProcessId);
        Assert.IsNotNull(process.UserName);
        Assert.IsNotNull(process.ParentId);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) {
            Assert.IsNotNull(process.Status);
            if (OperatingSystem.IsMacOS() || File.Exists("/proc/self/io")) {
                Assert.IsNotNull(process.DiskBytesPerSecond);
            }
        }
    }
}
