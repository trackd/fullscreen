using fullscreen.Commands;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace fullscreen.Tests;

[TestClass]
public sealed class ShowSystemMonitorTests {
    [Cmdlet(VerbsCommon.New, "FixtureSession")]
    public sealed class DeniedSessionCommand : PSCmdlet {
        protected override void EndProcessing() => WriteError(new ErrorRecord(
            new System.Management.Automation.Remoting.PSRemotingTransportException("[ptp030] Connecting to remote server ptp030 failed: Access is denied."),
            "AccessDenied,PSSessionOpenFailed", ErrorCategory.OpenError, "ptp030"));
    }

    [TestMethod]
    public void ConnectionFailureRemainsTerminatingAndPreservesTheUnderlyingRemotingRecord() {
        var state = InitialSessionState.CreateDefault2();
        state.Commands.Add(new SessionStateCmdletEntry("New-FixtureSession", typeof(DeniedSessionCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using var shell = PowerShell.Create();
        shell.Runspace = runspace;
        shell.AddCommand("New-FixtureSession").AddParameter("ErrorAction", ActionPreference.Stop);
        var thrown = Assert.Throws<ActionPreferenceStopException>(() => shell.Invoke());
        var failure = RemoteSessionOptions.Failure(thrown, "ptp030");
        Assert.IsInstanceOfType<System.Management.Automation.Remoting.PSRemotingTransportException>(failure.Exception);
        Assert.AreEqual(thrown.ErrorRecord.FullyQualifiedErrorId, failure.FullyQualifiedErrorId);
        Assert.AreEqual(ErrorCategory.OpenError, failure.CategoryInfo.Category);
        Assert.AreEqual("ptp030", failure.TargetObject);
        Assert.Contains("Access is denied", failure.Exception.Message);
        Assert.DoesNotContain("preference variable", failure.Exception.Message);
    }

    [TestMethod]
    public void RemoteSessionOptionsForwardConfigurationAndOptionalCredential() {
        var credential = new object();
        var options = RemoteSessionOptions.Build("host", credential, "PowerShell.7");
        Assert.AreEqual("host", options["ComputerName"]);
        Assert.AreEqual("PowerShell.7", options["ConfigurationName"]);
        Assert.AreSame(credential, options["Credential"]);
        Assert.AreEqual("Stop", options["ErrorAction"]?.ToString());

        options = RemoteSessionOptions.Build("host", null, null);
        Assert.DoesNotContain("ConfigurationName", options.Keys.Cast<string>());
        Assert.DoesNotContain("Credential", options.Keys.Cast<string>());
    }
}
