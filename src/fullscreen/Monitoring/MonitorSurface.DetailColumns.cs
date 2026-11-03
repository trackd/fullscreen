using System.Globalization;

namespace fullscreen.Monitoring;
/// <summary>Identifies a selectable network-interface table column.</summary>
public enum NetworkColumn {
    /// <summary>Displays the network-interface name.</summary>
    Interface,
    /// <summary>Displays the receive rate in bytes per second.</summary>
    Receive,
    /// <summary>Displays the send rate in bytes per second.</summary>
    Send,
    /// <summary>Displays the interface link speed.</summary>
    Link,
    /// <summary>Displays the cumulative received bytes.</summary>
    TotalReceived,
    /// <summary>Displays the cumulative sent bytes.</summary>
    TotalSent,
    /// <summary>Displays the errors.</summary>
    Errors
}

/// <summary>Identifies a selectable graphics-device table column.</summary>
public enum GpuColumn {
    /// <summary>Displays the device.</summary>
    Device,
    /// <summary>Displays the provider.</summary>
    Provider,
    /// <summary>Displays the GPU utilization percentage.</summary>
    Utilization,
    /// <summary>Displays the used graphics memory in bytes.</summary>
    UsedMemory,
    /// <summary>Displays the total graphics memory in bytes.</summary>
    TotalMemory,
    /// <summary>Displays the device temperature in degrees Celsius.</summary>
    Temperature
}

public sealed partial class MonitorSurface {
    private static readonly string[] NetworkColumnNames = [
        "Interface",
        "Receive",
        "Send",
        "Link",
        "TotalReceived",
        "TotalSent",
        "Errors"
    ];
    private static readonly string[] GpuColumnNames = [
        "Device",
        "Provider",
        "Utilization",
        "UsedMemory",
        "TotalMemory",
        "Temperature"
    ];
    private readonly bool[] _networkColumns = [true, true, true, true, true, true, true];
    private readonly bool[] _gpuColumns = [true, true, true, true, true, true];
    private readonly List<ProcessColumn> _pickerProcesses = [];
    private readonly List<GpuColumn> _pickerGpu = [];
    private CollectorCapabilities? _pickerCapabilities;
    private MonitorView _pickerView;
    /// <summary>Checks whether a detail-table column is currently shown.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>True when the column is visible.</returns>
    public bool IsColumnVisible(NetworkColumn column) =>
        _networkColumns[(int)column];
    /// <summary>Checks whether a detail-table column is currently shown.</summary>
    /// <param name="column">The table column to read or configure.</param>
    /// <returns>True when the column is visible.</returns>
    public bool IsColumnVisible(GpuColumn column) =>
        _gpuColumns[(int)column] && AvailableColumn(column);
    /// <summary>Applies included and excluded columns for network and GPU views.</summary>
    /// <param name="network">The network-interface columns included in display.</param>
    /// <param name="excludedNetwork">The network-interface columns excluded from display.</param>
    /// <param name="gpu">The graphics-device columns included in display.</param>
    /// <param name="excludedGpu">The graphics-device columns excluded from display.</param>
    public void ConfigureDetailColumns(
        NetworkColumn[]? network,
        NetworkColumn[]? excludedNetwork,
        GpuColumn[]? gpu,
        GpuColumn[]? excludedGpu
    ) {
        ConfigureDetail(_networkColumns, network, excludedNetwork);
        ConfigureDetail(_gpuColumns, gpu, excludedGpu);
    }

    private static void ConfigureDetail<T>(bool[] selected, T[]? requested, T[]? excluded)
        where T : struct, Enum {
        var next = new bool[selected.Length];
        Array.Fill(next, requested is null);
        if (requested is not null) {
            foreach (var column in requested) {
                if (!Enum.IsDefined(column)) {
                    throw new ArgumentException("Invalid detail column.");
                }

                next[Convert.ToInt32(column, CultureInfo.InvariantCulture)] = true;
            }
        }

        if (excluded is not null) {
            foreach (var column in excluded) {
                if (!Enum.IsDefined(column)) {
                    throw new ArgumentException("Invalid excluded detail column.");
                }

                next[Convert.ToInt32(column, CultureInfo.InvariantCulture)] = false;
            }
        }

        if (!next.Contains(true)) {
            throw new ArgumentException("At least one detail column must remain visible.");
        }

        next.CopyTo(selected, 0);
    }

    private int PickerCount => _pickerView == MonitorView.Network ? NetworkColumnNames.Length : _pickerView == MonitorView.Gpu ? _pickerGpu.Count : _pickerProcesses.Count;
    private string PickerTitle => _pickerView switch {
        MonitorView.Network => "NETWORK COLUMNS",
        MonitorView.Gpu => "GPU COLUMNS",
        _ => "PROCESS COLUMNS"
    };

    private string PickerName(int index) =>
        _pickerView == MonitorView.Network ? NetworkColumnNames[index] : _pickerView == MonitorView.Gpu ? _pickerGpu[index].ToString() : _pickerProcesses[index].ToString();
    private bool PickerContains(int index) =>
        _pickerView == MonitorView.Network ? _networkColumns[index] : _pickerView == MonitorView.Gpu ? _gpuColumns[(int)_pickerGpu[index]] : ColumnSettings.Contains(_pickerProcesses[index]);
    /// <summary>Opens the column-selection interface for the current view.</summary>
    public void OpenColumns() {
        if (View is MonitorView.Dashboard or MonitorView.Services) {
            return;
        }

        _pickerView = View;
        _columnPickerIndex = 0;
        UpdatePickerCapabilities();
        ColumnsOpen = true;
        Refresh();
    }

    private void UpdatePickerCapabilities() {
        _pickerCapabilities = Frame.Capabilities;
        _pickerProcesses.Clear();
        foreach (var column in Enum.GetValues<ProcessColumn>()) {
            if (AvailableColumn(column)) {
                _pickerProcesses.Add(column);
            }
        }
        _pickerGpu.Clear();
        _pickerGpu.AddRange(Enum.GetValues<GpuColumn>().Where(AvailableColumn));
        _columnPickerIndex = Math.Clamp(_columnPickerIndex, 0, Math.Max(0, PickerCount - 1));
    }

    private void ToggleActiveColumn(int index) {
        if (_pickerView is MonitorView.Network or MonitorView.Gpu) {
            var columns = _pickerView == MonitorView.Network ? _networkColumns : _gpuColumns;
            int mapped = _pickerView == MonitorView.Gpu ? (int)_pickerGpu[index] : index;
            int shown = _pickerView == MonitorView.Gpu ? _pickerGpu.Count(column => columns[(int)column]) : columns.Count(x => x);
            if (!columns[mapped] || shown > 1) {
                columns[mapped] = !columns[mapped];
            }
        }
        else {
            ColumnSettings.Toggle(_pickerProcesses[index], Model.Tree);
        }
    }

    private void ResetActiveColumns() {
        if (_pickerView is MonitorView.Network or MonitorView.Gpu) {
            Array.Fill(_pickerView == MonitorView.Network ? _networkColumns : _gpuColumns, true);
        }
        else {
            ColumnSettings.Configure(null, null, Model.Tree);
        }

        _columnLayoutWidth = -1;
    }
}
