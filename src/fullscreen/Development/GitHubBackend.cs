using System.Globalization;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace fullscreen.Development;
/// <summary>Selects which pull request states to load.</summary>
/// <param name="Number">The resource number.</param>
/// <param name="Head">The current Git head revision.</param>
public sealed record PullRequestState(int Number, string Head);
/// <summary>Selects which workflow run states to load.</summary>
/// <param name="Id">The stable resource or process identifier.</param>
/// <param name="Head">The current Git head revision.</param>
/// <param name="Status">The raw Git porcelain status text.</param>
/// <param name="Attempt">The workflow run attempt number.</param>
public sealed record WorkflowRunState(long Id, string Head, string Status, int Attempt);
/// <summary>Loads GitHub resources and exposes repository operations through GitHub CLI.</summary>
public sealed partial class GitHubBackend : IDeveloperBackend {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };
    private readonly string _directory;
    private readonly ICommandRunner _runner;
    private readonly int _pageSize;
    private readonly string _host;
    private string? _repository;
    private readonly ConcurrentDictionary<int, string> _reviewedHeads = new();
    private readonly ConcurrentDictionary<long, (WorkflowRunState Run, long[] Environments, string Names)> _deployments = new();
    /// <summary>Gets the displayed heading.</summary>
    public string Title => "◈ GITHUB · " + _host + "/" + (_repository ?? "detecting repository");
    /// <summary>Gets the available tab headings.</summary>
    public string[] Tabs { get; } = ["Issues", "Pull requests", "Runs", "Workflows"];

    /// <summary>Initializes a new GitHubBackend instance.</summary>
    /// <param name="directory">The parent or working directory.</param>
    /// <param name="repository">The repository path or owner/name identifier.</param>
    /// <param name="runner">The child-process runner used by the backend.</param>
    /// <param name="pageSize">The number of entries that fit in the visible page. Defaults to 50.</param>
    /// <param name="host">The GitHub host name. Defaults to &quot;github.com&quot;.</param>
    public GitHubBackend(
        string directory,
        string? repository,
        ICommandRunner runner,
        int pageSize = 50,
        string host = "github.com"
    ) {
        if (repository is not null && !RepositoryNamePattern().IsMatch(repository)) {
            throw new ArgumentException("Repository must be owner/name.", nameof(repository));
        }

        if (!Uri.CheckHostName(host).Equals(UriHostNameType.Dns)) {
            throw new ArgumentException("HostName must be a DNS hostname.", nameof(host));
        }

        _directory = directory;
        _repository = repository;
        _runner = runner;
        _pageSize = pageSize;
        _host = host;
    }

    private async Task<string> Gh(
        IReadOnlyList<string> arguments,
        CancellationToken token,
        string? input = null
    ) =>
        (await _runner.RunAsync(new("gh", _directory, arguments, input), token).ConfigureAwait(false)).RequireSuccess();
    private async Task Resolve(CancellationToken token) {
        _repository ??= (await Gh([
            "repo",
            "view",
            "--json",
            "nameWithOwner",
            "--jq",
            ".nameWithOwner"
        ], token).ConfigureAwait(false)).Trim();
        if (!RepositoryNamePattern().IsMatch(_repository)) {
            throw new InvalidOperationException("Could not determine a repository. Supply -Repository owner/name.");
        }
    }

    private async Task<JsonElement> Api(
        string path,
        CancellationToken token,
        string method = "GET",
        object? body = null
    ) {
        var arguments = new List<string>
        {
            "api",
            "--hostname",
            _host,
            "--method",
            method,
            "-H",
            "Accept: application/vnd.github+json",
            $"repos/{_repository}/{path}"};
        string? input = null;
        if (body is not null) {
            arguments.Add("--input");
            arguments.Add("-");
            input = JsonSerializer.Serialize(body);
        }

        string response = await Gh(arguments, token, input).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response)) {
            return default;
        }

        using var json = JsonDocument.Parse(response);
        return json.RootElement.Clone();
    }

    private static string Text(
        JsonElement node,
        string name
    ) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.Null ? "" : value.ToString() : "";
    private static long Number(
        JsonElement node,
        string name
    ) =>
        node.TryGetProperty(name, out var value) && value.TryGetInt64(out long number) ? number : 0;
    private static string User(JsonElement node) =>
        node.TryGetProperty("user", out var user) ? Text(user, "login") : "";
    private static string Pretty(JsonElement node) =>
        JsonSerializer.Serialize(node, JsonOptions);
    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.LoadAsync"/>
    public async Task<DeveloperPage> LoadAsync(int view, int page, CancellationToken token) {
        await Resolve(token).ConfigureAwait(false);
        string query = $"per_page={_pageSize}&page={page + 1}";
        if (view == 0) {
            var data = await Api("issues?state=open&sort=updated&direction=desc&" + query, token).ConfigureAwait(false);
            var items = data.EnumerateArray().Where(issue => !issue.TryGetProperty("pull_request", out _)).Select(issue => new DeveloperItem(
                Number(issue, "number").ToString(CultureInfo.InvariantCulture),
                [
                    Text(issue, "number"),
                    Text(issue, "title"),
                    User(issue),
                    Text(issue, "comments"),
                    Text(issue, "updated_at")
                ],
                (int)Number(issue, "number")
            )).ToArray();
            return new(
                [
                    "#",
                    "Title",
                    "Author",
                    "Comments",
                    "Updated"
                ],
                items,
                $"Open issues · page {page + 1} · PRs excluded · [ / ] pages · F5 refresh · A actions",
                [
                    9,
                    48,
                    20,
                    12,
                    0
                ]
            );
        }

        if (view == 1) {
            var data = await Api("pulls?state=open&sort=updated&direction=desc&" + query, token).ConfigureAwait(false);
            var items = data.EnumerateArray().Select(pr => new DeveloperItem(
                Number(pr, "number").ToString(CultureInfo.InvariantCulture),
                [
                    Text(pr, "number"),
                    Text(pr, "title"),
                    User(pr),
                    Text(pr, "draft") == "True" ? "Draft" : "Ready",
                    Text(pr.GetProperty("head"), "ref"),
                    Text(pr, "updated_at")
                ],
                new PullRequestState((int)Number(pr, "number"), Text(pr.GetProperty("head"), "sha"))
            )).ToArray();
            return new(
                [
                    "#",
                    "Title",
                    "Author",
                    "State",
                    "Branch",
                    "Updated"
                ],
                items,
                $"Open PRs · page {page + 1} · inspect diff before review · A actions",
                [
                    9,
                    48,
                    20,
                    12,
                    24,
                    0
                ]
            );
        }

        if (view == 2) {
            var data = await Api("actions/runs?" + query, token).ConfigureAwait(false);
            var items = data.GetProperty("workflow_runs").EnumerateArray().Select(run => new DeveloperItem(
                Text(run, "id"),
                [
                    Text(run, "id"),
                    Text(run, "display_title"),
                    Text(run, "head_branch"),
                    Text(run, "status"),
                    Text(run, "conclusion"),
                    Text(run, "run_attempt")
                ],
                new WorkflowRunState(
                    Number(run, "id"),
                    Text(run, "head_sha"),
                    Text(run, "status"),
                    (int)Number(run, "run_attempt")
                )
            )).ToArray();
            return new(
                [
                    "Run",
                    "Pipeline",
                    "Branch",
                    "Status",
                    "Conclusion",
                    "Attempt"
                ],
                items,
                $"Workflow runs · page {page + 1} · inspect jobs / pending environments · A actions",
                [
                    15,
                    42,
                    24,
                    18,
                    16,
                    0
                ]
            );
        }

        var workflows = await Api("actions/workflows?" + query, token).ConfigureAwait(false);
        return new(
            [
                "ID",
                "Workflow",
                "State",
                "Path"
            ],
            [..workflows.GetProperty("workflows").EnumerateArray().Select(workflow => new DeveloperItem(
                Text(workflow, "id"),
                [
                    Text(workflow, "id"),
                    Text(workflow, "name"),
                    Text(workflow, "state"),
                    Text(workflow, "path")
                ],
                Number(workflow, "id")
            ))],
            $"Workflows · page {page + 1} · A actions to dispatch on an explicit branch",
            [
                15,
                38,
                18,
                0
            ]
        );
    }

    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.DetailAsync"/>
    public async Task<string> DetailAsync(int view, DeveloperItem item, CancellationToken token) {
        if (view == 0) {
            var issue = await Api($"issues/{item.Key}", token).ConfigureAwait(false);
            long page = Math.Max(1, (Number(issue, "comments") + 99) / 100);
            var comments = await Api($"issues/{item.Key}/comments?per_page=100&page={page}", token).ConfigureAwait(false);
            return Text(issue, "html_url") +
                "\n\n" + Text(issue, "title") +
                "\n\n" + Text(issue, "body") +
                "\n\nCOMMENTS (latest page, up to 100)\n" + string.Join(
                    "\n\n",
                    comments.EnumerateArray().Select(comment => User(comment) +
                ": " + Text(comment, "body"))
                );
        }

        if (item.State is PullRequestState pr) {
            _reviewedHeads.TryRemove(pr.Number, out _);
            var current = await Api($"pulls/{pr.Number}", token).ConfigureAwait(false);
            string head = Text(current.GetProperty("head"), "sha");
            string diff = await Gh(
                [
                    "api",
                    "--hostname",
                    _host,
                    "-H",
                    "Accept: application/vnd.github.diff",
                    $"repos/{_repository}/pulls/{pr.Number}"
                ],
                token
            ).ConfigureAwait(false);
            var checks = await Api($"commits/{head}/check-runs?per_page=100", token).ConfigureAwait(false);
            var statuses = await Api($"commits/{head}/status", token).ConfigureAwait(false);
            var reviews = await Api($"pulls/{pr.Number}/reviews?per_page=100", token).ConfigureAwait(false);
            var verified = await Api($"pulls/{pr.Number}", token).ConfigureAwait(false);
            if (Text(verified.GetProperty("head"), "sha") != head) {
                throw new InvalidOperationException("PR head changed while loading the diff. Refresh and inspect again.");
            }

            _reviewedHeads[pr.Number] = head;
            string checkLines = string.Join(
                '\n',
                checks.GetProperty("check_runs").EnumerateArray().Select(check => Text(check, "name") +
                " · " + Text(check, "status") +
                " · " + Text(check, "conclusion"))
            );
            string statusLines = statuses.TryGetProperty("contexts", out var contexts) ? string.Join(
                '\n',
                contexts.EnumerateArray().Select(status => Text(status, "context") +
                " · " + Text(status, "state"))
            ) : "";
            string reviewLines = string.Join(
                '\n',
                reviews.EnumerateArray().Select(review => User(review) +
                " · " + Text(review, "state") +
                " · " + Text(review, "body"))
            );
            return Text(current, "html_url") +
                "\nHEAD " + head +
                "\n\n" + Text(current, "title") +
                "\n\n" + Text(current, "body") +
                "\n\nCHECKS (first 100)\n" + checkLines +
                "\n" + statusLines +
                "\n\nREVIEWS (first 100)\n" + reviewLines +
                "\n\nDIFF · GitHub may omit binary/oversized patches\n" + diff;
        }

        if (item.State is WorkflowRunState run) {
            var current = await Api($"actions/runs/{run.Id}", token).ConfigureAwait(false);
            var jobs = await Api($"actions/runs/{run.Id}/jobs?per_page=100", token).ConfigureAwait(false);
            var pending = await Api($"actions/runs/{run.Id}/pending_deployments", token).ConfigureAwait(false);
            var eligible = pending.EnumerateArray().Where(environment => environment.TryGetProperty("current_user_can_approve", out var canApprove)
                && canApprove.ValueKind == JsonValueKind.True).ToArray();
            _deployments[run.Id] = (new(
                run.Id,
                Text(current, "head_sha"),
                Text(current, "status"),
                (int)Number(current, "run_attempt")
            ), [.. eligible.Select(environment => Number(environment.GetProperty("environment"), "id"))], string.Join(
                ", ",
                eligible.Select(environment => Text(environment.GetProperty("environment"), "name"))
            ));
            string jobLines = string.Join(
                "\n\n",
                jobs.GetProperty("jobs").EnumerateArray().Select(job => Text(job, "name") +
                " · " + Text(job, "status") +
                " · " + Text(job, "conclusion") +
                "\n" + (job.TryGetProperty("steps", out var steps) ? string.Join(
                    '\n',
                    steps.EnumerateArray().Select(step => "  " + Text(step, "number") +
                ". " + Text(step, "name") +
                " · " + Text(step, "status") +
                " · " + Text(step, "conclusion"))
                ) : ""))
            );
            return Text(current, "html_url") +
                "\n\n" + Text(current, "display_title") +
                "\n" + Text(current, "status") +
                " · " + Text(current, "conclusion") +
                "\nBranch: " + Text(current, "head_branch") +
                "\nHead: " + Text(current, "head_sha") +
                "\nAttempt: " + Text(current, "run_attempt") +
                "\n\nJOBS AND STEPS (first 100 jobs)\n" + jobLines +
                "\n\nPENDING DEPLOYMENTS\n" + Pretty(pending);
        }

        return Pretty(await Api($"actions/workflows/{item.Key}", token).ConfigureAwait(false));
    }

    /// <inheritdoc cref="fullscreen.Development.IDeveloperBackend.Operations"/>
    public DeveloperOperation[] Operations(int view, DeveloperItem? item) {
        if (item is null) {
            return [];
        }

        var operations = new List<DeveloperOperation>();
        if (view == 0) {
            operations.Add(new("Comment on issue…", $"Post a comment to {_repository} issue #{item.Key}.", async (
                body,
                token
            ) => {
                RequireBody(body);
                await Api($"issues/{item.Key}/comments", token, "POST", new { body }).ConfigureAwait(false);
                return "Comment posted.";
            }, "Comment"));
        }

        if (item.State is PullRequestState pr && _reviewedHeads.TryGetValue(pr.Number, out string? head)) {
            foreach (var (label, action) in new[]
            {
                ("Approve PR…", "APPROVE"),
                ("Request changes…", "REQUEST_CHANGES"),
                ("Comment review…", "COMMENT")
            }

            ) {
                operations.Add(new(label, $"Submit {action} on {_repository} PR #{pr.Number}\nReviewed head: {head}\nThe head will be checked again before submission.", async (
                    body,
                    token
                ) => {
                    if (action != "APPROVE") {
                        RequireBody(body);
                    }

                    await ReviewAsync(pr.Number, head, action, body, token).ConfigureAwait(false);
                    return "Review submitted on " + head;
                }, action == "APPROVE" ? null : "Review body"));
            }
        }

        if (item.State is WorkflowRunState run) {
            operations.Add(new(
                "View failed-step logs",
                "Load failed-step logs for this workflow attempt.",
                async (
                    _,
                    token
                ) => await Gh(
                    [
                        "run",
                        "view",
                        run.Id.ToString(CultureInfo.InvariantCulture),
                        "--attempt",
                        run.Attempt.ToString(CultureInfo.InvariantCulture),
                        "--repo",
                        _host +
                "/" + _repository,
                        "--log-failed"
                    ],
                    token
                ).ConfigureAwait(false),
                ReadOnly: true
            ));
            operations.Add(new("Re-run failed jobs", $"Re-run failed jobs for {_repository} run {run.Id}, attempt {run.Attempt}.", async (
                _,
                token
            ) => {
                await VerifyRun(run, token).ConfigureAwait(false);
                await Api($"actions/runs/{run.Id}/rerun-failed-jobs", token, "POST").ConfigureAwait(false);
                return "Re-run requested.";
            }));
            operations.Add(new("Cancel run", $"Cancel {_repository} workflow run {run.Id}, attempt {run.Attempt}.", async (
                _,
                token
            ) => {
                await VerifyRun(run, token).ConfigureAwait(false);
                await Api($"actions/runs/{run.Id}/cancel", token, "POST").ConfigureAwait(false);
                return "Cancellation requested.";
            }));
            if (run.Status == "action_required") {
                operations.Add(new("Approve workflow run", $"Allow {_repository} workflow run {run.Id} to execute.\nHead: {run.Head}", async (
                    _,
                    token
                ) => {
                    await VerifyRun(run, token).ConfigureAwait(false);
                    await Api($"actions/runs/{run.Id}/approve", token, "POST").ConfigureAwait(false);
                    return "Workflow approval submitted.";
                }));
            }

            if (_deployments.TryGetValue(run.Id, out var pending) && pending.Environments.Length > 0) {
                operations.Add(new("Approve pending environments", $"Approve deployment to: {pending.Names}\nRepository: {_repository}\nRun: {run.Id}, attempt {pending.Run.Attempt}\nHead: {pending.Run.Head}", async (
                    _,
                    token
                ) => {
                    await VerifyRun(pending.Run, token).ConfigureAwait(false);
                    var fresh = await Api($"actions/runs/{run.Id}/pending_deployments", token).ConfigureAwait(false);
                    var ids = fresh.EnumerateArray().Where(environment => environment.TryGetProperty("current_user_can_approve", out var canApprove)
                        && canApprove.ValueKind == JsonValueKind.True).Select(environment => Number(environment.GetProperty("environment"), "id")).Order().ToArray();
                    if (!ids.SequenceEqual(pending.Environments.Order())) {
                        throw new InvalidOperationException("Pending environments changed. Inspect the run again before approval.");
                    }

                    await Api(
                        $"actions/runs/{run.Id}/pending_deployments",
                        token,
                        "POST",
                        new { environment_ids = pending.Environments, state = "approved", comment = "Approved from Fullscreen after reviewing pending environments." }
                    ).ConfigureAwait(false);
                    return "Environment approval submitted.";
                }));
            }
        }

        if (view == 3) {
            operations.Add(new("Run workflow…", $"Dispatch {_repository} workflow {item.Key} on the branch/tag entered below. GitHub requires workflow_dispatch support; workflows with required inputs are not handled here.", async (
                reference,
                token
            ) => {
                RequireBody(reference);
                await Api(
                    $"actions/workflows/{item.Key}/dispatches",
                    token,
                    "POST",
                    new Dictionary<string, string> { ["ref"] = reference }
                ).ConfigureAwait(false);
                return "Workflow dispatch requested.";
            }, "Branch or tag"));
        }

        return [.. operations];
    }

    /// <summary>Submits a pull request review after validating its expected head revision.</summary>
    /// <param name="number">The resource number.</param>
    /// <param name="expectedHead">The expected pull request head revision used to reject stale
    /// reviews.</param>
    /// <param name="action">The operation identifier.</param>
    /// <param name="body">The request or review body.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public async Task ReviewAsync(
        int number,
        string expectedHead,
        string action,
        string body,
        CancellationToken token
    ) {
        if (action is not ("APPROVE" or "REQUEST_CHANGES" or "COMMENT")) {
            throw new ArgumentException("Invalid review event.");
        }

        if (action != "APPROVE") {
            RequireBody(body);
        }

        var current = await Api($"pulls/{number}", token).ConfigureAwait(false);
        if (Text(current.GetProperty("head"), "sha") != expectedHead || Text(current, "state") != "open") {
            throw new InvalidOperationException("PR changed or closed. Refresh and inspect the new head before reviewing.");
        }

        await Api(
            $"pulls/{number}/reviews",
            token,
            "POST",
            new { commit_id = expectedHead, @event = action, body }
        ).ConfigureAwait(false);
        _reviewedHeads.TryRemove(number, out _);
    }

    private async Task VerifyRun(WorkflowRunState expected, CancellationToken token) {
        var current = await Api($"actions/runs/{expected.Id}", token).ConfigureAwait(false);
        if (Text(current, "head_sha") != expected.Head || Number(current, "run_attempt") != expected.Attempt) {
            throw new InvalidOperationException("Workflow attempt changed. Refresh before acting.");
        }
    }

    private static void RequireBody(string body) {
        if (string.IsNullOrWhiteSpace(body)) {
            throw new ArgumentException("A nonempty value is required.");
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryNamePattern();
}
