using System.Net;
using System.Text.Json;

namespace fullscreen.Development;

public sealed partial class AzureDevOpsBackend : IPipelineBackend {
    bool IPipelineBackend.IsPipelineView(int view) => view == 5;

    async Task<PipelineSnapshot> IPipelineBackend.ReadPipelineAsync(DeveloperItem item, CancellationToken token) {
        var build = await Get(Api($"build/builds/{E(item.Key)}"), token).ConfigureAwait(false);
        JsonElement timeline;
        try {
            timeline = await Get(Api($"build/builds/{E(item.Key)}/timeline"), token).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) {
            timeline = JsonSerializer.SerializeToElement(new { records = Array.Empty<object>() });
        }
        var artifacts = await Get(Api($"build/builds/{E(item.Key)}/artifacts"), token).ConfigureAwait(false);
        var records = Values(timeline, "records").ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal) { S(timeline, "id") };
        for (int index = 0; index < records.Count; index++) {
            string nested = S(P(records[index], "details"), "id");
            if (nested.Length == 0 || !seen.Add(nested)) {
                continue;
            }
            if (seen.Count > 100) {
                throw new InvalidOperationException("Run exceeds 100 nested timelines; inspect it in the web UI.");
            }
            var detail = await Get(Api($"build/builds/{E(item.Key)}/timeline/{E(nested)}"), token).ConfigureAwait(false);
            records.AddRange(Values(detail, "records"));
        }
        records = [.. records.GroupBy(record => S(record, "id"), StringComparer.Ordinal)
            .Select(group => group.MaxBy(record => Number(record, "attempt"))).OrderBy(record => Number(record, "order"))];
        var nodes = records.Select(record => new PipelineNode(S(record, "id"), S(record, "parentId") is { Length: > 0 } parent ? parent : null, S(record, "type"),
            S(record, "name"), GitHubBackend.StatusLabel(S(record, "state"), S(record, "result")),
            GitHubBackend.Duration(record, "startTime", "finishTime"),
            S(P(record, "log"), "id") is { Length: > 0 } log ? log : null,
            "Worker: " + S(record, "workerName") + "\nAttempt: " + S(record, "attempt") + "\n" +
            string.Join('\n', Values(record, "issues").Select(issue => S(issue, "type") + ": " + S(issue, "message"))))).ToArray();
        return new(S(P(build, "definition"), "name") + " · " + S(build, "buildNumber"),
            GitHubBackend.StatusLabel(S(build, "status"), S(build, "result")), nodes,
            [.. Values(artifacts).Select(artifact => new PipelineArtifact(S(artifact, "name"), S(artifact, "name"),
                S(P(P(artifact, "resource"), "properties"), "artifactsize")))],
            "Stages/jobs/tasks refresh every 3s. Select a task to follow its published log; job/stage selections follow their active task. " +
            "Agent buffering may delay new lines. Artifact downloads are ZIP archives, not extracted.");
    }

    async Task<PipelineLog> IPipelineBackend.ReadPipelineLogAsync(DeveloperItem item, PipelineNode node, CancellationToken token) {
        if (node.LogId is null) {
            return new(node.Details, "No published log yet for this node.");
        }
        // Request a bounded tail instead of downloading a growing multi-megabyte log on each poll.
        var logs = await Get(Api($"build/builds/{E(item.Key)}/logs"), token).ConfigureAwait(false);
        var log = Values(logs).FirstOrDefault(entry => S(entry, "id") == node.LogId);
        int lines = Number(log, "lineCount");
        int start = Math.Max(0, lines - 2000);
        var response = await Get(Api($"build/builds/{E(item.Key)}/logs/{E(node.LogId)}?startLine={start}&endLine={Math.Max(start + 1999, lines - 1)}"), token).ConfigureAwait(false);
        string text = response.ValueKind == System.Text.Json.JsonValueKind.Array
            ? string.Join('\n', response.EnumerateArray().Select(line => line.ToString()))
            : response.ValueKind == System.Text.Json.JsonValueKind.String ? response.GetString()!
            : string.Join('\n', Values(response).Select(line => line.ToString()));
        return new(text, start > 0 ? $"Showing the last 2,000 published lines, starting at line {start + 1}." : "");
    }

    async Task IPipelineBackend.DownloadPipelineArtifactAsync(DeveloperItem item, PipelineArtifact artifact, string destination, CancellationToken token) {
        var artifacts = await Get(Api($"build/builds/{E(item.Key)}/artifacts"), token).ConfigureAwait(false);
        if (!Values(artifacts).Any(entry => S(entry, "name") == artifact.Id)) {
            throw new InvalidOperationException("Artifact no longer exists. Refresh the run.");
        }
        await _client.DownloadAsync(Api($"build/builds/{E(item.Key)}/artifacts?artifactName={E(artifact.Id)}&$format=zip") + "&api-version=7.1",
            destination, token).ConfigureAwait(false);
    }
}
