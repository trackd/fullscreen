using fullscreen.Custom;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Files;
/// <summary>Optional TextMate preview; the module owns parsing and grammar dependencies. Falls back to plain text.</summary>
public sealed partial class TextMatePreview : Visual {
    private readonly string? _language;
    private readonly string? _path;
    private readonly bool _forced;
    private Visual _content = new TextBlock("");
    private string? _loaded;
    /// <summary>Gets or sets the source text to parse or display.</summary>
    [Bindable]
    public partial string Text { get; set; }

    /// <summary>Initializes a new TextMatePreview instance.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="language">The explicit syntax language, or null for automatic selection. Defaults to
    /// &quot;markdown&quot;.</param>
    /// <param name="path">The filesystem or resource path. Defaults to null.</param>
    /// <param name="forced">Whether the requested operation bypasses the optional guard. Defaults to
    /// false.</param>
    public TextMatePreview(string text, string? language = "markdown", string? path = null, bool forced = false) {
        Text = text;
        _language = language;
        _path = path;
        _forced = forced;
        AttachChild(_content);
        HorizontalAlignment = Align.Stretch;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _content : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        if (_loaded != Text) {
            Visual next;
            // Unknown extensions (.txt) are plain text; only a missing module or a forced language shows a notice.
            string? notice = null;
            try {
                next = _language is not null
                    && SyntaxPreview.Render(Text, _language, _path, _forced, out notice) is { } renderable ? Spectre(renderable) : Plain(_forced || notice == SyntaxPreview.MissingNotice ? notice : null);
            }
            catch (Exception error) when (error is not OutOfMemoryException) {
                next = Plain(_forced ? error.Message : null);
            }

            DetachChild(_content);
            _content = next;
            AttachChild(_content);
            _loaded = Text;
        }

        _content.Measure(constraints);
        return SizeHints.Flex(new(0, 0), _content.DesiredSize, new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    private static SpectreView Spectre(object renderable) {
        var view = new SpectreView();
        view.SetContent(renderable);
        return view;
    }

    private TextBlock Plain(string? notice) =>
        new(FileReader.Sanitize(notice is null ? Text : notice +
        "\n\n" + Text, true));
    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        _content.Arrange(finalRect);
}
