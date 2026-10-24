using System.Globalization;
using System.Text;
using System.Text.Json;

namespace fullscreen.Development;
/// <summary>Azure CLI owns sign-in and refresh credentials; retain only the current access token.</summary>
/// <param name="runner">The child-process runner used by the backend.</param>
/// <param name="executable">The executable used for child-process requests.</param>
/// <param name="powerShell">The PowerShell instance used to execute callbacks.</param>
/// <param name="directory">The parent or working directory.</param>
/// <param name="tenant">The Microsoft Entra tenant used for authentication. Defaults to null.</param>
/// <param name="clock">The clock used to timestamp collection. Defaults to null.</param>
public sealed class AzureCliAuthentication(
    ICommandRunner runner,
    string executable,
    string powerShell,
    string directory,
    string? tenant = null,
    TimeProvider? clock = null
) : IAzureDevOpsTokenProvider, IDisposable {
    // Input travels through stdin as JSON, never as executable PowerShell source.
    private static readonly string Bridge = Convert.ToBase64String(Encoding.Unicode.GetBytes("""
        $ErrorActionPreference = 'Stop'
        $PSNativeCommandUseErrorActionPreference = $false
        $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
        $env:AZURE_CORE_ENABLE_BROKER_ON_WINDOWS = 'false'
        $env:AZURE_CORE_LOGIN_EXPERIENCE_V2 = 'off'
        $azureArguments = if ($request.Operation -eq 'Login') {
            @('login', '--allow-no-subscriptions', '--scope', '499b84ac-1321-427f-aa17-267ca6975798/.default', '--output', 'none', '--only-show-errors')
        }
        elseif ($request.Operation -eq 'Token') {
            @('account', 'get-access-token', '--resource', '499b84ac-1321-427f-aa17-267ca6975798', '--output', 'json', '--only-show-errors')
        }
        else {
            throw 'Unsupported Azure CLI operation.'
        }
        if ($request.TenantId) { $azureArguments += @('--tenant', $request.TenantId) }
        & $request.AzPath @azureArguments
        exit $LASTEXITCODE
        """));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1);
    /// <inheritdoc/>
    public void Dispose() =>
        _gate.Dispose();
    private string? _accessToken;
    private DateTimeOffset _expires;
    private CommandRequest Request(string operation) =>
        CreateRequest(powerShell, directory, executable, operation, tenant);
    internal static CommandRequest CreateRequest(
        string powerShell,
        string directory,
        string executable,
        string operation,
        string? tenant
    ) =>
        new(
            powerShell,
            directory,
            [
                "-NoProfile",
                "-EncodedCommand",
                Bridge
            ],
            Input: JsonSerializer.Serialize(new { AzPath = executable, Operation = operation, TenantId = tenant }),
            TimeoutSeconds: operation == "Login" ? 300 : 60
        );
    /// <summary>Starts Azure CLI sign-in for the configured tenant.</summary>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public async Task SignInAsync(CancellationToken token) {
        var current = await runner.RunAsync(Request("Token"), token).ConfigureAwait(false);
        if (current.ExitCode == 0 && !current.Truncated) {
            ReadToken(current.Output);
            return;
        }

        var login = await runner.RunAsync(Request("Login"), token).ConfigureAwait(false);
        if (login.ExitCode != 0 || login.Truncated) {
            throw Failure();
        }

        await GetTokenAsync(token).ConfigureAwait(false);
    }

    /// <summary>Obtains an Azure DevOps token through Azure CLI.</summary>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the access token.</returns>
    public async Task<string> GetTokenAsync(CancellationToken token) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (_accessToken is not null && _expires > _clock.GetUtcNow().AddMinutes(2)) {
                return _accessToken;
            }

            var result = await runner.RunAsync(Request("Token"), token).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.Truncated) {
                throw Failure();
            }

            ReadToken(result.Output);
            return _accessToken!;
        }
        finally {
            _gate.Release();
        }
    }

    private void ReadToken(string json) {
        try {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? access = root.GetProperty("accessToken").GetString();
            if (string.IsNullOrWhiteSpace(access) || access.Contains('\r') || access.Contains('\n')) {
                throw Failure();
            }

            DateTimeOffset expires = _clock.GetUtcNow().AddMinutes(3);
            if (root.TryGetProperty("expires_on", out var epoch)
                && long.TryParse(epoch.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)) {
                expires = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }

            if (expires <= _clock.GetUtcNow().AddSeconds(30)) {
                throw Failure();
            }

            _accessToken = access;
            _expires = expires;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException
            or ArgumentOutOfRangeException) {
            throw Failure();
        }
    }

    private static InvalidOperationException Failure() =>
        new("Azure CLI sign-in is required or could not acquire an Azure DevOps token. Close the TUI and run az login --allow-no-subscriptions for the organization's tenant, then reopen fado. CLI diagnostic output is withheld to protect credentials.");
}
