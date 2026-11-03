using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private readonly Dictionary<string, UserSessionRow> _markedSessions = new(StringComparer.Ordinal);
    internal int MarkedSessionCount => _markedSessions.Count;
    private static string SessionKey(UserSessionRow login) =>
        login.UserName + "\0" + login.SessionId + "\0" + login.LogonTime?.ToFileTime();
    private bool CanLogoff(UserSessionRow login) =>
        Frame.Platform.Contains("Windows", StringComparison.OrdinalIgnoreCase)
        && login.IsNamedWindowsSession && login.LogonTime.HasValue;
    private UserSessionRow[] VisibleSessions() =>
        [.. Users.Rows.Where(row => row.Process is null).SelectMany(row => row.Logins).Where(CanLogoff).DistinctBy(SessionKey)];
    private void PruneSessionMarks() {
        if (_markedSessions.Count == 0) {
            return;
        }
        var current = Frame.Users.Take(Frame.UserCount).Where(CanLogoff).Select(SessionKey).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _markedSessions.Keys.ToArray()) {
            if (!current.Contains(key)) {
                _markedSessions.Remove(key);
            }
        }
    }

    internal void ToggleSelectedSessions() {
        ProjectUsers();
        var logins = Users.Selected?.Logins.Where(CanLogoff).ToArray() ?? [];
        bool clear = logins.All(login => _markedSessions.ContainsKey(SessionKey(login)));
        foreach (var login in logins) {
            if (clear) {
                _markedSessions.Remove(SessionKey(login));
            }
            else {
                _markedSessions[SessionKey(login)] = login;
            }
        }
        Refresh();
    }

    internal void MarkAllSessions() {
        ProjectUsers();
        var logins = VisibleSessions();
        bool clear = logins.All(login => _markedSessions.ContainsKey(SessionKey(login)));
        foreach (var login in logins) {
            if (clear) {
                _markedSessions.Remove(SessionKey(login));
            }
            else {
                _markedSessions[SessionKey(login)] = login;
            }
        }
        Refresh();
    }

    internal void ShowSessionSelection() {
        if (ActionBusy) {
            return;
        }
        ProjectUsers();
        var logins = VisibleSessions().Concat(_markedSessions.Values).DistinctBy(SessionKey).ToArray();
        var checks = logins.Select(login => (Login: login, Check: new CheckBox(
            login.UserName + " · #" + login.SessionId + " · " + login.State + " · " + ClientName(login),
            _markedSessions.ContainsKey(SessionKey(login))))).ToArray();
        var choices = new VStack(new TextBlock(
            "Select Windows sessions to log off. Logging off closes applications and can lose unsaved work.") { Wrap = true });
        if (checks.Length == 0) {
            choices.Add(new TextBlock("No Windows sessions with a verified logon time are available. Logoff is unavailable on other platforms.") { Wrap = true });
        }
        choices.Add(new HStack(
            new Button("Mark all").Click(() => { foreach (var entry in checks) { entry.Check.IsChecked = true; } }),
            new Button("Clear marks").Click(() => { foreach (var entry in checks) { entry.Check.IsChecked = false; } })));
        var rows = new VStack();
        foreach (var entry in checks) {
            rows.Add(entry.Check);
        }
        choices.Add(rows);
        var selectionMessage = new TextBlock("") { Wrap = true };
        choices.Add(selectionMessage);
        var content = new VStack(new ScrollViewer(choices) { MaxHeight = Math.Max(1, Bounds.Height - 8) });
        Dialog? dialog = null;
        content.Add(new HStack(new Button("Cancel").Click(() => dialog!.Close()),
            new Button("Log off selected…") { IsEnabled = ActionsEnabled && checks.Length > 0 }.Click(() => {
                var selected = checks.Where(entry => entry.Check.IsChecked).Select(entry => entry.Login).ToArray();
                if (selected.Length == 0) {
                    selectionMessage.Text = "Select at least one session to log off.";
                    return;
                }
                foreach (var entry in checks) {
                    _markedSessions.Remove(SessionKey(entry.Login));
                }
                foreach (var login in selected) {
                    _markedSessions[SessionKey(login)] = login;
                }
                dialog!.Close();
                ConfirmSessionLogoff(selected);
            })));
        dialog = new Dialog {
            Title = new TextBlock("Select sessions to log off"),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            MaxHeight = Math.Max(1, Bounds.Height - 2),
            Content = content
        };
        ShowMonitorDialog(dialog);
    }

    /// <summary>Raised after a batch of session logoffs has been explicitly confirmed.</summary>
    public event Action<IReadOnlyList<MonitorActionRequest>>? SessionLogoffRequested;

    private void ConfirmSessionLogoff(UserSessionRow[] sessions) {
        if (!ActionsEnabled || ActionBusy) {
            return;
        }
        var requests = sessions.Select(login => new MonitorActionRequest(MonitorAction.LogoffSession, login.UserName) {
            SessionId = login.SessionId!.Value,
            SessionLogonTime = login.LogonTime!.Value.ToFileTime()
        }).ToArray();
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Confirm logoff · " + requests.Length + " sessions"),
            IsModal = true,
            Width = Math.Max(1, Math.Min(100, Bounds.Width - 4)),
            Content = new VStack(
                new ScrollViewer(new TextBlock(
                    $"Target: {SourceLabel}{(IsRemote ? " (remote)" : " (local)")}\nLog off {requests.Length} sessions? Applications will close and unsaved work may be lost, including this monitor if you select its session.\n\n" +
                    string.Join("\n", sessions.Select(login =>
                        login.UserName + " · session #" + login.SessionId + " · " + login.State + " · logon " + SessionTime(login.LogonTime)))) { Wrap = true }) {
                    MaxHeight = Math.Max(1, Bounds.Height - 8)
                },
                new HStack(new Button("Cancel").Click(() => dialog!.Close()),
                    new Button("Confirm logoff").Click(() => {
                        dialog!.Close();
                        if (ActionsEnabled && !ActionBusy) {
                            SessionLogoffRequested?.Invoke(requests);
                        }
                    })))
        };
        ShowMonitorDialog(dialog);
    }

    internal void ShowSessionResults(string results) {
        if (App is null) {
            return;
        }
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock("Session logoff results"),
            IsModal = true,
            Width = Math.Max(1, Bounds.Width - 4),
            Content = new VStack(
                new ScrollViewer(new TextBlock(fullscreen.Files.FileReader.Sanitize(
                    results.Replace(" | ", "\n", StringComparison.Ordinal), true)) { Wrap = true }) {
                    MaxHeight = Math.Max(1, Bounds.Height - 8)
                },
                new Button("Close").Click(() => dialog!.Close()))
        };
        ShowMonitorDialog(dialog);
    }
}
