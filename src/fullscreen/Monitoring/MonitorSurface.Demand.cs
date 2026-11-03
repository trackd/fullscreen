namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    /// <summary>Gets or sets whether dashboard summaries continue sampling while another view is active.</summary>
    public bool BackgroundSampling { get; set; }
    /// <summary>Gets the metric groups needed by the active view.</summary>
    public CollectorMetrics RequiredMetrics {
        get {
            if (Paused) {
                return CollectorMetrics.None;
            }

            if (Bounds.Width > 0 && (Bounds.Width < 35 || Bounds.Height < 12)) {
                return CollectorMetrics.None;
            }

            const CollectorMetrics summaries = CollectorMetrics.Cpu | CollectorMetrics.Memory | CollectorMetrics.Network | CollectorMetrics.Gpu;
            CollectorMetrics background = BackgroundSampling ? summaries : CollectorMetrics.None;
            if (View == MonitorView.Dashboard) {
                return summaries;
            }

            if (View == MonitorView.Services) {
                return CollectorMetrics.Services | background;
            }

            if (View == MonitorView.Users) {
                return background | CollectorMetrics.Users | (IncludeUserProcesses ? CollectorMetrics.Processes | CollectorMetrics.Owners : CollectorMetrics.None);
            }

            bool compact = Bounds.Width > 0 && (Bounds.Width < 76 || Bounds.Height < 20);
            CollectorMetrics required = background | (!Minimal && (ProcessTable || compact) ? summaries : CollectorMetrics.None);
            if (View == MonitorView.Network) {
                return required | CollectorMetrics.Network;
            }

            if (View == MonitorView.Gpu) {
                return required | CollectorMetrics.Gpu;
            }

            required |= CollectorMetrics.Processes;
            foreach (var column in AvailableProcessColumns) {
                required |= ColumnMetrics(column);
            }

            foreach (var filter in Model.ColumnFilters.Keys) {
                required |= ColumnMetrics(filter);
            }

            if (Model.Filter.Length != 0 || Model.Sort == ProcessSort.UserName) {
                required |= CollectorMetrics.Owners;
            }

            required |= Model.Sort switch {
                ProcessSort.Gpu or ProcessSort.GpuMemory => CollectorMetrics.ProcessGpu,
                ProcessSort.Disk => CollectorMetrics.ProcessDisk,
                ProcessSort.Network => CollectorMetrics.ProcessNetwork,
                _ => CollectorMetrics.None
            };
            if (!Supports(CollectorCapabilities.ProcessOwner)) { required &= ~CollectorMetrics.Owners; }
            if (!Supports(CollectorCapabilities.ProcessDisk)) { required &= ~CollectorMetrics.ProcessDisk; }
            if (!Supports(CollectorCapabilities.ProcessNetwork)) { required &= ~CollectorMetrics.ProcessNetwork; }
            if (!Supports(CollectorCapabilities.ProcessGpu) && !Supports(CollectorCapabilities.ProcessGpuMemory)) { required &= ~CollectorMetrics.ProcessGpu; }
            return required;
        }
    }

    private static CollectorMetrics ColumnMetrics(ProcessColumn column) =>
        column switch {
            ProcessColumn.UserName => CollectorMetrics.Owners,
            ProcessColumn.MemoryPercent => CollectorMetrics.Memory,
            ProcessColumn.Gpu or ProcessColumn.GpuMemory => CollectorMetrics.ProcessGpu,
            ProcessColumn.Disk => CollectorMetrics.ProcessDisk,
            ProcessColumn.Network => CollectorMetrics.ProcessNetwork,
            _ => CollectorMetrics.None
        };
    /// <inheritdoc/>
    protected override void OnMinimalChanged() {
        _columnLayoutWidth = -1;
        Dirty = true;
        Refresh();
    }
}
