using fullscreen.Charts;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI.Geometry;

namespace fullscreen.Tests;

[TestClass]
public sealed class ChartHelperTests {
    public static IEnumerable<(double[] Values, double Q1, double Median, double Q3, double Lower, double Upper)> BoxCases => [
        (new[] { 5d }, 5, 5, 5, 5, 5),
        (new[] { 5d, 5, 5, 5 }, 5, 5, 5, 5, 5),
        (new[] { -4d, -2, 0, 4 }, -2.5, -1, 1, -4, 4),
        (new[] { 100d, 1, 8, 3, 7, 2, 6, 4, 5 }, 3, 5, 7, 1, 8),
        (new[] { -double.MaxValue, double.MaxValue }, -double.BitDecrement(double.MaxValue / 2), 0, double.BitDecrement(double.MaxValue / 2), -double.MaxValue, double.MaxValue)
    ];

    [TestMethod]
    [DynamicData(nameof(BoxCases))]
    public void BoxSummariesPreserveQuantilesWhiskersAndSortedBufferOwnership(double[] values, double q1, double median, double q3, double lower, double upper) {
        var expected = new BoxSummary(q1, median, q3, lower, upper);
        Assert.AreEqual(expected, ChartStatistics.Summarize(values));
        double[] sorted = [.. values];
        Assert.AreEqual(expected, ChartStatistics.SummarizeSorted(values));
        CollectionAssert.AreEqual(sorted, values, "Reading the cached sorted distribution must not rewrite it.");
    }

    [TestMethod]
    public void EmptyBoxInputsRetainTheExistingError() {
        var unsorted = Assert.ThrowsExactly<ArgumentException>(() => ChartStatistics.Summarize([]));
        var sorted = Assert.ThrowsExactly<ArgumentException>(() => ChartStatistics.SummarizeSorted([]));
        Assert.AreEqual("values", unsorted.ParamName);
        Assert.AreEqual(unsorted.Message, sorted.Message);
    }

    [TestMethod]
    public void ClippingPreservesOverlapEmptyEdgesAndOffscreenCoordinates() {
        Assert.AreEqual(new Rectangle(2, 3, 4, 5), ChartClipping.Intersect(new(0, 0, 10, 10), new(2, 3, 4, 5)));
        Assert.AreEqual(new Rectangle(10, 0, 0, 5), ChartClipping.Intersect(new(0, 0, 10, 10), new(10, 0, 5, 5)));
        Assert.AreEqual(new Rectangle(0, 0, 4, 3), ChartClipping.Intersect(new(-2, -3, 6, 6), new(0, 0, 8, 8)));
        Assert.AreEqual(new Rectangle(20, 20, 0, 0), ChartClipping.Intersect(new(0, 0, 5, 5), new(20, 20, 2, 2)));
    }

    [TestMethod]
    public void ErasingClipsToViewportAndLeavesPopupCellsUntouched() {
        using var output = new StringWriter();
        var writer = new AnsiWriter(output);
        ChartClipping.Erase(writer, new(-1, -1, 6, 4), [new(1, 0, 2, 1), new(0, 1, 4, 1)], new(10, 20, 4, 3));
        Assert.AreEqual("\x1b[21;11H\x1b[1X\x1b[21;14H\x1b[1X\x1b[23;11H\x1b[4X", output.ToString());
    }
}
