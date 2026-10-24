using System.Text;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Custom;

public sealed partial class TuiDesignerApp {
    private readonly Padder _previewContent = new() {
        Padding = new(0),
        HorizontalAlignment = Align.Stretch,
        VerticalAlignment = Align.Stretch
    };
    private PreviewMarkers? _markers;
    private string? _dropId;
    private bool _dropAfter;
    private void InitializeMarkers() {
        _markers = new(this);
        _previewHost.Content = new ZStack(_previewContent, _markers) {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
    }

    private sealed partial class PreviewMarkers : Visual {
        private readonly TuiDesignerApp _owner;
        [Bindable]
        private partial int Revision { get; set; }

        public PreviewMarkers(TuiDesignerApp owner) {
            _owner = owner;
            IsHitTestVisible = false;
            HorizontalAlignment = Align.Stretch;
            VerticalAlignment = Align.Stretch;
        }

        public void Refresh() =>
            Revision++;
        protected override SizeHints MeasureCore(in LayoutConstraints constraints) =>
            SizeHints.Flex(new(0, 0), new(0, 0), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
        protected override void RenderOverride(CellBuffer buffer) {
            _ = Revision;
            if (_owner._rows.Count == 0) {
                return;
            }

            string id = _owner._dropId ?? _owner._rows[_owner._selection].Node.Id;
            if (!_owner._preview.Controls.TryGetValue(id, out var control) || control.Bounds.Width <= 0
                || control.Bounds.Height <= 0) {
                return;
            }

            var rect = control.Bounds;
            var style = _owner._theme.AccentStyle;
            void Cell(int x, int y, char value) {
                if (Bounds.Contains(x, y)) {
                    buffer.SetCell(x, y, new Rune(value), style);
                }
            }

            if (_owner._dropId is not null && !_owner._rows.First(row => row.Node.Id == id).Node.IsContainer) {
                bool horizontal = _owner._rows.First(row => row.Node.Id == id).Parent?.Type == "HStack";
                if (horizontal) {
                    int x = _owner._dropAfter ? rect.Right - 1 : rect.X;
                    for (int y = rect.Y; y < rect.Bottom; y++) {
                        Cell(x, y, '│');
                    }
                }
                else {
                    int y = _owner._dropAfter ? rect.Bottom - 1 : rect.Y;
                    for (int x = rect.X; x < rect.Right; x++) {
                        Cell(x, y, '─');
                    }
                }

                return;
            }

            int left = Math.Max(Bounds.X, rect.X - 1), right = Math.Min(Bounds.Right - 1, rect.Right);
            int top = Math.Max(Bounds.Y, rect.Y - 1), bottom = Math.Min(Bounds.Bottom - 1, rect.Bottom);
            for (int x = left + 1; x < right; x++) {
                Cell(x, top, '─');
                Cell(x, bottom, '─');
            }

            for (int y = top + 1; y < bottom; y++) {
                Cell(left, y, '│');
                Cell(right, y, '│');
            }

            Cell(left, top, '┌');
            Cell(right, top, '┐');
            Cell(left, bottom, '└');
            Cell(right, bottom, '┘');
        }
    }
}
