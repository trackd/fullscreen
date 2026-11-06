using System.Collections;
using System.Management.Automation;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Custom;
using Spectre.Console;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Rendering;
using Table = Spectre.Console.Table;
using Paragraph = XenoAtom.Terminal.UI.Controls.Paragraph;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RefreshEfficiencyTests {
    private sealed class Sample {
        internal int Reads;
        internal double Current = 10;
        public double Value { get { Reads++; return Current; } }
    }

    [TestMethod]
    public async Task HiddenCollectionChart_DefersProjectionAndCatchesUpOnShow() {
        var sample = new Sample();
        var document = new TuiDocument {
            Root = new() {
                Id = "tabs", Type = "TabControl", Bind = "page",
                Children = [
                    new() { Id = "chart", Type = "Chart", Bind = "rows", YProperty = ["Value"], TabTitle = "Chart" },
                    new() { Id = "other", Type = "Text", Text = "Other", TabTitle = "Other" }
                ]
            }
        };
        using var app = new CustomTuiApp(document, new Hashtable { ["rows"] = new[] { sample }, ["page"] = 0 }, tickInterval: 1);
        var chart = (Chart)app.Controls["chart"];
        var tabs = (TabControl)app.Controls["tabs"];
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => { Thread.Sleep(2); app.Tick(); });
        tabs.SelectedIndex = 1;
        ui.Tick();
        int reads = sample.Reads;
        long version = chart.DataVersion;
        sample.Current = 42;
        for (int tick = 0; tick < 10; tick++) { ui.Tick(); }
        Assert.AreEqual(reads, sample.Reads, "A hidden chart must not evaluate source getters.");
        Assert.AreEqual(version, chart.DataVersion);
        tabs.SelectedIndex = 0;
        ui.Tick();
        ui.Tick();
        Assert.IsGreaterThan(reads, sample.Reads);
        Assert.AreEqual(42d, chart.GetData(100)[0].Points[0].Y);
    }

    [TestMethod]
    public void Spectre_UnchangedOutputRetainsParsedRunsAndMutableContentStillUpdates() {
        var view = new SpectreView();
        var table = new Table().AddColumn("Name").AddRow("First");
        view.SetContent(table);
        VisualSnapshotRenderer.Render(view, 60, 20);
        var paragraph = view.EnumerateVisualsDepthFirst().OfType<Paragraph>().Single();
        var runs = paragraph.Runs;
        int revision = (int)typeof(SpectreView).GetProperty("Revision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        view.SetContent(table);
        Assert.AreSame(runs, paragraph.Runs);
        Assert.AreEqual(revision, typeof(SpectreView).GetProperty("Revision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
        table.AddRow("Second");
        view.SetContent(table);
        Assert.Contains("Second", paragraph.Text!);
        Assert.AreNotSame(runs, paragraph.Runs);
    }

    [TestMethod]
    public async Task HiddenSpectre_DefersRenderingAndShowsLatestMutableContent() {
        var table = new Table().AddColumn("Name").AddRow("First");
        var definition = new TuiDocument {
            Root = new() { Id = "tabs", Type = "TabControl", Children = [
                new() { Id = "spectre", Type = "Spectre", Bind = "content", TabTitle = "Content" },
                new() { Id = "other", Type = "Text", Text = "Other", TabTitle = "Other" }
            ] }
        };
        using var app = new CustomTuiApp(definition, new Hashtable { ["content"] = table }, tickInterval: 1);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => { Thread.Sleep(2); app.Tick(); });
        var paragraph = app.Controls["spectre"].EnumerateVisualsDepthFirst().OfType<Paragraph>().Single();
        var tabs = (TabControl)app.Controls["tabs"];
        tabs.SelectedIndex = 1;
        ui.Tick();
        var runs = paragraph.Runs;
        table.AddRow("Second");
        for (int tick = 0; tick < 10; tick++) { ui.Tick(); }
        Assert.AreSame(runs, paragraph.Runs);
        Assert.DoesNotContain("Second", paragraph.Text!);
        tabs.SelectedIndex = 0;
        ui.Tick();
        ui.Tick();
        Assert.Contains("Second", paragraph.Text!);
    }

    [TestMethod]
    public void RefreshSource_AlwaysRebuildsAndPreservesCurrentSourceObjects() {
        var source = new PSObject();
        source.Properties.Add(new PSNoteProperty("Value", 10d));
        using var app = new ObjectChartApp([source], yProperties: ["Value"]);
        var data = app.Data;
        long version = app.Surface.Plot.DataVersion;
        app.ReplaceObjects([source]);
        Assert.AreNotSame(data, app.Data);
        Assert.IsGreaterThan(version, app.Surface.Plot.DataVersion);
        Assert.AreSame(source, app.Data.Objects[0]);
        data = app.Data;
        source.Properties["Value"].Value = 20d;
        app.ReplaceObjects([source]);
        Assert.AreNotSame(data, app.Data);
        Assert.AreEqual(20d, app.Surface.Plot.GetData(100)[0].Points[0].Y);
        var next = new PSObject();
        next.Properties.Add(new PSNoteProperty("Value", 20d));
        app.ReplaceObjects([next]);
        Assert.AreSame(next, app.Data.Objects[0], "A replacement source must not leave inspection pointing at stale objects.");
    }
}
