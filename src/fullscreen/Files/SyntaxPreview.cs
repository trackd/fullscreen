using System.Management.Automation;

namespace fullscreen.Files;
/// <summary>Creates syntax-highlighted text and Markdown previews.</summary>
public static class SyntaxPreview {
    internal const string MissingNotice = "Install-Module TextMate for syntax highlighting.";
    /// <summary>Creates a Markdown preview visual.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <returns>The configured preview visual.</returns>
    public static TextMatePreview Markdown(string text) =>
        new(text, "markdown");
    /// <summary>Highlights a file by extension, or by a forced TextMate language id or extension.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="path">The filesystem or resource path.</param>
    /// <param name="language">The explicit syntax language, or null for automatic selection. Defaults to null.</param>
    /// <returns>The configured preview visual.</returns>
    public static TextMatePreview File(
        string text,
        string path,
        string? language = null
    ) =>
        new(text, language ?? Language(path), path, language is not null);
    /// <summary>Creates a source or diff preview visual.</summary>
    /// <param name="text">The source text to parse or display.</param>
    /// <param name="fileName">The executable or source filename.</param>
    /// <param name="diff">Whether to use diff presentation. Defaults to false.</param>
    /// <returns>The configured preview visual.</returns>
    public static TextMatePreview Code(
        string text,
        string fileName,
        bool diff = false
    ) =>
        new(text, diff ? "diff" : Language(fileName));
    // Test-TextMate decides which extensions have grammars.
    private static string? Language(string fileName) =>
        Path.GetExtension(fileName) is { Length: > 1 } extension ? extension : null;
    /// <summary>Returns a Spectre renderable, or null with a notice when the module is missing or has no grammar.</summary>
    internal static object? Render(string text, string language, string? path, bool forced, out string? notice) {
        using var pipeline = OptionalModule.Open("TextMate");
        notice = pipeline is null ? MissingNotice : null;
        if (pipeline is null) {
            return null;
        }

        // Files are tested by path; a forced or pathless language is an id (rust) or an extension (.rs).
        pipeline.AddCommand("TextMate\\Test-TextMate").AddParameter("ErrorAction", ActionPreference.Stop);
        if (path is not null && !forced) {
            pipeline.AddParameter("File", path);
        }
        else {
            pipeline.AddParameter(language.StartsWith('.') ? "Extension" : "Language", language);
        }

        if (pipeline.Invoke() is not [{ BaseObject: true }]) {
            notice = $"TextMate has no grammar for '{language}'.";
            return null;
        }

        pipeline.Commands.Clear();
        // Preserve the bounded reader's text for large files. Small files retain
        // TextMate's path context, including Markdown's relative link base.
        bool fileInput = path is not null && System.IO.File.Exists(path) && new FileInfo(path).Length <= 65536;
        pipeline.AddCommand("TextMate\\Format-TextMate").AddParameter("Language", language).AddParameter("ErrorAction", ActionPreference.Stop);
        // TextMate treats direct arguments as paths and pipeline input as source.
        if (fileInput) {
            pipeline.AddParameter("InputObject", path);
        }

        var output = fileInput ? pipeline.Invoke() : pipeline.Invoke(new object[] { text });
        if (pipeline.HadErrors) {
            throw new InvalidOperationException(pipeline.Streams.Error[0].ToString());
        }

        if (output.Count != 1) {
            throw new InvalidOperationException("Format-TextMate did not return one renderable.");
        }

        return output[0].BaseObject;
    }
}
