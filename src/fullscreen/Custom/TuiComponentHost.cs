using fullscreen.Hosting;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Custom;
/// <summary>Owns an embedded application's worker lifetime without opening another terminal.</summary>
public sealed partial class TuiComponentHost : Themes.ThemedSurface, IDisposable {
    private readonly Func<IFullscreenApp> _factory;
    private readonly bool _preview;
    private readonly TextBlock _placeholder;
    private IFullscreenApp? _application;
    private Visual Child => _application?.Root ?? _placeholder;

    private bool _closedByUser, _disposed;
    [Bindable]
    private partial int Revision { get; set; }
    /// <summary>Gets whether the application should continue running.</summary>
    public bool Running => _application is not null;
    /// <summary>Gets the selected chart point and its original source reference.</summary>
    public object? Selection => _application?.Root switch {
        Files.FileManagerSurface files => files.Model.Selected?.FullPath,
        Files.CommanderSurface commander => commander.Active.Model.Selected?.FullPath,
        Monitoring.MonitorSurface monitor when monitor.View == Monitoring.MonitorView.Users => monitor.Users.Selected,
        _ => null
    };

    /// <summary>Raised when closed is requested.</summary>
    public event Action<object?>? Closed;
    /// <summary>Initializes a new TuiComponentHost instance.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="factory">The factory used to create the hosted component.</param>
    /// <param name="preview">The preview content, or null to clear the preview. Defaults to false.</param>
    public TuiComponentHost(string name, Func<IFullscreenApp> factory, bool preview = false) {
        _factory = factory;
        _preview = preview;
        _placeholder = new TextBlock(preview ? $"{name} · preview only\nLive workers start when Invoke-Tui displays this component." : $"{name} · opens when visible");
        AttachChild(_placeholder);
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    internal static bool IsVisibleWithin(Visual visual, Visual root) {
        var bounds = visual.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || visual.App is null) {
            return false;
        }

        for (Visual? current = visual; current is not null; current = current.Parent) {
            if (!current.IsVisible || current.Bounds.Width <= 0 || current.Bounds.Height <= 0
                || bounds.X >= current.Bounds.X + current.Bounds.Width
                || bounds.Y >= current.Bounds.Y + current.Bounds.Height
                || bounds.X + bounds.Width <= current.Bounds.X
                || bounds.Y + bounds.Height <= current.Bounds.Y) {
                return false;
            }

            if (ReferenceEquals(current, root)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>Updates hosted components beneath the supplied visual root.</summary>
    /// <param name="root">The root visual whose descendants are inspected.</param>
    public void Tick(Visual root) {
        if (_disposed || _preview) {
            return;
        }

        if (!IsVisibleWithin(this, root)) {
            Stop();
            _closedByUser = false;
            return;
        }

        if (_closedByUser) {
            return;
        }

        if (_application is null) {
            _application = _factory();
            DetachChild(_placeholder);
            AttachChild(_application.Root);
            Palette.Apply(_application.Root);
            Revision++;
        }

        if (!_application.Tick()) {
            object? selection = Selection;
            Stop();
            _closedByUser = true;
            _placeholder.Text = "Component closed · switch away and back to reopen";
            Closed?.Invoke(selection);
        }
    }

    private void Stop() {
        if (_application is null) {
            return;
        }

        var application = _application;
        _application = null;
        DetachChild(application.Root);
        AttachChild(_placeholder);
        try {
            application.Dispose();
        }
        finally {
            Revision++;
        }
    }

    /// <inheritdoc/>
    protected override int ChildrenCount => 1;

    /// <inheritdoc/>
    protected override Visual GetChild(int index) =>
        index == 0 ? Child : throw new ArgumentOutOfRangeException(nameof(index));
    /// <inheritdoc/>
    protected override SizeHints MeasureCore(in LayoutConstraints constraints) {
        _ = Revision;
        Child.Measure(constraints);
        return SizeHints.Flex(new(20, 6), new(80, 18), new(int.MaxValue, int.MaxValue), 1, 1, 1, 1);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(in Rectangle finalRect) =>
        Child.Arrange(finalRect);
    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        Stop();
    }
}
