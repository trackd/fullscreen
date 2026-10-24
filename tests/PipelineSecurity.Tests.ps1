BeforeAll {
    $approvalScript = Join-Path $PSScriptRoot '../.github/Set-ContributorWorkflowApproval.ps1'
    $originalLastExitCode = $global:LASTEXITCODE
    $policyTestState = @{}
    # A local GitHub CLI stand-in keeps policy tests independent of real access.
    $githubStub = {
        $global:LASTEXITCODE = 0
        $request = $args -join ' '
        $body = @($input) -join ''
        $policyTestState.requests.Add($request)
        if ($request -match '--method PUT') {
            $policyTestState.submittedPolicy = $body | ConvertFrom-Json
            if ($policyTestState.putFails) { $global:LASTEXITCODE = 1 }
            return
        }
        if ($request -match ' user$') { return (@{ login = $policyTestState.accountLogin } | ConvertTo-Json) }
        if ($request -match '/fork-pr-contributor-approval$') { return (@{ approval_policy = $policyTestState.reportedPolicy } | ConvertTo-Json) }
        return (@{ full_name = 'trackd/fullscreen'; private = $policyTestState.isPrivate; permissions = @{ admin = $policyTestState.hasAdmin } } | ConvertTo-Json)
    }.GetNewClosure()
    Set-Item -Path Function:gh -Value $githubStub
}

AfterAll { $global:LASTEXITCODE = $originalLastExitCode }

Describe 'Contributor workflow approval setup' {
    BeforeEach {
        $policyTestState.requests = [Collections.Generic.List[string]]::new()
        $policyTestState.submittedPolicy = $null
        $policyTestState.isPrivate = $false
        $policyTestState.hasAdmin = $true
        $policyTestState.accountLogin = 'trackd'
        $policyTestState.reportedPolicy = 'all_external_contributors'
        $policyTestState.putFails = $false
    }
    It 'Requires approval for all external contributors and verifies the public repository policy' {
        & $approvalScript
        $policyTestState.submittedPolicy.approval_policy | Should -BeExactly 'all_external_contributors'
        @($policyTestState.requests | Where-Object { $_ -match '--method PUT' }).Count | Should -Be 1
        $policyTestState.requests[-1] | Should -Match 'repos/trackd/fullscreen/actions/permissions/fork-pr-contributor-approval$'
        @($policyTestState.requests | Where-Object { $_ -match 'visibility|secrets|branches/.*/protection|releases' }).Count | Should -Be 0
    }

    It 'Rejects a private repository before attempting any settings mutation' {
        $policyTestState.isPrivate = $true
        { & $approvalScript } | Should -Throw -ErrorId 'PublicRepositoryRequired*'
        @($policyTestState.requests | Where-Object { $_ -match '--method PUT' }).Count | Should -Be 0
    }

    It 'Does not mutate settings in WhatIf mode' {
        & $approvalScript -WhatIf
        @($policyTestState.requests | Where-Object { $_ -match '--method PUT' }).Count | Should -Be 0
        $policyTestState.submittedPolicy | Should -BeNullOrEmpty
    }

    It 'Rejects a different account before attempting a settings mutation' {
        $policyTestState.accountLogin = 'contributor'
        { & $approvalScript } | Should -Throw -ErrorId 'RepositoryOwnerRequired*'
        @($policyTestState.requests | Where-Object { $_ -match '--method PUT' }).Count | Should -Be 0
    }

    It 'Rejects unverified administrator access before attempting a settings mutation' {
        $policyTestState.hasAdmin = $false
        { & $approvalScript } | Should -Throw -ErrorId 'RepositoryAdministrationRequired*'
        @($policyTestState.requests | Where-Object { $_ -match '--method PUT' }).Count | Should -Be 0
    }

    It 'Reports an API rejection without claiming approval enforcement' {
        $policyTestState.putFails = $true
        { & $approvalScript } | Should -Throw -ErrorId 'WorkflowApprovalConfigurationFailed*'
        @($policyTestState.requests | Where-Object { $_ -match '/fork-pr-contributor-approval$' }).Count | Should -Be 0
    }

    It 'Rejects a weaker readback policy instead of claiming all contributors are covered' {
        $policyTestState.reportedPolicy = 'first_time_contributors'
        { & $approvalScript } | Should -Throw -ErrorId 'WorkflowApprovalVerificationFailed*'
    }
}
