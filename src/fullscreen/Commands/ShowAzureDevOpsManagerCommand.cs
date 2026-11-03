using System.Management.Automation;
using fullscreen.Development;

namespace fullscreen.Commands;
/// <summary>Selects the Azure DevOps resource page shown in the developer TUI.</summary>
public enum AzureDevOpsView {
    /// <summary>Shows the boards view.</summary>
    Boards,
    /// <summary>Shows the work items view.</summary>
    WorkItems,
    /// <summary>Shows the stories view.</summary>
    Stories,
    /// <summary>Shows the tasks view.</summary>
    Tasks,
    /// <summary>Shows the pull requests view.</summary>
    PullRequests,
    /// <summary>Shows the pipelines view.</summary>
    Pipelines,
    /// <summary>Shows the team's configured backlog levels in planning order.</summary>
    Backlog
}

/// <summary>Browse Azure DevOps boards, work items and development activity in a fullscreen TUI.</summary>
[Cmdlet(VerbsCommon.Show, "AzureDevOpsManager")]
[Alias("fado")]
public sealed class ShowAzureDevOpsManagerCommand : ShowDeveloperCommand {
    /// <summary>Required Azure DevOps Services organization name or HTTPS dev.azure.com organization URL.
    /// Legacy organization.visualstudio.com URLs are accepted. Azure DevOps Server is not
    /// supported.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Organization { get; set; } = "";

    /// <summary>Required project name or ID.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Project { get; set; } = "";

    /// <summary>Optional team name or ID. Limits work item queries to configured team area paths.
    /// Boards and sprint selection use the default team when omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Team { get; set; }

    /// <summary>Entra tenant GUID or domain. When omitted, use the Azure CLI selected account tenant.
    /// FULLSCREEN_AZURE_TENANT_ID supplies a configured default. Use the Azure DevOps organization tenant
    /// for guest access.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? TenantId { get; set; }

    /// <summary>Initial tab: Boards (default), WorkItems, Stories, Tasks, PullRequests or
    /// Pipelines or Backlog.</summary>
    [Parameter]
    public AzureDevOpsView View { get; set; } = AzureDevOpsView.Boards;

    /// <summary>Story work item type, default User Story for Agile. Use Product Backlog Item for Scrum or
    /// Issue for Basic/custom processes. Applies to the Stories tab and create-story action.</summary>
    [Parameter]
    [ArgumentCompletions("User Story", "Product Backlog Item", "Requirement", "Issue")]
    [ValidateNotNullOrEmpty]
    public string StoryType { get; set; } = "User Story";

    /// <inheritdoc/>
    protected override void EndProcessing() =>
        Run((_, stop) => {
            // Validate the destination before launching any external authentication UI.
            using (var validation = new AzureDevOpsClient(Organization, "validate", true)) {
            }

            var az = InvokeCommand.GetCommand("az", CommandTypes.Application) as ApplicationInfo ?? throw new InvalidOperationException("Azure CLI is required. Install Azure CLI and ensure az is on PATH, then rerun fado.");
            string? tenant = TenantId ?? Environment.GetEnvironmentVariable("FULLSCREEN_AZURE_TENANT_ID");
            if (tenant == "organizations") {
                tenant = null;
            }

            if (tenant is not null
                && !(Guid.TryParse(tenant, out Guid parsedTenant)
                || Uri.CheckHostName(tenant) == UriHostNameType.Dns && tenant.Contains('.'))) {
                throw new ArgumentException("TenantId must be a tenant GUID or domain.");
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected) {
                throw new InvalidOperationException("Fullscreen requires an interactive terminal with VT input/output.");
            }

            string powerShell = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(PSObject).Assembly.Location)!,
                OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh"
            );
            var authentication = new AzureCliAuthentication(
                new CommandRunner(),
                az.Path,
                powerShell,
                SessionState.Path.CurrentFileSystemLocation.Path,
                tenant
            );
            AzureDevOpsClient? client = null;
            try {
                Host.UI.WriteLine("Using Azure CLI sign-in. Complete browser sign-in if prompted; Ctrl+C cancels.");
                authentication.SignInAsync(stop).GetAwaiter().GetResult();
                client = new AzureDevOpsClient(Organization, authentication);
                return new AzureDevOpsBackend(client, Project, Team, PageSize, StoryType);
            }
            catch {
                if (client is null) {
                    authentication.Dispose();
                }
                else {
                    client.Dispose();
                }

                throw;
            }
        }, (int)View, "FullscreenAzureDevOpsManagerFailed");
}
