using System.Globalization;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private readonly Dictionary<int, string> _networkFilters = [], _gpuFilters = [];
    /// <summary>Sets or clears the filter for a network-interface column.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <param name="value">The input value to format, display, or append.</param>
    public void SetNetworkFilter(NetworkColumn column, string? value) =>
        SetDetailFilter(true, (int)column, value);
    /// <summary>Sets or clears the filter for a graphics-device column.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <param name="value">The input value to format, display, or append.</param>
    public void SetGpuFilter(GpuColumn column, string? value) =>
        SetDetailFilter(false, (int)column, value);
    private void SetDetailFilter(bool network, int column, string? value) {
        var filters = network ? _networkFilters : _gpuFilters;
        if (value is null) {
            filters.Remove(column);
        }
        else {
            filters[column] = value;
        }

        _detailOffset = 0;
        Dirty = true;
        Refresh();
    }

    private static double? NetworkValue(in NetworkRow row, int column) =>
        column switch {
            1 => row.ReceiveBytesPerSecond,
            2 => row.SendBytesPerSecond,
            3 => row.Speed,
            4 => row.ReceivedBytes,
            5 => row.SentBytes,
            6 => row.Errors,
            _ => null
        };
    private static double? GpuValue(in GpuRow row, int column) =>
        column switch {
            2 => row.Utilization,
            3 => row.UsedMemory,
            4 => row.TotalMemory,
            5 => row.Temperature,
            _ => null
        };
    private static bool DetailNumberMatches(
        double? number,
        string value
    ) =>
        value.StartsWith('>')
        && double.TryParse(value.AsSpan(1), CultureInfo.InvariantCulture, out double minimum)
        && number.HasValue
        && number.Value > minimum;
    private bool NetworkMatches(in NetworkRow row) {
        if (!DetailMatches(row.Name, row.Id)) {
            return false;
        }

        foreach (var pair in _networkFilters) {
            if (pair.Key == 0 ? !string.Equals(pair.Value, row.Name, StringComparison.OrdinalIgnoreCase) : !DetailNumberMatches(NetworkValue(row, pair.Key), pair.Value)) {
                return false;
            }
        }

        return true;
    }

    private bool GpuMatches(in GpuRow row) {
        if (!DetailMatches(row.Name, row.Provider)) {
            return false;
        }

        foreach (var pair in _gpuFilters) {
            if (pair.Key < 2) {
                if (!string.Equals(pair.Value, pair.Key == 0 ? row.Name : row.Provider, StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }
            else if (!DetailNumberMatches(GpuValue(row, pair.Key), pair.Value)) {
                return false;
            }
        }

        return true;
    }

    private void OpenDetailHeaderMenu(PointerEventArgs e) {
        bool network = View == MonitorView.Network;
        var enabled = network ? _networkColumns : _gpuColumns;
        var names = network ? NetworkColumnNames : GpuColumnNames;
        int x = 1, column = -1;
        for (int i = 0; i < names.Length; i++) {
            if (!enabled[i] || !network && !AvailableColumn((GpuColumn)i)) {
                continue;
            }

            int width = DetailColumnWidth(network, i);
            if (e.LocalX >= x && e.LocalX < x + width) {
                column = i;
                break;
            }

            x += width + 1;
        }

        if (column < 0) {
            return;
        }

        int chosen = column;
        var items = new List<MenuItem>
        {
            new("Clear this filter", () => SetDetailFilter(network, chosen, null)),
            new("Clear table filters", () =>
            {
                (network ? _networkFilters : _gpuFilters).Clear();
                _detailOffset = 0;
                Refresh();
            })
        };
        if (column == 0 || !network && column == 1) {
            var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (network) {
                foreach (ref readonly var row in Frame.Networks.AsSpan(0, Frame.NetworkCount)) {
                    values.Add(row.Name);
                }
            }
            else {
                foreach (ref readonly var row in Frame.Gpus.AsSpan(0, Frame.GpuCount)) {
                    values.Add(column == 0 ? row.Name : row.Provider);
                }
            }

            foreach (string value in values.Take(Math.Max(1, Math.Min(12, Bounds.Height - 8)))) {
                string captured = value;
                items.Add(new(value, () => SetDetailFilter(network, chosen, captured)));
            }
        }
        else {
            items.Add(new("Greater than zero", () => SetDetailFilter(network, chosen, ">0")));
        }

        ContextMenuService.Show(this, items, e.UiX, e.UiY);
        e.Handled = true;
    }

    private int DetailColumnWidth(
        bool network,
        int column
    ) =>
        column == 0 ? (Bounds.Width >= 100 ? 27 : 18) : !network && column == 1 ? 19 : 15;
}
