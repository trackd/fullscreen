using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using fullscreen.Charts;
using fullscreen.Files;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OptionalModuleTests {
    private static string Arrange(ImagePreview preview, int width = 40, int height = 10) {
        preview.Measure(new LayoutConstraints(0, width, 0, height));
        preview.Arrange(new Rectangle(0, 0, width, height));
        return preview.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single().Text ?? "";
    }
    [TestMethod]
    public async Task ImageFilesReturnMetadataWithoutReading() {
        using var fixture = new FileManagerTests.Fixture();
        fixture.File("broken.png", "not an image");
        var entry = FileReader.ReadDirectory(fixture.Root, CancellationToken.None).Entries.Single(e => e.Name == "broken.png");
        var preview = await FileReader.ReadPreviewAsync(entry, false, CancellationToken.None);
        Assert.AreEqual(PreviewKind.Image, preview.Kind);
        Assert.AreEqual("broken.png · 12 bytes", preview.Text);
    }
    [TestMethod]
    public void TextMatePreviewFallsBackToPlainText() {
        static string Text(TextMatePreview preview) {
            preview.Measure(new LayoutConstraints(0, 60, 0, 10));
            return preview.EnumerateVisualsDepthFirst().OfType<TextBlock>().Single().Text ?? "";
        }
        Assert.AreEqual(SyntaxPreview.MissingNotice + "\n\nvar a = 1;", Text(SyntaxPreview.File("var a = 1;", "a.cs")), "Without TextMate the file stays readable, with an install notice.");
        Assert.AreEqual("plain", Text(SyntaxPreview.File("plain", "LICENSE")), "Files without an extension are plain text.");
    }
    [TestMethod]
    public void NoticesExplainMissingGraphicsOrModule() {
        try {
            ImagePreview.SixelPresenter = false;
            Assert.Contains(ImagePreview.GraphicsNotice, Arrange(new("a.png", "a.png")));
            ImagePreview.SixelPresenter = true;
            var preview = new ImagePreview("a.png", "a.png");
            Assert.Contains(ImagePreview.MissingNotice, Arrange(preview), "Without a runspace the Sixel module cannot load.");
            Assert.IsNull(preview.Payload);
        }
        finally { ImagePreview.SixelPresenter = false; }
    }
    [TestMethod]
    public async Task SixelModulePayloadIsConvertedOncePresentedOnceAndErased() {
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        var previousRunspace = Runspace.DefaultRunspace;
        Runspace.DefaultRunspace = runspace;
        ImagePreview.SixelPresenter = true;
        try {
            using (var setup = PowerShell.Create(RunspaceMode.CurrentRunspace)) {
                setup.AddScript("New-Module -Name Sixel -ScriptBlock { function ConvertTo-Sixel { param($Path, $Width, $Height, $Protocol, [switch]$Force) $global:SixelCalls += \"$Width x $Height;\"; $s = [psobject]::new(\"$([char]27)P0;1q#0~$([char]27)\\\"); $s.PSObject.Properties.Add([psnoteproperty]::new(\"Height\", $Height + 1)); $s } } | Import-Module").Invoke();
            }

            var preview = new ImagePreview("a.png", "a.png");
            Assert.AreEqual("a.png", Arrange(preview));
            Arrange(preview);
            Assert.AreEqual("\x1bP0;1q#0~\x1b\\", preview.Payload);
            Assert.AreEqual("40 x 8;40 x 7;", runspace.SessionStateProxy.GetVariable("SixelCalls"), "A one-row overshoot is retried once to fit the 8 rows below the info line, once per size.");

            using var backend = new InMemoryTerminalBackend(new TerminalSize(80, 24));
            using var session = Terminal.Open(backend, new TerminalOptions { ImplicitStartInput = true }, force: true);
            await using var app = new TerminalApp(new TextBlock(""), session.Instance);
            var context = (TerminalGraphicsPresentContext)Activator.CreateInstance(typeof(TerminalGraphicsPresentContext), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [app, session.Instance, TerminalHostKind.Fullscreen, new Rectangle(0, 0, 80, 24), 0, default(Rectangle), TerminalGraphicsTextFrameKind.None], null)!;
            var bounds = new Rectangle(0, 2, 40, 8);
            var commands = new GraphicsCommandBuffer {
                new GraphicsCommand(1, bounds, bounds, TerminalGraphicContent.FromObject(preview), ImageScaleMode.Stretch, false, 0, true, null)
            };
            using var presenter = new SixelChartPresenter(TerminalGraphicsCapabilities.None, new TerminalPixelMetrics(400, 288, 4, 12, 100, 24));
            using var output = new StringWriter();
            var writer = new AnsiWriter(output);
            Assert.IsTrue(presenter.HasPendingOutput(commands, context));
            await presenter.PresentAsync(commands, context, writer, CancellationToken.None);
            Assert.Contains(preview.Payload!, output.ToString());
            Assert.IsFalse(presenter.HasPendingOutput(commands, context), "An unchanged image is not sent again.");
            output.GetStringBuilder().Clear();
            await presenter.PresentAsync([], context, writer, CancellationToken.None);
            Assert.Contains("\x1b[40X", output.ToString(), "A removed image must erase its cells.");
        }
        finally { ImagePreview.SixelPresenter = false; Runspace.DefaultRunspace = previousRunspace; }
    }
}