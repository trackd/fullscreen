using System.Management.Automation;
using fullscreen.Hosting;
using fullscreen.Objects;

namespace fullscreen.Commands;
/// <summary>Explore PowerShell objects in a fullscreen table.</summary>
[Cmdlet(VerbsCommon.Show, "ObjectTable")]
[Alias("fobject")]
[OutputType(typeof(PSObject))]
public sealed class ShowObjectTableCommand : FullscreenCommand {
    private readonly List<PSObject> _objects = [];
    /// <summary>Objects from the pipeline or an explicit array. Null entries are skipped; input is
    /// collected before the UI opens.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [AllowNull]
    public PSObject[]? InputObject { get; set; }

    /// <summary>Literal property names for the Formatted table, in order. Overrides automatic display
    /// metadata. Missing properties produce empty cells.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    [ValidateNotNullOrEmpty]
    public string[]? Property { get; set; }

    /// <summary>Literal property names initially hidden. C can reveal them. At least one column must remain
    /// visible.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    [ValidateNotNullOrEmpty]
    public string[]? ExcludeProperty { get; set; }

    /// <summary>Table title.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Title { get; set; } = "◈ OBJECTS";

    /// <summary>Initial case-insensitive filter across displayed cell values. Plain text matches
    /// substrings; regex: and wildcard: prefixes select regular-expression and wildcard matching. Slash
    /// edits the filter. Regular expressions have a 50 ms timeout.</summary>
    [Parameter]
    [ValidateNotNull]
    public string Filter { get; set; } = "";

    /// <summary>Initial displayed column label for typed sorting in Formatted, such as CPU(s).
    /// With Property, use the supplied names. Raw numeric and timestamp values sort by type.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    [ValidateNotNullOrEmpty]
    public string? SortBy { get; set; }

    /// <summary>Literal property name used as the primary sort key, arranging equal values contiguously.
    /// SortBy orders rows within each group. G cycles grouping.</summary>
    [Parameter]
    [ArgumentCompleter(typeof(ObjectPropertyArgumentCompleter))]
    [ValidateNotNullOrEmpty]
    public string? GroupBy { get; set; }

    /// <summary>Reverse the initial sort direction within groups.</summary>
    [Parameter]
    public SwitchParameter Descending { get; set; }

    /// <summary>Return original marked objects, or the current object, when Enter accepts in the root
    /// Formatted table. Enter inside Inspect or JSON does not accept. Q or Escape cancels with
    /// no output.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Initial lazy tab: Formatted (default), Inspect or Json.
    /// Unopened tabs perform no metadata lookup, getter evaluation or
    /// serialization.</summary>
    [Parameter]
    public ObjectView View { get; set; }

    /// <summary>Maximum depth of the bounded JSON snapshot, 1 through 20; default 4. Only the selected
    /// object is snapshotted when first viewed. Circular references, depth limits and failing getters
    /// become markers. Collections and property maps are limited to 1000 entries each, with a shared budget
    /// of 10000 complex values. Display is capped at 1 MiB after serialization; up to 16 visited objects
    /// are cached.</summary>
    [Parameter]
    [ValidateRange(1, 20)]
    public int JsonDepth { get; set; } = 4;

    /// <summary>Maximum buffered input objects, default 100000. Exceeding the limit raises a terminating
    /// error rather than silently dropping records.</summary>
    [Parameter]
    [ValidateRange(1, 1000000)]
    public int MaximumCount { get; set; } = 100000;

    /// <inheritdoc/>
    protected override void ProcessRecord() {
        if (InputObject is null) {
            return;
        }

        foreach (var item in InputObject) {
            if (item is null) {
                continue;
            }

            if (_objects.Count >= MaximumCount) {
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException("Input exceeds MaximumCount; limit the upstream pipeline or increase MaximumCount."),
                    "FullscreenObjectLimit",
                    ErrorCategory.LimitsExceeded,
                    MaximumCount
                ));
            }

            _objects.Add(item);
        }
    }

    /// <inheritdoc/>
    protected override void EndProcessing() {
        if (PipelineStopToken.IsCancellationRequested) {
            return;
        }

        try {
            using var app = new ObjectTableApp(
                _objects,
                new ObjectTableOptions {
                    Property = Property,
                    ExcludeProperty = ExcludeProperty,
                    Title = Tables.TableModel.Text(Title),
                    Filter = Filter ?? "",
                    SortBy = SortBy,
                    GroupBy = GroupBy,
                    Descending = Descending.IsPresent,
                    PassThru = PassThru.IsPresent,
                    View = View,
                    JsonDepth = JsonDepth
                }
            );
            FullscreenHost.Run(app, PipelineStopToken, ResolveTheme(), Minimal.IsPresent);
            if (PassThru.IsPresent) {
                foreach (object item in app.Chosen) {
                    WriteObject(item, false);
                }
            }
        }
        catch (PipelineStoppedException) {
            throw;
        }
        catch (Exception error) {
            ThrowTerminatingError(new ErrorRecord(error, "FullscreenObjectTableFailed", ErrorCategory.InvalidOperation, null));
        }
        finally {
            _objects.Clear();
        }
    }
}
