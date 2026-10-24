using System.Globalization;
using System.Buffers;
using System.Text;

namespace fullscreen.Files;
/// <summary>Reads directory snapshots and bounded, sanitized file previews.</summary>
public static class FileReader {
    /// <summary>Stores the directory entry limit and maximum preview size in bytes.</summary>
    public const int MaxEntries = 100000, MaxTextBytes = 65536;
    /// <summary>Reads a directory and optionally its parent&apos;s entries.</summary>
    /// <param name="path">The directory path to read.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <param name="includeParent">Whether the snapshot includes parent-directory entries. Defaults to
    /// true.</param>
    /// <returns>The directory snapshot, including whether the listing was truncated.</returns>
    public static DirectorySnapshot ReadDirectory(string path, CancellationToken token, bool includeParent = true) {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var entries = ReadEntries(path, MaxEntries, token, out bool truncated);
        FileEntry[] parentEntries = [];
        string? parent = Directory.GetParent(path)?.FullName;
        if (includeParent && parent is not null) {
            try {
                parentEntries = ReadEntries(parent, 5000, token, out _);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            }
        }

        Array.Sort(
            parentEntries,
            static (
                a,
                b
            ) => a.IsDirectory != b.IsDirectory ? a.IsDirectory ? -1 : 1 : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)
        );
        return new(path, entries, parentEntries, truncated);
    }

    private static FileEntry[] ReadEntries(string path, int maximum, CancellationToken token, out bool truncated) {
        var entries = new List<FileEntry>();
        truncated = false;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos()) {
            token.ThrowIfCancellationRequested();
            if (entries.Count == maximum) {
                truncated = true;
                break;
            }

            try {
                var attributes = info.Attributes;
                bool directory = (attributes & FileAttributes.Directory) != 0;
                entries.Add(new(
                    info.FullName,
                    info.Name,
                    directory,
                    (attributes & FileAttributes.ReparsePoint) != 0,
                    directory ? 0 : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc,
                    (attributes & FileAttributes.Hidden) != 0 || info.Name.StartsWith('.')
                ));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            }
        }

        return [.. entries];
    }

    /// <summary>Reads a bounded preview of a directory or file.</summary>
    /// <param name="entry">The filesystem entry to preview.</param>
    /// <param name="showHidden">Whether hidden entries are included in directory previews.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing preview content and its presentation kind.</returns>
    public static async Task<FilePreview> ReadPreviewAsync(FileEntry entry, bool showHidden, CancellationToken token) {
        if (entry.IsDirectory) {
            var children = ReadEntries(entry.FullPath, 200, token, out bool truncated);
            Array.Sort(
                children,
                static (
                    a,
                    b
                ) => a.IsDirectory != b.IsDirectory ? a.IsDirectory ? -1 : 1 : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)
            );
            var text = new StringBuilder();
            foreach (var child in children) {
                if (showHidden || !child.Hidden) {
                    text.Append(child.IsDirectory ? "▸ " : "  ").AppendLine(Sanitize(child.Name));
                }
            }

            if (truncated) {
                text.AppendLine("… first 200 entries");
            }

            return new(entry.FullPath, PreviewKind.Directory, text.ToString());
        }

        string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        // The formats the Sixel module (ImageSharp) decodes.
        bool image = extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".tga"
            or ".pbm";
        token.ThrowIfCancellationRequested();
        if (image) {
            return new(entry.FullPath, PreviewKind.Image, $"{Sanitize(entry.Name)} · {entry.Length:N0} bytes");
        }

        // shortcut: on Unix, FIFOs and devices report length 0 and open() can block forever; zero-length /proc files lose their preview too.
        if (!OperatingSystem.IsWindows() && entry.Length == 0) {
            return new(entry.FullPath, PreviewKind.Unavailable, $"{Sanitize(entry.Name)} · 0 bytes");
        }

        const int limit = MaxTextBytes;
        using var stream = new FileStream(
            entry.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        const int capacity = limit + 1;
        byte[] rented = ArrayPool<byte>.Shared.Rent(capacity);
        try {
            int count = 0;
            while (count < capacity) {
                int read = await stream.ReadAsync(rented.AsMemory(count, capacity - count), token).ConfigureAwait(false);
                if (read == 0) {
                    break;
                }

                count += read;
            }

            token.ThrowIfCancellationRequested();
            bool truncated = count > limit;
            count = Math.Min(count, limit);
            string text;
            try {
                if (count >= 2 && rented[0] == 0xff && rented[1] == 0xfe) {
                    text = Encoding.Unicode.GetString(rented, 2, count - 2);
                }
                else if (count >= 2 && rented[0] == 0xfe && rented[1] == 0xff) {
                    text = Encoding.BigEndianUnicode.GetString(rented, 2, count - 2);
                }
                else if (rented.AsSpan(0, count).Contains((byte)0)) {
                    return HexPreview(entry.FullPath, rented.AsSpan(0, Math.Min(count, 512)));
                }
                else {
                    int start = count >= 3 && rented[0] == 0xef && rented[1] == 0xbb && rented[2] == 0xbf ? 3 : 0;
                    // A bounded UTF-8 preview may finish inside a scalar; decode only the complete prefix.
                    int used = count - start;
                    if (truncated) {
                        while (used > 0 && (rented[start + used] & 0xc0) == 0x80) {
                            used--;
                        }
                    }

                    text = new UTF8Encoding(false, true).GetString(rented, start, used);
                }
            }
            catch (DecoderFallbackException) {
                return HexPreview(entry.FullPath, rented.AsSpan(0, Math.Min(count, 512)));
            }

            text = Sanitize(text, preserveLines: true);
            if (truncated) {
                text += "\n\n… preview limited to 64 KiB";
            }

            return new(
                entry.FullPath,
                extension is ".md" or ".markdown" ? PreviewKind.Markdown : PreviewKind.Text,
                text
            );
        }
        finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static FilePreview HexPreview(string path, ReadOnlySpan<byte> bytes) {
        var text = new StringBuilder("BINARY · first 512 bytes\n\n");
        for (int offset = 0; offset < bytes.Length; offset += 16) {
            text.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            foreach (byte value in bytes.Slice(offset, Math.Min(16, bytes.Length - offset))) {
                text.Append(value.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            }

            text.AppendLine();
        }

        return new(path, PreviewKind.Binary, text.ToString());
    }

    /// <summary>Removes terminal control characters from display text.</summary>
    /// <param name="value">The terminal text to sanitize.</param>
    /// <param name="preserveLines">Whether newline characters remain in sanitized text. Defaults to
    /// false.</param>
    /// <returns>Text safe for terminal display.</returns>
    public static string Sanitize(string value, bool preserveLines = false) {
        return string.Create(value.Length, (value, preserveLines), static (chars, state) => {
            for (int i = 0; i < chars.Length; i++) {
                char c = state.value[i];
                chars[i] = char.IsControl(c) && !(state.preserveLines && c is '\n' or '\r' or '\t') ? '�' : c;
            }
        });
    }
}
