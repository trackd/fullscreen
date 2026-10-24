using System.Collections;
using System.Management.Automation;
using fullscreen.Custom;
using fullscreen.Hosting;

namespace fullscreen.Commands;
/// <summary>Run a custom fullscreen TUI from a native PowerShell definition, JSON or PSD1 file.</summary>
[Cmdlet(VerbsLifecycle.Invoke, "Tui", DefaultParameterSetName = "Definition")]
[Alias("ftui")]
[OutputType(typeof(PSObject))]
public sealed class InvokeTuiCommand : CustomTuiCommand {
    /// <summary>One version-1 hashtable, PSCustomObject or JSON string. Can be passed through the pipeline.
    /// Mutually exclusive with Path. No runtime assembly-qualified types are accepted in saved
    /// documents.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "Definition")]
    [ValidateNotNull]
    public PSObject? Definition { get; set; }

    /// <summary>Literal PSD1 or JSON/JSONC layout file. PSD1 files are parsed as data without executing
    /// code. LiteralPath is an alias. Mutually exclusive with Definition.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Path")]
    [Alias("LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Initial hashtable values overriding definition State. State is copied into app-owned
    /// storage; inputs with Bind populate missing keys.</summary>
    [Parameter]
    public Hashtable? State { get; set; }

    /// <summary>Script block receiving param($tuiEvent, $state). Event contains Id, Kind and Value. Kinds
    /// include Start, Change, Click, Selection, Submit and Close; use Get-TuiControl to see each
    /// control&apos;s supported events. Start has an empty Id, and Click has a null Value. Mutate state
    /// directly or return one hashtable with State updates, Close and optional Value. Other success-stream
    /// output is rejected. Handlers execute only in Invoke-Tui, never in the designer preview.</summary>
    [Parameter]
    public ScriptBlock? OnEvent { get; set; }

    /// <summary>Optional periodic script block receiving param($tuiEvent, $state), with Kind Tick. Same
    /// response contract as OnEvent; runs on the PowerShell/UI thread, without a background
    /// runspace.</summary>
    [Parameter]
    public ScriptBlock? OnTick { get; set; }

    /// <summary>Map control events to scriptblocks, for example @{ &apos;save:Click&apos; = {
    /// param($tuiEvent, $state) ... } }. Keys are case-insensitive and validated against the layout before
    /// opening a terminal. Use :Start and :Tick for lifecycle handlers. A matching entry replaces OnEvent
    /// or OnTick for that event; unmatched events use those callbacks as before. Handlers share the same
    /// state, reply contract and UI thread as OnEvent.</summary>
    [Parameter]
    public Hashtable? Handler { get; set; }

    /// <summary>Minimum periodic callback and chart-sampling interval in milliseconds, 16–60000, default
    /// 250. Tick callbacks use Handler[&apos;:Tick&apos;] when supplied, otherwise OnTick. Without either
    /// callback, chart sampling still runs at this interval.</summary>
    [Parameter]
    [ValidateRange(16, 60000)]
    public int TickInterval { get; set; } = 250;

    /// <summary>Select Auto (Sixel with native fallback), Sixel, Native or Regis (experimental Contour
    /// vectors). Applies to charts throughout the custom TUI. DRCS chart rendering is disabled.</summary>
    [Parameter]
    public Charts.ChartRenderer ChartRenderer { get; set; }

    /// <inheritdoc/>
    protected override void EndProcessing() =>
        RunCustom(stop => {
            var theme = ResolveTheme();
            var document = Path is not null ? TuiDocument.Load(FilePath(Path)) : TuiDocument.FromObject(Definition!);
            var dispatch = CreateDispatcher(document, Handler, OnEvent, OnTick);
            using var app = new CustomTuiApp(
                document,
                State,
                dispatch,
                dispatch,
                TickInterval,
                directory: SessionState.Path.CurrentFileSystemLocation.Path,
                cancellationToken: stop
            );
            FullscreenHost.Run(app, stop, theme, Minimal.IsPresent, ChartRenderer);
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("State", app.State.Clone()));
            result.Properties.Add(new PSNoteProperty("Value", app.Value));
            result.Properties.Add(new PSNoteProperty("Cancelled", app.Cancelled || stop.IsCancellationRequested));
            result.Properties.Add(new PSNoteProperty("LastEvent", app.LastEvent));
            WriteObject(result, false);
        }, "FullscreenInvokeTuiFailed");
    internal static Func<TuiEvent, Hashtable, TuiReply?> CreateDispatcher(
        TuiDocument document,
        Hashtable? handlers,
        ScriptBlock? onEvent,
        ScriptBlock? onTick
    ) {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ":Start",
            ":Tick"
        };
        void Visit(TuiNode node) {
            foreach (string kind in TuiControlCatalog.Describe(node.Type).Events) {
                allowed.Add(node.Id + ":" + kind);
            }

            foreach (var child in node.Children) {
                Visit(child);
            }
        }

        Visit(document.Root);
        var map = new Dictionary<string, ScriptBlock>(StringComparer.OrdinalIgnoreCase);
        if (handlers is not null) {
            foreach (DictionaryEntry entry in handlers) {
                if (entry.Key is not string key || !allowed.Contains(key)) {
                    throw new ArgumentException($"Unknown TUI handler '{entry.Key}'. Use a control ID and supported event, such as 'save:Click', or ':Start' / ':Tick'.");
                }

                object? value = entry.Value is PSObject wrapper ? wrapper.BaseObject : entry.Value;
                if (value is not ScriptBlock script) {
                    throw new ArgumentException($"Handler '{key}' must be a scriptblock.");
                }

                if (!map.TryAdd(key, script)) {
                    throw new ArgumentException($"Duplicate TUI handler '{key}'.");
                }
            }
        }

        return (ev, state) => {
            ScriptBlock? script = map.TryGetValue(ev.Id +
                ":" + ev.Kind, out var specific) ? specific : ev.Kind == "Tick" ? onTick : onEvent;
            return script is null ? null : Callback(script, ev, state);
        };
    }

