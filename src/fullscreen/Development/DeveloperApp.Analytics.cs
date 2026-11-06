using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using fullscreen.Charts;

namespace fullscreen.Development;

public sealed partial class DeveloperApp {
    private string? _reviewSprint;
    private bool _analyticsLast30;
    private string? _analyticsBoard;

    private void LoadBoardDashboard(AzureDevOpsBackend backend) {
        Busy = true;
        long version = Interlocked.Increment(ref _version);
        Queue(new(version, async token => {
            SprintReview? review = null;
            string? failure = null;
            try {
                review = await backend.LoadSprintReviewAsync(null, token, currentOnly: true).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException) {
                failure = error.Message;
            }
            return () => {
                Busy = false;
                _dashboard.Children.Clear();
                var content = new VStack(new TextBlock("TEAM DASHBOARD · current sprint · not affected by list or assignee filters") { Wrap = true }) { Spacing = 1 };
                if (review is not null && review.Path.Length > 0) {
                    content.Add(new TextBlock($"{review.Name} · {review.Total} in scope · {review.Completed} completed · {review.Total - review.Completed} not completed") { Wrap = true });
                    if (review.States.Length > 0) {
                        content.Add(SummaryChart("Current sprint by state", review.States.Select(state => (state.State, (double)state.Count)), 10));
                    }
                    content.Add(new TextBlock(string.Join(" · ", review.States.Select(state => state.State + ": " + state.Count))) { Wrap = true });
                }
                else {
                    content.Add(new TextBlock(failure is null ? "No current sprint configured. Use Y Boards analytics for Last 30 days." : "Dashboard unavailable: " + failure) { Wrap = true });
                }
                content.Add(new TextBlock("Above: backlog levels for one team, not multiple teams. Enter opens the selected level's cards.\nWork items / Stories / Tasks default to the current sprint; I → All sprints clears it. Backlog is always all sprints. Y opens historical Analytics.") { Wrap = true });
                _dashboard.Add(new ScrollViewer(content) { HorizontalScrollEnabled = false });
                Surface.Status = _summary;
                Surface.Refresh();
            };
        }));
    }

    private Chart SummaryChart(string title, IEnumerable<(string Label, double Value)> data, int height = 14) {
        var rows = data.ToArray();
        var chart = new Chart(rows.Select(row => row.Value)) {
            Title = title,
            GraphKind = GraphKind.HorizontalBars,
            MinHeight = height,
            MaxHeight = height,
            Minimum = 0,
            PreferTerminalCells = true
        };
        foreach (var row in rows) {
            chart.Labels.Add(row.Label);
        }
        Surface.Palette.Apply(chart);
        return chart;
    }

    private void LoadBoardsAnalytics() {
        if (Busy || _backend is not AzureDevOpsBackend backend) {
            return;
        }
        Busy = true;
        _error = false;
        long version = Interlocked.Increment(ref _version);
        Surface.Status = "Loading Azure Boards Analytics · daily snapshots…";
        Surface.Refresh();
        string? board = _analyticsBoard ?? (View == 0 && backend.BoardLevelLabel.Length > 0 ? backend.BoardLevelLabel : null);
        Queue(new(version, async token => {
            var analytics = await backend.LoadBoardsAnalyticsAsync(_analyticsLast30, board, token).ConfigureAwait(false);
            return () => {
                Busy = false;
                ShowBoardsAnalytics(analytics);
            };
        }));
    }

