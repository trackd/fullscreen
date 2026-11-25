# Terminal graphics

Charts select Sixel when the terminal reports support, then fall back to terminal
cells. Charts do not require the separate Sixel PowerShell module; that optional
module supplies image previews.

Use `-ChartRenderer Native` to force terminal cells. Renderer choices vary by
command; use its parameter help for supported modes. ReGIS is an experimental
alternative that requires a compatible terminal and explicit selection.

Press **F12** to inspect renderer selection, graphics support, cell/pixel geometry
and timing, including while a dialog is open. For misplaced images or stale
regions, record the terminal/version, window dimensions and geometry shown by F12.
Retry after resizing and compare Native mode. SSH and multiplexers may affect
capability detection.

Chart zoom is opt-in with EnableZoom. Shift-drag selects a range where supported.
See the [chart cookbook](charts.md) for input and chart examples.
