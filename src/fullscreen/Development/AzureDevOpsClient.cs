using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace fullscreen.Development;
/// <summary>Defines authenticated JSON requests to an Azure DevOps organization.</summary>
public interface IAzureDevOpsClient {
    /// <summary>Reads a bounded, paginated Analytics query for the selected project.</summary>
    Task<JsonElement> ReadAnalyticsAsync(string project, string query, CancellationToken token) =>
        throw new NotSupportedException("This transport does not support Azure Boards Analytics.");
    /// <summary>Downloads an organization-relative resource to a new file without extracting it.</summary>
    Task DownloadAsync(string path, string destination, CancellationToken token) =>
        throw new NotSupportedException("This Azure DevOps transport does not support downloads.");
    /// <summary>Gets the normalized Azure DevOps organization URL.</summary>
    string OrganizationUrl { get; }

    /// <summary>Gets the token used to request another resource page.</summary>
    string? ContinuationToken => null;

    /// <summary>Sends a JSON request to Azure DevOps.</summary>
    /// <param name="method">The HTTP request method.</param>
    /// <param name="path">The organization-relative request path.</param>
    /// <param name="body">The request or review body.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <param name="patch">Whether to use the JSON Patch content type. Defaults to false.</param>
    /// <returns>A task containing the parsed JSON response.</returns>
    Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken token,
        bool patch = false
    );
}

