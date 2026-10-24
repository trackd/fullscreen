using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private void ExcludeProcess(string name) {
        Model.ExcludedNames.Add(name);
        Dirty = true;
        Refresh();
    }

    private void ExcludeSelectedProcess() {
        foreach (ref readonly var row in Model.Rows) {
            if (row.Id == Model.SelectedId) {
                ExcludeProcess(row.Name);
                return;
            }
        }
    }

    /// <summary>Gets or sets whether interactive process, service, and session actions are
    /// enabled.</summary>
    public bool ActionsEnabled { get; set; }
    /// <summary>Gets or sets whether a monitor action is executing.</summary>
    public bool ActionBusy { get; set; }
    /// <summary>Gets or sets the most recent action result or progress message.</summary>
    public string ActionMessage { get; set; } = "";

    /// <summary>Raised when action requested is requested.</summary>
    public event Action<MonitorActionRequest>? ActionRequested;
    internal MenuItem[] ProcessActions(ProcessRow row) =>
        [new(
            "Stop process…",
            () => ConfirmAction(new(MonitorAction.StopProcess, row.Name, row.Id, row.StartTimeTicks))
        )
    {
        IsEnabled = ActionsEnabled && !ActionBusy && row.StartTimeTicks > 0
    }

        ];
    internal MenuItem[] ServiceActions(ServiceRow row) =>
        [new("Start service…", () => ConfirmAction(new(MonitorAction.StartService, row.Name)))
    {
        IsEnabled = ActionsEnabled && !ActionBusy
    }, new("Stop service…", () => ConfirmAction(new(MonitorAction.StopService, row.Name)))
    {
        IsEnabled = ActionsEnabled && !ActionBusy
    }, new("Restart service…", () => ConfirmAction(new(MonitorAction.RestartService, row.Name)))
    {
        IsEnabled = ActionsEnabled && !ActionBusy
    }, new("Force stop service…", () => ConfirmAction(new(MonitorAction.ForceStopService, row.Name)))
    {
        IsEnabled = ActionsEnabled && !ActionBusy
    }

        ];
    private void OpenSelectedActions() =>
        OpenSelectedActions(
            Bounds.X + 1,
            Bounds.Y + (View == MonitorView.Users ? UserTop : View == MonitorView.Services ? ServiceTop + 3 : TableTop)
        );
    private void OpenSelectedActions(int x, int y) {
        if (View == MonitorView.Users) {
            ProjectUsers();
            ShowUserActions(x, y);
            return;
        }

        if (View == MonitorView.Services) {
            ProjectServices();
            foreach (ref readonly var row in Services.Rows) {
                if (row.Name != Services.SelectedName) {
                    continue;
                }

                var items = new List<MenuItem>
                {
                    new(row.Name)
                    {
                        IsEnabled = false
                    },
                    MenuItem.Separator()
                };
                items.AddRange(ServiceActions(row));
                ContextMenuService.Show(this, items, x, y);
                return;
            }
        }
        else if (ProcessTable) {
            foreach (ref readonly var row in Model.Rows) {
                if (row.Id == Model.SelectedId) {
                    ShowProcessMenu(row, x, y);
                    return;
                }
            }
        }
    }

    private void ConfirmAction(MonitorActionRequest request) {
        if (!ActionsEnabled || ActionBusy) {
            return;
        }

        string target = request.Action == MonitorAction.StopProcess ? $"{request.Name} · PID {request.ProcessId}" : request.Action == MonitorAction.LogoffSession ? $"{request.Name} · session #{request.SessionId}" : request.Name;
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Confirm " + request.Action),
            Width = 72,
            IsModal = true,
            Content = new VStack(new Paragraph($"{request.Action}: {target}\nTarget: {SourceLabel}{(IsRemote ? " (remote)" : " (local)")}" + (request.Action == MonitorAction.ForceStopService ? "\nWindows: stops dependent services too. Linux: sends SIGKILL to the service group, then stops the unit. Not supported on macOS." : "") + (request.Action == MonitorAction.LogoffSession ? "\nLogs off this session and closes its applications. Unsaved work may be lost." : "")) { Wrap = true }, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Confirm").Click(() => {
                dialog!.Close();
                if (ActionsEnabled && !ActionBusy) {
                    ActionRequested?.Invoke(request);
                }
            })))
        };
        dialog.Show();
    }
}
