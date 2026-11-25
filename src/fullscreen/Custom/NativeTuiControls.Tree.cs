using System.Runtime.CompilerServices;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Custom;

internal static partial class NativeTuiControls {
    private sealed class LazyBranch {
        internal bool Pending;
    }
    private sealed class LazyTree {
        internal Dictionary<TreeNode, LazyBranch> Branches { get; } = [];
        internal int Count;

        internal TreeNode Create(object? value, TreeNode? parent, int depth, ref int count) {
            if (++count > 10000 || depth > 24) {
                throw new ArgumentException("TreeView allows at most 10,000 nodes and 24 levels.");
            }
            var identity = Unwrap(value);
            for (var ancestor = parent; identity is not null && ancestor is not null; ancestor = ancestor.Parent) {
                if (ReferenceEquals(identity, Unwrap(ancestor.Data))) {
                    throw new ArgumentException("TreeView data contains a cycle.");
                }
            }
            var node = new TreeNode(new TextBlock(Text(Member(value, "Text") ?? Member(value, "Name") ?? value))) { Data = value };
            if (Member(value, "HasChildren") is { } hasChildren && Convert.ToBoolean(Unwrap(hasChildren), System.Globalization.CultureInfo.InvariantCulture)) {
                Branches.Add(node, new());
                node.Children.Add(new TreeNode(new TextBlock("Loading…")));
            }
            return node;
        }

        internal void Load(TreeNode parent, object? children) {
            int depth = 1;
            for (var ancestor = parent.Parent; ancestor is not null; ancestor = ancestor.Parent) {
                depth++;
            }
            int count = Count;
            var added = new List<TreeNode>();
            try {
                foreach (var child in Items(children)) {
                    added.Add(Create(child, parent, depth, ref count));
                }
            }
            catch {
                foreach (var child in added) {
                    Branches.Remove(child);
                }
                throw;
            }
            parent.Children.Clear();
            foreach (var child in added) {
                parent.Children.Add(child);
            }
            Count = count;
            Branches.Remove(parent);
        }
    }

    private static readonly ConditionalWeakTable<TreeView, LazyTree> LazyTrees = new();

    private static void ReplaceLazyTree(TreeView tree, object? rows) {
        var state = new LazyTree();
        var roots = new List<TreeNode>();
        int count = 0;
        foreach (var row in Items(rows)) {
            roots.Add(state.Create(row, null, 0, ref count));
        }
        state.Count = count;
        LazyTrees.Remove(tree);
        LazyTrees.Add(tree, state);
        tree.Roots.Clear();
        foreach (var root in roots) {
            tree.Roots.Add(root);
        }
    }

    internal static void ExpandTree(TreeView tree, string id, Func<TuiEvent, bool> enqueue) {
        if (!LazyTrees.TryGetValue(tree, out var state)) {
            return;
        }
        // Completion runs from the event queue after this traversal has finished.
        foreach (var (node, branch) in state.Branches) {
            if (branch.Pending || !node.IsExpanded) {
                continue;
            }
            bool visible = true;
            for (var parent = node.Parent; parent is not null; parent = parent.Parent) {
                visible &= parent.IsExpanded;
            }
            if (!visible) {
                continue;
            }
            branch.Pending = enqueue(new TuiEvent(id, "Expand", node.Data) {
                Complete = children => {
                    try {
                        state.Load(node, children);
                    }
                    finally {
                        branch.Pending = false;
                    }
                }
            });
            break;
        }
    }
}
