namespace fullscreen.Files;
/// <summary>No-overwrite operations. Copies publish a complete staged item; moves never emulate copy/delete.</summary>
public static class FileOperations {
    /// <summary>Run <paramref name="action"/> for every source; one failure doesn't stop the rest. Returns the failed sources and a status line.</summary>
    internal static async Task<(string[] Failed, string? Error)> EachAsync(
        string[] sources,
        Func<string, Task> action,
        CancellationToken token
    ) {
        var failed = new List<string>();
        string? first = null;
        foreach (string source in sources) {
            token.ThrowIfCancellationRequested();
            try {
                await action(source).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                failed.Add(source);
                first ??= FileReader.Sanitize(Path.GetFileName(source)) + ": " + error.Message;
            }
        }

        return failed.Count == 0 ? ([], null) : ([.. failed], $"{failed.Count} of {sources.Length} failed · {first}");
    }

    /// <summary>Delete one explicitly selected direct child. Directory links are removed, never traversed.</summary>
    /// <param name="source">The selected filesystem entry&apos;s path.</param>
    /// <param name="parent">The confirmed parent directory containing the selected entry.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    public static void Delete(string source, string parent, CancellationToken token) {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        if (!FileManagerModel.PathComparer.Equals(Path.GetDirectoryName(source), parent)
            || FileManagerModel.PathComparer.Equals(source, Path.GetPathRoot(source))) {
            throw new IOException("Only a selected child of the confirmed directory can be deleted.");
        }

        token.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.Directory) != 0) {
            Directory.Delete(source, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
        }
        else {
            File.Delete(source);
        }
    }

    /// <summary>Moves an entry into a uniquely named bucket in its parent&apos;s .fullscreen-trash
    /// directory.</summary>
    /// <param name="source">The selected filesystem entry&apos;s path.</param>
    /// <returns>The entry&apos;s new path inside .fullscreen-trash, for later recovery.</returns>
    public static string Trash(string source) {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string parent = Path.GetDirectoryName(source) ?? throw new IOException("A filesystem root cannot be moved to trash.");
        if (Path.GetFileName(source).Equals(".fullscreen-trash", StringComparison.OrdinalIgnoreCase)) {
            throw new IOException("Open the trash folder to recover its contents.");
        }

        string trash = Path.Combine(parent, ".fullscreen-trash");
        if (Directory.Exists(trash) && (File.GetAttributes(trash) & FileAttributes.ReparsePoint) != 0) {
            throw new IOException("The trash directory must not be a symbolic link.");
        }

        Directory.CreateDirectory(trash);
        string bucket = Path.Combine(trash, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bucket);
        string target = Path.Combine(bucket, Path.GetFileName(source));
        Move(source, target);
        return target;
    }

    /// <summary>Combines a parent directory with a validated child name.</summary>
    /// <param name="directory">The parent directory of the requested child.</param>
    /// <param name="name">One child filename, without directory separators.</param>
    /// <returns>The child filesystem path.</returns>
    public static string ChildPath(string directory, string name) {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/')
            || name.Contains('\\')) {
            throw new ArgumentException("Enter one valid file or folder name.", nameof(name));
        }

        return Path.Combine(Path.GetFullPath(directory), name);
    }

    /// <summary>Creates a named child directory.</summary>
    /// <param name="parent">The confirmed parent directory containing the selected entry.</param>
    /// <param name="name">One child filename, without directory separators.</param>
    public static void CreateDirectory(string parent, string name) {
        string path = ChildPath(parent, name);
        EnsureAbsent(path);
        Directory.CreateDirectory(path);
    }

    /// <summary>Renames a filesystem entry within its parent directory.</summary>
    /// <param name="source">The selected filesystem entry&apos;s path.</param>
    /// <param name="name">One child filename, without directory separators.</param>
    /// <returns>The renamed entry&apos;s path.</returns>
    public static string Rename(string source, string name) {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string destination = ChildPath(Path.GetDirectoryName(source)!, name);
        // Let .NET recognize the same entry on case-insensitive volumes, including
        // macOS and Unix mounts. A distinct, exactly named destination stays protected.
        if (!string.Equals(source, destination, StringComparison.Ordinal)
            && string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) {
            if (Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(source)!).Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal))) {
                throw new IOException($"Destination already exists: {destination}");
            }

            if (Directory.Exists(source)) {
                Directory.Move(source, destination);
            }
            else {
                File.Move(source, destination);
            }

            return destination;
        }

        Move(source, destination);
        return destination;
    }

    private static void EnsureAbsent(string path) {
        if (Path.Exists(path) || File.Exists(path) || Directory.Exists(path)) {
            throw new IOException($"Destination already exists: {path}");
        }
    }

    private static void ValidateDestination(string source, string destination) {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.GetFullPath(destination);
        if (FileManagerModel.PathComparer.Equals(source, destination)) {
            throw new IOException("Source and destination are the same.");
        }

        if (Directory.Exists(source) && destination.StartsWith(
            source + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        )) {
            throw new IOException("A folder cannot be placed inside itself.");
        }

        if (Directory.Exists(source)) {
            string realSource = Path.TrimEndingDirectorySeparator(ResolveDirectory(source));
            string realDestination = Path.Combine(ResolveDirectory(Path.GetDirectoryName(destination)!), Path.GetFileName(destination));
            if (FileManagerModel.PathComparer.Equals(realSource, realDestination) || realDestination.StartsWith(
                realSource + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
            )) {
                throw new IOException("A folder cannot be placed inside itself through a directory link.");
            }
        }

        EnsureAbsent(destination);
    }

    private static string ResolveDirectory(string path) {
        path = Path.GetFullPath(path);
        string current = Path.GetPathRoot(path)!;
        foreach (string segment in path[current.Length..].Split(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ],
            StringSplitOptions.RemoveEmptyEntries
        )) {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) {
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unable to resolve directory link.");
            }
        }

        return current;
    }

    /// <summary>Moves a filesystem entry to a destination path.</summary>
    /// <param name="source">The selected filesystem entry&apos;s path.</param>
    /// <param name="destination">The target filesystem path, which must not already exist.</param>
    public static void Move(string source, string destination) {
        ValidateDestination(source, destination);
        if (Directory.Exists(source)) {
            Directory.Move(source, destination);
        }
        else {
            File.Move(source, destination, overwrite: false);
        }
    }

    /// <summary>Copies a file or directory tree with cancellation support.</summary>
    /// <param name="source">The selected filesystem entry&apos;s path.</param>
    /// <param name="destination">The target filesystem path, which must not already exist.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public static async Task CopyAsync(string source, string destination, CancellationToken token) {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        ValidateDestination(source, destination);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) {
            throw new IOException("Copying symbolic links is not supported; the link will not be followed.");
        }

        string parent = Path.GetDirectoryName(destination)!;
        string staging = Path.Combine(parent, ".fullscreen-copy-" + Guid.NewGuid().ToString("N"));
        // Validate the private recursive cleanup target before any copying starts.
        if (!FileManagerModel.PathComparer.Equals(Path.GetDirectoryName(staging), parent)
            || !Path.GetFileName(staging).StartsWith(".fullscreen-copy-", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Invalid staging cleanup target.");
        }

        bool directory = Directory.Exists(source);
        var directories = new List<(string Source, string Target)>();
        try {
            if (directory) {
                CreateCopyDirectory(source, staging);
                directories.Add((source, staging));
                var pending = new Stack<(string Source, string Target)>();
                pending.Push((source, staging));
                while (pending.TryPop(out var pair)) {
                    token.ThrowIfCancellationRequested();
                    foreach (var info in new DirectoryInfo(pair.Source).EnumerateFileSystemInfos()) {
                        token.ThrowIfCancellationRequested();
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) {
                            throw new IOException("Folder contains a symbolic link; copy cancelled.");
                        }

                        string target = Path.Combine(pair.Target, info.Name);
                        if (info is DirectoryInfo) {
                            CreateCopyDirectory(info.FullName, target);
                            pending.Push((info.FullName, target));
                            directories.Add((info.FullName, target));
                        }
                        else {
                            await CopyFileAsync(info.FullName, target, token).ConfigureAwait(false);
                        }
                    }
                }

                if (!OperatingSystem.IsWindows()) {
                    for (int i = directories.Count - 1; i >= 0; i--) {
                        File.SetUnixFileMode(directories[i].Target, File.GetUnixFileMode(directories[i].Source));
                    }
                }

                token.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
            }
            else {
                await CopyFileAsync(source, staging, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(staging, destination, overwrite: false);
            }
        }
        finally {
            if (Directory.Exists(staging)) {
                // Restore owner traversal/write on our private staging directories before removing a failed copy.
                if (!OperatingSystem.IsWindows()) {
                    foreach (var (_, Target) in directories) {
                        if (Directory.Exists(Target)) {
                            File.SetUnixFileMode(
                                Target,
                                File.GetUnixFileMode(Target) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                            );
                        }
                    }
                }

                Directory.Delete(staging, recursive: true);
            }
            else if (File.Exists(staging)) {
                File.Delete(staging);
            }
        }
    }

    private static void CreateCopyDirectory(string source, string destination) {
        if (OperatingSystem.IsWindows()) {
            Directory.CreateDirectory(destination);
        }
        else {
            Directory.CreateDirectory(
                destination,
                File.GetUnixFileMode(source) | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token) {
        var options = new FileStreamOptions {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 65536,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows()) {
            options.UnixCreateMode = File.GetUnixFileMode(source);
        }

        using (var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        ))
        using (var output = new FileStream(destination, options)) {
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }

        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
    }
}
