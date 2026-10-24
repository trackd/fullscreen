using fullscreen.Objects;
using System.Management.Automation;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Custom;
/// <summary>Composes the same navigation, formatted and JSON views used by fobject.</summary>
public sealed class ReusableObjectExplorer : Themes.ThemedSurface, IDisposable {
    private readonly TuiNode _node;
    private ObjectTableApp _application;
    private object? _pending;
    private bool _dirty;
    /// <summary>Gets the hosted application instance.</summary>
    public ObjectTableApp Application => _application;
    /// <summary>Gets the selected chart point and its original source reference.</summary>
    public object? Selection => _application.Surface.Model.Current?.Original;

    /// <summary>Initializes a new ReusableObjectExplorer instance.</summary>
    /// <param name="node">The control definition being validated.</param>
    /// <param name="value">The original object displayed by the embedded explorer.</param>
    public ReusableObjectExplorer(TuiNode node, object? value) {
        _node = node;
        _application = Create([]);
        AttachChild(_application.Root);
        Replace(value);
    }

    private ObjectTableApp Create(IReadOnlyList<PSObject> objects) {
        var app = new ObjectTableApp(
            objects,
            new() {
                Title = _node.Text.Length > 0 ? _node.Text : "OBJECTS",
                Property = _node.Columns.Length > 0 ? _node.Columns : null,
                View = ObjectView.Expanded
            }
        );
        app.Surface.Embedded = true;
        app.Surface.Minimal = _node.Minimal;
        return app;
    }

    /// <summary>Replaces the object displayed by the embedded explorer.</summary>
    /// <param name="value">The original object displayed by the embedded explorer.</param>
    public void Replace(object? value) {
        _pending = value;
        _dirty = true;
    }

    /// <summary>Processes embedded explorer navigation and pending updates.</summary>
    /// <param name="root">The root visual whose descendants are inspected.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    public void Tick(Visual root, CancellationToken token) {
        if (!_dirty || !TuiComponentHost.IsVisibleWithin(this, root)) {
            return;
        }

        var objects = _pending is null ? [] : TuiTableData.Snapshot(_pending, token).Objects;
        var previous = _application;
        bool focused = previous.Root.HasFocusWithin;
        var app = Create(objects);
        DetachChild(previous.Root);
        _application = app;
        AttachChild(app.Root);
        Palette.Apply(app.Root);
        previous.Dispose();
        if (focused) {
            App?.Focus(app.Root);
        }

        _dirty = false;
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? _application.Root : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _application.Root.Measure(constraints);
        return SizeHints.Flex(new(20, 6), new(80, 18), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        _application.Root.Arrange(finalRect);
    /// <inheritdoc/>
    public void Dispose() =>
        _application.Dispose();
}
