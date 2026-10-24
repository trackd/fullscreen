namespace fullscreen.Development;
/// <summary>Represents one resource row in a developer page.</summary>
/// <param name="Key">The stable lookup key.</param>
/// <param name="Cells">The formatted cell texts.</param>
/// <param name="State">The state exposed to the current view or callback. Defaults to null.</param>
public sealed record DeveloperItem(string Key, string[] Cells, object? State = null);
/// <summary>Contains a page of resource rows, columns, and optional paging information.</summary>
/// <param name="Columns">The table column names or column definitions.</param>
/// <param name="Items">The original items represented by the group or page.</param>
/// <param name="Summary">The display summary.</param>
/// <param name="Widths">The preferred display widths in terminal cells. Defaults to null.</param>
public sealed record DeveloperPage(string[] Columns, DeveloperItem[] Items, string Summary, int[]? Widths = null);
/// <summary>Describes an action offered for a developer resource.</summary>
/// <param name="Label">The human-readable label.</param>
/// <param name="Description">The human-readable description.</param>
/// <param name="Execute">The cancellable callback that executes the operation.</param>
/// <param name="InputLabel">The label of the operation&apos;s input prompt. Defaults to null.</param>
/// <param name="InitialText">The initial text shown in the operation prompt. Defaults to &quot;&quot;.</param>
/// <param name="ReadOnly">Whether actions that change external state are disabled. Defaults to false.</param>
/// <param name="TargetView">The view activated by the operation. Defaults to null.</param>
public sealed record DeveloperOperation(
    string Label,
    string Description,
    Func<string, CancellationToken, Task<string>> Execute,
    string? InputLabel = null,
    string InitialText = "",
    bool ReadOnly = false,
    int? TargetView = null
);
/// <summary>Defines resource pages, details, and actions consumed by the developer TUI.</summary>
public interface IDeveloperBackend {
    /// <summary>Gets the displayed heading.</summary>
    string Title { get; }

    /// <summary>Gets the available tab headings.</summary>
    string[] Tabs { get; }

    /// <summary>Loads a page of resources for the requested view.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="page">The zero-based resource page index.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the requested resource page.</returns>
    Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token);
    /// <summary>Loads displayable details for a selected resource.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="item">The selected resource.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the resource detail text.</returns>
    Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token);
    /// <summary>Lists the operations available for the current resource selection.</summary>
    /// <param name="view">The resource or monitor view index.</param>
    /// <param name="item">The selected resource.</param>
    /// <returns>The available resource operations.</returns>
    DeveloperOperation[] Operations(int view, DeveloperItem? item);
}
