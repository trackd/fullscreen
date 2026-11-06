using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tables;

public sealed partial class TableSurface {
    private (int Line, int Column) _documentAnchor;
    private (int Line, int Column) _documentCaret;
    private bool _selectingDocument;
    private bool HasDocumentSelection => _documentAnchor != _documentCaret;

    internal string SelectedDocumentText {
        get {
            if (_document is null || !HasDocumentSelection) { return ""; }
            var (start, end) = DocumentSelectionRange();
            if (start.Line == end.Line) { return _document[start.Line][start.Column..end.Column]; }
            var text = new StringBuilder();
            text.Append(_document[start.Line].AsSpan(start.Column));
            for (int line = start.Line + 1; line < end.Line; line++) { text.Append('\n').Append(_document[line]); }
            text.Append('\n').Append(_document[end.Line].AsSpan(0, end.Column));
            return text.ToString();
        }
    }

    private ((int Line, int Column) Start, (int Line, int Column) End) DocumentSelectionRange() =>
        _documentAnchor.CompareTo(_documentCaret) <= 0
            ? (_documentAnchor, _documentCaret) : (_documentCaret, _documentAnchor);

    private void ResetDocumentSelection() {
        _documentAnchor = _documentCaret = (0, 0);
        _selectingDocument = false;
    }

    private bool HandleDocumentSelectionKey(KeyEventArgs e) {
        if (_document is not { Length: > 0 }) { return false; }
        bool control = (e.Modifiers & TerminalModifiers.Ctrl) != 0;
        if (control && e.Char is 'a' or 'A' or '\u0001') {
            _documentAnchor = (0, 0);
            _documentCaret = (_document.Length - 1, _document[^1].Length);
        }
        else if (control && e.Char is 'c' or 'C' or '\u0003') {
            CopyDocumentSelection();
        }
        else if ((e.Modifiers & TerminalModifiers.Shift) != 0
            && e.Key is TerminalKey.Left or TerminalKey.Right or TerminalKey.Up or TerminalKey.Down
                or TerminalKey.Home or TerminalKey.End or TerminalKey.PageUp or TerminalKey.PageDown) {
            int line = _documentCaret.Line;
            int column = _documentCaret.Column;
            switch (e.Key) {
                case TerminalKey.Left:
                    if (column > 0) { column--; if (column > 0 && char.IsLowSurrogate(_document[line][column])) { column--; } }
                    else if (line > 0) { line--; column = _document[line].Length; }
                    break;
                case TerminalKey.Right:
                    if (column < _document[line].Length) {
                        column += char.IsHighSurrogate(_document[line][column]) && column + 1 < _document[line].Length ? 2 : 1;
                    }
                    else if (line + 1 < _document.Length) { line++; column = 0; }
                    break;
                case TerminalKey.Up:
                    line--;
                    break;
                case TerminalKey.Down:
                    line++;
                    break;
                case TerminalKey.PageUp:
                    line -= DocumentRows;
                    break;
                case TerminalKey.PageDown:
                    line += DocumentRows;
                    break;
                case TerminalKey.Home:
                    column = 0;
                    if (control) { line = 0; }
                    break;
                case TerminalKey.End:
                    if (control) { line = _document.Length - 1; }
                    column = _document[line].Length;
                    break;
            }
            line = Math.Clamp(line, 0, _document.Length - 1);
            column = Math.Clamp(column, 0, _document[line].Length);
            if (column > 0 && column < _document[line].Length && char.IsLowSurrogate(_document[line][column])) {
                column--;
            }
            _documentCaret = (line, column);
            if (line < _documentOffset) { _documentOffset = line; }
            else if (line >= _documentOffset + DocumentRows) { _documentOffset = line - DocumentRows + 1; }
            int cells = CellWidth(_document[line].AsSpan(0, column));
            _documentX = Math.Max(0, Math.Min(_documentX, column));
            if (cells - CellWidth(_document[line].AsSpan(0, Math.Min(_documentX, column))) >= Bounds.Width - 3) {
                _documentX = column;
            }
        }
        else if (e.Key == TerminalKey.Escape && HasDocumentSelection) {
            _documentAnchor = _documentCaret;
        }
        else { return false; }
        e.Handled = true;
        Refresh();
        return true;
    }

    private void CopyDocumentSelection() {
        string text = SelectedDocumentText;
        Status = text.Length == 0 ? "Select text before copying"
            : App?.Terminal.Clipboard.TrySetText(text) == true ? "Selection copied"
            : "Clipboard is unavailable in this terminal";
        Refresh();
    }

    private (int Line, int Column) DocumentPoint(int x, int y) {
        int line = Math.Clamp(y - DocumentTop + _documentOffset, 0, _document!.Length - 1);
        string text = _document[line];
        int column = Math.Min(_documentX, text.Length);
        if (column > 0 && column < text.Length && char.IsLowSurrogate(text[column])) { column--; }
        int target = Math.Max(0, x - 2);
        for (int cells = 0; column < text.Length;) {
            int length = char.IsHighSurrogate(text[column]) && column + 1 < text.Length ? 2 : 1;
            int width = CellWidth(text.AsSpan(column, length));
            if (cells + width > target) { break; }
            cells += width;
            column += length;
        }
        return (line, column);
    }

    private void ExtendDocumentSelection(PointerEventArgs e) {
        if (e.LocalY < DocumentTop) { ScrollDocument(-1); }
        else if (e.LocalY >= DocumentTop + DocumentRows) { ScrollDocument(1); }
        _documentCaret = DocumentPoint(e.LocalX, e.LocalY);
        e.Handled = true;
        Refresh();
    }

    private void RenderDocumentSelection(CellBuffer buffer, int line, int y) {
        if (!HasDocumentSelection) { return; }
        var (start, end) = DocumentSelectionRange();
        if (line < start.Line || line > end.Line) { return; }
        string text = _document![line];
        int from = Math.Max(Math.Min(_documentX, text.Length), line == start.Line ? start.Column : 0);
        int to = line == end.Line ? end.Column : text.Length;
        if (to <= from) { return; }
        int x = 2 + CellWidth(text.AsSpan(Math.Min(_documentX, text.Length), from - Math.Min(_documentX, text.Length)));
        Write(buffer, x, y, text.AsSpan(from, to - from), SelectedStyle, Math.Max(0, Bounds.Width - x - 1));
    }
}
