using System.Text.RegularExpressions;
using System.Text.Json;
using fullscreen.Files;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Development;

public sealed partial class DeveloperApp {
    private void ShowExpanded(DeveloperItem item, string text) {
        if (_backend is GitBackend) {
            _expanded = false;
            Surface.ShowCodeDetail(text, "change.diff", true);
            return;
        }

        if (_backend is not AzureDevOpsBackend ado || !ado.Details.TryGetValue(item.Key, out var detail)) {
            Surface.SetDocument(text);
            return;
        }

        Dialog? dialog = null;
        void Close() {
            dialog!.Close();
            _expanded = false;
        }

        var actions = new VStack(new Button("Back to list").Click(Close)) {
            Spacing = 1
        };
        foreach (var operation in ado.Operations(View, item).Where(operation => !_readOnly || operation.ReadOnly)) {
            var captured = operation;
            actions.Add(new Button(captured.Label).Click(() => {
                Close();
                Prompt(captured);
            }));
        }

        var fields = detail.GetProperty("fields");
        string Field(string name) {
            if (!fields.TryGetProperty(name, out var value)) {
                return "—";
            }

            string result = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("displayName", out var display) ? display.GetString() ?? "" : value.ToString();
            return FileReader.Sanitize(
                System.Net.WebUtility.HtmlDecode(Regex.Replace(result, "<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))),
                true
            );
        }

        var content = new VStack(
            new Border(new VStack(
                new TextBlock("TITLE · " + Field("System.WorkItemType")),
                new TextBlock(Field("System.Title"))
            )),
            new Border(new VStack(
                new TextBlock("STATE · " + Field("System.State")),
                new TextBlock("Assigned to · " + Field("System.AssignedTo")),
                new TextBlock("Priority · " + Field("Microsoft.VSTS.Common.Priority") +
            "   Remaining hours · " + Field("Microsoft.VSTS.Scheduling.RemainingWork")),
                new TextBlock("Area · " + Field("System.AreaPath")),
                new TextBlock("Iteration · " + Field("System.IterationPath")),
                new TextBlock("Tags · " + Field("System.Tags"))
            )),
            new Border(new VStack(new TextBlock("DESCRIPTION"), new TextBlock(Field("System.Description")))),
            new Border(new VStack(
                new TextBlock("ACCEPTANCE CRITERIA"),
                new TextBlock(Field("Microsoft.VSTS.Common.AcceptanceCriteria"))
            )),
            new Border(new VStack(
                new TextBlock("DISCUSSION · latest 50 comments"),
                new TextBlock(FileReader.Sanitize(ado.Discussions.GetValueOrDefault(item.Key, "No comments."), true))
            )),
            new TextBlock("RELATED ITEMS · select to open inside this TUI")
        ) {
            Spacing = 1
        };
        if (detail.TryGetProperty("relations", out var links)) {
            foreach (var link in links.EnumerateArray()) {
                if (!link.TryGetProperty("url", out var url)
                    || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var address)) {
                    continue;
                }

                string id = address.Segments[^1];
                // Only numeric work-item links become internal navigation. Never open arbitrary URLs.
                if (!address.AbsolutePath.Contains("/_apis/wit/workItems/", StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(id, out _)) {
                    continue;
                }

                string relation = link.TryGetProperty("rel", out var rel) ? rel.GetString() ?? "Related" : "Related";
                content.Add(new Button(relation + " · #" + id).Click(() => {
                    dialog!.Close();
                    _expanded = true;
                    Detail(new(id, [], JsonSerializer.SerializeToElement(new { rev = 0 })));
                }));
            }
        }

        content.Add(new TextBlock("ACTIONS · edits use revision checks and confirmation"));
        content.Add(actions);
        dialog = new Dialog {
            Title = new TextBlock("Work item #" + item.Key),
            IsModal = true,
            Width = Math.Max(30, Math.Min(110, Surface.Bounds.Width - 4)),
            Height = Math.Max(10, Surface.Bounds.Height - 4),
            Content = new ScrollViewer(content)
        };
        dialog.KeyDown((_, e) => {
            if (e.Key == TerminalKey.Escape) {
                Close();
                e.Handled = true;
            }
        });
        dialog.Show();
    }

    private void ShowKanban() {
        _expanded = true;
        var lanes = new HStack {
            Spacing = 1
        };
        var items = Surface.Model.Visible.Select(row => (DeveloperItem)row.Original).Where(item => item.Cells.Length > 4);
        foreach (var group in items.GroupBy(item => string.IsNullOrEmpty(item.Cells[4]) ? item.Cells[3] : item.Cells[4])) {
            var cards = new VStack {
                Spacing = 1
            };
            foreach (var item in group) {
                var captured = item;
                cards.Add(new Button($"#{item.Key} · {item.Cells[1]}\n{item.Cells[2]}\n{item.Cells[5]}").Click(() => {
                    _boardDialog?.Close();
                    _expanded = true;
                    Detail(captured);
                }));
            }

            lanes.Add(new Border(new VStack(new TextBlock(group.Key +
                " · " + group.Count()), cards)) {
                    MinWidth = 28,
                    MaxWidth = 38
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
