using fullscreen.Commands;

namespace fullscreen.Tests;

[TestClass]
public sealed class ShowSystemMonitorTests {
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
