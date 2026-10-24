using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace fullscreen.Development;
/// <summary>Defines authenticated JSON requests to an Azure DevOps organization.</summary>
public interface IAzureDevOpsClient {
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

/// <summary>Bounded REST transport. Credentials never enter process arguments; redirects are disabled.</summary>
public sealed class AzureDevOpsClient : IAzureDevOpsClient, IDisposable {
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
