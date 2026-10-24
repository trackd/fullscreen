<#
.SYNOPSIS
Require approval before external contributors' fork workflows run.
.DESCRIPTION
Configures trackd/fullscreen after it becomes public. Trusts the repository
owner's pushes and requires approval for all external contributors, including
returning contributors. Run with GitHub CLI authenticated as trackd.
.EXAMPLE
pwsh -NoProfile -File ./.github/Set-ContributorWorkflowApproval.ps1 -WhatIf
.EXAMPLE
pwsh -NoProfile -File ./.github/Set-ContributorWorkflowApproval.ps1 -Verbose
.OUTPUTS
None.
.NOTES
CODEOWNERS and required code-owner reviews protect merging separately.
This script does not change visibility, branch rules, secrets or releases.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param()

$ErrorActionPreference = 'Stop'
$repositoryName = 'trackd/fullscreen'
$apiArguments = @('-H', 'X-GitHub-Api-Version: 2026-03-10')
$account = & gh api @apiArguments user | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $account.login -ne 'trackd') {
    $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new(
        [InvalidOperationException]::new('Authenticate GitHub CLI as trackd before configuring workflow approval.'),
        'RepositoryOwnerRequired', [Management.Automation.ErrorCategory]::PermissionDenied, $repositoryName))
}

if ($PSCmdlet.ShouldProcess($repositoryName, 'Require approval for all external contributors before fork workflows run')) {
    $metadata = & gh api @apiArguments "repos/$repositoryName" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $metadata.full_name -ne $repositoryName -or -not $metadata.permissions.admin) {
        $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new('Could not verify administrator access to trackd/fullscreen.'),
            'RepositoryAdministrationRequired', [Management.Automation.ErrorCategory]::PermissionDenied, $repositoryName))
    }
    if ($metadata.private -ne $false) {
        $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new('GitHub rejects public-fork approval settings while the repository is private. Run this again after making it public.'),
            'PublicRepositoryRequired', [Management.Automation.ErrorCategory]::InvalidOperation, $repositoryName))
    }
    $endpoint = "repos/$repositoryName/actions/permissions/fork-pr-contributor-approval"
    '{"approval_policy":"all_external_contributors"}' | & gh api @apiArguments --method PUT $endpoint --input -
    if ($LASTEXITCODE -ne 0) {
        $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new('GitHub rejected the contributor approval policy.'),
            'WorkflowApprovalConfigurationFailed', [Management.Automation.ErrorCategory]::InvalidOperation, $repositoryName))
    }
    $configured = & gh api @apiArguments $endpoint | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $configured.approval_policy -ne 'all_external_contributors') {
        $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new('Could not verify that all external contributors require workflow approval.'),
            'WorkflowApprovalVerificationFailed', [Management.Automation.ErrorCategory]::InvalidResult, $repositoryName))
    }
    Write-Verbose -Message 'Verified: owner pushes remain automatic; all external contributor fork workflows require approval.'
}
