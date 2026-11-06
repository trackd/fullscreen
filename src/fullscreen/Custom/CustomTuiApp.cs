using System.Collections;
using CodeControl = fullscreen.Files.TextMatePreview;
using ImageControl = fullscreen.Files.ImagePreview;
using System.Diagnostics;
using System.Globalization;
using fullscreen.Hosting;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;

namespace fullscreen.Custom;
/// <summary>Describes an interaction delivered to a custom TUI event handler.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Kind">The operation or event category.</param>
/// <param name="Value">The input value to format, display, or append. Defaults to null.</param>
public sealed record TuiEvent(string Id, string Kind, object? Value = null);
/// <summary>Describes state changes and an optional close request returned by a TUI event handler.</summary>
/// <param name="State">The state exposed to the current view or callback. Defaults to null.</param>
/// <param name="Close">Whether the callback requests closing the application. Defaults to false.</param>
/// <param name="Value">The input value to format, display, or append. Defaults to null.</param>
public sealed record TuiReply(IDictionary? State = null, bool Close = false, object? Value = null);
/// <summary>All callbacks run synchronously from Tick on the invoking PowerShell/UI thread.</summary>
public sealed partial class CustomTuiApp : IFullscreenApp {
    private sealed record Binding(TuiNode Node, Visual Control) {
        public object? Last { get; set; }
        public object? LastEdges { get; set; }
        public bool Deferred { get; set; }
    }

