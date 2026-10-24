using System.Management.Automation;
using fullscreen.Charts;

namespace fullscreen.Tests;

[TestClass]
public sealed class ChartDiscoveryTests {
    public sealed class Row {
        public int Reads { get; private set; }
        public int Value { get { Reads++; return 42; } }
        public int? Optional { get; } = 3;
        public string Dangerous { get { Reads++; throw new InvalidOperationException("Discovery executed a getter."); } }
    }
    [TestMethod]
    public void DiscoveryUsesMetadataAndOnlySelectedFieldsExecuteGetters() {
        var first = new Row();
        var second = new Row();
        var data = new ObjectChartData([PSObject.AsPSObject(first), PSObject.AsPSObject(second)]);
        Assert.Contains("Dangerous", data.Properties);
        Assert.Contains("Optional", data.NumericProperties);
        Assert.AreEqual(0, first.Reads);
        Assert.AreEqual(0, second.Reads);
        var plotted = data.Project(null, ["Value"], TestContext.CancellationToken);
        CollectionAssert.AreEqual(new double?[] { 42, 42 }, plotted[0].Points.Select(point => point.Y).ToArray());
        Assert.AreEqual(1, first.Reads);
        Assert.AreEqual(1, second.Reads);
    }

    public TestContext TestContext { get; set; } = null!;
}
