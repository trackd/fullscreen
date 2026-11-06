using System.Text.RegularExpressions;
using System.Text.Json;
using fullscreen.Files;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Text;

namespace fullscreen.Development;

public sealed partial class DeveloperApp {
    private readonly Stack<DeveloperItem> _itemHistory = new();
    private bool _analyticsRequested;

    private void UpdateAzureCommands() {
        Surface.PriorityShortcuts = ["k", "K", "v", "V", "u", "U", "y", "Y", .. View is 1 or 2 or 3 ? new[] { "i", "I" } : [],
            .. View is 0 or 6 ? new[] { "l", "L" } : [], .. View == 1 ? new[] { "t", "T" } : []];
        Surface.FooterHelp = "Q close · " + (View == 5 ? "Enter monitor" : "Enter details") + " · / filter · C columns · S sort · G group · [/] pages";
        Surface.ExtraHelp = "A actions · V preview · F5 refresh" +
            (AzureDevOpsBackend.IsWorkItemView(View) ? " · K kanban · M filter assignee" : "") +
            (View is 1 or 2 or 3 ? " · I sprint" : "");
        var commands = new List<(string, Action)> {
            ("A Actions", () => ShowActions(Surface.Bounds.X + 1, Surface.Bounds.Y + 3)),
            ("V Preview " + (_previewEnabled ? "☑" : "☐"), TogglePreview)
        };
        if (AzureDevOpsBackend.IsWorkItemView(View)) {
            commands.Add((_backend is AzureDevOpsBackend { MineOnly: true } ? "M Filter: everyone" : "M Filter: mine",
                () => { if (!Busy) { Execute(_backend.Operations(View, null).First(), ""); } }));
            if (View != 1 || _backend is not AzureDevOpsBackend { AllWorkItemTypes: true }) {
                commands.Add(("K Kanban", ShowKanban));
            }
        }
        if (View is 1 or 2 or 3) {
            commands.Add(("I Sprint: " + ((AzureDevOpsBackend)_backend).SprintLabel, ShowSprints));
        }
        if (View == 0) {
            commands.Add(("L Board level: " + ((AzureDevOpsBackend)_backend).BoardLevelLabel, ShowBoardLevels));
        }
        if (View == 1) {
            var ado = (AzureDevOpsBackend)_backend;
            commands.Add(("T Type: " + (ado.BoardOpen ? "Board mappings" : ado.WorkItemTypeLabel), ShowWorkItemTypes));
        }
        if (View == 6) {
            commands.Add(("L Backlog level: " + ((AzureDevOpsBackend)_backend).BacklogLabel, ShowBacklogLevels));
        }
        if (AzureDevOpsBackend.IsWorkItemView(View) || View == 5) {
            commands.Add((View == 5 ? "U Run summary" : "U Sprint review", ShowAnalytics));
        }
        if (View == 5) {
            commands.Add(("Enter Monitor run", RequestPipelineMonitor));
        }
        if (View != 4 && View != 5) {
            commands.Add(("Y Boards analytics", () => LoadBoardsAnalytics()));
        }
        commands.Add(("F5 Refresh", () => { if (!Busy) { Load(); } }));
        Surface.ToolbarCommands = [.. commands];
        Surface.Refresh();
    }

    private void TogglePreview() {
        _previewEnabled = !_previewEnabled;
        Surface.SetPreview("DETAILS · Enter to open", _previewEnabled ? _detail : null);
        UpdateAzureCommands();
    }

