# fullscreen

Interactive terminal apps and a composable TUI framework for PowerShell, built
with [XenoAtom.Terminal.UI](https://xenoatom.github.io/terminal/). Monitor system
activity, browse files and objects, follow logs, manage development work, or
build custom apps with reusable controls and a visual designer.

## Previews

![Performance dashboard with Sixel graphs and demonstration data](docs/images/dashboard.png)

## Install

Requires PowerShell 7.6 or later. Install the module from PowerShell Gallery,
then open the system monitor:

```powershell
Install-Module fullscreen
Import-Module fullscreen
ftop
```

## Cmdlets

| Cmdlet | Alias | Description |
| --- | --- | --- |
| [Show-SystemMonitor](docs/en-US/Show-SystemMonitor.md) | `ftop` | Processes, services, network, GPU and graphs; local or remote. |
| [Show-FileManager](docs/en-US/Show-FileManager.md) | `ffile` | Browse files with previews and return selected paths. |
| [Show-Commander](docs/en-US/Show-Commander.md) | `fmc` | Dual-pane file manager with copy, move and recoverable trash. |
| [Show-ObjectTable](docs/en-US/Show-ObjectTable.md) | `fobject` | Explore pipeline objects, inspect properties and return selections. |
| [Out-Chart](docs/en-US/Out-Chart.md) | | Chart whole pipeline objects; change axes, values and graph type in the TUI. |
| [Show-LogViewer](docs/en-US/Show-LogViewer.md) | `flog` | Follow and filter text or JSON logs. |
| [Show-GitManager](docs/en-US/Show-GitManager.md) | `fgit` | Manage Git changes, history, branches and worktrees; requires `git` on `PATH`. |
| [Show-GitHubManager](docs/en-US/Show-GitHubManager.md) | `fgh` | Review issues, pull requests and workflows; requires an authenticated `gh` CLI. |
| [Show-AzureDevOpsManager](docs/en-US/Show-AzureDevOpsManager.md) | `fado` | Browse boards, work items, pull requests and pipelines; requires Azure CLI. |
| [Show-TuiDesigner](docs/en-US/Show-TuiDesigner.md) | `fdesign` | Design layouts visually and export PowerShell scaffolding. |
| [Invoke-Tui](docs/en-US/Invoke-Tui.md) | `ftui` | Run a custom TUI with state and PowerShell callbacks. |
| [Get-TuiControl](docs/en-US/Get-TuiControl.md) | — | Discover reusable controls, properties, bindings and events. |
| [New-TuiControl](docs/en-US/New-TuiControl.md) | — | Create a control definition for a custom layout. |
| [New-TuiDefinition](docs/en-US/New-TuiDefinition.md) | — | Create or validate a layout from native PowerShell objects. |
| [Import-TuiDefinition](docs/en-US/Import-TuiDefinition.md) | — | Load a PSD1 or JSON layout. |
| [Export-TuiDefinition](docs/en-US/Export-TuiDefinition.md) | — | Save PSD1/JSON or export a runnable PowerShell scaffold. |
| [Get-FullscreenTheme](docs/en-US/Get-FullscreenTheme.md) | — | Discover palettes and customize theme colors. |

Press **F12** in any interactive cmdlet, including `Invoke-Tui` and the designer,
to toggle XenoAtom's diagnostic overlay. It shows frame timings, repaint work,
host output and keyboard focus. F12 also works while a dialog is open and in
minimal mode; press it again to hide the overlay.

<details>
<summary>Object exploration, user sessions and themes</summary>

Screenshots use demonstration data rendered by Contour from the current build.
Most views use a 130×40 terminal. Open an image to see it at full size.

| Expanded objects | Nested array with sortable columns |
| --- | --- |
| ![Expanded object table with type colors](docs/images/objects.png) | ![Nested array with a Back breadcrumb](docs/images/objects-nested.png) |

| Property inspector | JSON highlighting |
| --- | --- |
| ![Property names, values and types](docs/images/objects-properties.png) | ![Nested JSON with colored brackets and braces](docs/images/objects-json.png) |

| Dark Modern | Light Modern | Monokai |
| --- | --- | --- |
| ![Dark Modern theme](docs/images/objects-theme-dark-modern.png) | ![Light Modern theme](docs/images/objects-theme-light-modern.png) | ![Monokai theme](docs/images/objects-theme-monokai.png) |

[Object action menu](docs/images/objects-menu.png) ·
[All theme color swatches](docs/images/themes.png) ·
[Grouped control catalog](docs/images/controls.png)

![User sessions with an expanded process list](docs/images/users.png)

</details>

## Custom TUIs

Compose controls in the [designer](docs/en-US/Show-TuiDesigner.md). Layouts use
PSD1 or JSON and can be exported as a `.ps1` scaffold for event handlers and live
updates. Linked exports keep handlers in the script while later layout edits
update the sibling `.tui.psd1` file.
The designer gives the canvas most of the screen, with a slim palette of controls on the right and
selected-item properties along the bottom. Use View to choose always-visible,
on-demand or hidden properties; the panel height and preference are remembered.
Right-click an item or press F7 for actions. The design prioritizes 40 or more
rows; 120×40 is recommended. Smaller windows remain usable.
The header is one row of icons; there is no footer or layout tree in the sidebar.
Tab and Shift-Tab move between form fields, including multiline inputs. F10
exits a custom app consistently; Escape follows the focused control's cancel/back action.
Use a tab child's `TabTitle` property for a caption independent of its ID.
<details>
<summary>Populated custom TUI example</summary>

Run [ReusableControls.ps1](examples/ReusableControls.ps1) for a deployment
workbench with bound selection, details, a simulated chart and an editable draft.
The screenshot includes two minutes of seeded demonstration history.

![Deployment workbench with populated controls and chart history](docs/images/custom-tui.png)

</details>

Charts share a numeric control with XenoAtom-style `Values`, `Minimum` and
`Maximum` properties. Sixel is selected automatically when supported, with
terminal-cell fallback. Experimental ReGIS vector rendering is available as an
explicit opt-in on compatible Contour terminals.
`GraphGallery.ps1` demonstrates all twelve graph styles with simulated data.

## Examples

Run an example after installing or building the module. Custom layout scripts
accept `-DefinitionOnly` to return native data without opening a terminal:

```powershell
./examples/ContactForm.ps1 -Theme 'Dark Modern'
$layout = ./examples/SettingsTabs.ps1 -DefinitionOnly
$layout | Show-TuiDesigner
$layout | Export-TuiDefinition -Path ./settings.tui.psd1
```

| Example | Demonstrates |
| --- | --- |
| [ControlGallery.ps1](examples/ControlGallery.ps1) | fullscreen controls, handler maps, shared state, selected objects and lazy tools |
| [NativeControls.ps1](examples/NativeControls.ps1) | Native widgets with populated data; filter by Name, or use All |
| [UserSessions.ps1](examples/UserSessions.ps1) | Standalone login sessions and optional account process expansion |
| [ContactForm.ps1](examples/ContactForm.ps1) | Bound inputs, validation and object results |
| [ReusableControls.ps1](examples/ReusableControls.ps1) | Tables, code, charts and multiline input |
| [Workspace.ps1](examples/Workspace.ps1) | Files, telemetry and logs in lazy tabs |
| [HandlerMap.ps1](examples/HandlerMap.ps1) | Direct event handlers and shared state |
| [DesignerStarter.ps1](examples/DesignerStarter.ps1) | A styled form and chart for the designer |
| [GraphGallery.ps1](examples/GraphGallery.ps1) | All twelve graph styles |
| [LiveChart.ps1](examples/LiveChart.ps1) | Numeric collections and scale options |
| [ProcessDashboard.ps1](examples/ProcessDashboard.ps1) | Native process data and tick callbacks |
| [BackgroundJob.ps1](examples/BackgroundJob.ps1) | Thread jobs, polling and owned-job cleanup |
| [LogTail.ps1](examples/LogTail.ps1) | Bounded file reads and error display |
| [SettingsTabs.ps1](examples/SettingsTabs.ps1) | Settings controls and validated results |
| [CommandHelp.ps1](examples/CommandHelp.ps1) | Lazy local help display |
| [SpectreContent.ps1](examples/SpectreContent.ps1) | Renderables supplied by TextMate or another module |
| [ToolModule.psm1](examples/ToolModule.psm1) | A pipeline picker that returns original objects |
| [NativeLayout.psd1](examples/NativeLayout.psd1) | A portable data-only layout |
| [ISpyBrowser.ps1](examples/ISpyBrowser.ps1) | Assembly browsing with optional TextMate previews |

`ControlGallery.ps1` demonstrates fullscreen controls across Form, Data, Logs,
Files, Monitor, Users, Commander, Objects and Media tabs. `NativeControls.ps1`
covers the native XenoAtom widgets, including command palettes, toasts, numeric
inputs, splitters and editable grids. CodeEditor is excluded.

Use the current catalog to list every supported control and its properties:

```powershell
Get-TuiControl
Get-TuiControl -Name UserList
```
For a tool you intend to keep editing, export with `-Script -LinkedLayout`.
Generated scripts expose `Theme`, `Minimal`, `State`, `TickInterval` and
`DefinitionOnly`; inline layouts omit default properties to keep the code small.
For example, `./MyTui.ps1 -Minimal -State @{ name = 'Ada' }` overrides a layout
default without changing the saved definition. Keep workers and live objects in
runtime state and dispose owned workers in `finally`; see `BackgroundJob.ps1`.

Keep jobs, credentials and live objects in `Invoke-Tui -State`; layout exports
contain serializable defaults. Handlers run on the UI thread, so put sustained
work in a background job. Spectre content uses the caller's loaded assembly;
fullscreen does not bundle Spectre or TextMate.

## Build from source

Building from a source checkout requires PowerShell 7.6 or later and the .NET 10
SDK. The build creates the module in `output/`, generates help and runs the checks:

```powershell
./build.ps1
Import-Module ./output/fullscreen.psd1
```

## Optional integrations

The built-in apps and native widgets need no optional PowerShell modules. TextMate
and Sixel are imported on demand for previews. Git, GitHub CLI and Azure CLI are
separate executables that must be installed and available on `PATH`.

| Dependency | Used by | Setup | Without it |
| --- | --- | --- | --- |
| [TextMate](https://www.powershellgallery.com/packages/TextMate) ([repo](https://github.com/trackd/TextMate)) PowerShell module | `Code` control and source/Markdown previews in file and object browsers | `Install-Module TextMate -Scope CurrentUser` | Source remains readable as plain text; the `Code` control shows an install notice. Object JSON retains its built-in coloring. |
| [Sixel](https://www.powershellgallery.com/packages/Sixel) ([repo](https://github.com/trackd/Sixel)) PowerShell module | Image previews in the file browser | `Install-Module Sixel -Scope CurrentUser`; images also need a Sixel-capable terminal | A notice explains whether the module or terminal graphics are missing. Charts use fullscreen's built-in renderer and fall back to terminal cells when graphics are unavailable. |
| [`git`](https://git-scm.com/) ([repo](https://github.com/git/git)) executable | [Show-GitManager](docs/en-US/Show-GitManager.md) | Install Git and ensure `git` is on `PATH`. | Git operations are unavailable. |
| [GitHub CLI](https://cli.github.com/) ([repo](https://github.com/cli/cli)) (`gh`) | [Show-GitHubManager](docs/en-US/Show-GitHubManager.md) | Install `gh` and sign in with `gh auth login`. | GitHub views are unavailable. The TUI uses existing CLI authentication and does not sign in for you. |
| [Azure CLI](https://learn.microsoft.com/cli/azure/) ([repo](https://github.com/Azure/azure-cli)) (`az`) and [Azure DevOps extension](https://github.com/Azure/azure-devops-cli-extension) | [Show-AzureDevOpsManager](docs/en-US/Show-AzureDevOpsManager.md) | Install Azure CLI, run `az extension add --name azure-devops`, then sign in with `az login`. | Azure DevOps views are unavailable. The app uses the Azure CLI account; no application registration is required. |

Custom `Spectre` controls accept renderables supplied by the caller, such as
TextMate output. Load the module that creates the renderable in the calling
session first; fullscreen does not bundle Spectre.Console or TextMate.
