namespace fullscreen.Monitoring;

public sealed partial class ProcessView {
    /// <summary>Gets or sets whether tree display requires the process-name column.</summary>
    public bool Tree { get; set; }

    private ProcessRow[] _nodes = new ProcessRow[1024];
    private int[] _parents = new int[1024], _firstChild = new int[1025], _lastChild = new int[1025], _nextSibling = new int[1024];
    private int[] _visibleNodes = new int[1024], _depths = new int[1024], _stack = new int[1024];
    private byte[] _state = new byte[1024];
    private bool[] _included = new bool[1024];
    private readonly Dictionary<int, int> _indices = new(1024);
    private readonly HashSet<Identity> _collapsed = [];
    private readonly List<Identity> _expired = [];
    private readonly record struct Identity(int Id, long Birth);
    /// <summary>Returns the tree depth of a visible row.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <returns>The zero-based tree depth.</returns>
    public int DepthAt(int row) =>
        Tree ? _depths[row] : 0;
    /// <summary>Checks whether a visible process row has child processes.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <returns>True when the row has children.</returns>
    public bool HasChildrenAt(int row) =>
        Tree && _firstChild[_visibleNodes[row]] >= 0;
    /// <summary>Checks whether a process row is the last visible sibling in its tree branch.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <returns>True when the row is the last sibling.</returns>
    public bool IsLastAt(int row) =>
        Tree && _nextSibling[_visibleNodes[row]] < 0;
    /// <summary>Checks whether a process row&apos;s children are collapsed.</summary>
    /// <param name="row">The zero-based visible process row index, or the source process row to
    /// inspect.</param>
    /// <returns>True when the row is collapsed.</returns>
    public bool IsCollapsedAt(int row) =>
        Tree && Filter.Length == 0 && ColumnFilters.Count == 0
        && _collapsed.Contains(new(_rows[row].Id, _rows[row].StartTimeTicks));
    /// <summary>Toggles the selected process branch.</summary>
    /// <returns>True when the tree changed.</returns>
    public bool ToggleSelected() {
        if (!Tree || !_indices.TryGetValue(SelectedId, out int index) || _firstChild[index] < 0) {
            return false;
        }

        var identity = new Identity(_nodes[index].Id, _nodes[index].StartTimeTicks);
        if (!_collapsed.Remove(identity)) {
            _collapsed.Add(identity);
        }

        return true;
    }

    /// <summary>Expands or collapses the selected process branch.</summary>
    /// <param name="collapse">Whether to collapse rather than expand the branch.</param>
    /// <returns>True when the tree changed.</returns>
    public bool CollapseSelected(bool collapse) {
        if (!Tree || !_indices.TryGetValue(SelectedId, out int index)) {
            return false;
        }

        var identity = new Identity(_nodes[index].Id, _nodes[index].StartTimeTicks);
        if (!collapse) {
            return _collapsed.Remove(identity);
        }

        if (_firstChild[index] >= 0 && _collapsed.Add(identity)) {
            return true;
        }

        int parent = _parents[index];
        if (parent >= 0 && parent < _indices.Count) {
            SelectedId = _nodes[parent].Id;
            return true;
        }

        return false;
    }

    /// <summary>Expands or collapses every process branch.</summary>
    /// <param name="collapse">Whether to collapse rather than expand the branch.</param>
    /// <returns>True when the tree changed.</returns>
    public bool CollapseAll(bool collapse) {
        if (!Tree) {
            return false;
        }

        if (!collapse) {
            bool changed = _collapsed.Count > 0;
            _collapsed.Clear();
            return changed;
        }

        bool added = false;
        for (int index = 0; index < _indices.Count; index++) {
            if (_firstChild[index] >= 0) {
                added |= _collapsed.Add(new(_nodes[index].Id, _nodes[index].StartTimeTicks));
            }
        }

        return added;
    }

