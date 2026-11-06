using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace fullscreen.Development;

public sealed partial class AzureDevOpsBackend {
    private readonly ConcurrentDictionary<string, JsonElement> _reviewedPulls = new();
    private readonly ConcurrentDictionary<string, JsonElement> _pipelineArtifacts = new();

    private string PullPath(DeveloperItem item) {
        string repository = item.State is JsonElement state ? S(P(state, "repository"), "id") : "";
        if (repository.Length == 0) {
            throw new InvalidOperationException("Refresh the pull request list to load its repository.");
        }
        return Api($"git/repositories/{E(repository)}/pullrequests/{E(item.Key)}");
    }

    private static string PullVersion(JsonElement pull) =>
        S(P(pull, "lastMergeSourceCommit"), "commitId") + ":" + S(P(pull, "lastMergeTargetCommit"), "commitId");

    private async Task<string> PullRequestDetail(DeveloperItem item, JsonElement state, CancellationToken token) {
        string path = PullPath(item);
        _reviewedPulls.TryRemove(path, out _);
        var pull = await Get(path, token).ConfigureAwait(false);
        var threads = await Get(path + "/threads", token).ConfigureAwait(false);
        string source = S(P(pull, "lastMergeSourceCommit"), "commitId");
        string target = S(P(pull, "lastMergeTargetCommit"), "commitId");
        var text = new StringBuilder("#" + item.Key + " · " + S(pull, "title") +
            "\nState: " + DeveloperStatus.PullRequestState(S(pull, "status"), S(pull, "isDraft") == "True",
                S(pull, "status") == "completed", S(pull, "mergeStatus")) +
            "\n" + S(pull, "status") + " · " + Identity(P(pull, "createdBy")) +
            "\n" + S(pull, "sourceRefName") + " → " + S(pull, "targetRefName") +
            "\n\nDESCRIPTION\n" + Plain(S(pull, "description")) +
            "\n\nREVIEWERS\n" + string.Join('\n', Values(pull, "reviewers").Select(reviewer =>
                VoteLabel(Number(reviewer, "vote")) + "  " + Identity(reviewer))));
        text.AppendLine("\n\nDISCUSSION");
        foreach (var thread in Values(threads).Where(thread => P(thread, "isDeleted").ValueKind != JsonValueKind.True)) {
            foreach (var comment in Values(thread, "comments").Where(comment => P(comment, "isDeleted").ValueKind != JsonValueKind.True)) {
                text.AppendLine(Identity(P(comment, "author")) + " · " + Plain(S(comment, "content")));
            }
        }
        if (source.Length > 0 && target.Length > 0) {
            string repository = S(P(state, "repository"), "id");
            var changes = await Get(Api($"git/repositories/{E(repository)}/diffs/commits?baseVersion={E(target)}&baseVersionType=commit&targetVersion={E(source)}&targetVersionType=commit&diffCommonCommit=true&$top=100"), token).ConfigureAwait(false);
            text.AppendLine("\nCHANGED FILES · first 100");
            foreach (var change in Values(changes, "changes")) {
                text.AppendLine(S(change, "changeType") + " · " + S(P(change, "item"), "path"));
            }
            text.AppendLine("\nOpen in browser for the full diff and inline review.");
            var verified = await Get(path, token).ConfigureAwait(false);
            if (PullVersion(verified) != PullVersion(pull) || S(verified, "status") != "active") {
                throw new InvalidOperationException("Pull request changed while loading. Refresh and inspect it again before reviewing.");
            }
            if (_reviewedPulls.Count > 64) {
                _reviewedPulls.Clear();
            }
            _reviewedPulls[path] = verified.Clone();
        }
        return text.ToString();
    }

    private static string VoteLabel(int vote) => vote switch {
        10 => "✓ Approved",
        5 => "✓ Approved with suggestions",
        -5 => "● Waiting for author",
        -10 => "✗ Rejected",
        _ => "○ No vote"
    };