    private void ShowSprints() {
        if (Busy || _backend is not AzureDevOpsBackend ado || View is not (1 or 2 or 3)) {
            return;
        }
        var options = ado.SprintOperations(View).Select(operation =>
            new MenuItem(operation.Label, () => Execute(operation, ""))).ToArray();
        ContextMenuService.Show(Surface, options, Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
    }

    private void ShowBacklogLevels() {
        if (Busy || _backend is not AzureDevOpsBackend ado || View != 6) {
            return;
        }
        var options = ado.BacklogOperations().Select(operation =>
            new MenuItem(operation.Label, () => Execute(operation, ""))).ToArray();
        if (options.Length == 0) {
            Surface.Status = "No backlog levels configured for this team.";
            Surface.Refresh();
            return;
        }
        ContextMenuService.Show(Surface, options, Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
    }

    private void ShowBoardLevels() {
        if (Busy || _backend is not AzureDevOpsBackend ado || View != 0) {
            return;
        }
        var options = ado.BoardLevelOperations().Select(operation =>
            new MenuItem(operation.Label, () => Execute(operation, ""))).ToArray();
        if (options.Length == 0) {
            Surface.Status = "No board levels available for this team.";
            Surface.Refresh();
            return;
        }
        ContextMenuService.Show(Surface, options, Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
    }

    private void ShowWorkItemTypes() {
        if (Busy || _backend is not AzureDevOpsBackend ado || View != 1) {
            return;
        }
        var options = ado.WorkItemTypeOperations().Select(operation =>
            new MenuItem(operation.Label, () => Execute(operation, ""))).ToArray();
        ContextMenuService.Show(Surface, options, Surface.Bounds.X + 2, Surface.Bounds.Y + 3);
    }

    private void ShowAnalytics() {
        if (Busy) {
            _analyticsRequested = true;
            Surface.Status = (View == 5 ? "Run summary" : "Sprint review") + " will open when the current request finishes.";
            Surface.Refresh();
            return;
        }
        _analyticsRequested = false;
        if (View != 5 && _backend is AzureDevOpsBackend ado) {
            LoadSprintReview(ado);
            return;
        }
        var counts = Surface.Model.Visible.Select(row => (DeveloperItem)row.Original)
            .GroupBy(item => item.State is JsonElement build
                ? (build.TryGetProperty("result", out var result) && result.GetString() is { Length: > 0 } value ? value : "In progress")
                : "Unknown", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => (Label: group.Key, Count: group.Count())).ToArray();
        if (counts.Length == 0) {
            Surface.Status = "No items on this filtered page to chart.";
            Surface.Refresh();
            return;
        }
        var chart = SummaryChart("Pipeline runs by result", counts.Select(count => (count.Label, (double)count.Count)),
            Math.Clamp(counts.Length * 3 + 4, 10, 25));
        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
            Surface.Status = _summary;
            Surface.Refresh();
        }
        var content = new VStack(AzureButton("Back to list · Esc", Close),
            new TextBlock("CURRENT FILTERED PAGE ONLY · " + Surface.Model.Visible.Count + " items; not historical trends.") { Wrap = true },
            chart,
            new TextBlock(string.Join("\n", counts.Select(count => count.Label + " · " + count.Count))) { Wrap = true });
        dialog = new Dialog {
            Title = new TextBlock("RUN SUMMARY · page " + (_page + 1)),
            IsModal = true,
            Width = Math.Max(1, Surface.Bounds.Width - 4),
            Height = Math.Max(1, Surface.Bounds.Height - 2),
            Content = new ScrollViewer(content)
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

    private void ShowExpanded(DeveloperItem item, string text) {
        if (_backend is GitBackend) {
            _expanded = false;
            Surface.ShowCodeDetail(text, "change.diff", true);
            return;
        }

        if (_backend is not AzureDevOpsBackend ado) {
            Surface.SetDocument(text);
            return;
        }

        if (!AzureDevOpsBackend.IsWorkItemView(View) || !ado.Details.TryGetValue(item.Key, out var detail)) {
            ShowAzureDocument(item, text);
            return;
        }

        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
            _itemHistory.Clear();
        }

        var navigation = new HStack {
            Spacing = 1
        };
        if (_itemHistory.TryPeek(out var previous)) {
            navigation.Add(AzureButton("Back to #" + previous.Key, () => {
                dialog!.Close();
                _itemHistory.Pop();
                _expanded = true;
                Detail(previous);
            }));
        }

        navigation.Add(AzureButton("Back to list · Esc", Close));
        var actions = AzureActions(ado.Operations(View, item), operation => {
            Close();
            Prompt(operation);
        });

        var fields = detail.GetProperty("fields");
        string Field(string name) {
            if (!fields.TryGetProperty(name, out var value)) {
                return "—";
            }

            string result = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("displayName", out var display) ? display.GetString() ?? "" : value.ToString();
            return FileReader.Sanitize(WorkItemText(result), true);
        }

        var metadata = new VStack(
                new TextBlock("STATE · " + Field("System.State")) { Wrap = true },
                new TextBlock("Assigned to · " + Field("System.AssignedTo")) { Wrap = true },
                new TextBlock("Priority · " + Field("Microsoft.VSTS.Common.Priority") +
            "   Remaining hours · " + Field("Microsoft.VSTS.Scheduling.RemainingWork")) { Wrap = true },
                new TextBlock("Area · " + Field("System.AreaPath")) { Wrap = true },
                new TextBlock("Iteration · " + Field("System.IterationPath")) { Wrap = true },
                new TextBlock("Tags · " + Field("System.Tags")) { Wrap = true });
        var narrative = new VStack(
            new TextBlock("DESCRIPTION"),
            new TextBlock(Field("System.Description")) { Wrap = true },
            new VStack(
                new TextBlock("ACCEPTANCE CRITERIA"),
                new TextBlock(Field("Microsoft.VSTS.Common.AcceptanceCriteria")) { Wrap = true }
            ),
            new VStack(
                new TextBlock("DISCUSSION · latest 50 comments"),
                new TextBlock(FileReader.Sanitize(ado.Discussions.GetValueOrDefault(item.Key, "No comments."), true)) { Wrap = true }
            )) { Spacing = 1 };
        var body = new Grid { ColumnGap = 3, RowGap = 1, HorizontalAlignment = Align.Stretch };
        bool wide = Surface.Bounds.Width >= 100;
        body.ColumnDefinitions.Add(new() { Width = GridLength.Star(2) });
        if (wide) {
            body.ColumnDefinitions.Add(new() { Width = GridLength.Star(1) });
        }
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (!wide) {
            body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        body.Cells.Add(new(narrative) { Row = wide ? 0 : 1, Column = 0 });
        body.Cells.Add(new(metadata) { Row = 0, Column = wide ? 1 : 0 });
        var related = new WrapHStack { Spacing = 1, RunSpacing = 1 };
        bool hasRelations = false;
        if (detail.TryGetProperty("relations", out var links)) {
            foreach (var link in links.EnumerateArray()) {
                string relation = link.TryGetProperty("rel", out var rel) ? rel.GetString() ?? "Related" : "Related";
                string label = relation switch {
                    "System.LinkTypes.Hierarchy-Reverse" => "Parent",
                    "System.LinkTypes.Hierarchy-Forward" => "Child",
                    "System.LinkTypes.Related" => "Related",
                    "AttachedFile" => "Attachment",
                    "Hyperlink" => "Hyperlink",
                    _ => relation.Split('.').Last()
                };
                string name = link.TryGetProperty("attributes", out var attributes)
                    ? attributes.TryGetProperty("name", out var named) ? named.GetString() ?? label : label
                    : label;
                if (!link.TryGetProperty("url", out var url)
                    || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var address)) {
                    continue;
                }

                string id = address.AbsolutePath.Split('/').Last();
                if (address.AbsolutePath.Contains("/_apis/wit/workItems/", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(id, out _)) {
                    related.Add(AzureButton(label + " · #" + id, () => {
                        dialog!.Close();
                        _itemHistory.Push(item);
                        _expanded = true;
                        Detail(new(id, [], JsonSerializer.SerializeToElement(new { rev = 0 })));
                    }));
                    hasRelations = true;
                }
                else if (address.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    && address.UserInfo.Length == 0) {
                    string linkLabel = FileReader.Sanitize(name, true);
                    related.Add(AzureButton(linkLabel + " · show URL", () => ShowRelationUrl(linkLabel, address)));
                    hasRelations = true;
                }
            }
        }

        if (!hasRelations) {
            related.Add(new TextBlock("No linked work items, attachments, or hyperlinks."));
        }

        var content = new VStack(
            navigation,
            new TextBlock(Field("System.WorkItemType") + " #" + item.Key + " · " + Field("System.Title")) { Wrap = true },
            actions,
            body,
            new TextBlock("RELATED ITEMS · follow work items; other links show their URL") { Wrap = true },
            related
        ) { Spacing = 1 };
        dialog = new Dialog {
            Title = new TextBlock("Work item #" + item.Key),
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
        dialog.Show();
    }

    private void ShowAzureDocument(DeveloperItem item, string text) {
        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
        }
        string clean = FileReader.Sanitize(text, true).Replace("\r\n", "\n", StringComparison.Ordinal);
        var runs = new List<StyledRun>();
        int offset = 0;
        foreach (string line in clean.Split('\n')) {
            if (Surface.DocumentAccent?.Invoke(line) is { } color) {
                runs.Add(new(offset, line.Length, Style.None.WithForeground(color)));
            }
            offset += line.Length + 1;
        }
        var content = new VStack(AzureButton("Back to list · Esc", Close),
            AzureActions(_backend.Operations(View, item), operation => { Close(); Prompt(operation); }),
            new Paragraph(clean) { Wrap = true, Runs = [.. runs] }) { Spacing = 1 };
        dialog = new Dialog {
            Title = new TextBlock(_backend.Tabs[View] + " · #" + item.Key),
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
        dialog.Show();
    }

    private Button AzureButton(string label, Action click) {
        var button = new Button(label) { MinHeight = 1, MaxHeight = 1 }.Click(click);
        button.SetStyle(Surface.Palette.CompactActionButton);
        return button;
    }

    private Button DeveloperButton(string label, Action click) =>
        _backend is AzureDevOpsBackend ? AzureButton(label, click) : new Button(label).Click(click);

    private VStack AzureActions(IEnumerable<DeveloperOperation> operations, Action<DeveloperOperation> execute) {
        var content = new VStack { Spacing = 1 };
        var groups = operations.Where(operation => !_readOnly || operation.ReadOnly)
            .GroupBy(operation => operation.Label is "Plan into iteration" or "Set area" or "Set priority" or "Set remaining work"
                ? "PLAN" : operation.Label.StartsWith("Create ", StringComparison.Ordinal) || operation.ReadOnly ? "LIST & CREATE" : "EDIT");
        foreach (var group in groups) {
            var row = new WrapHStack(new TextBlock(group.Key + " ·")) { Spacing = 1, RunSpacing = 1 };
            foreach (var operation in group) {
                var captured = operation;
                row.Add(AzureButton(captured.Label, () => execute(captured)));
            }
            content.Add(row);
        }
        return content;
    }

    private void ShowRelationUrl(string label, Uri address) {
        Dialog? dialog = null;
        dialog = new Dialog {
            Title = new TextBlock(label),
            IsModal = true,
            Width = Math.Max(30, Math.Min(100, Surface.Bounds.Width - 4)),
            Content = new VStack(
                new Border(new ScrollViewer(new TextBlock(address.AbsoluteUri) { Wrap = true })),
                new Button("Close").Click(() => dialog!.Close())
            )
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                dialog.Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }

    private static string WorkItemText(string text) {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        text = Regex.Replace(text, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ", options | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
        text = Regex.Replace(text, @"<\s*li\b[^>]*>", "\n• ", options, TimeSpan.FromMilliseconds(50));
        text = Regex.Replace(text, @"<\s*(?:br|p|div|h[1-6]|tr)\b[^>]*>|<\s*/(?:p|div|li|h[1-6]|tr|table)\s*>", "\n", options, TimeSpan.FromMilliseconds(50));
        text = Regex.Replace(text, "<[^>]*>", " ", options, TimeSpan.FromMilliseconds(50));
        text = System.Net.WebUtility.HtmlDecode(text);
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t]+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)).Trim())
            .ToArray();
        return string.Join("\n", lines).Trim();
    }

    private void ShowKanban() {
        if (Busy) {
            return;
        }
        if (View == 1 && _backend is AzureDevOpsBackend { AllWorkItemTypes: true }) {
            Surface.Status = "All types is a list-only view. Choose a type with T or open a board level before using Kanban.";
            Surface.Refresh();
            return;
        }
        _expanded = true;
        var lanes = new HStack {
            Spacing = 1
        };
        var items = Surface.Model.Visible.Select(row => (DeveloperItem)row.Original)
            .Where(item => item.Cells.Length > 4).ToArray();
        static string Lane(DeveloperItem item) => string.IsNullOrWhiteSpace(item.Cells[4]) ? item.Cells[3] : item.Cells[4];
        var groups = items.GroupBy(Lane).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        string[] laneNames = View == 1 && _backend is AzureDevOpsBackend { BoardOpen: true } ado && ado.BoardColumns.Length > 0
            ? ado.BoardColumns
            : [.. groups.Keys];
        laneNames = [.. laneNames.Concat(groups.Keys).Distinct(StringComparer.OrdinalIgnoreCase)];
        int laneWidth = Math.Clamp((Surface.Bounds.Width - 8) / Math.Max(1, laneNames.Length) - 1, 24, 42);
        foreach (string laneName in laneNames) {
            var cards = new VStack {
                Spacing = 1
            };
            var groupItems = groups.GetValueOrDefault(laneName, []);
            foreach (var item in groupItems) {
                var captured = item;
                string assignee = string.IsNullOrWhiteSpace(item.Cells[5]) ? "Unassigned" : item.Cells[5];
                var card = new VStack(
                    new TextBlock("#" + item.Key + " · " + item.Cells[1]) { Wrap = true },
                    new TextBlock(item.Cells[2]) { Wrap = true },
                    new TextBlock(assignee + " · " + item.Cells[3]) { Wrap = true }
                ) { MinWidth = Math.Max(1, laneWidth - 6), MaxWidth = Math.Max(1, laneWidth - 6) };
                cards.Add(new Button(card).Click(() => {
                    _boardDialog?.Close();
                    _expanded = true;
                    Detail(captured);
                }));
            }

            if (groupItems.Length == 0) {
                cards.Add(new TextBlock("No items") { Wrap = true });
            }
            lanes.Add(new Border(new VStack(new Border(new TextBlock(laneName +
                " · " + groupItems.Length) { Wrap = true }), cards)) {
                    MinWidth = laneWidth,
                    MaxWidth = laneWidth
                });
        }

        _boardDialog = new Dialog {
            Title = new TextBlock("KANBAN · current filtered page · select a card to inspect / move"),
            IsModal = true,
            Width = Math.Max(30, Surface.Bounds.Width - 4),
            Height = Math.Max(10, Surface.Bounds.Height - 4),
            Content = new VStack(new Button("Back to list").Click(() => {
                _boardDialog!.Close();
                _expanded = false;
            }), new ScrollViewer(lanes))
        };
        _boardDialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                _boardDialog.Close();
                _expanded = false;
                e.Handled = true;
            }
        });
        _boardDialog.Show();
    }
    private Dialog? _boardDialog;
    private bool _showBoardWhenLoaded;
}