/// <summary>Bounded REST transport with credential-isolated artifact redirects.</summary>
public sealed class AzureDevOpsClient : IAzureDevOpsClient, IDisposable {
    /// <inheritdoc/>
    public async Task<JsonElement> ReadAnalyticsAsync(string project, string query, CancellationToken token) {
        string organization = _http.BaseAddress!.Host == "dev.azure.com"
            ? _http.BaseAddress.AbsolutePath.Trim('/')
            : Uri.EscapeDataString(_http.BaseAddress.Host[..^".visualstudio.com".Length]);
        var root = new Uri($"https://analytics.dev.azure.com/{organization}/{Uri.EscapeDataString(project)}/_odata/v3.0-preview/");
        var address = new Uri(root, query);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<JsonElement>();
        while (true) {
            if (!root.IsBaseOf(address) || address.UserInfo.Length != 0 || !address.IsDefaultPort || address.Scheme != "https"
                || !seen.Add(address.AbsoluteUri) || seen.Count > 100) {
                throw new InvalidOperationException("Analytics returned an unsafe, repeated or excessive continuation link.");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Authorization = _tokens is null ? _authorization
                : new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(timeout.Token).ConfigureAwait(false));
            request.Headers.Accept.Add(new("application/json"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new HttpRequestException($"Azure Boards Analytics returned {(int)response.StatusCode}. Check Analytics access and project permissions.", null, response.StatusCode);
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0) {
                if (buffer.Length + count > 4 * 1024 * 1024) {
                    throw new InvalidOperationException("Analytics response exceeds 4 MiB. Choose a smaller date range.");
                }
                buffer.Write(chunk, 0, count);
            }
            using var data = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            rows.AddRange(data.RootElement.GetProperty("value").EnumerateArray().Select(row => row.Clone()));
            if (rows.Count > 20000) {
                throw new InvalidOperationException("Analytics query exceeds 20,000 aggregate rows. Choose a smaller date range.");
            }
            if (!data.RootElement.TryGetProperty("@odata.nextLink", out var next)) {
                return JsonSerializer.SerializeToElement(new { value = rows });
            }
            address = new Uri(address, next.GetString() ?? throw new InvalidOperationException("Analytics returned an empty continuation link."));
        }
    }

    /// <inheritdoc/>
    public async Task DownloadAsync(string path, string destination, CancellationToken token) {
        if (path.StartsWith('/') || !Uri.TryCreate(path, UriKind.Relative, out _) || path.Contains("..", StringComparison.Ordinal)) {
            throw new ArgumentException("Expected relative API path.", nameof(path));
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        token = timeout.Token;
        var address = new Uri(_http.BaseAddress!, path);
        if (!_http.BaseAddress!.IsBaseOf(address)) {
            throw new ArgumentException("Expected an API path within the selected organization.", nameof(path));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = _tokens is null ? _authorization : new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(token).ConfigureAwait(false));
        request.Headers.Accept.Add(new("application/zip"));
        HttpResponseMessage response = await SendArtifactRequest(request, token).ConfigureAwait(false);
        try {
            for (int redirect = 0; (int)response.StatusCode is 301 or 302 or 303 or 307 or 308; redirect++) {
                if (redirect >= 5 || response.Headers.Location is not { } location) {
                    throw new HttpRequestException("Artifact redirect limit exceeded or redirect has no location.");
                }
                address = new Uri(address, location);
                bool organization = new Uri(OrganizationUrl + "/").IsBaseOf(address);
                bool storage = address.Host.EndsWith(".vsblob.vsassets.io", StringComparison.OrdinalIgnoreCase)
                    || address.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase);
                if (address.Scheme != "https" || address.UserInfo.Length != 0 || !address.IsDefaultPort || !(organization || storage)) {
                    throw new HttpRequestException("Artifact redirected outside the organization or supported Azure artifact storage.");
                }
                response.Dispose();
                using var redirected = new HttpRequestMessage(HttpMethod.Get, address);
                // Signed storage URLs must never receive the organization's credentials.
                if (organization) {
                    redirected.Headers.Authorization = request.Headers.Authorization;
                }
                response = await SendArtifactRequest(redirected, token).ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode) {
                throw new HttpRequestException($"Artifact download returned {(int)response.StatusCode}.", null, response.StatusCode);
            }
            if (response.Content.Headers.ContentLength > PipelineFiles.MaximumBytes) {
                throw new IOException("Artifact exceeds the 1 GiB download limit.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await PipelineFiles.CopyNewAsync(stream, destination, token).ConfigureAwait(false);
        }
        finally {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendArtifactRequest(HttpRequestMessage request, CancellationToken token) {
        try {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        }
        catch (HttpRequestException error) {
            throw new HttpRequestException("Artifact request failed. Check connectivity and artifact access.", null, error.StatusCode);
        }
    }

    private readonly HttpClient _http;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly IAzureDevOpsTokenProvider? _tokens;
    /// <summary>Gets the normalized Azure DevOps organization URL.</summary>
    public string OrganizationUrl { get; }
    /// <summary>Gets the token used to request another resource page.</summary>
    public string? ContinuationToken { get; private set; }

    /// <summary>Owns the supplied token provider when it implements IDisposable.</summary>
    /// <param name="organization">The Azure DevOps organization name or URL.</param>
    /// <param name="tokens">The platform metadata tokens.</param>
    /// <param name="handler">The callback that receives TUI events. Defaults to null.</param>
    public AzureDevOpsClient(
        string organization,
        IAzureDevOpsTokenProvider tokens,
        HttpMessageHandler? handler = null
    ) : this(organization, "provider", true, handler) {
        _tokens = tokens;
        _authorization = null;
    }

    /// <summary>Initializes a new AzureDevOpsClient instance.</summary>
    /// <param name="organization">The Azure DevOps organization name or URL.</param>
    /// <param name="credential">The credential used to establish a remote session.</param>
    /// <param name="bearer">The access token used to authorize requests.</param>
    /// <param name="handler">The callback that receives TUI events. Defaults to null.</param>
    public AzureDevOpsClient(string organization, string credential, bool bearer, HttpMessageHandler? handler = null) {
        if (!organization.Contains("://", StringComparison.Ordinal)) {
            organization = "https://dev.azure.com/" + Uri.EscapeDataString(organization);
        }

        if (!Uri.TryCreate(organization.TrimEnd('/') +
            "/", UriKind.Absolute, out var address) || address.Scheme != "https" || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 || !(address.Host == "dev.azure.com"
                && address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 1
                || address.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
                && address.AbsolutePath == "/")) {
            throw new ArgumentException("Organization must be an Azure DevOps Services organization name or HTTPS organization URL.");
        }

        if (string.IsNullOrWhiteSpace(credential)) {
            throw new ArgumentException("Supply AccessToken, PersonalAccessToken, AZURE_DEVOPS_EXT_PAT or AZURE_DEVOPS_PAT.");
        }

        OrganizationUrl = address.AbsoluteUri.TrimEnd('/');
        _authorization = new(
            bearer ? "Bearer" : "Basic",
            bearer ? credential : Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + credential))
        );
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    /// <summary>Sends an authenticated JSON request to Azure DevOps.</summary>
    /// <param name="method">The HTTP request method.</param>
    /// <param name="path">The organization-relative request path.</param>
    /// <param name="body">The request or review body.</param>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <param name="patch">Whether to use the JSON Patch content type. Defaults to false.</param>
    /// <returns>A task containing the parsed JSON response.</returns>
    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken token,
        bool patch = false
    ) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        token = timeout.Token;
        if (path.StartsWith('/') || !Uri.TryCreate(path, UriKind.Relative, out _)
            || path.Contains("..", StringComparison.Ordinal)) {
            throw new ArgumentException("Expected relative API path.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = _tokens is null ? _authorization : new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(token).ConfigureAwait(false));
        request.Headers.Accept.Add(new("application/json"));
        if (body is not null) {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                patch ? "application/json-patch+json" : "application/json"
            );
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        ContinuationToken = response.Headers.TryGetValues("x-ms-continuationtoken", out var continuation) ? continuation.FirstOrDefault() : null;
        // Server error text may contain request data or secrets. Report only status and request identity.
        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException(
                $"Azure DevOps returned {(int)response.StatusCode} ({response.StatusCode}). Check permissions, process rules or stale item revision; refresh before retrying.",
                null,
                response.StatusCode
            );
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0) {
            if (buffer.Length + count > 4 * 1024 * 1024) {
                throw new InvalidOperationException("Azure DevOps response exceeds 4 MiB. Reduce PageSize.");
            }

            buffer.Write(chunk, 0, count);
        }

        if (buffer.Length == 0) {
            return JsonSerializer.SerializeToElement(new { });
        }

        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        return document.RootElement.Clone();
    }

    /// <inheritdoc/>
    public void Dispose() {
        _http.Dispose();
        if (_tokens is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}
