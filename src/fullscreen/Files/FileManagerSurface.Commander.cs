namespace fullscreen.Files;

public sealed partial class FileManagerSurface {
    /// <summary>Requests copying or moving the selected operation paths to another directory.</summary>
    /// <param name="destination">The directory that receives the selected entries.</param>
    /// <param name="move">Whether to move rather than copy the source paths.</param>
    public void TransferTo(string destination, bool move) {
        if (Busy || Model.OperationPaths() is not { Length: > 0 } paths) {
            return;
        }

        destination = Path.GetFullPath(destination);
        var operation = new FileOperation(
            move ? FileOperationKind.Move : FileOperationKind.Copy,
            paths,
            Model.CurrentPath,
            destination
        );
        Confirm(
            operation.Description +
            "\n" + List(paths) +
            "\n\nTo: " + destination +
            "\nExisting destinations will not be overwritten.",
            () => OperationRequested?.Invoke(operation)
        );
    }

    /// <summary>The first eight paths, one per line, for confirmation dialogs.</summary>
    internal static string List(string[] paths) =>
        string.Join('\n', paths.Take(8).Select(path => FileReader.Sanitize(path))) + (paths.Length > 8 ? $"\n… and {paths.Length - 8} more" : "");
    /// <summary>Requests moving the selected operation paths to the trash.</summary>
    public void TrashSelected() {
        if (Busy || Model.OperationPaths() is not { Length: > 0 } paths) {
            return;
        }

        Confirm(
            "Move " + paths.Length +
            " selected item(s) to .fullscreen-trash?\n" + string.Join('\n', paths.Take(8).Select(path => FileReader.Sanitize(path))) +
            "\n\nRecover files from the UUID subfolders using the other pane. This is Fullscreen's local trash, not the OS recycle bin.",
            () => OperationRequested?.Invoke(new(FileOperationKind.Trash, paths, Model.CurrentPath, ""))
        );
    }

    /// <summary>Prompts for and requests creation of a child directory.</summary>
    public void CreateFolder() =>
        NewDirectory();
    /// <summary>Prompts for and requests renaming the selected entry.</summary>
    public void RenameSelected() =>
        Rename();
    /// <summary>Opens the file-manager navigation help dialog.</summary>
    public void ShowNavigationHelp() =>
        Help();
    /// <summary>Requests a fresh snapshot of the current directory.</summary>
    public void RefreshDirectory() {
        if (!Busy && Model.CurrentPath.Length > 0) {
            Navigate(Model.CurrentPath, false);
        }
    }

    /// <summary>Navigates to the supplied directory.</summary>
    /// <param name="path">The filesystem or resource path.</param>
    public void ShowDirectory(string path) {
        if (!Busy) {
            Navigate(path);
        }
    }
}
