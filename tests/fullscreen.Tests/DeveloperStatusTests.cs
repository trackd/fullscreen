using System.Xml.Linq;
using fullscreen.Development;
using fullscreen.Tables;
using fullscreen.Themes;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class DeveloperStatusTests {
    [TestMethod]
    [DataRow("success", "Success")]
    [DataRow("succeeded", "Success")]
    [DataRow("failure", "Error")]
    [DataRow("failed", "Error")]
    [DataRow("error", "Error")]
    [DataRow("approved", "Success")]
    [DataRow("changes_requested", "Error")]
    [DataRow("in_progress", "Warning")]
    [DataRow("notStarted", "Warning")]
    [DataRow("partiallySucceeded", "Warning")]
    [DataRow("skipped", "Muted")]
    [DataRow("cancelled", "Muted")]
    [DataRow("neutral", "Muted")]
    [DataRow("stale", "Muted")]
    [DataRow("commented", "Muted")]
    [DataRow("dismissed", "Muted")]
    [DataRow("completed", "Muted")]
    public void RunLabelsAndDetailLinesUseTheSameSemanticColors(string result, string color) {
        var palette = ThemePalette.Default;
        var expected = color switch {
            "Success" => palette.Success,
            "Error" => palette.Error,
            "Warning" => palette.Warning,
            _ => Muted(palette)
        };
        string label = GitHubBackend.StatusLabel("completed", result);
        Assert.AreEqual(expected, DeveloperStatus.Color(result, palette));
        Assert.AreEqual(expected, DeveloperStatus.LineColor("  " + label + "  Step name", palette));
        Assert.IsNull(DeveloperStatus.LineColor("A job name containing failure", palette));
    }

    [TestMethod]
    [DataRow("open", false, false, "", "Open", "Success")]
    [DataRow("open", true, false, "", "Draft", "Muted")]
    [DataRow("closed", false, false, "", "Closed", "Error")]
    [DataRow("closed", false, true, "", "Merged", "Purple")]
    [DataRow("open", false, false, "dirty", "Blocked", "Warning")]
    [DataRow("active", false, false, "conflicts", "Blocked", "Warning")]
    [DataRow("completed", false, true, "", "Merged", "Purple")]
    [DataRow("abandoned", false, false, "", "Closed", "Error")]
    public void PullRequestStatesMatchGitHubAndAzureSemantics(string state, bool draft, bool merged, string mergeability, string label, string color) {
        var palette = ThemePalette.Default;
        string actual = DeveloperStatus.PullRequestState(state, draft, merged, mergeability);
        Assert.AreEqual(label, actual);
        var expected = color switch {
            "Success" => palette.Success,
            "Error" => palette.Error,
            "Warning" => palette.Warning,
            "Purple" => palette.Purple,
            _ => Muted(palette)
        };
        Assert.AreEqual(expected, DeveloperStatus.Color(actual, palette));
    }

    [TestMethod]
    [DoNotParallelize]
    public void StatusColorsReachTablePreviewAndExpandedDocumentCells() {
        var palette = ThemePalette.Default;
        var model = new TableModel(["Name", "Result"]);
        model.Rows.Add(new(1, "run", ["Build", "✗ failure"], ["Build", "failure"]));
        model.Rebuild();
        var surface = new TableSurface(model, "Runs") {
            CellAccent = (row, column) => column == 1 ? DeveloperStatus.Color(row.Cells[column], palette) : null,
            DocumentAccent = line => DeveloperStatus.LineColor(line, palette)
        };
        string detail = "✓ success  Compile\n  ✗ failure  Test\n  ○ skipped  Publish";
        surface.SetPreview("Jobs", detail);
        AssertColors(VisualSnapshotRenderer.Render(surface, 120, 25));
        surface.SetDocument(detail);
        AssertColors(VisualSnapshotRenderer.Render(surface, 120, 25));

        void AssertColors(CellBuffer buffer) {
            var svg = XDocument.Parse(CellBufferSvgExporter.Export(buffer));
            foreach (var (text, color) in new[] { ("Compile", palette.Success), ("Test", palette.Error), ("Publish", Muted(palette)) }) {
                var rgb = color.ToRgb();
                Assert.IsTrue(svg.Descendants().Any(element => element.Name.LocalName == "text"
                    && element.Value.Contains(text, StringComparison.Ordinal)
                    && (string?)element.Attribute("fill") == $"rgb({rgb.R},{rgb.G},{rgb.B})"), text);
            }
        }
    }

    private static Color Muted(ThemePalette palette) {
        Assert.IsTrue(palette.TableMuted.TryGetForeground(out var color));
        return color;
    }
}
