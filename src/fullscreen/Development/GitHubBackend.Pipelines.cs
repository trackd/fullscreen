using System.Globalization;
using System.Text.Json;

namespace fullscreen.Development;

public sealed partial class GitHubBackend : IPipelineBackend {
    private readonly Dictionary<string, PipelineLog> _jobLogs = new(StringComparer.Ordinal);
    bool IPipelineBackend.IsPipelineView(int view) => view == 2;

    async Task<PipelineSnapshot> IPipelineBackend.ReadPipelineAsync(DeveloperItem item, CancellationToken token) {
        await Resolve(token).ConfigureAwait(false);
        var current = await Api($"actions/runs/{item.Key}", token).ConfigureAwait(false);
        long attempt = Number(current, "run_attempt");
        var jobs = await PipelinePages($"actions/runs/{item.Key}/attempts/{attempt}/jobs", "jobs", token).ConfigureAwait(false);
        var artifacts = await PipelinePages($"actions/runs/{item.Key}/artifacts", "artifacts", token).ConfigureAwait(false);
        var nodes = new List<PipelineNode>();
        foreach (var job in jobs) {
            string id = Text(job, "id");
            string status = Text(job, "status"), conclusion = Text(job, "conclusion");
            nodes.Add(new(id, null, "Job", Text(job, "name"), StatusLabel(status, conclusion),
                Duration(job, "started_at", "completed_at"), id,
                "Runner: " + Text(job, "runner_name") + "\nAttempt: " + attempt.ToString(CultureInfo.InvariantCulture)));
            if (job.TryGetProperty("steps", out var steps)) {
                foreach (var step in steps.EnumerateArray()) {
                    nodes.Add(new(id + ":" + Text(step, "number"), id, "Step", Text(step, "name"),
                        StatusLabel(Text(step, "status"), Text(step, "conclusion")), Duration(step, "started_at", "completed_at"),
                        id, "Selected step: " + Text(step, "name") + "\nGitHub provides a combined job log, not a separate step log. Use / to find the step heading."));
                }
            }
        }
        return new(Text(current, "name") + " · attempt " + attempt, StatusLabel(Text(current, "status"), Text(current, "conclusion")),
            [.. nodes], [.. artifacts.Select(artifact => new PipelineArtifact(Text(artifact, "id"), Text(artifact, "name"),
                FormatBytes(Number(artifact, "size_in_bytes")), Text(artifact, "expired") == "True"))],
            "GitHub jobs/steps refresh every 3s. Job logs normally become available after completion. " +
            "Steps share their job log. The API does not expose a resolved needs/dependency graph.");
    }

    private async Task<JsonElement[]> PipelinePages(string path, string property, CancellationToken token) {
        var results = new List<JsonElement>();
        for (int page = 1; page <= 100; page++) {
            var data = await Api(path + $"?per_page=100&page={page}", token).ConfigureAwait(false);
            var entries = data.GetProperty(property).EnumerateArray().ToArray();
            results.AddRange(entries);
            if (entries.Length < 100 || data.TryGetProperty("total_count", out var total) && results.Count >= total.GetInt32()) {
                return [.. results];
            }
        }
        throw new InvalidOperationException("Run exceeds 10,000 jobs or artifacts; narrow the run in the web UI.");
    }

    async Task<PipelineLog> IPipelineBackend.ReadPipelineLogAsync(DeveloperItem item, PipelineNode node, CancellationToken token) {
        if (node.LogId is null) {
            return new("", "No log is attached to this node.");
        }
        if (_jobLogs.TryGetValue(node.LogId, out var cached)) {
            return cached;
        }
        var job = await Api($"actions/jobs/{node.LogId}", token).ConfigureAwait(false);
        if (Text(job, "status") != "completed") {
            return new("", "GitHub has not published this job's log yet. Job and step status continues to refresh; logs will load after completion.");
        }
        var result = await _runner.RunAsync(new("gh", _directory,
            ["api", "--hostname", _host, $"repos/{_repository}/actions/jobs/{node.LogId}/logs"]), token).ConfigureAwait(false);
        if (result.ExitCode != 0) {
            result.RequireSuccess();
        }
        var log = new PipelineLog(result.Output, result.Truncated ? "Log exceeds 4 MiB; showing the beginning only. Use the web UI for the complete log." : "");
        if (_jobLogs.Count >= 8) {
            _jobLogs.Remove(_jobLogs.Keys.First());
        }
        _jobLogs[node.LogId] = log;
        return log;
    }

    async Task IPipelineBackend.DownloadPipelineArtifactAsync(DeveloperItem item, PipelineArtifact artifact, string destination, CancellationToken token) {
        var artifacts = await PipelinePages($"actions/runs/{item.Key}/artifacts", "artifacts", token).ConfigureAwait(false);
        var current = artifacts.SingleOrDefault(entry => Text(entry, "id") == artifact.Id);
        if (current.ValueKind == JsonValueKind.Undefined || Text(current, "expired") == "True") {
            throw new InvalidOperationException("Artifact is missing or expired. Refresh the run.");
        }
        await _runner.DownloadAsync(new("gh", _directory,
            ["api", "--hostname", _host, $"repos/{_repository}/actions/artifacts/{artifact.Id}/zip"], TimeoutSeconds: 600),
            destination, token).ConfigureAwait(false);
    }
}