    /// <summary>Invokes a PowerShell event handler and converts its response to a TUI reply.</summary>
    /// <param name="script">The PowerShell callback script.</param>
    /// <param name="ev">The control interaction delivered to the callback.</param>
    /// <param name="state">The state exposed to the current view or callback.</param>
    /// <returns>The converted reply, or null when the handler returns no reply.</returns>
    public static TuiReply? Callback(ScriptBlock script, TuiEvent ev, Hashtable state) {
        var eventObject = new PSObject();
        eventObject.Properties.Add(new PSNoteProperty("Id", ev.Id));
        eventObject.Properties.Add(new PSNoteProperty("Kind", ev.Kind));
        eventObject.Properties.Add(new PSNoteProperty("Value", ev.Kind == "Click" ? null : ev.Value));
        var output = script.Invoke(eventObject, state);
        if (output.Count == 0) {
            return null;
        }

        if (output.Count != 1 || output[0].BaseObject is not IDictionary response) {
            throw new ArgumentException("TUI handlers must emit no output or one hashtable with State, Close and/or Value. Assign other command output to a variable.");
        }

        object? Get(string key) {
            foreach (DictionaryEntry pair in response) {
                if (string.Equals(pair.Key.ToString(), key, StringComparison.OrdinalIgnoreCase)) {
                    return pair.Value is PSObject item && item.BaseObject is not PSCustomObject ? item.BaseObject : pair.Value;
                }
            }

            return null;
        }

        object? update = Get("State");
        if (update is not null && update is not IDictionary) {
            throw new ArgumentException("Handler State must be a dictionary.");
        }

        return new(update as IDictionary, LanguagePrimitives.IsTrue(Get("Close")), Get("Value"));
    }
}