    private readonly List<Binding> _bindings = [];
    private readonly Dictionary<Charts.Chart, Charts.ChartTopology> _diagramSnapshots = [];
    private readonly Dictionary<Charts.Chart, Charts.ChartDataSeries[]> _objectSeriesSnapshots = [];
    private readonly Dictionary<Charts.Chart, Charts.ChartTask[]> _taskSnapshots = [];
    private readonly Dictionary<Charts.Chart, Charts.ChartFrame[]> _frameSnapshots = [];
    private readonly List<Visual> _chrome = [];
    private readonly List<(TuiNode Node, ReusableTable Table)> _tables = [];
    private readonly List<(TuiNode Node, ReusableObjectExplorer Explorer)> _explorers = [];
    private readonly List<(TuiNode Node, Visual Control)> _native = [];
    private readonly List<(TuiNode Node, TuiComponentHost Host)> _components = [];
    private readonly Dictionary<string, object?> _selections = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;
    private readonly TuiDocument _document;
    private Themes.ThemePalette _theme = Themes.ThemePalette.Default;
    private readonly Queue<TuiEvent> _events = new();
    private readonly Func<TuiEvent, Hashtable, TuiReply?>? _handler;
    private readonly Func<TuiEvent, Hashtable, TuiReply?>? _onTick;
    private readonly TimeSpan _interval;
    private readonly bool _preview;
    private readonly CancellationToken _cancellationToken;
    private long _lastTick = Stopwatch.GetTimestamp();
    private bool _closed;
    /// <summary>Gets whether the operation was cancelled.</summary>
    public bool Cancelled { get; private set; } = true;
    /// <summary>Gets the value accepted by the custom application.</summary>
    public object? Value { get; private set; }
    /// <summary>Gets the most recently handled TUI interaction.</summary>
    public string LastEvent { get; private set; } = "";
    /// <summary>Gets the state exposed to the current view or callback.</summary>
    public Hashtable State { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets the controls indexed by document identifier.</summary>
    public Dictionary<string, Visual> Controls { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets the root visual whose descendants are inspected.</summary>
    public Visual Root { get; }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.SetMinimal"/>
    public void SetMinimal(bool minimal) {
        foreach (var visual in _chrome) {
            visual.IsVisible = !minimal;
        }
    }

    /// <summary>Initializes a new CustomTuiApp instance.</summary>
    /// <param name="document">The layout document to validate, render, or serialize.</param>
    /// <param name="state">The state exposed to the current view or callback. Defaults to null.</param>
    /// <param name="handler">The callback that receives TUI events. Defaults to null.</param>
    /// <param name="onTick">The periodic update callback. Defaults to null.</param>
    /// <param name="tickInterval">The interval in milliseconds between callback invocations. Defaults to
    /// 250.</param>
    /// <param name="preview">The preview content, or null to clear the preview. Defaults to false.</param>
    /// <param name="directory">The parent or working directory. Defaults to null.</param>
    /// <param name="cancellationToken">The token used to cancel the operation. Defaults to default.</param>
    public CustomTuiApp(
        TuiDocument document,
        IDictionary? state = null,
        Func<TuiEvent, Hashtable, TuiReply?>? handler = null,
        Func<TuiEvent, Hashtable, TuiReply?>? onTick = null,
        int tickInterval = 250,
        bool preview = false,
        string? directory = null,
        CancellationToken cancellationToken = default
    ) {
        _cancellationToken = cancellationToken;
        _document = document;
        _directory = directory ?? Environment.CurrentDirectory;
        document.Validate();
        _handler = preview ? null : handler;
        _onTick = preview ? null : onTick;
        _interval = TimeSpan.FromMilliseconds(tickInterval);
        _preview = preview;
        foreach (var pair in document.State) {
            State[pair.Key] = TuiDocument.Plain(pair.Value);
        }

        if (state is not null) {
            foreach (DictionaryEntry pair in state) {
                State[pair.Key] = pair.Value;
            }
        }

        var content = Build(document.Root);
        foreach (var (node, control) in _native.Where(entry => entry.Control is SearchReplacePopup)) {
            NativeTuiControls.Configure(control, node);
        }

        _toastHost = new ToastHost(content);
        var viewport = new ScrollViewer(_toastHost) {
            HorizontalScrollEnabled = false,
            VerticalScrollEnabled = content.VerticalAlignment != Align.Stretch,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        if (document.Title.Length > 0) {
            _chrome.Add(new TextBlock(document.Title));
            _chrome.Add(new Rule());
        }
        var footer = new TextBlock(preview ? "Preview sandbox · handlers are not executed" : "Tab / Shift-Tab focus · Esc back/cancel · F10 exit · F12 diagnostics");
        Root = new VStack([.. _chrome, viewport, footer]) {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch
        };
        _chrome.Add(footer);
        foreach (var (_, control) in _native.Where(entry => entry.Control is CommandPalette)) {
            foreach (var command in control.Commands.ToArray()) {
                control.Commands.Remove(command);
                Root.AddCommand(command);
            }
        }

        if (!preview) {
            Root.AddCommand(new Command {
                Id = "Fullscreen.Tui.Close",
                LabelMarkup = "Exit app",
                Gesture = new KeyGesture(TerminalKey.F10),
                Execute = _ => _closed = true
            });
        }

        Root.KeyDown((_, e) => {
            if (!_preview && !e.Handled && e.Key == TerminalKey.Escape) {
                _closed = true;
                e.Handled = true;
            }
        });
        _events.Enqueue(new("", "Start"));
    }

    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.ApplyTheme"/>
    public void ApplyTheme(Themes.ThemePalette theme) {
        _theme = theme;
        theme.Apply(Root);
        // Inactive tab content is detached, so theme the registered controls as well.
        foreach (var control in Controls.Values) {
            theme.Apply(control);
        }

        ApplyNodeStyles(_document.Root);
    }

    private void ApplyNodeStyles(TuiNode node) {
        var control = Controls[node.Id];
        if (node.Foreground.Length > 0 || node.Background.Length > 0 || node.Bold) {
            static Color ReadColor(
                string value,
                Color fallback
            ) =>
                value.Length == 0 ? fallback : Color.Rgb(
                    Convert.ToByte(value.Substring(1, 2), 16),
                    Convert.ToByte(value.Substring(3, 2), 16),
                    Convert.ToByte(value.Substring(5, 2), 16)
                );
            var foreground = ReadColor(node.Foreground, _theme.Foreground);
            var background = ReadColor(node.Background, _theme.Background);
            var basis = control.GetTheme();
            control.SetStyle(new XenoAtom.Terminal.UI.Styling.Theme {
                Foreground = foreground,
                Background = background,
                Surface = background,
                SurfaceAlt = basis.SurfaceAlt,
                PopupSurface = basis.PopupSurface,
                ControlFill = background,
                ControlFillHover = basis.ControlFillHover,
                ControlFillPressed = basis.ControlFillPressed,
                InputFill = background,
                InputFillFocused = basis.InputFillFocused,
                Border = basis.Border,
                FocusBorder = basis.FocusBorder,
                Accent = basis.Accent,
                Selection = basis.Selection,
                Disabled = basis.Disabled,
                Primary = basis.Primary,
                Success = basis.Success,
                Warning = basis.Warning,
                Error = basis.Error,
                Muted = basis.Muted
            });
            control.SetStyle(new XenoAtom.Terminal.UI.Styling.TextBlockStyle {
                Foreground = foreground,
                Background = background,
                FillBackground = node.Background.Length > 0,
                TextStyle = node.Bold ? XenoAtom.Terminal.UI.Styling.TextStyle.Bold : XenoAtom.Terminal.UI.Styling.TextStyle.None
            });
        }

        foreach (var child in node.Children) {
            ApplyNodeStyles(child);
        }
    }

    private Visual Build(TuiNode node) {
        object? value = node.Bind.Length != 0 && State.ContainsKey(node.Bind) ? State[node.Bind] : null;
        string text = value is null ? node.Text : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        Visual control;
        bool native = false;
        switch (node.Type) {
            case "VStack":
                control = new VStack([.. node.Children.Select(Build)]) {
                    Spacing = node.Spacing
                };
                break;
            case "HStack":
                control = new HStack([.. node.Children.Select(Build)]) {
                    Spacing = node.Spacing
                };
                break;
            case "Border":
                control = new Border(node.Children.Count == 0 ? new TextBlock("") : Build(node.Children[0]));
                break;
            case "Tabs":
                control = new TabControl([.. node.Children.Select(child => new TabPage(new TextBlock(child.TabTitle.Length == 0 ? child.Id : child.TabTitle), Build(child)))]);
                break;
            case "TextBox":
                control = new TextBox(text);
                break;
            case "TextArea":
                control = new TextArea(text) {
                    MinHeight = node.Height ?? 4,
                    MaxHeight = node.Height ?? 4,
                    AcceptTab = false
                };
                break;
            case "Button":
                control = new Button(node.Children.Count == 0 ? new TextBlock(node.Text) : Build(node.Children[0])).Click(() => {
                    if (_events.Count < 256) {
                        _events.Enqueue(new(node.Id, "Click", node.Close));
                    }
                });
                break;
            case "CheckBox":
                control = new CheckBox(node.Text, value is null ? node.Checked : Flag(value));
                break;
            case "Select":
                control = new Select<string>(node.Items, node.Items.Length == 0 ? -1 : Math.Max(0, Array.IndexOf(node.Items, text)));
                break;
            case "Progress":
                control = new ProgressBar(value is null ? node.Value : Math.Clamp(Number(value), 0, 1));
                break;
            case "Chart":
                if (IsChartCollection(value) || node.YProperty.Length > 0 || Charts.ChartTopology.Supports(Enum.Parse<Charts.GraphKind>(node.GraphKind, true))) {
                    var numericChart = new Charts.Chart {
                        Title = node.Text,
                        Minimum = node.Minimum,
                        Maximum = node.Maximum,
                        ReversalAmount = node.ReversalAmount,
                        FlameReverse = node.FlameReverse,
                        FlameFrameRows = node.FlameFrameRows,
                        GraphKind = Enum.Parse<Charts.GraphKind>(node.GraphKind, true),
                        ShowLegend = node.ShowLegend,
                        ShowTooltips = node.ShowTooltips,
                        ShowAxes = node.ShowAxes,
                        MinHeight = node.Height ?? 8
                    };
                    if (Charts.ChartTopology.Supports(numericChart.GraphKind)) {
                        ReplaceChartTopology(numericChart, node, value);
                    }
                    else if (numericChart.GraphKind == Charts.GraphKind.Gantt) {
                        ReplaceChartTasks(numericChart, node, value);
                    }
                    else if (numericChart.GraphKind == Charts.GraphKind.Flamegraph) {
                        ReplaceChartFrames(numericChart, node, value);
                    }
                    else if (node.YProperty.Length > 0) {
                        ReplaceChartObjectSeries(numericChart, node, value);
                    }
                    else {
                        ReplaceChartValues(numericChart, value);
                    }
                    control = numericChart;
                }
                else {
                    if (Charts.ChartMarket.Supports(Enum.Parse<Charts.GraphKind>(node.GraphKind, true))
                        || Charts.ChartStackedArea.Supports(Enum.Parse<Charts.GraphKind>(node.GraphKind, true))
                        || node.GraphKind.Equals("Flamegraph", StringComparison.OrdinalIgnoreCase) || node.GraphKind.Equals("Gantt", StringComparison.OrdinalIgnoreCase)) {
                        throw new ArgumentException("Marimekko/Kagi and stacked areas require ordered collections; Flamegraph/Gantt require frame/task records. These kinds cannot use the scalar history adapter.");
                    }
                    var chart = new Charts.HistoryChart {
                        Title = node.Text,
                        Maximum = node.Maximum ?? 0,
                        GraphKind = Enum.Parse<Charts.GraphKind>(node.GraphKind, true),
                        ShowLegend = node.ShowLegend,
                        ShowTooltips = node.ShowTooltips,
                        ShowAxes = node.ShowAxes,
                        MinHeight = node.Height ?? 8
                    };
                    chart.Add(value is null ? null : Number(value), DateTimeOffset.UtcNow);
                    control = chart;
                }

                break;
            case "Code":
                control = new CodeControl(text, node.Language ?? "markdown", forced: node.Language is not null);
                break;
            case "Image":
                control = new ImageControl(ImagePath(value is null ? node.Path : text), node.Text.Length == 0 ? null : node.Text);
                break;
            case "Spectre":
                var spectre = new SpectreView();
                spectre.SetContent(_preview ? null : value);
                control = spectre;
                break;
            case "FTable":
            case "FLogView":
                var table = new ReusableTable(node, value);
                table.Submitted += selected => Enqueue(node.Id, "Submit", selected);
                _tables.Add((node, table));
                control = table;
                break;
            case "ObjectExplorer":
                var explorer = new ReusableObjectExplorer(node, value);
                _explorers.Add((node, explorer));
                control = explorer;
                break;
            case "FileBrowser":
            case "FileCommander":
            case "PerformanceMonitor":
            case "UserList":
                var component = new TuiComponentHost(
                    node.Type,
                    () => node.Type == "FileCommander" ? new Files.CommanderApp(
                        Path.GetFullPath(node.Path.Length == 0 ? "." : node.Path, _directory),
                        Path.GetFullPath(
                            node.SecondaryPath.Length == 0 ? node.Path.Length == 0 ? "." : node.Path : node.SecondaryPath,
                            _directory
                        )
                    ) : node.Type == "FileBrowser" ? new Files.FileManagerApp(
                        Path.GetFullPath(node.Path.Length == 0 ? "." : node.Path, _directory),
                        minimal: node.Minimal
                    ) : new Monitoring.MonitorApp(
                        30,
                        node.RefreshInterval,
                        "",
                        Monitoring.ProcessSort.Cpu,
                        Enum.Parse<Monitoring.MonitorView>(node.View, true),
                        minimal: node.Minimal,
                        includeUserProcesses: node.IncludeProcesses,
                        usersOnly: node.Type == "UserList"
                    ) { GraphType = Enum.Parse<Charts.GraphKind>(node.GraphKind, true) },
                    _preview
                );
                component.Closed += selected => Enqueue(node.Id, "Close", selected);
                _components.Add((node, component));
                control = component;
                break;
            case "Rule":
                control = new Rule();
                break;
            default:
                native = NativeTuiControls.Contains(node.Type);
                control = native ? NativeTuiControls.Create(
                    node,
                    value,
                    Build,
                    ev => Enqueue(ev.Id, ev.Kind, ev.Value),
                    id => Controls.GetValueOrDefault(id)
                ) : new TextBlock(text);
                break;
        }

        if (node.Width is { } width) {
            control.MinWidth = width;
            control.MaxWidth = width;
        }

        if (node.Height is { } height) {
            control.MinHeight = height;
            control.MaxHeight = height;
        }

        control.IsEnabled = node.Enabled;
        control.HorizontalAlignment = Enum.Parse<Align>(node.HorizontalAlignment, true);
        control.VerticalAlignment = Enum.Parse<Align>(node.VerticalAlignment, true);
        if (control is Popup) {
            control.HorizontalAlignment = Align.Stretch;
            control.VerticalAlignment = Align.Stretch;
        }

        if (NativeTuiControls.Contains(node.Type)) {
            if (!native) {
                NativeTuiControls.Configure(control, node);
            }

            _native.Add((node, control));
        }

        Controls.Add(node.Id, control);
        if (control is Charts.Chart frameChart) {
            frameChart.SelectionChanged += selection => {
                object value = selection.Source ?? selection.Y;
                if (node.SelectionBind.Length > 0) {
                    State[node.SelectionBind] = value;
                }
                Enqueue(node.Id, "Selection", value);
            };
            frameChart.PointInvoked += selection => Enqueue(node.Id, "Invoke", selection.Source ?? selection.Y);
        }
        if (node.Bind.Length != 0) {
            var binding = new Binding(node, control) {
                Last = control is ReusableTable or ReusableObjectExplorer ? value : Read(control),
                LastEdges = node.EdgeBind.Length == 0 ? null : State[node.EdgeBind]
            };
            if (IsInput(control) && !State.ContainsKey(node.Bind)) {
                State[node.Bind] = binding.Last;
            }

            _bindings.Add(binding);
        }

        return NativeTuiControls.IsOverlay(control) ? new TextBlock("") {
            IsVisible = false
        }

        : control;
    }

    // PowerShell stores many values (Get-Random, pipeline output) as PSObject wrappers.
    private static double Number(object? value) =>
        Convert.ToDouble(
            value is System.Management.Automation.PSObject wrapped ? wrapped.BaseObject : value,
            CultureInfo.InvariantCulture
        );
    private static object? UnwrapChartValue(object? value) =>
        value is System.Management.Automation.PSObject wrapped ? wrapped.BaseObject : value;
    private static bool IsChartCollection(object? value) =>
        UnwrapChartValue(value) is IEnumerable and not string;
    private void ReplaceChartTopology(Charts.Chart chart, TuiNode node, object? value) {
        List<object> Rows(object? collection, string field) {
            if (_preview && collection is null) {
                return [];
            }
            if (UnwrapChartValue(collection) is not IEnumerable rows || collection is string) {
                throw new ArgumentException($"Relationship Chart requires separate node/edge collections; '{field}' is not a collection.");
            }
            var result = new List<object>();
            foreach (var row in rows) {
                result.Add(row ?? throw new ArgumentException($"Diagram '{field}' records cannot be null."));
            }
            return result;
        }
        var topology = Charts.ChartTopology.FromObjects(Rows(value, node.Bind), Rows(State[node.EdgeBind], node.EdgeBind));
        if (_diagramSnapshots.TryGetValue(chart, out var previous)
            && previous.Nodes.AsSpan().SequenceEqual(topology.Nodes) && previous.Edges.AsSpan().SequenceEqual(topology.Edges)) {
            return;
        }
        _ = topology.Layout(chart.GraphKind);
        chart.SetData(topology.Data);
        _diagramSnapshots[chart] = topology;
    }
    private void ReplaceChartTasks(Charts.Chart chart, TuiNode node, object? value) {
        if (UnwrapChartValue(value) is not IEnumerable values || value is string) {
            throw new ArgumentException("Gantt requires a collection of absolute task intervals.");
        }
        var rows = new List<System.Management.Automation.PSObject>();
        var sources = new List<object>();
        foreach (var item in values) {
            rows.Add(item is null ? throw new ArgumentException("Gantt task rows cannot be null.") : System.Management.Automation.PSObject.AsPSObject(item));
            sources.Add(item);
        }
        var tasks = rows.Count == 0 ? [] : new Charts.ObjectChartData(rows).ReadTasks(
            node.StartProperty, node.EndProperty, node.TaskNameProperty,
            node.ProgressProperty.Length == 0 ? null : node.ProgressProperty, [.. Enumerable.Range(0, rows.Count)], sources);
        if (_taskSnapshots.TryGetValue(chart, out var previous) && previous.AsSpan().SequenceEqual(tasks)) {
            return;
        }
        chart.SetData(new Charts.ChartSchedule(tasks).Data);
        _taskSnapshots[chart] = tasks;
    }
    private void ReplaceChartFrames(Charts.Chart chart, TuiNode node, object? value) {
        if (UnwrapChartValue(value) is not IEnumerable values || value is string) {
            throw new ArgumentException("Flamegraph requires a collection of FrameId/ParentId/self-weight records.");
        }
        var rows = new List<System.Management.Automation.PSObject>();
        var sources = new List<object>();
        foreach (var item in values) {
            if (item is null) {
                throw new ArgumentException("Flamegraph frame rows cannot be null.");
            }
            rows.Add(System.Management.Automation.PSObject.AsPSObject(item));
            sources.Add(item);
        }
        var data = rows.Count == 0 ? null : new Charts.ObjectChartData(rows);
        var frames = data is null ? [] : data.ReadFrames(node.FrameIdProperty, node.ParentIdProperty,
            node.FrameNameProperty.Length == 0 ? null : node.FrameNameProperty,
            data.Resolve(node.WeightProperty), [.. Enumerable.Range(0, rows.Count)], sources);
        if (_frameSnapshots.TryGetValue(chart, out var previous) && previous.AsSpan().SequenceEqual(frames)) {
            return;
        }
        chart.SetData(new Charts.ChartFlameTree(frames).Data);
        _frameSnapshots[chart] = frames;
    }
    private static void ReplaceChartValues(Charts.Chart chart, object? value) {
        var samples = new List<double>();
        if (UnwrapChartValue(value) is IEnumerable values and not string) {
            foreach (var item in values) {
                samples.Add(UnwrapChartValue(item) is null ? double.NaN : Number(item));
            }
        }
        else {
            samples.Add(value is null ? double.NaN : Number(value));
        }
        if (chart.Values.SequenceEqual(samples)) {
            return;
        }
        chart.Values.Clear();
        chart.Values.AddRange(samples);
    }

    private void ReplaceChartObjectSeries(Charts.Chart chart, TuiNode node, object? value) {
        if (_preview && value is null) {
            chart.SetData([.. node.YProperty.Select(name => new Charts.ChartDataSeries(name, []))]);
            return;
        }
        if (UnwrapChartValue(value) is not IEnumerable values || value is string) {
            throw new ArgumentException("Chart YProperty requires a collection of numeric object rows.");
        }
        var sources = new List<object>();
        var rows = new List<System.Management.Automation.PSObject>();
        foreach (var item in values) {
            if (item is null) {
                throw new ArgumentException("Chart object-series rows cannot be null.");
            }
            if (rows.Count >= 1_000_000) {
                throw new ArgumentException("Chart object-series binding supports at most 1,000,000 rows.");
            }
            sources.Add(item);
            rows.Add(System.Management.Automation.PSObject.AsPSObject(item));
        }
        var projection = rows.Count == 0 ? null : new Charts.ObjectChartData(rows);
        var data = projection is null
            ? [.. node.YProperty.Select(name => new Charts.ChartDataSeries(name, []))]
            : projection.Project(node.XProperty.Length == 0 ? null : node.XProperty, node.YProperty);
        projection?.ApplyErrorBounds(data, node.LowerProperty.Length == 0 ? null : node.LowerProperty,
            node.UpperProperty.Length == 0 ? null : node.UpperProperty);
        if (projection is not null && !_objectSeriesSnapshots.ContainsKey(chart)) {
            chart.XAxis = chart.XAxis with { Scale = projection.ScaleFor(node.XProperty.Length == 0 ? null : node.XProperty) };
        }
        for (int series = 0; series < data.Length; series++) {
            data[series].SourceIndex = series;
            data[series].Role = node.SeriesRole.Length == 0 ? Charts.ChartSeriesRole.Auto
                : Enum.Parse<Charts.ChartSeriesRole>(node.SeriesRole[series], true);
            for (int index = 0; index < data[series].Points.Length; index++) {
                data[series].Points[index] = data[series].Points[index] with { Source = sources[index] };
            }
        }
        var previous = _objectSeriesSnapshots.GetValueOrDefault(chart) ?? [];
        bool unchanged = previous.Length == data.Length;
        for (int series = 0; series < data.Length; series++) {
            if (series >= previous.Length || previous[series].Property != data[series].Property) {
                unchanged = false;
                continue;
            }
            data[series].Visible = previous[series].Visible;
            data[series].Color = previous[series].Color;
            data[series].LineStyle = previous[series].LineStyle;
            data[series].Marker = previous[series].Marker;
            // Retain user-edited presentation when the sampled source rows change.
            data[series].Role = previous[series].Role;
            unchanged &= previous[series].Span.SequenceEqual(data[series].Span);
        }
        if (unchanged) {
            return;
        }
        chart.SetData(data);
        _objectSeriesSnapshots[chart] = data;
    }

    private static bool Flag(object? value) =>
        Convert.ToBoolean(
            value is System.Management.Automation.PSObject wrapped ? wrapped.BaseObject : value,
            CultureInfo.InvariantCulture
        );
    private string ImagePath(string path) =>
        path.Length == 0 ? "" : Path.GetFullPath(path, _directory);
    private static bool IsInput(Visual control) =>
        control is TextBox or TextArea or CheckBox or Select<string> || NativeTuiControls.IsInput(control);
    private static object? Read(Visual control) =>
        control switch {
            TextBox input => input.Text,
            TextArea area => area.Text,
            CheckBox check => check.IsChecked,
            Select<string> select => select.SelectedIndex >= 0 && select.SelectedIndex < select.Items.Count ? select.Items[select.SelectedIndex] : "",
            TextBlock text => text.Text,
            CodeControl code => code.Text,
            ImageControl image => image.Path,
            ProgressBar bar => bar.Value,
            _ => NativeTuiControls.Read(control)
        };
    private void Capture() {
        foreach (var (node, control) in _native) {
            if (node.SelectionBind.Length > 0) {
                CaptureSelection(node, NativeTuiControls.Selection(control));
            }
        }

        foreach (var (node, explorer) in _explorers) {
            CaptureSelection(node, explorer.Selection);
        }

        foreach (var entry in _tables) {
            CaptureSelection(entry.Node, entry.Table.Selection);
        }

        foreach (var (Node, Host) in _components) {
            if (Host.Running) {
                CaptureSelection(Node, Host.Selection);
            }
        }

        foreach (var binding in _bindings) {
            if (IsInput(binding.Control)) {
                object? value = Read(binding.Control);
                if (!Equals(value, binding.Last)) {
                    State[binding.Node.Bind] = value;
                    binding.Last = value;
                    if (_events.Count < 256) {
                        _events.Enqueue(new(binding.Node.Id, "Change", value));
                    }
                }
            }
        }
    }

    private void Enqueue(string id, string kind, object? value) {
        if (_events.Count < 256) {
            _events.Enqueue(new(id, kind, value));
        }
    }

    private void CaptureSelection(TuiNode node, object? selected) {
        if (_selections.TryGetValue(node.Id, out var oldSelection) && selected is Monitoring.UserViewRow user
            && oldSelection is Monitoring.UserViewRow previousUser
            && user.Key == previousUser.Key) {
            _selections[node.Id] = selected;
            if (node.SelectionBind.Length > 0) {
                State[node.SelectionBind] = selected;
            }

            return;
        }

        if (_selections.TryGetValue(node.Id, out var previous)
            && (ReferenceEquals(previous, selected)
            || selected is string text && previous is string old && text == old
            || selected is string[] selectedItems && previous is string[] previousItems
            && selectedItems.SequenceEqual(previousItems))) {
            return;
        }

        _selections[node.Id] = selected;
        if (node.SelectionBind.Length > 0) {
            State[node.SelectionBind] = selected;
        }

        if (selected is not null || previous is not null) {
            Enqueue(node.Id, "Selection", selected);
        }
    }

    private void ApplyState() {
        foreach (var binding in _bindings) {
            if (!State.ContainsKey(binding.Node.Bind)) {
                continue;
            }

            object? value = State[binding.Node.Bind];
            if (binding.Control is Charts.Chart or SpectreView
                && Root.App is not null
                && !TuiComponentHost.IsVisibleWithin(binding.Control, Root)) {
                binding.Deferred = true;
                continue;
            }
            if (binding.Control is SpectreView spectre) {
                if (!_preview && (_chartSampleDue || binding.Deferred || !ReferenceEquals(value, binding.Last))) {
                    spectre.SetContent(value);
                }

                binding.Deferred = false;
                binding.Last = value;
                continue;
            }

            if (binding.Control is Charts.HistoryChart chart) {
                if (_chartSampleDue) {
                    chart.Add(value is null ? null : Number(value), DateTimeOffset.UtcNow);
                }

                binding.Last = value;
                continue;
            }

            if (binding.Control is Charts.Chart numericChart) {
                object? edges = binding.Node.EdgeBind.Length == 0 ? null : State[binding.Node.EdgeBind];
                if (_chartSampleDue || binding.Deferred || !ReferenceEquals(value, binding.Last) || !ReferenceEquals(edges, binding.LastEdges)) {
                    if (Charts.ChartTopology.Supports(numericChart.GraphKind)) {
                        ReplaceChartTopology(numericChart, binding.Node, value);
                    }
                    else if (numericChart.GraphKind == Charts.GraphKind.Gantt) {
                        ReplaceChartTasks(numericChart, binding.Node, value);
                    }
                    else if (numericChart.GraphKind == Charts.GraphKind.Flamegraph) {
                        ReplaceChartFrames(numericChart, binding.Node, value);
                    }
                    else if (binding.Node.YProperty.Length > 0) {
                        ReplaceChartObjectSeries(numericChart, binding.Node, value);
                    }
                    else {
                        ReplaceChartValues(numericChart, value);
                    }
                }

                binding.Last = value;
                binding.LastEdges = edges;
                binding.Deferred = false;
                continue;
            }

            if (Equals(value, binding.Last)) {
                continue;
            }

            if (binding.Control is ReusableTable table) {
                table.Replace(value);
                binding.Last = value;
                continue;
            }

            if (binding.Control is ReusableObjectExplorer explorer) {
                explorer.Replace(value);
                binding.Last = value;
                continue;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            switch (binding.Control) {
                case TextBox input:
                    input.Text = text;
                    break;
                case TextArea area:
                    area.Text = text;
                    break;
                case TextBlock label:
                    label.Text = text;
                    break;
                case CodeControl code:
                    code.Text = text;
                    break;
                case ImageControl image:
                    image.Path = ImagePath(text);
                    break;
                case CheckBox check:
                    check.IsChecked = Flag(value);
                    break;
                case Select<string> select:
                    select.SelectedIndex = Array.IndexOf(binding.Node.Items, text);
                    break;
                case ProgressBar bar:
                    bar.Value = Math.Clamp(Number(value), 0, 1);
                    break;
                default:
                    if (NativeTuiControls.Contains(binding.Node.Type)) {
                        NativeTuiControls.Update(binding.Control, binding.Node, value);
                    }

                    break;
            }

            binding.Last = IsInput(binding.Control) ? Read(binding.Control) : value;
            if (IsInput(binding.Control)) {
                State[binding.Node.Bind] = binding.Last;
            }
        }
    }

    private void Reply(TuiReply? reply) {
        if (reply is null) {
            return;
        }

        if (reply.State is not null) {
            foreach (DictionaryEntry pair in reply.State) {
                State[pair.Key] = pair.Value;
            }
        }

        if (reply.Close && !_preview) {
            _closed = true;
            Cancelled = false;
            Value = reply.Value;
        }
    }

    private bool _chartSampleDue;
    /// <inheritdoc cref="fullscreen.Hosting.IFullscreenApp.Tick"/>
    public bool Tick() {
        foreach (var (_, Host) in _components) {
            Host.Tick(Root);
        }

        foreach (var entry in _tables) {
            entry.Table.Tick(Root, _cancellationToken);
        }

        foreach (var (_, explorer) in _explorers) {
            explorer.Tick(Root, _cancellationToken);
        }

        Capture();
        for (int i = 0; i < 32 && !_closed && _events.TryDequeue(out var ev); i++) {
            LastEvent = ev.Id;
            Reply(_handler?.Invoke(ev, State));
            if (ev.Kind == "Click" && ev.Value is true && !_preview) {
                _closed = true;
                Cancelled = false;
            }
        }

        _chartSampleDue = !_closed && Stopwatch.GetElapsedTime(_lastTick) >= _interval;
        if (_chartSampleDue) {
            _lastTick = Stopwatch.GetTimestamp();
            Reply(_onTick?.Invoke(new("", "Tick"), State));
        }

        ApplyState();
        SyncNativeOverlays();
        return !_closed;
    }

    /// <inheritdoc/>
    public void Dispose() {
        CloseNativeOverlays();
        _closed = true;
        _events.Clear();
        List<Exception>? errors = null;
        foreach (var (_, control) in _native) {
            NativeTuiControls.Dispose(control);
        }

        foreach (var (_, explorer) in _explorers) {
            explorer.Dispose();
        }

        foreach (var (Node, Host) in _components) {
            try {
                Host.Dispose();
            }
            catch (Exception error) {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null) {
            throw new AggregateException("Component cleanup failed.", errors);
        }
    }
}