    private void BuildTree(ReadOnlySpan<ProcessRow> source) {
        int length = source.Length;
        _nodes = CollectorFrame.EnsureCapacity(_nodes, length);
        _parents = CollectorFrame.EnsureCapacity(_parents, length);
        _firstChild = CollectorFrame.EnsureCapacity(_firstChild, length + 1);
        _lastChild = CollectorFrame.EnsureCapacity(_lastChild, length + 1);
        _nextSibling = CollectorFrame.EnsureCapacity(_nextSibling, length);
        _visibleNodes = CollectorFrame.EnsureCapacity(_visibleNodes, length);
        _depths = CollectorFrame.EnsureCapacity(_depths, length);
        _stack = CollectorFrame.EnsureCapacity(_stack, length);
        _state = CollectorFrame.EnsureCapacity(_state, length);
        _included = CollectorFrame.EnsureCapacity(_included, length);
        length = 0;
        foreach (ref readonly var row in source) {
            if (!ExcludedNames.Contains(row.Name)) {
                _nodes[length++] = row;
            }
        }

        _nodes.AsSpan(0, length).Sort(_comparison);
        _indices.Clear();
        for (int i = 0; i < length; i++) {
            _indices[_nodes[i].Id] = i;
        }

        for (int i = 0; i < length; i++) {
            ref readonly var row = ref _nodes[i];
            int parent = -1;
            if (row.ParentId.HasValue && _indices.TryGetValue(row.ParentId.Value, out int candidate)
                && candidate != i) {
                // A recycled parent PID must not adopt children of its previous process.
                if (row.StartTimeTicks == 0 || _nodes[candidate].StartTimeTicks == 0
                    || _nodes[candidate].StartTimeTicks <= row.StartTimeTicks) {
                    parent = candidate;
                }
            }

            _parents[i] = parent;
        }

        _state.AsSpan(0, length).Clear();
        for (int i = 0; i < length; i++) {
            if (_state[i] != 0) {
                continue;
            }

            int node = i, count = 0;
            while (node >= 0 && _state[node] == 0) {
                _state[node] = 1;
                _stack[count++] = node;
                node = _parents[node];
            }

            if (node >= 0 && _state[node] == 1) {
                _parents[node] = -1;
            }

            for (int j = 0; j < count; j++) {
                _state[_stack[j]] = 2;
            }
        }

        _firstChild.AsSpan(0, length + 1).Fill(-1);
        _lastChild.AsSpan(0, length + 1).Fill(-1);
        _nextSibling.AsSpan(0, length).Fill(-1);
        for (int i = 0; i < length; i++) {
            int parent = _parents[i] < 0 ? length : _parents[i];
            if (_lastChild[parent] < 0) {
                _firstChild[parent] = i;
            }
            else {
                _nextSibling[_lastChild[parent]] = i;
            }

            _lastChild[parent] = i;
        }

        _included.AsSpan(0, length).Clear();
        if (Filter.Length == 0 && ColumnFilters.Count == 0) {
            _included.AsSpan(0, length).Fill(true);
        }
        else {
            for (int i = 0; i < length; i++) {
                if (!MatchesFilters(_nodes[i])) {
                    continue;
                }

                int node = i;
                while (node >= 0 && !_included[node]) {
                    _included[node] = true;
                    node = _parents[node];
                }
            }
        }

        _expired.Clear();
        foreach (var identity in _collapsed) {
            if (!_indices.TryGetValue(identity.Id, out int index) || _nodes[index].StartTimeTicks != identity.Birth) {
                _expired.Add(identity);
            }
        }

        foreach (var identity in _expired) {
            _collapsed.Remove(identity);
        }

        int current = _firstChild[length], depth = 0;
        while (current >= 0) {
            if (_included[current]) {
                _rows[Count] = _nodes[current];
                _visibleNodes[Count] = current;
                _depths[Count++] = depth;
                if (_firstChild[current] >= 0
                    && (Filter.Length > 0 || ColumnFilters.Count > 0
                    || !_collapsed.Contains(new(_nodes[current].Id, _nodes[current].StartTimeTicks)))) {
                    current = _firstChild[current];
                    depth++;
                    continue;
                }
            }

            while (_nextSibling[current] < 0 && _parents[current] >= 0) {
                current = _parents[current];
                depth--;
            }

            current = _nextSibling[current];
        }
    }
}
