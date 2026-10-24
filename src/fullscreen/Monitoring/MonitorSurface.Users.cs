using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;
using System.Globalization;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    /// <summary>Gets the login-session row buffer or current user view.</summary>
    public UserView Users { get; } = new();
    private int UserTop => Minimal || UsersOnly ? 2 : 4;
    private int UserDetailsHeight => Bounds.Height >= 24 ? 4 : 0;
    private int UserPage => Math.Max(1, Bounds.Height - UserTop - Math.Max(1, Footer) - UserDetailsHeight);

    private void ProjectUsers() {
        Users.Filter = Model.Filter;
        Users.IncludeProcesses = IncludeUserProcesses;
        Users.Sort = Model.Sort;
        Users.Ascending = Model.Ascending;
        Users.Update(Frame);
    }

    internal void ShowUserSessions() {
        ColumnsOpen = false;
        View = MonitorView.Users;
        Dirty = true;
        Refresh();
    }

    private bool UserKey(KeyEventArgs e) {
        ProjectUsers();
        switch (e.Key) {
            case TerminalKey.F6:
                ProcessSort[] sorts = [ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Id, ProcessSort.Name];
                Model.Sort = sorts[(Array.IndexOf(sorts, Model.Sort) + 1) % sorts.Length];
                break;
            case TerminalKey.Up:
                Users.Move(-1, UserPage);
                break;
            case TerminalKey.Down:
                Users.Move(1, UserPage);
                break;
            case TerminalKey.PageUp:
                Users.Move(-UserPage, UserPage);
                break;
            case TerminalKey.PageDown:
                Users.Move(UserPage, UserPage);
                break;
            case TerminalKey.Home:
                Users.Move(-int.MaxValue, UserPage);
                break;
            case TerminalKey.End:
                Users.Move(int.MaxValue, UserPage);
                break;
            case TerminalKey.Right:
                Users.Expand(true);
                break;
            case TerminalKey.Left:
                Users.Expand(false);
                break;
            case TerminalKey.Enter:
                if (Users.Selected?.Process is { } process) {
                    ShowUserProcessActions(process);
                }
                else {
                    Users.Expand();
                }

                break;
            default:
                return false;
        }

        e.Handled = true;
        Refresh();
        return true;
    }

    private void ShowUserProcessActions(ProcessRow process) =>
        ContextMenuService.Show(
            this,
            [
                new MenuItem(process.Name +
        " · PID " + process.Id) { IsEnabled = false },
                ..ProcessActions(process)
            ],
            Bounds.X + 2,
            Bounds.Y + UserTop
        );
    internal MenuItem[] UserSessionActions(UserViewRow row) =>
        [..row.Logins.Select(login => new MenuItem(
            $"Log off {login.Session} #{login.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"}…",
            () => ConfirmAction(new(MonitorAction.LogoffSession, login.UserName) {
                SessionId = login.SessionId ?? -1,
                SessionLogonTime = login.LogonTime?.ToFileTime() ?? 0
            })
        ) { IsEnabled = ActionsEnabled && !ActionBusy
        && Frame.Platform.Contains("Windows", StringComparison.OrdinalIgnoreCase)
        && login.SessionId >= 0
        && login.LogonTime.HasValue })];
    private void ShowUserActions(int x, int y) {
        if (Users.Selected is not { } row) {
            return;
        }

        if (row.Process is { } process) {
            ShowUserProcessActions(process);
            return;
        }

        ContextMenuService.Show(
            this,
            [
                new MenuItem(row.UserName) { IsEnabled = false },
                new MenuItem("Session details…", ShowUserDetails),
                ..UserSessionActions(row)
            ],
            x,
            y
        );
    }

    private bool UserPointer(PointerEventArgs e) {
        int y = e.UiY - Bounds.Y;
        if (y == UserTop - 1 && IncludeUserProcesses && e.Button == TerminalMouseButton.Left) {
            ProjectUsers();
            var columns = UserColumnLayout();
            int x = e.UiX - Bounds.X;
            ProcessSort? sort = null;
            if (x >= 2 && x < columns.Id) {
                sort = ProcessSort.Name;
            }
            else if (x >= columns.Id && x < columns.Cpu) {
                sort = ProcessSort.Id;
            }
            else if (x >= columns.Cpu && x < columns.Memory) {
                sort = ProcessSort.Cpu;
            }
            else if (x >= columns.Memory && x < columns.Memory + 11) {
                sort = ProcessSort.Memory;
            }
            if (sort.HasValue) {
                if (Model.Sort == sort.Value) {
                    Model.Ascending = !Model.Ascending;
                }
                else {
                    Model.Sort = sort.Value;
                }
                Refresh();
                e.Handled = true;
                return true;
            }
        }
        if (y < UserTop || y >= UserTop + UserPage) {
            return false;
        }

        ProjectUsers();
        int index = Users.Offset + y - UserTop;
        if ((uint)index >= (uint)Users.Rows.Count) {
            return false;
        }

        var row = Users.Rows[index];
        Users.SelectedKey = row.Key;
        if (e.Button == TerminalMouseButton.Right) {
            ShowUserActions(e.UiX, e.UiY);
        }
        else if (e.Button == TerminalMouseButton.Left
            && (e.ClickCount >= 2 || e.UiX - Bounds.X < 6 + Math.Min(row.Depth, 8) * 2)) {
            Users.Expand();
        }

        e.Handled = true;
        Refresh();
        return true;
    }

    private readonly record struct UserColumns(int Id, int Cpu, int Memory, int State, int Client, int Idle, int Logon);

    private UserColumns UserColumnLayout() {
        int right = Bounds.Width - 1;
        var logins = Users.Rows.Where(row => row.Process is null).SelectMany(row => row.Logins).ToArray();
        int logon = -1, idle = -1, client = -1;
        if (Bounds.Width >= 110 && logins.Any(login => login.LogonTime.HasValue)) {
            logon = right - 20;
            right = logon - 1;
        }
        if (Bounds.Width >= 110 && logins.Any(login => login.IdleTime.HasValue)) {
            idle = right - 12;
            right = idle - 1;
        }
        if (Bounds.Width >= 110 && logins.Any(login => ClientName(login) != "—")) {
            client = right - 16;
            right = client - 1;
        }
        int state = IncludeUserProcesses ? -1 : Math.Max(25, right / 2);
        if (IncludeUserProcesses && right >= 75) {
            state = right - 22;
            right = state - 1;
        }
        if (!IncludeUserProcesses) {
            return new(-1, -1, -1, state, client, idle, logon);
        }
        int memory = Math.Max(22, right - 11);
        int cpu = memory - 8;
        int id = cpu - 9;
        return new(id, cpu, memory, state, client, idle, logon);
    }
    private void RenderUsers(CellBuffer buffer) {
        ProjectUsers();
        Users.Offset = Math.Clamp(Users.Offset, 0, Math.Max(0, Users.Rows.Count - UserPage));
        int top = Minimal || UsersOnly ? 0 : 2;
        if (!Minimal && !UsersOnly) {
            RenderNavigation(buffer);
            Rule(buffer, 1);
        }

        Write(
            buffer,
            1,
            top,
            IncludeUserProcesses ? "USERS · account totals · ←/→ branches · F6 sort · R reverse · T tree" : "USERS · login sessions · L load processes",
            Accent,
            Bounds.Width - 2
        );
        var columns = UserColumnLayout();
        string SortLabel(string label, ProcessSort sort) {
            if (!IncludeUserProcesses || Model.Sort != sort) {
                return label;
            }
            return label + (Model.Ascending ? " ↑" : " ↓");
        }
        Write(buffer, 2, top + 1, SortLabel("USER / PROCESS", ProcessSort.Name), Muted,
            (IncludeUserProcesses ? columns.Id : columns.State) - 3);
        if (IncludeUserProcesses) {
            Write(buffer, columns.Id, top + 1, SortLabel("PID/#", ProcessSort.Id), Muted, 8);
            Write(buffer, columns.Cpu, top + 1, SortLabel("CPU %", ProcessSort.Cpu), Accent, 7);
            Write(buffer, columns.Memory, top + 1, SortLabel("RSS", ProcessSort.Memory), Green, 11);
        }
        if (columns.State >= 0) {
            int end = new[] { columns.Client, columns.Idle, columns.Logon, Bounds.Width - 1 }
                .Where(position => position > columns.State).Min();
            Write(buffer, columns.State, top + 1, "STATE / SESSION", Muted, end - columns.State - 1);
        }
        Write(buffer, columns.Client, top + 1, "CLIENT", Muted, 15);
        Write(buffer, columns.Idle, top + 1, "IDLE TIME", Muted, 11);
        Write(buffer, columns.Logon, top + 1, "LOGON TIME", Muted, 20);

        int stateEnd = new[] { columns.Client, columns.Idle, columns.Logon, Bounds.Width - 1 }
            .Where(position => position > columns.State).DefaultIfEmpty(Bounds.Width - 1).Min();
        for (int index = Users.Offset; index < Math.Min(Users.Rows.Count, Users.Offset + UserPage); index++) {
            var row = Users.Rows[index];
            int y = UserTop + index - Users.Offset;
            Style style = row.Key == Users.SelectedKey ? Selected : (index & 1) == 0 ? Body : Panel;
            Fill(buffer, 1, y, Bounds.Width - 2, 1, style);
            string label = row.UserName;
            if (row.Process is { } process) {
                string branch = " ";
                if (Users.Tree && row.HasChildren) {
                    branch = row.Collapsed ? "+" : "−";
                }
                label = "  " + new string(' ', Math.Min(row.Depth, 8) * 2) + branch + " " + process.Name;
            }
            else if (IncludeUserProcesses) {
                label = (Users.IsExpanded(row.Key) ? "− " : "+ ") + row.UserName;
            }
            Write(buffer, 2, y, label, style, (IncludeUserProcesses ? columns.Id : columns.State) - 3);
            if (IncludeUserProcesses) {
                Write(buffer, columns.Id, y,
                    (row.Process?.Id ?? row.ProcessCount).ToString(CultureInfo.InvariantCulture), style, 8);
                Write(buffer, columns.Cpu, y, row.CpuPercent.ToString("F1", CultureInfo.InvariantCulture), style, 7);
                Quantity(buffer, columns.Memory, y, row.WorkingSet, false, style, 11);
            }

            Write(buffer, columns.State, y,
                row.Sessions.Length == 0 ? row.Status : row.Status + " · " + row.Sessions,
                style, stateEnd - columns.State - 1);
            if (row.Process is null) {
                Write(buffer, columns.Client, y, string.Join(", ", row.Logins.Select(ClientName)), style, 15);
                Write(buffer, columns.Idle, y, string.Join(", ", row.Logins.Select(login => IdleTime(login.IdleTime))), style, 11);
                Write(buffer, columns.Logon, y, string.Join(", ", row.Logins.Select(login => SessionTime(login.LogonTime))), style, 20);
            }
        }
        if (Users.Rows.Count == 0) {
            Write(
                buffer,
                2,
                UserTop,
                Model.Filter.Length == 0 ? "No login sessions reported" : "No matching users or sessions",
                Muted,
                Bounds.Width - 3
            );
        }

        RenderUserDetails(buffer);
        RenderCompactFooter(buffer);
    }

    private static string SessionTime(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";
    private static string ClientName(UserSessionRow login) =>
        login.ClientName.Length > 0 ? login.ClientName : login.Host.Length > 0 ? login.Host : "—";
    private static string IdleTime(TimeSpan? idle) =>
        idle?.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "—";
    private static string SessionSummary(UserSessionRow login) =>
        $"{login.Session}{(login.SessionId.HasValue ? " #" + login.SessionId : "")} · {login.State} · Login {(login.LogonTime.HasValue ? SessionTime(login.LogonTime) : login.LoginTime.Length > 0 ? login.LoginTime : "—")} · Idle {login.IdleTime?.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "—"} · Client {(login.ClientName.Length > 0 ? login.ClientName : login.Host.Length > 0 ? login.Host : "—")} {login.Protocol}";
    private void RenderUserDetails(CellBuffer buffer) {
        if (UserDetailsHeight == 0) {
            return;
        }

        int y = Bounds.Height - Math.Max(1, Footer) - UserDetailsHeight;
        Rule(buffer, y++);
        var selected = Users.Selected;
        if (selected is null) {
            return;
        }

        Write(
            buffer,
            2,
            y++,
            selected.UserName +
            $" · {selected.Logins.Count} login sessions · D all details",
            Accent,
            Bounds.Width - 3
        );
        if (selected.Logins.Count > 0) {
            var login = selected.Logins[0];
            Write(
                buffer,
                2,
                y++,
                $"{login.Session} #{login.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"} · {login.State} · Client {(login.ClientName.Length > 0 ? login.ClientName : login.Host.Length > 0 ? login.Host : "—")} · {login.Protocol}",
                Body,
                Bounds.Width - 3
            );
            Write(
                buffer,
                2,
                y,
                $"Login {(login.LogonTime.HasValue ? SessionTime(login.LogonTime) : login.LoginTime.Length > 0 ? login.LoginTime : "—")} · Idle {login.IdleTime?.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "—"}",
                Body,
                Bounds.Width - 3
            );
        }

        if (selected.Logins.Count == 0) {
            Write(
                buffer,
                2,
                y,
                "No login recorded · process ownership does not imply an interactive login",
                Muted,
                Bounds.Width - 3
            );
        }
    }

    private void ShowUserDetails() {
        ProjectUsers();
        if (Users.Selected is not { } selected) {
            return;
        }

        string text = selected.Logins.Count == 0 ? "No login sessions recorded." : string.Join(
            "\n\n",
            selected.Logins.Select(login => SessionSummary(login) +
            $"\nHost: {login.Host}\nConnected: {SessionTime(login.ConnectTime)}\nDisconnected: {SessionTime(login.DisconnectTime)}\nLast input: {SessionTime(login.LastInputTime)}")
        );
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock(selected.UserName + " · sessions"),
            Width = Math.Max(30, Math.Min(110, Bounds.Width - 4)),
            IsModal = true,
            Content = new VStack(
                new ScrollViewer(new Paragraph(text) { Wrap = true }) { MaxHeight = Math.Max(5, Bounds.Height - 8) },
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        dialog.Show();
    }
}
