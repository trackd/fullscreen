using System.Text.Json;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Input;

namespace fullscreen.Tables;

internal sealed class JsonFoldDocument {
    internal string Source { get; }
    internal string[] Lines { get; }
    internal Dictionary<int, int> Ends { get; } = [];
    internal HashSet<int> Collapsed { get; } = [];

    internal JsonFoldDocument(string source) {
        using var parsed = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 128 });
        Source = source;
        Lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var openings = new Stack<int>();
        bool quoted = false, escaped = false;
        for (int line = 0; line < Lines.Length; line++) {
            foreach (char token in Lines[line]) {
                if (quoted) {
                    if (escaped) {
                        escaped = false;
                    }
                    else if (token == '\\') {
                        escaped = true;
                    }
                    else if (token == '"') {
                        quoted = false;
                    }
                }
                else if (token == '"') {
                    quoted = true;
                }
                else if (token is '{' or '[') {
                    openings.Push(line);
                }
                else if (token is '}' or ']') {
                    int start = openings.Pop();
                    if (line > start) {
                        Ends[start] = line;
                    }
                }
            }
        }
    }
}

public sealed partial class TableSurface {
    internal bool EnableJsonFolding { get; set; }
    private JsonFoldDocument? _jsonFolds;
    private readonly Dictionary<string, JsonFoldDocument> _jsonDocuments = new(StringComparer.Ordinal);
    private readonly Queue<string> _jsonDocumentOrder = [];
    private int[] _jsonLineMap = [];

    private void InitializeJsonFolding(string? text) {
        _jsonFolds = null;
        _jsonLineMap = [];
        if (!EnableJsonFolding || DocumentTitle != "JSON" || text is null) {
            return;
        }
        if (!_jsonDocuments.TryGetValue(text, out var folds)) {
            try {
                folds = new JsonFoldDocument(text);
            }
            catch (JsonException) {
                return;
            } // Truncated/invalid snapshots remain readable as plain text.
            if (_jsonDocuments.Count == 16) {
                _jsonDocuments.Remove(_jsonDocumentOrder.Dequeue());
            }
            _jsonDocuments.Add(text, folds);
            _jsonDocumentOrder.Enqueue(text);
        }
        _jsonFolds = folds;
        ProjectJsonFolds(0);
    }

    private void ProjectJsonFolds(int originalCaret) {
        if (_jsonFolds is not { } folds) {
            return;
        }
        var lines = new List<string>();
        var map = new List<int>();
        for (int line = 0; line < folds.Lines.Length; line++) {
            map.Add(line);
            string text = folds.Lines[line];
            if (folds.Collapsed.Contains(line) && folds.Ends.TryGetValue(line, out int end)) {
                lines.Add(text + " … " + folds.Lines[end].Trim());
                line = end;
            }
            else {
                lines.Add(text);
            }
        }
        _document = [.. lines];
        _jsonLineMap = [.. map];
        int selected = Math.Max(0, Array.FindLastIndex(_jsonLineMap, line => line <= originalCaret));
        _documentAnchor = _documentCaret = (selected, 0);
        _selectingDocument = false;
        _documentOffset = Math.Clamp(_documentOffset, Math.Max(0, selected - DocumentRows + 1), selected);
        Refresh();
    }

    private void ToggleJsonFold(bool? collapse = null) {
        if (_jsonFolds is not { } folds || _jsonLineMap.Length == 0) {
            return;
        }
        int original = _jsonLineMap[_documentCaret.Line];
        int start = original;
        if (!folds.Ends.ContainsKey(start) && collapse == true) {
            start = folds.Ends.Where(pair => pair.Key < original && pair.Value >= original).Select(pair => pair.Key).DefaultIfEmpty(-1).Max();
        }
        if (!folds.Ends.ContainsKey(start)) {
            return;
        }
        bool close = collapse ?? !folds.Collapsed.Contains(start);
        if (close) {
            folds.Collapsed.Add(start);
        }
        else {
            folds.Collapsed.Remove(start);
        }
        ProjectJsonFolds(start);
    }

    private bool HandleJsonNavigation(KeyEventArgs e) {
        if (_jsonFolds is null || e.Modifiers != TerminalModifiers.None) {
            return false;
        }
        if (e.Key == TerminalKey.Enter) {
            ToggleJsonFold();
        }
        else if (e.Key == TerminalKey.Left) {
            ToggleJsonFold(true);
        }
        else if (e.Key == TerminalKey.Right) {
            ToggleJsonFold(false);
        }
        else {
            int line = _documentCaret.Line;
            int next = e.Key switch {
                TerminalKey.Up => line - 1,
                TerminalKey.Down => line + 1,
                TerminalKey.PageUp => line - DocumentRows,
                TerminalKey.PageDown => line + DocumentRows,
                TerminalKey.Home => 0,
                TerminalKey.End => _document!.Length - 1,
                _ => -1
            };
            if (next == -1 && e.Key != TerminalKey.Up) {
                return false;
            }
            line = Math.Clamp(next, 0, _document!.Length - 1);
            _documentAnchor = _documentCaret = (line, 0);
            _documentOffset = Math.Clamp(_documentOffset, Math.Max(0, line - DocumentRows + 1), line);
            Refresh();
        }
        e.Handled = true;
        return true;
    }
}
