using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    /// <summary>Gets the service row buffer or current service view.</summary>
    public ServiceView Services { get; } = new();
    private int ServiceTop => Minimal ? 0 : 2;
    private int ServicePage => Math.Max(1, Bounds.Height - ServiceTop - 3 - Footer);

    private void ProjectServices() {
        Services.Filter = Model.Filter;
        Services.Update(Frame.Services.AsSpan(0, Frame.ServiceCount));
    }

    private void ScrollServices(int delta) {
        ProjectServices();
        Services.Move(delta, ServicePage);
    }

    private bool ServiceKey(KeyEventArgs e) {
        if (e.Key == TerminalKey.F6) {
            Services.Sort = (ServiceColumn)(((int)Services.Sort + 1) % Enum.GetValues<ServiceColumn>().Length);
            e.Handled = true;
            Refresh();
            return true;
        }

        int? delta = e.Key switch {
            TerminalKey.Up => -1,
            TerminalKey.Down => 1,
            TerminalKey.PageUp => -ServicePage,
            TerminalKey.PageDown => ServicePage,
            TerminalKey.Home => -int.MaxValue,
            TerminalKey.End => int.MaxValue,
            _ => null
        };
        if (!delta.HasValue) {
            return false;
        }

        ScrollServices(delta.Value);
        e.Handled = true;
        Refresh();
        return true;
    }

    private bool HasServiceDetails {
        get {
            foreach (ref readonly var row in Frame.Services.AsSpan(0, Frame.ServiceCount)) {
                if (!string.IsNullOrWhiteSpace(row.Detail) && row.Detail != "—") {
                    return true;
                }
            }

            return false;
        }
    }

    private IEnumerable<(ServiceColumn Column, int X, int Width)> ServiceColumns() {
        int name = Math.Clamp(Bounds.Width / 4, 12, 35), x = 1;
        yield return (ServiceColumn.Name, x, name);
        x += name + 1;
        yield return (ServiceColumn.State, x, 13);
        x += 14;
        if (Bounds.Width >= 48) {
            yield return (ServiceColumn.ProcessId, x, 8);
            x += 9;
        }

        if (Bounds.Width >= 100) {
            yield return (ServiceColumn.UserName, x, 24);
            x += 25;
        }

        if (Bounds.Width >= 75) {
            int remaining = Bounds.Width - x - 1;
            int description = Bounds.Width >= 110 && HasServiceDetails ? remaining / 2 : remaining;
            yield return (ServiceColumn.Description, x, description);
            x += description + 1;
        }

        if (Bounds.Width >= 110 && HasServiceDetails) {
            yield return (ServiceColumn.Detail, x, Math.Max(1, Bounds.Width - x - 1));
        }
    }

    /// <summary>Sets or clears the filter for a service column.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <param name="value">The input value to format, display, or append.</param>
    public void SetServiceFilter(ServiceColumn column, string? value) {
        if (string.IsNullOrEmpty(value)) {
            Services.ColumnFilters.Remove(column);
        }
        else {
            Services.ColumnFilters[column] = value;
        }

        Services.Offset = 0;
        Dirty = true;
        Refresh();
    }

    private void RenderServices(CellBuffer buffer) {
        ProjectServices();
        Services.Offset = Math.Clamp(Services.Offset, 0, Math.Max(0, Services.Count - ServicePage));
        int top = ServiceTop;
        if (!Minimal) {
            RenderNavigation(buffer);
            Rule(buffer, 1);
        }

        Write(
            buffer,
            1,
            top,
            $"SERVICES · {Services.Count} shown · sort {Services.Sort} {(Services.Ascending ? "↑" : "↓")}",
            Accent,
            Bounds.Width - 2
        );
        Write(
            buffer,
            1,
            top + 1,
            SourceError ?? Frame.ServiceStatus,
            SourceError is null ? Muted : Hot,
            Bounds.Width - 2
        );
        var columns = ServiceColumns().ToArray();
        foreach (var column in columns) {
            string name = column.Column == ServiceColumn.ProcessId ? "PID" : column.Column.ToString().ToUpperInvariant();
            string sort = Services.Sort == column.Column ? (Services.Ascending ? " ↑" : " ↓") : "";
            Write(
                buffer,
                column.X,
                top + 2,
                name + sort + (Services.ColumnFilters.ContainsKey(column.Column) ? " *" : ""),
                Accent,
                column.Width - 2
            );
            Write(buffer, column.X + column.Width - 2, top + 2, "⌄", Muted, 1);
        }

        int visible = Math.Min(ServicePage, Services.Count - Services.Offset);
        for (int i = 0; i < visible; i++) {
            ref readonly var row = ref Services.Rows[Services.Offset + i];
            int y = top + 3 + i;
            bool selected = row.Name == Services.SelectedName;
            if (selected) {
                Fill(buffer, 1, y, Bounds.Width - 2, 1, Selected);
            }

            foreach (var column in columns) {
                var style = selected ? Selected : column.Column == ServiceColumn.State ? row.State is "running" or "active" ? Green : row.State == "failed" ? Hot : Muted : Body;
                Write(buffer, column.X, y, ServiceView.Text(row, column.Column), style, column.Width);
            }
        }

        if (visible == 0) {
            Write(
                buffer,
                1,
                top + 3,
                Frame.ServiceCount == 0 ? "No services returned by this service manager" : "No matching services",
                Muted,
                Bounds.Width - 2
            );
        }

        RenderCompactFooter(buffer);
    }

    private bool ServicePointer(PointerEventArgs e) {
        if (e.Button is not (TerminalMouseButton.Left or TerminalMouseButton.Right)) {
            return false;
        }

        if (e.LocalY == ServiceTop + 2) {
            foreach (var column in ServiceColumns()) {
                if (e.LocalX < column.X || e.LocalX >= column.X + column.Width) {
                    continue;
                }

                if (e.Button == TerminalMouseButton.Right || e.LocalX >= column.X + column.Width - 2) {
                    OpenServiceHeaderMenu(column.Column, e.UiX, e.UiY);
                }
                else {
                    Services.Ascending = Services.Sort != column.Column || !Services.Ascending;
                    Services.Sort = column.Column;
                    Refresh();
                }

                e.Handled = true;
                return true;
            }
        }

        int rowIndex = Services.Offset + e.LocalY - ServiceTop - 3;
        if (e.LocalY >= ServiceTop + 3 && e.LocalY < ServiceTop + 3 + ServicePage
            && (uint)rowIndex < (uint)Services.Count) {
            Services.SelectedName = Services.Rows[rowIndex].Name;
            if (e.Button == TerminalMouseButton.Right) {
                OpenSelectedActions(e.UiX, e.UiY);
            }

            e.Handled = true;
            Refresh();
            return true;
        }

        return false;
    }

    private void OpenServiceHeaderMenu(ServiceColumn column, int x, int y) {
        var items = new List<MenuItem>
        {
            new("Sort ascending", () =>
            {
                Services.Sort = column;
                Services.Ascending = true;
                Refresh();
            }),
            new("Sort descending", () =>
            {
                Services.Sort = column;
                Services.Ascending = false;
                Refresh();
            }),
            MenuItem.Separator(),
            new("Filter…", () => EditServiceFilter(column)),
            new("Clear this filter", () => SetServiceFilter(column, null)),
            new("Clear service filters", () =>
            {
                Services.ColumnFilters.Clear();
                Services.Offset = 0;
                Refresh();
            })
        };
        var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ref readonly var row in Frame.Services.AsSpan(0, Frame.ServiceCount)) {
            values.Add(ServiceView.Text(row, column));
        }

        var choices = new MenuItem("Filter value");
        foreach (var value in values.Take(Math.Max(1, Math.Min(12, Bounds.Height - 8)))) {
            string captured = value;
            choices.Items.Add(new MenuItem(value, () => SetServiceFilter(column, captured)));
        }

        items.Add(choices);
        ContextMenuService.Show(this, items, x, y);
    }

    private void EditServiceFilter(ServiceColumn column) {
        var input = new TextBox(Services.ColumnFilters.GetValueOrDefault(column, ""));
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock($"Filter service {column}"),
            Width = 60,
            IsModal = true,
            Content = new VStack(new TextBlock(column == ServiceColumn.ProcessId ? "Exact value, ~contains, or >number" : "Exact value · ~text contains · empty clears"), input, new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Apply").Click(() => {
                SetServiceFilter(column, input.Text);
                dialog!.Close();
            })))
        };
        dialog.Show();
        App?.Focus(input);
    }
}
