namespace fullscreen.Development;

internal sealed record PipelineNode(string Id, string? ParentId, string Kind, string Name, string Status,
    string Duration, string? LogId = null, string Details = "");
internal sealed record PipelineArtifact(string Id, string Name, string Size, bool Expired = false);
internal sealed record PipelineSnapshot(string Title, string Status, PipelineNode[] Nodes, PipelineArtifact[] Artifacts, string Notice);
internal sealed record PipelineLog(string Text, string Notice = "");

internal interface IPipelineBackend {
    bool IsPipelineView(int view);
    Task<PipelineSnapshot> ReadPipelineAsync(DeveloperItem run, CancellationToken token);
    Task<PipelineLog> ReadPipelineLogAsync(DeveloperItem run, PipelineNode node, CancellationToken token);
    Task DownloadPipelineArtifactAsync(DeveloperItem run, PipelineArtifact artifact, string destination, CancellationToken token);
}

internal static class PipelineFiles {
    internal const long MaximumBytes = 1024L * 1024 * 1024;
    internal static async Task CopyNewAsync(Stream source, string destination, CancellationToken token) {
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        bool complete = false;
        try {
            byte[] buffer = new byte[65536];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) {
                total += count;
                if (total > MaximumBytes) {
                    throw new IOException("Artifact exceeds the 1 GiB download limit.");
                }
                await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            await target.FlushAsync(token).ConfigureAwait(false);
            complete = true;
        }
        finally {
            if (!complete) {
                await target.DisposeAsync().ConfigureAwait(false);
                File.Delete(destination);
            }
        }
    }
}