    private void ShowBoardsAnalytics(BoardsAnalytics analytics) {
        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
            Surface.Status = _summary;
            Surface.Refresh();
        }
        void Reload(bool last30) {
            Close();
            _analyticsLast30 = last30;
            LoadBoardsAnalytics();
        }
        var content = new VStack(new WrapHStack(
            AzureButton("Back · Esc", Close),
            AzureButton("Current sprint", () => Reload(false)),
            AzureButton("Last 30 days", () => Reload(true)),
            AzureButton("Choose board level", () => ContextMenuService.Show(dialog!,
                [.. analytics.Boards.Select(name => new MenuItem(name, () => { Close(); _analyticsBoard = name; LoadBoardsAnalytics(); }))],
                dialog!.Bounds.X + 2, dialog.Bounds.Y + 2)),
            AzureButton("Refresh", () => Reload(_analyticsLast30))
        ) { Spacing = 1, RunSpacing = 1 },
            new TextBlock(analytics.Board + " · " + analytics.Range) { Wrap = true }) { Spacing = 1 };
        if (analytics.Flow.Length > 0) {
            var dates = analytics.Flow.Select(row => row.Date).Distinct().Order().ToArray();
            var chart = new Chart { Title = "Cumulative flow · daily board column counts", GraphKind = GraphKind.StackedArea,
                MinHeight = 18, MaxHeight = 18, Maximum = null, Minimum = 0, ShowLegend = true, PreferTerminalCells = true };
            foreach (var column in analytics.Flow.Select(row => row.Column).Distinct()) {
                var indexed = analytics.Flow.Where(row => row.Column == column).GroupBy(row => row.Date).ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
                var series = new ChartSeries(column, dates.Select(date => (double)indexed.GetValueOrDefault(date)));
                foreach (var date in dates) {
                    series.Labels.Add(date.ToString("MM-dd", CultureInfo.InvariantCulture));
                }
                chart.Series.Add(series);
            }
            Surface.Palette.Apply(chart);
            content.Add(chart);
            content.Add(new TextBlock("Latest snapshot · " + dates[^1] + " · " + string.Join(" · ", analytics.Flow.Where(row => row.Date == dates[^1]).Select(row => row.Column + ": " + row.Count))) { Wrap = true });
        }
        else {
            content.Add(new TextBlock("No board snapshots in this range. Analytics may not yet have collected this board's data.") { Wrap = true });
        }
        if (analytics.Velocity.Length > 0) {
            var chart = new Chart { Title = "Velocity · count of work items · last eligible six sprints", GraphKind = GraphKind.Bars,
                MinHeight = 16, MaxHeight = 16, Minimum = 0, ShowLegend = true, PreferTerminalCells = true };
            foreach (var (name, values) in new[] {
                ("Planned (first day)", analytics.Velocity.Select(sprint => (double)sprint.Planned)),
                ("Completed", analytics.Velocity.Select(sprint => (double)sprint.Completed)),
                ("Incomplete", analytics.Velocity.Select(sprint => (double)sprint.Incomplete))
            }) {
                var series = new ChartSeries(name, values);
                foreach (var sprint in analytics.Velocity) {
                    series.Labels.Add(sprint.Name);
                }
                chart.Series.Add(series);
            }
            Surface.Palette.Apply(chart);
            content.Add(chart);
            content.Add(new TextBlock(string.Join("\n", analytics.Velocity.Select(sprint => $"{sprint.Name}: planned {sprint.Planned}, completed {sprint.Completed}, incomplete {sprint.Incomplete}"))) { Wrap = true });
        }
        else {
            content.Add(new TextBlock("Velocity unavailable: no eligible sprint-start snapshots in this range.") { Wrap = true });
        }
        AddCapacity(content, analytics.Capacity);
        content.Add(new TextBlock(analytics.Notice) { Wrap = true });
        dialog = new Dialog {
            Title = new TextBlock("AZURE BOARDS ANALYTICS"),
            IsModal = true,
            Width = Math.Max(1, Root.Bounds.Width - 4),
            Height = Math.Max(1, Root.Bounds.Height - 2),
            Content = new ScrollViewer(new Border(content)) { HorizontalScrollEnabled = false }
        };
        dialog.KeyDown((_, e) => { if (e.Key == TerminalKey.Escape) { Close(); e.Handled = true; } });
        _expanded = true;
        dialog.Show();
    }

    private void AddCapacity(VStack content, CapacityReview capacity) {
        if (capacity.Hours is { } hours) {
            content.Add(SummaryChart("Sprint · current estimated hours vs team capacity",
                new[] { ("Team capacity (h)", hours) }.Concat(capacity.Planned.Where(type => type.Reported > 0)
                    .Select(type => (type.Type + " estimates (h)", type.Hours)))));
            content.Add(new TextBlock(FormattableString.Invariant($"Team capacity: {hours:0.##} h") + "\n" +
                string.Join("\n", capacity.Planned.Select(type => FormattableString.Invariant($"{type.Type}: {type.Hours:0.##} estimated h · {type.Reported}/{type.Total} have OriginalEstimate")))) { Wrap = true });
        }
        content.Add(new TextBlock(capacity.Notice) { Wrap = true });
    }

    private void LoadSprintReview(AzureDevOpsBackend backend) {
        _error = false;
        _loadingDetailKey = null;
        Busy = true;
        long version = Interlocked.Increment(ref _version);
        Surface.Status = "Loading team sprint review · all work-item types and pages…";
        Surface.Refresh();
        Queue(new(version, async token => {
            var review = await backend.LoadSprintReviewAsync(_reviewSprint, token).ConfigureAwait(false);
            var capacity = await backend.LoadCapacityAsync(review.Path, token).ConfigureAwait(false);
            return () => {
                Busy = false;
                ShowSprintReview(review, backend, capacity);
            };
        }));
    }

    private void ShowSprintReview(SprintReview review, AzureDevOpsBackend backend, CapacityReview capacity) {
        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
            Surface.Status = _summary;
            Surface.Refresh();
        }
        var choose = AzureButton("Choose sprint", () => {
            var options = review.Sprints.Select(sprint => new MenuItem(sprint.Name + " · " + sprint.Path, () => {
                dialog!.Close();
                _expanded = false;
                _reviewSprint = sprint.Path;
                LoadSprintReview(backend);
            })).ToArray();
            if (options.Length > 0) {
                ContextMenuService.Show(dialog!, options, dialog!.Bounds.X + 2, dialog.Bounds.Y + 2);
            }
        });
        choose.IsEnabled = review.Sprints.Length > 0;
        var toolbar = new WrapHStack(
            AzureButton("Back to list · Esc", Close),
            choose,
            AzureButton("Refresh review", () => { Close(); LoadSprintReview(backend); })
        ) { Spacing = 1, RunSpacing = 1 };
        var content = new VStack(toolbar,
            new TextBlock(review.Path.Length == 0 ? "No current sprint. Choose a configured team sprint to review." : review.Path) { Wrap = true },
            new TextBlock("TEAM SPRINT SNAPSHOT · all work-item types and pages; ignores list filters and assignee filters.") { Wrap = true }
        ) { Spacing = 1 };
        if (review.Path.Length > 0) {
            content.Add(new TextBlock("As of · " + review.AsOf) { Wrap = true });
            content.Add(new TextBlock("Dates · " + (review.Start.Length == 0 ? "not configured" : review.Start) + " – " +
                (review.Finish.Length == 0 ? "not configured" : review.Finish)) { Wrap = true });
            content.Add(new TextBlock(FormattableString.Invariant(
                $"IN SCOPE {review.Total}    COMPLETED {review.Completed}    NOT COMPLETED {review.Total - review.Completed}    REMOVED STATE {review.Removed}")) { Wrap = true });
            if (review.Unknown > 0) {
                content.Add(new TextBlock(review.Unknown.ToString(CultureInfo.InvariantCulture) + " items have unknown workflow categories and are counted as not completed.") { Wrap = true });
            }
            if (review.States.Length > 0) {
                var chart = SummaryChart("Sprint work by current state", review.States.Select(state => (state.State, (double)state.Count)),
                    Math.Clamp(review.States.Length * 2 + 3, 10, 25));
                content.Add(chart);
                content.Add(new TextBlock(string.Join("   ", review.States.Select(state => state.State + " · " + state.Count))) { Wrap = true });
            }
            else {
                content.Add(new TextBlock("No work items are currently assigned to this team's sprint.") { Wrap = true });
            }
            content.Add(new TextBlock("ESTIMATES & HOURS · kept separate by work-item type; coverage shows missing values.") { Wrap = true });
            content.Add(new TextBlock(review.Measures.Length > 0 ? string.Join("\n", review.Measures) : "No estimates or hours reported.") { Wrap = true });
            foreach (var metric in review.Metrics ?? []) {
                content.Add(SummaryChart(metric.Type + " · " + metric.Unit,
                    [("Completed", metric.Completed), ("Not completed", metric.Remaining)], 8));
            }
            AddCapacity(content, capacity);
        }
        if (review.Sprints.Length == 0) {
            content.Add(new TextBlock("No team iterations configured. Configure sprint iterations in Azure DevOps and refresh.") { Wrap = true });
        }
        content.Add(new TextBlock("Current snapshot, not historical velocity or burndown. Scope added/removed since sprint start and items moved out of the sprint require historical Analytics data.") { Wrap = true });
        dialog = new Dialog {
            Title = new TextBlock("SPRINT REVIEW · " + review.Name),
            IsModal = true,
            Width = Math.Max(1, Surface.Bounds.Width - 4),
            Height = Math.Max(1, Surface.Bounds.Height - 2),
            Content = new ScrollViewer(new Border(content)) { HorizontalScrollEnabled = false }
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                Close();
                e.Handled = true;
            }
        });
        _expanded = true;
        dialog.Show();
    }
}
