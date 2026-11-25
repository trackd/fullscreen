# Custom TUIs

Create definitions from PowerShell objects or PSD1/JSON layouts. Compose a root with New-TuiControl, validate it with New-TuiDefinition, and run it with Invoke-Tui. Show-TuiDesigner edits layouts visually; Export-TuiDefinition creates runnable PowerShell scaffolding.

## State and callbacks

Run a custom terminal app from a layout created with New-TuiDefinition,
Export-TuiDefinition or Show-TuiDesigner. Layouts contain data, not executable
handlers; behavior comes from the script blocks passed to this command.

Controls bind to the shared State dictionary. Inputs update their bound values;
lists, grids and charts publish selections through SelectionBind. Replace bound
collections to refresh their contents. Get-TuiControl describes each control's
properties, options and events.

Shared document commands emit `Command` using the command ID, whether invoked
from a shortcut, menu, command bar or palette. Scoped/state-disabled actions are
omitted from contextual footer hints. Existing item-label `Click` events remain
available for layouts without shared commands.

For a `TreeView` with `Lazy = $true`, rows advertise `HasChildren = $true`.
The first expansion emits `Expand` with the original row; return
`@{ Value = @(...child rows...) }`. Null or an empty collection completes a leaf.
Child results are cached until the bound root collection is replaced. The control
does not inspect `Children` properties in lazy mode. Requests are synchronous;
prepare slow data in a background job. `Get-TuiControl TreeView` exposes the property
and event, and exported scaffolds include the callback.

Charts can share a `RangeBind` state key containing `MinimumX` and `MaximumX`, or
null. Shift-dragging a chart updates this key before its `Range` callback; other
charts sharing the key show the same interval. Updating state sets/clears ranges
without echoing callbacks. Intervals must be finite and ordered. Relationship
diagrams do not accept this binding. The repository's CommandTree sample combines
lazy loading, shared commands and linked chart intervals.

Charts prefer Sixel when supported and otherwise render terminal cells. Wheel zoom
and keyboard zoom/pan are disabled by default; set `EnableZoom = $true` on a Chart
to opt in, including scalar-bound history charts. Embedded charts otherwise leave
the wheel to their surrounding scroll viewer.
Single-series Bars and HorizontalBars automatically color each category and show
matching category legends. Grouped and stacked bars retain per-series colors;
explicit series colors take precedence.

Handlers run on the UI thread and can update State or request that the app close.
Keep callbacks short, avoid host output, and use background jobs for long work.
When the app closes, the command returns State, Value, Cancelled and LastEvent.

## Saving and exporting

A PowerShell export embeds its layout. With -LinkedLayout, it loads a sibling .tui.psd1 file. Existing scripts are preserved; use -Force when intentionally replacing layout data. Saved definitions contain serializable defaults; pass jobs and runtime objects through Invoke-Tui -State.

Generated scripts accept -Theme, -Minimal, -State, -TickInterval and -DefinitionOnly. Exported callbacks are scaffolding: add your application's behavior.

## Next steps

- [Control cookbook](controls.md)
- [Chart cookbook](charts.md)
- [Runnable examples](../../examples/README.md)
