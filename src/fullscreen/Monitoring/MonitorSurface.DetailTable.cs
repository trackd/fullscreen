using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Monitoring;

public sealed partial class MonitorSurface {
    private void RenderDetailTable(CellBuffer buffer, bool network) {
        var enabled = network ? _networkColumns : _gpuColumns;
        var names = network ? NetworkColumnNames : GpuColumnNames;
        int top = TableTop, page = PageSize;
        Span<int> positions = stackalloc int[7];
        positions.Fill(-1);
        Span<int> widths = stackalloc int[7];
        int x = 1;
        for (int i = 0; i < names.Length; i++) {
            if (!enabled[i] || !network && !AvailableColumn((GpuColumn)i)) {
                continue;
            }

            int width = DetailColumnWidth(network, i);
            if (x >= Bounds.Width - 4) {
                break;
            }

            positions[i] = x;
            widths[i] = Math.Min(width, Bounds.Width - x - 1);
            Write(buffer, x, top - 1, names[i], Accent, widths[i]);
            x += width + 1;
            Write(
                buffer,
                positions[i] + widths[i] - 1,
                top - 1,
                (network ? _networkFilters : _gpuFilters).ContainsKey(i) ? "◆" : "⌄",
                Accent
            );
        }

        int offset = Math.Clamp(_detailOffset, 0, Math.Max(0, DetailCount - page));
        int count = network ? Frame.NetworkCount : Frame.GpuCount;
        for (int i = 0, matched = 0, visible = 0; i < count && visible < page; i++) {
            if (!(network ? NetworkMatches(Frame.Networks[i]) : GpuMatches(Frame.Gpus[i])) || matched++ < offset) {
                continue;
            }

            int y = top + visible++;
            Fill(buffer, 1, y, Bounds.Width - 2, 1, (i & 1) == 0 ? Panel : Body);
            for (int c = 0; c < names.Length; c++) {
                x = positions[c];
                if (x < 0) {
                    continue;
                }

                int w = widths[c];
                if (network) {
                    var row = Frame.Networks[i];
                    switch (c) {
                        case 0:
                            Write(buffer, x, y, row.Name, Body, w);
                            break;
                        case 1:
                            Quantity(buffer, x, y, row.ReceiveBytesPerSecond, true, Green, w);
                            break;
                        case 2:
                            Quantity(buffer, x, y, row.SendBytesPerSecond, true, Purple, w);
                            break;
                        case 3:
                            LinkSpeed(buffer, x, y, row.Speed);
                            break;
                        case 4:
                            Quantity(buffer, x, y, row.ReceivedBytes, false, Body, w);
                            break;
                        case 5:
                            Quantity(buffer, x, y, row.SentBytes, false, Body, w);
                            break;
                        case 6:
                            Metric(buffer, x, y, row.Errors, Muted, w, "F0");
                            break;
                    }
                }
                else {
                    var row = Frame.Gpus[i];
                    switch (c) {
                        case 0:
                            Write(buffer, x, y, row.Name, Body, w);
                            break;
                        case 1:
                            Write(buffer, x, y, row.Provider, Muted, w);
                            break;
                        case 2:
                            Metric(buffer, x, y, row.Utilization, Purple, w);
                            break;
                        case 3:
                            Quantity(buffer, x, y, row.UsedMemory, false, Green, w);
                            break;
                        case 4:
                            Quantity(buffer, x, y, row.TotalMemory, false, Muted, w);
                            break;
                        case 5:
                            Metric(buffer, x, y, row.Temperature, Hot, w);
                            break;
                    }
                }
            }
        }

        if (DetailCount == 0) {
            Write(buffer, 2, top, network ? Frame.NetworkStatus : Frame.GpuStatus, Muted, Bounds.Width - 4);
        }
    }
}
