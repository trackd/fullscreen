using System.Globalization;
using System.Reflection;
using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;

namespace fullscreen.Custom;
/// <summary>Optional, ALC-safe Spectre adapter. Only styled text reaches XenoAtom's cell renderer.</summary>
public sealed partial class SpectreView : Visual {
    // Paragraph's single-line mode also discards hard line breaks. Spectre has
    // already laid out the document at the viewport width; retain those rows.
    private readonly Paragraph _paragraph = new() {
        Wrap = true,
        HorizontalAlignment = Align.Stretch
    };
    private object? _content;
    private string? _ansi;
    private int _width = -1, _renderedRevision = -1;
    [Bindable]
    private partial int Revision { get; set; }

    /// <summary>Initializes a new SpectreView instance.</summary>
    public SpectreView() {
        AttachChild(_paragraph);
        HorizontalAlignment = Align.Stretch;
    }

    /// <summary>Replaces the value rendered by the optional Spectre.Console integration.</summary>
    /// <param name="value">The input value to format, display, or append.</param>
    public void SetContent(object? value) {
        _content = value?.GetType().FullName == "System.Management.Automation.PSObject" ? value.GetType().GetProperty("BaseObject")!.GetValue(value) : value;
        if (_width > 0) {
            string ansi = RenderContent(_width);
            if (ansi == _ansi) {
                return;
            }
            ApplyAnsi(ansi);
        }
        Revision++;
    }

    private string RenderContent(int width) =>
        _content is null ? "Spectre · bind an IRenderable through Invoke-Tui -State" : RenderToAnsi(_content, width);

    private void ApplyAnsi(string ansi) {
        if (ansi == _ansi) {
            return;
        }
        var (text, runs) = ParseAnsi(ansi);
        _paragraph.Text = text;
        _paragraph.Runs = runs;
        _ansi = ansi;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _paragraph : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        int width = Math.Clamp(constraints.MaxWidth, 1, 1000);
        if (_width != width || _renderedRevision != Revision) {
            if (_width != width || _ansi is null) {
                ApplyAnsi(RenderContent(width));
            }
            _width = width;
            _renderedRevision = Revision;
        }

        _paragraph.Measure(new Size(width, constraints.MaxHeight));
        return SizeHints.Flex(new Size(0, 0), _paragraph.DesiredSize, new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        _paragraph.Arrange(finalRect);
    internal static string RenderToAnsi(object content, int width) {
        if (content.GetType().FullName == "System.Management.Automation.PSObject") {
            content = content.GetType().GetProperty("BaseObject")!.GetValue(content)!;
        }

        var contract = content.GetType().GetInterfaces().FirstOrDefault(type => type.FullName == "Spectre.Console.Rendering.IRenderable") ?? throw new ArgumentException("Spectre controls require a Spectre.Console.Rendering.IRenderable. Pass the object in Invoke-Tui -State, not in the layout.");
        var assembly = contract.Assembly;
        Type Find(string name) =>
            assembly.GetType("Spectre.Console." + name, throwOnError: true)!;
        using var writer = new BoundedWriter();
        object settings = Activator.CreateInstance(Find("AnsiConsoleSettings"))!;
        void SetEnum(string property, string name) {
            var info = settings.GetType().GetProperty(property)!;
            info.SetValue(settings, Enum.Parse(info.PropertyType, name));
        }

        SetEnum("Ansi", "Yes");
        SetEnum("ColorSystem", "TrueColor");
        SetEnum("Interactive", "No");
        settings.GetType().GetProperty("Out")!.SetValue(settings, Activator.CreateInstance(Find("AnsiConsoleOutput"), writer));
        object console = Find("AnsiConsole").GetMethod("Create", [settings.GetType()])!.Invoke(null, [settings])!;
        var consoleContract = Find("IAnsiConsole");
        object profile = consoleContract.GetProperty("Profile")!.GetValue(console)!;
        profile.GetType().GetProperty("Width")!.SetValue(profile, width);
        try {
            consoleContract.GetMethod("Write", [contract])!.Invoke(console, [content]);
        }
        catch (TargetInvocationException error) {
            throw new ArgumentException(
                "Spectre rendering failed: " + error.InnerException?.Message,
                error.InnerException ?? error
            );
        }

        return writer.ToString();
    }

    internal static (string Text, StyledRun[] Runs) ParseAnsi(string ansi) {
        using var parser = new AnsiStyledTextParser();
        var text = new StringBuilder();
        var runs = new List<StyledRun>();
        foreach (var run in parser.Parse(ansi)) {
            // Retain layout newlines and tabs; discard all other control characters.
            string clean = new([.. run.Text.Where(character => !char.IsControl(character) || character is '\n' or '\t')]);
            if (clean.Length == 0) {
                continue;
            }

            var style = Style.None.WithTextStyle((TextStyle)(int)run.Style.Decorations);
            if (run.Style.Foreground is { Kind: not AnsiColorKind.Default } foreground) {
                style = style.WithForeground(foreground);
            }

            if (run.Style.Background is { Kind: not AnsiColorKind.Default } background) {
                style = style.WithBackground(background);
            }

            runs.Add(new(text.Length, clean.Length, style));
            text.Append(clean);
        }

        return (text.ToString(), runs.ToArray());
    }

    private sealed class BoundedWriter : StringWriter {
        public BoundedWriter() : base(CultureInfo.InvariantCulture) {
        }

        public override void Write(char value) {
            Limit(1);
            base.Write(value);
        }

        public override void Write(string? value) {
            Limit(value?.Length ?? 0);
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count) {
            Limit(count);
            base.Write(buffer, index, count);
        }

        private void Limit(int count) {
            if (GetStringBuilder().Length + count > 1024 * 1024) {
                throw new InvalidOperationException("Spectre output exceeds 1 MiB; render a smaller document or page.");
            }
        }
    }
}
