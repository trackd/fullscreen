namespace fullscreen.Development;
/// <summary>Provides cancellable acquisition of Azure DevOps access tokens.</summary>
public interface IAzureDevOpsTokenProvider {
    /// <summary>Obtains an access token for Azure DevOps requests.</summary>
    /// <param name="token">The token used to cancel the operation.</param>
    /// <returns>A task containing the access token.</returns>
    Task<string> GetTokenAsync(CancellationToken token);
}
