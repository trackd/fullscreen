# Examples

Run these scripts from a source checkout after importing fullscreen.

The [examples](.) folder contains runnable scripts. Custom layout scripts
accept `-DefinitionOnly` to return the layout without opening a terminal:

```powershell
./examples/ContactForm.ps1 -Theme 'Dark Modern'
$layout = ./examples/SettingsTabs.ps1 -DefinitionOnly
$layout | Show-TuiDesigner
$layout | Export-TuiDefinition -Path ./settings.tui.psd1
```

| Example | Demonstrates |
| --- | --- |
| [PingPlotter.ps1](PingPlotter.ps1) | Route-hop histories, latency bands, loss and replay |
| [PowerShellUniversalMonitor.ps1](PowerShellUniversalMonitor.ps1) | PSU service, jobs, endpoints and optional metrics |
| [ServiceCards.ps1](ServiceCards.ps1) | Independently expandable service cards |
| [CommandTree.ps1](CommandTree.ps1) | Scoped commands, lazy trees and linked chart ranges |
| [ControlGallery.ps1](ControlGallery.ps1) | Every fullscreen control, handler maps, shared state and selected objects |
| [NativeControls.ps1](NativeControls.ps1) | Native widgets with populated data; filter by name |
| [UserSessions.ps1](UserSessions.ps1) | Login sessions with expandable account processes |
| [ContactForm.ps1](ContactForm.ps1) | Bound inputs, validation and object results |
| [ReusableControls.ps1](ReusableControls.ps1) | Deployment workbench with tables, code, charts and multiline input |
| [Workspace.ps1](Workspace.ps1) | Files, telemetry and logs in tabs |
| [HandlerMap.ps1](HandlerMap.ps1) | Direct event handlers and shared state |
| [DesignerStarter.ps1](DesignerStarter.ps1) | A styled form and chart for the designer |
| [GraphGallery.ps1](GraphGallery.ps1) | The original twelve graph styles in tabs |
| [ChartShowcase.ps1](ChartShowcase.ps1) | All 26 chart kinds, cycling in one window every three seconds for recording |
| [ChartCombinations.ps1](ChartCombinations.ps1) | Named multi-line/area series, grouped bars, column/line roles and clickable legends |
| [StackedAreas.ps1](StackedAreas.ps1) | Additive and 100-percent request-outcome areas, missing-component gaps and portable definitions |
| [RadialCharts.ps1](RadialCharts.ps1) | Pie slices, semicircular load gauges, radial lanes and radar comparisons |
| [WaterfallHeatmap.ps1](WaterfallHeatmap.ps1) | Signed cumulative changes and matrix cells with a shared color scale |
| [MarketCharts.ps1](MarketCharts.ps1) | Variable-width Marimekko composition and fixed-price Kagi reversals |
| [Flamegraph.ps1](Flamegraph.ps1) | Hierarchical frames with exclusive self samples and inclusive widths |
| [Gantt.ps1](Gantt.ps1) | Absolute task intervals, progress and milestones |
| [RelationshipCharts.ps1](RelationshipCharts.ps1) | Flow/network layouts with separate node and edge collections |
| [LiveChart.ps1](LiveChart.ps1) | Numeric collections and scale options |
| [ProcessDashboard.ps1](ProcessDashboard.ps1) | Live process data and tick callbacks |
| [BackgroundJob.ps1](BackgroundJob.ps1) | Thread jobs, polling and cleanup |
| [LogTail.ps1](LogTail.ps1) | Bounded file reads and error display |
| [EventViewer.ps1](EventViewer.ps1) | Windows event logs, background queries, filters, resizable panes and event details; optional Windows PSSession |
| [SettingsTabs.ps1](SettingsTabs.ps1) | Settings controls and validated results |
| [CommandHelp.ps1](CommandHelp.ps1) | Local help display |
| [SpectreContent.ps1](SpectreContent.ps1) | Spectre renderables supplied by TextMate or another module |
| [ToolModule.psm1](ToolModule.psm1) | A pipeline picker that returns original objects |
| [NativeLayout.psd1](NativeLayout.psd1) | A portable data-only layout |
| [ISpyBrowser.ps1](ISpyBrowser.ps1) | Assembly browsing with optional TextMate previews |

The eight chart examples from `ChartCombinations.ps1` through
`RelationshipCharts.ps1` accept `-Theme`, `-Minimal` and `-DefinitionOnly`,
plus their chart-specific options.
`-DefinitionOnly` returns a complete portable layout for `Show-TuiDesigner`,
`Export-TuiDefinition` or `Invoke-Tui`; `-DataOnly` still returns original sample
records (separate Nodes/Edges for relationship charts). DataOnly takes precedence
when both output switches are present. Presentation parameters do not change
portable data; pass them to `Invoke-Tui` when running a returned definition.

Run `./examples/ChartShowcase.ps1 -Theme Monokai -Minimal` for an automatic
showcase. `-SecondsPerChart 5` changes the dwell time; `-Once` closes after one
complete cycle. Previous, Pause / Resume and Next control playback without
opening another window. `-DefinitionOnly` returns the static layout; timed
callbacks remain in the script, as with generated scaffolds.
