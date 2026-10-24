using System.Collections;
using System.Runtime.Loader;
using fullscreen.Custom;
using Spectre.Console;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
public sealed class SpectreTests {
    [TestMethod]
    public void PreviewPreservesDocumentRowsAndReflowsAtViewportWidth() {
        var view = new SpectreView();
        view.SetContent(new Rows(new Markup("[bold red]Document heading[/]"),
            new Panel("first line\n    indented code\nlast line") { Expand = true },
            new Text("Wrapping prose " + string.Join(" ", Enumerable.Repeat("preview", 12))),
            new Text("Document footer")));
        var scroll = new XenoAtom.Terminal.UI.Controls.ScrollViewer(view) { HorizontalScrollEnabled = false };
        var wide = VisualSnapshotRenderer.Render(scroll, 70, 100).ToMarkupLines();
        var narrow = VisualSnapshotRenderer.Render(scroll, 30, 100).ToMarkupLines();
        foreach (var lines in new[] { wide, narrow }) {
            Assert.Contains("Document heading", lines[0]);
            Assert.IsTrue(lines.Any(line => line.Contains("    indented code")));
            Assert.IsTrue(lines.Any(line => line.Contains("last line")));
            Assert.IsTrue(lines.Any(line => line.Contains("Document footer")));
        }
        Assert.IsGreaterThan(wide.Count, narrow.Count);
    }
    [TestMethod]
    public void ActualSpectreObjectsRenderWithStylesAndReflowToAvailableWidth() {
        var panel = new Panel(new Markup("[bold red]Hello[/] 界 e\u0301 " + new string('x', 70))) { Expand = true };
        string ansi = SpectreView.RenderToAnsi(panel, 70);
        var parsed = SpectreView.ParseAnsi(ansi);
        Assert.Contains("Hello", parsed.Text);
        Assert.Contains("界", parsed.Text);
        Assert.IsTrue(parsed.Runs.Any(run => run.Style.TextStyle != 0));
        Assert.Contains("\u001b[", ansi);
        string narrow = SpectreView.RenderToAnsi(panel, 25);
        Assert.IsGreaterThan(parsed.Text.Count(character => character == '\n'), SpectreView.ParseAnsi(narrow).Text.Count(character => character == '\n'));
        var document = new TuiDocument { Root = new() { Id = "spectre", Type = "Spectre", Bind = "content" } };
        using var app = new CustomTuiApp(document, new Hashtable { ["content"] = panel });
        foreach (int width in new[] { 80, 35, 100 }) {
            VisualSnapshotRenderer.Render(app.Root, width, 25);
        }
    }
    [TestMethod]
    public void ForeignAssemblyLoadContextDoesNotRequireCastingToLocalSpectreTypes() {
        var context = new AssemblyLoadContext("Spectre adapter test", isCollectible: true);
        try {
            var assembly = context.LoadFromAssemblyPath(typeof(Markup).Assembly.Location);
            var type = assembly.GetType("Spectre.Console.Markup", true)!;
            object value = Activator.CreateInstance(type, ["[green]Foreign context[/]", null])!;
            Assert.Contains("Foreign context", SpectreView.ParseAnsi(SpectreView.RenderToAnsi(value, 40)).Text);
        }
        finally { context.Unload(); }
    }
    [TestMethod]
    public void NonTextTerminalControlsAreNotPassedThroughAndInvalidValuesAreRejected() {
        var result = SpectreView.ParseAnsi("\u001b[31mred\u001b[0m\u001b[2J\u001b]0;title\u0007\u001b[999;999Htext");
        Assert.AreEqual("redtext", result.Text);
        Assert.Throws<ArgumentException>(() => SpectreView.RenderToAnsi(new object(), 80));
    }
}
