using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Development;

public sealed partial class DeveloperApp {
    private string? _reviewSprint;

    private void LoadSprintReview(AzureDevOpsBackend backend) {
        _error = false;
        _loadingDetailKey = null;
        Busy = true;
        long version = Interlocked.Increment(ref _version);
        Surface.Status = "Loading team sprint review · all work-item types and pages…";
        Surface.Refresh();
        Queue(new(version, async token => {
            var review = await backend.LoadSprintReviewAsync(_reviewSprint, token).ConfigureAwait(false);
            return () => {
                Busy = false;
                ShowSprintReview(review, backend);
            };
        }));
    }

    private void ShowSprintReview(SprintReview review, AzureDevOpsBackend backend) {
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
                var chart = new Charts.Chart(review.States.Select(state => (double)state.Count)) {
                    Title = "Sprint work by current state",
                    GraphKind = Charts.GraphKind.HorizontalBars,
                    MinHeight = Math.Clamp(review.States.Length * 2 + 3, 10, 25),
                    MaxHeight = 25,
                    Minimum = 0
                };
                foreach (var state in review.States) {
                    chart.Labels.Add(state.State);
                }
                Surface.Palette.Apply(chart);
                content.Add(chart);
                content.Add(new TextBlock(string.Join("   ", review.States.Select(state => state.State + " · " + state.Count))) { Wrap = true });
            }
            else {
                content.Add(new TextBlock("No work items are currently assigned to this team's sprint.") { Wrap = true });
            }
            content.Add(new TextBlock("ESTIMATES & HOURS · kept separate by work-item type; coverage shows missing values.") { Wrap = true });
            content.Add(new TextBlock(review.Measures.Length > 0 ? string.Join("\n", review.Measures) : "No estimates or hours reported.") { Wrap = true });
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
