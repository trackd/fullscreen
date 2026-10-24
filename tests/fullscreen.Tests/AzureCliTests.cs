using System.Text.Json;
using fullscreen.Development;

namespace fullscreen.Tests;

[TestClass]
public sealed class AzureCliTests {
    [TestMethod]
    public async Task PowerShellBridgePassesLiteralArgumentsAndConfiguresBrowserLoginWithoutChangingParentEnvironment() {
        string root = Path.Combine(Path.GetTempPath(), "Fullscreen-AzureCli-" + Guid.NewGuid().ToString("N"));
        string resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "Fullscreen-AzureCli-"), StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Unexpected CLI fixture path.");
        }
        Directory.CreateDirectory(root);
        try {
            string fake = Path.Combine(root, "fake az with spaces.ps1");
            File.WriteAllText(fake, "[pscustomobject]@{ Arguments = $args; Broker = $env:AZURE_CORE_ENABLE_BROKER_ON_WINDOWS; Selector = $env:AZURE_CORE_LOGIN_EXPERIENCE_V2 } | ConvertTo-Json -Compress");
            string tenant = "literal;$(value) 'quoted'";
            string? before = Environment.GetEnvironmentVariable("AZURE_CORE_ENABLE_BROKER_ON_WINDOWS");
            foreach (string operation in new[] { "Token", "Login" }) {
                var result = await new CommandRunner().RunAsync(AzureCliAuthentication.CreateRequest(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh", root, fake, operation, tenant), default);
                using var json = JsonDocument.Parse(result.RequireSuccess());
                string[] arguments = [.. json.RootElement.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()!)];
                Assert.AreEqual(tenant, arguments[^1]);
                Assert.AreEqual(operation == "Token" ? "account" : "login", arguments[0]);
                Assert.Contains(operation == "Token" ? "499b84ac-1321-427f-aa17-267ca6975798" : "499b84ac-1321-427f-aa17-267ca6975798/.default", arguments);
                Assert.AreEqual("false", json.RootElement.GetProperty("Broker").GetString());
                Assert.AreEqual("off", json.RootElement.GetProperty("Selector").GetString());
            }
            Assert.AreEqual(before, Environment.GetEnvironmentVariable("AZURE_CORE_ENABLE_BROKER_ON_WINDOWS"));
        }
        finally {
            Directory.Delete(resolved, true);
        }
    }
    [TestMethod]
    public async Task ReusesCliSessionAndCachesAccessTokenUntilNearExpiry() {
        var time = new Clock();
        var runner = new Runner(time);
        using var auth = Create(runner, time);
        await auth.SignInAsync(default);
        Assert.HasCount(1, runner.Requests);
        Assert.AreEqual("access-token", await auth.GetTokenAsync(default));
        Assert.HasCount(1, runner.Requests);
        time.Now = time.Now.AddMinutes(59);
        await auth.GetTokenAsync(default);
        Assert.HasCount(2, runner.Requests);
        var request = runner.Requests[0];
        Assert.AreEqual("pwsh.exe", request.FileName);
        Assert.AreEqual("-NoProfile", request.Arguments[0]);
        Assert.AreEqual("-EncodedCommand", request.Arguments[1]);
        using var payload = JsonDocument.Parse(request.Input!);
        Assert.AreEqual("path with spaces/az.cmd", payload.RootElement.GetProperty("AzPath").GetString());
        Assert.AreEqual("contoso.onmicrosoft.com", payload.RootElement.GetProperty("TenantId").GetString());
    }

    private static readonly string[] expected = ["Token", "Login", "Token"];

    [TestMethod]
    public async Task InitialMissingSessionLogsInButRefreshFailureNeverLaunchesBrowserOrLeaksDiagnostics() {
        var time = new Clock();
        var runner = new Runner(time) { FailToken = true };
        using var auth = Create(runner, time);
        await auth.SignInAsync(default);
        CollectionAssert.AreEqual(expected, runner.Requests.Select(Operation).ToArray());
        Assert.AreEqual(300, runner.Requests[1].TimeoutSeconds);
        time.Now = time.Now.AddHours(2);
        runner.FailToken = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetTokenAsync(default));
        Assert.IsFalse(error.Message.Contains("server-secret", StringComparison.Ordinal));
        Assert.AreEqual("Token", Operation(runner.Requests[^1]));
    }
    [TestMethod]
    public async Task InvalidCliTokenIsRejectedWithoutPrintingPayload() {
        var time = new Clock();
        var runner = new Runner(time) { Malformed = true };
        using var auth = Create(runner, time);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SignInAsync(default));
        Assert.IsFalse(error.Message.Contains("server-secret", StringComparison.Ordinal));
    }
    [TestMethod]
    public async Task DisposingClientDisposesItsAuthenticationProvider() {
        var time = new Clock();
        using var auth = Create(new Runner(time), time);
        using var client = new AzureDevOpsClient("fixture", auth);

        client.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => auth.GetTokenAsync(default));
    }

    private static AzureCliAuthentication Create(Runner runner, Clock time) =>
        new(runner, "path with spaces/az.cmd", "pwsh.exe", ".", "contoso.onmicrosoft.com", time);
    private static string Operation(CommandRequest request) {
        using var payload = JsonDocument.Parse(request.Input!);
        return payload.RootElement.GetProperty("Operation").GetString()!;
    }
    private sealed class Clock : TimeProvider {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        public override DateTimeOffset GetUtcNow() =>
            Now;
    }
    private sealed class Runner(Clock time) : ICommandRunner {
        public List<CommandRequest> Requests { get; } = [];
        public bool FailToken, Malformed;
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Operation(request) == "Login") {
                FailToken = false;
                return Task.FromResult(new CommandResult(0, "", ""));
            }
            return Task.FromResult(FailToken ? new CommandResult(1, "", "server-secret") : new CommandResult(0, Malformed ? "server-secret" : JsonSerializer.Serialize(new {
                accessToken = "access-token",
                expires_on = time.Now.AddHours(1).ToUnixTimeSeconds()
            }), ""));
        }
    }
}
