using System.Management.Automation;
using System.Security;
using fullscreen.Commands;
using fullscreen.Monitoring;

namespace fullscreen.Tests;

[TestClass]
public sealed class MonitorSudoTests {
    [TestMethod]
    public void NoChallengeSkipsPromptAndAuthentication() {
        using var source = new Source { Required = false };
        var result = MonitorSudo.Prepare(source, _ => throw new AssertFailedException("Unnecessary callback."), CancellationToken.None);
        Assert.AreEqual(SudoPreparation.NotNeeded, result);
        Assert.AreEqual(1, source.Samples);
        Assert.AreEqual(0, source.Authentications);
        Assert.IsFalse(new ShowSystemMonitorCommand().Sudo.IsPresent);
    }

    [TestMethod]
    public void PromptRunsOnCallingThreadWithTargetAndUsesASecretCopy() {
        using var source = new Source { Required = true, Accept = true };
        using var original = new SecureString();
        original.AppendChar('x');
        original.MakeReadOnly();
        int thread = Environment.CurrentManagedThreadId;
        int prompts = 0;
        var result = MonitorSudo.Prepare(source, request => {
            prompts++;
            Assert.AreEqual(thread, Environment.CurrentManagedThreadId);
            Assert.AreEqual("debian-fixture", request.Target);
            Assert.AreEqual(source.SudoReason, request.Reason);
            return original;
        }, CancellationToken.None);
        Assert.AreEqual(SudoPreparation.Authenticated, result);
        Assert.AreEqual(1, prompts);
        Assert.AreEqual(1, source.Authentications);
        Assert.AreEqual(CollectorMetrics.Gpu, source.Requested);
        Assert.IsNotNull(source.Received);
        Assert.AreNotSame(original, source.Received);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = source.Received.Length);
        Assert.AreEqual(1, original.Length, "Caller-owned secrets must remain usable.");
    }

    [TestMethod]
    public void CancelledPromptKeepsUnprivilegedSourceWithoutAuthenticating() {
        using var source = new Source { Required = true };
        Assert.AreEqual(SudoPreparation.Cancelled, MonitorSudo.Prepare(source, _ => null, CancellationToken.None));
        Assert.AreEqual(0, source.Authentications);
        Assert.AreEqual(1, source.Samples);
    }

    [TestMethod]
    public void RejectedAuthenticationIsOneAttemptAndReportsFailure() {
        using var source = new Source { Required = true, Accept = false };
        using var password = new SecureString();
        Assert.AreEqual(SudoPreparation.Failed, MonitorSudo.Prepare(source, _ => password, CancellationToken.None));
        Assert.AreEqual(1, source.Authentications);
        Assert.AreEqual(1, source.Samples);
    }

    [TestMethod]
    public void CancellationPreventsSamplingAndUserPrompt() {
        using var source = new Source { Required = true };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => MonitorSudo.Prepare(source,
            _ => throw new AssertFailedException("Cancelled collection prompted."), cancellation.Token));
        Assert.AreEqual(0, source.Samples);
        Assert.AreEqual(0, source.Authentications);
    }

    [TestMethod]
    public void CallbackExceptionsPropagateBeforeAuthentication() {
        using var source = new Source { Required = true };
        var failure = new InvalidOperationException("callback failed");
        Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(() =>
            MonitorSudo.Prepare(source, _ => throw failure, CancellationToken.None)));
        Assert.AreEqual(0, source.Authentications);
    }

    [TestMethod]
    public void PromptRejectsPlaintextAndMultipleResultsBeforeAuthentication() {
        using var source = new Source { Required = true };
        var error = Assert.ThrowsExactly<ArgumentException>(() => MonitorSudo.Prepare(source,
            _ => MonitorSudo.PasswordFromPrompt([PSObject.AsPSObject("not-a-secret-fixture")]), CancellationToken.None));
        Assert.DoesNotContain("not-a-secret-fixture", error.Message);
        using var password = new SecureString();
        Assert.ThrowsExactly<ArgumentException>(() => MonitorSudo.PasswordFromPrompt([PSObject.AsPSObject(password), PSObject.AsPSObject(password)]));
        Assert.AreSame(password, MonitorSudo.PasswordFromPrompt([PSObject.AsPSObject(password)]));
        Assert.IsNull(MonitorSudo.PasswordFromPrompt([]));
        Assert.AreEqual(0, source.Authentications);
    }

    private sealed class Source : IMonitorSource, IMonitorSudoSource {
        internal bool Required, Accept;
        internal int Samples, Authentications;
        internal CollectorMetrics Requested;
        internal SecureString? Received;
        public string Label => "debian-fixture";
        public bool IsRemote => true;
        public string SudoReason => "Read GPU counters for this fixture";
        public void Sample(CollectorFrame destination, CancellationToken cancellationToken) => Sample(destination, CollectorMetrics.All, cancellationToken);
        public void Sample(CollectorFrame destination, CollectorMetrics metrics, CancellationToken cancellationToken) { Samples++; Requested = metrics; }
        public bool NeedsSudoAuthentication(CancellationToken token) => Required;
        public bool AuthenticateSudo(SecureString password, CancellationToken token) {
            Authentications++;
            Received = password;
            _ = password.Length;
            return Accept;
        }
        public void Dispose() { }
    }
}
