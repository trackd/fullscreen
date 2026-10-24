using System.Management.Automation;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Files;
/// <summary>Optional image preview; the Sixel module decodes and encodes, the Sixel chart presenter places the payload.</summary>
public sealed partial class ImagePreview : Visual, IGraphicsRenderableVisual {
    internal const string MissingNotice = "Install-Module Sixel to preview images (PNG, JPEG, GIF, BMP, WebP, TIFF, TGA, PBM).";
    internal const string GraphicsNotice = "Image previews need a terminal with Sixel graphics.";
    /// <summary>Set by the host while the Sixel presenter runs; DRCS and native renderers cannot place images.</summary>
    internal static bool SixelPresenter { get; set; }

    private readonly string? _info;
    private readonly TextBlock _text;
    private (int Width, int Height) _size = (-1, -1);
    private string? _loaded;
    /// <summary>Image file; changing it converts the new image on the next layout.</summary>
    [Bindable]
    public partial string Path { get; set; }

    private TerminalGraphicContent? _content;
    /// <summary>The Sixel payload for the current size; a new string whenever the image changes.</summary>
    internal string? Payload { get; private set; }
    private Rectangle ImageBounds => new(Bounds.X, Bounds.Y + 2, Bounds.Width, Math.Max(0, Bounds.Height - 2));

    /// <param name="info">Caption above the image; defaults to the file name.</param>
    /// <summary>Initializes a new ImagePreview instance.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    public ImagePreview(string path, string? info = null) {
        Path = path;
        _info = info;
        _text = new TextBlock("");
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        AttachChild(_text);
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _text : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _text.Measure(constraints);
        return SizeHints.Flex(new(0, 0), _text.DesiredSize, new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) {
        _text.Arrange(finalRect);
        var size = (finalRect.Width, Math.Max(0, finalRect.Height - 2));
        if (size == _size && Path == _loaded) {
            return;
        }

        _size = size;
        _loaded = Path;
        Payload = null;
        string? notice;
        // ConvertTo-Sixel runs once per size and path, in the caller's runspace.
        try {
            notice = Path.Length == 0 ? null : !SixelPresenter ? GraphicsNotice : size.Width <= 0 || size.Item2 <= 0 ? null : Load(size.Width, size.Item2);
        }
        catch (Exception error) when (error is not OutOfMemoryException) {
            Payload = null;
            notice = error.Message;
        }

        string info = _info ?? System.IO.Path.GetFileName(Path);
        _text.Text = FileReader.Sanitize(notice is null ? info : info + "\n\n" + notice, true);
    }

    private string? Load(int width, int height) {
        Payload = null;
        using var pipeline = OptionalModule.Open("Sixel");
        if (pipeline is null) {
            return MissingNotice;
        }

        // Sixel 0.7.1 rounds the row count up by one when height limits the fit; retry once without the overshoot.
        // Remove the retry once the fixed Sixel module is the minimum supported version.
        for (int target = height; ;) {
            // The host already detected Sixel support, so -Force skips the module's own protocol probe.
            pipeline.Commands.Clear();
            pipeline.AddCommand("Sixel\\ConvertTo-Sixel").AddParameter("Path", Path).AddParameter("Width", width).AddParameter("Height", target).AddParameter("Protocol", "Sixel").AddParameter("Force").AddParameter("ErrorAction", ActionPreference.Stop);
            var output = pipeline.Invoke();
            // The payload is written to the terminal unchanged, so accept only one Sixel DCS string.
            if (output.Count != 1 || output[0].BaseObject is not string payload
                || !payload.StartsWith("\u001bP", StringComparison.Ordinal)) {
                throw new InvalidOperationException("ConvertTo-Sixel did not return one Sixel image.");
            }

            int rows = output[0].Properties["Height"]?.Value is int value ? value : 0;
            if (rows <= height) {
                Payload = payload;
                return null;
            }

            if (target != height || target <= 1) {
                return "The image does not fit the preview.";
            }

            target = Math.Max(1, height - (rows - height));
        }
    }

    /// <inheritdoc/>
    public void RenderGraphics(GraphicsRenderContext context) {
        if (Payload is not null && App?.IsGraphicsPresentationEnabled == true && ImageBounds.Height > 0) {
            context.Add(
                ImageBounds,
                _content ??= TerminalGraphicContent.FromObject(this),
                ImageScaleMode.Stretch,
                false
            );
        }
    }
}