    private DeveloperOperation[] PullRequestOperations(DeveloperItem? item) {
        if (item is null) {
            return [];
        }
        string path = PullPath(item);
        string repository = S(P((JsonElement)item.State!, "repository"), "name");
        var operations = new List<DeveloperOperation> {
            new("Open pull request in browser", "View the full diff and inline comments.", (_, _) =>
                Task.FromResult(OpenAzureUrl(_client.OrganizationUrl + "/" + E(_project) + "/_git/" + E(repository) + "/pullrequest/" + E(item.Key))), ReadOnly: true)
        };
        if (!_reviewedPulls.TryGetValue(path, out var expected)) {
            return [.. operations];
        }

        async Task Verify(CancellationToken token) {
            var current = await Get(path, token).ConfigureAwait(false);
            if (S(current, "status") != "active" || PullVersion(current) != PullVersion(expected)) {
                _reviewedPulls.TryRemove(path, out _);
                throw new InvalidOperationException("Pull request source or target changed. Refresh and inspect it again before reviewing.");
            }
        }
        operations.Add(new("Add PR comment", "Post a discussion comment on pull request #" + item.Key + ".", async (body, token) => {
            ArgumentException.ThrowIfNullOrWhiteSpace(body);
            await Verify(token).ConfigureAwait(false);
            await _client.SendAsync(HttpMethod.Post, path + "/threads?api-version=7.1",
                new { comments = new[] { new { parentCommentId = 0, content = body, commentType = 1 } }, status = 1 }, token).ConfigureAwait(false);
            return "Comment posted";
        }, "Comment"));
        foreach (var (label, vote) in new[] {
            ("Approve", 10), ("Approve with suggestions", 5), ("Wait for author", -5), ("Reject", -10), ("Reset vote", 0)
        }) {
            operations.Add(new(label, "Set your review vote on PR #" + item.Key +
                ". Source and target must still match the inspected version. Review the full diff before approving.", async (_, token) => {
                    await Verify(token).ConfigureAwait(false);
                    var connection = await _client.SendAsync(HttpMethod.Get,
                        "_apis/connectionData?api-version=7.1-preview.1", null, token).ConfigureAwait(false);
                    string identity = S(P(connection, "authenticatedUser"), "id");
                    if (!Guid.TryParse(identity, out var reviewerId) || reviewerId == Guid.Empty) {
                        throw new InvalidOperationException("Azure DevOps did not return your authenticated reviewer identity.");
                    }
                    await Verify(token).ConfigureAwait(false);
                    await _client.SendAsync(HttpMethod.Put, path + "/reviewers/" + E(identity) + "?api-version=7.1",
                        new { id = identity, vote }, token).ConfigureAwait(false);
                    return "Review vote saved";
                }));
        }
        return [.. operations];
    }

    private async Task<string> PipelineDetail(DeveloperItem item, CancellationToken token) {
        var build = await Get(Api($"build/builds/{E(item.Key)}"), token).ConfigureAwait(false);
        var timeline = await Get(Api($"build/builds/{E(item.Key)}/timeline"), token).ConfigureAwait(false);
        var artifacts = await Get(Api($"build/builds/{E(item.Key)}/artifacts"), token).ConfigureAwait(false);
        if (_pipelineArtifacts.Count > 64) {
            _pipelineArtifacts.Clear();
        }
        _pipelineArtifacts[item.Key] = artifacts.Clone();
        return S(P(build, "definition"), "name") + " · " + S(build, "buildNumber") +
            "\n" + GitHubBackend.StatusLabel(S(build, "status"), S(build, "result")) +
            " · " + GitHubBackend.Duration(build, "startTime", "finishTime") +
            "\nBranch · " + S(build, "sourceBranch") +
            "\nRequested by · " + Identity(P(build, "requestedFor")) +
            "\nCommit · " + S(build, "sourceVersion") +
            "\n\nJOBS AND STEPS\n" + string.Join('\n',
                Values(timeline, "records").OrderBy(record => Number(record, "order")).Select(record =>
                    (S(record, "type") == "Task" ? "  " : "") +
                    GitHubBackend.StatusLabel(S(record, "state"), S(record, "result")) +
                    " · " + S(record, "name") + " · " + GitHubBackend.Duration(record, "startTime", "finishTime") +
                    "\n" + string.Join('\n', Values(record, "issues").Select(issue => "  " + S(issue, "type") + " · " + S(issue, "message"))))) +
            "\n\nARTIFACTS\n" + string.Join('\n', Values(artifacts).Select(artifact => S(artifact, "name"))) +
            "\n\nUse Actions to open the run or its artifacts in the browser.";
    }

    private DeveloperOperation[] PipelineOperations(DeveloperItem? item) {
        if (item is null) {
            return [];
        }
        var operations = new List<DeveloperOperation> {
            new("Open pipeline run in browser", "Open logs, jobs and run actions in Azure DevOps.", (_, _) =>
                Task.FromResult(OpenAzureUrl(_client.OrganizationUrl + "/" + E(_project) + "/_build/results?buildId=" + E(item.Key))), ReadOnly: true)
        };
        if (_pipelineArtifacts.TryGetValue(item.Key, out var artifacts)) {
            foreach (var artifact in Values(artifacts)) {
                string url = S(P(artifact, "resource"), "downloadUrl");
                if (url.Length > 0) {
                    operations.Add(new("Open artifact · " + S(artifact, "name"), "Open the artifact download in your browser.",
                        (_, _) => Task.FromResult(OpenAzureUrl(url)), ReadOnly: true));
                }
            }
        }
        return [.. operations];
    }

    private string OpenAzureUrl(string value) {
        var organization = new Uri(_client.OrganizationUrl.TrimEnd('/') + "/");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps
            || address.UserInfo.Length > 0 || !organization.IsBaseOf(address)) {
            throw new InvalidOperationException("Azure DevOps returned a link outside the selected organization.");
        }
        Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        return "Opened " + address.AbsoluteUri;
    }
}
