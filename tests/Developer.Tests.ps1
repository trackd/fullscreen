BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Developer' -Tag 'Integration' {
    It 'Reads every Git view from an owned temporary repository' {
        $gitCommand = Get-Command -Name Show-GitManager -ErrorAction Stop
        $githubCommand = Get-Command -Name Show-GitHubManager -ErrorAction Stop
        $null = Get-Command -Name git -ErrorAction Stop
        foreach ($command in @($gitCommand, $githubCommand)) {
            foreach ($name in @('Path', 'View', 'ReadOnly', 'RefreshInterval', 'PageSize')) {
                [bool]($command.Parameters.ContainsKey($name)) | Should -BeTrue -Because "Missing developer parameter $name"
            }
            ($command.Parameters['ReadOnly'].ParameterType) | Should -Be ([switch]) -Because 'ReadOnly must be a switch.'
            ((Get-Help -Name $command.Name -Full).Synopsis) | Should -Match ('fullscreen') -Because 'Developer help missing.'
        }
        $fixtureRoot = Join-Path -Path ($TestDrive) -ChildPath ('Fullscreen-Developer-' + [guid]::NewGuid().ToString('N'))
        $null = [System.IO.Directory]::CreateDirectory($fixtureRoot)
        & git -C $fixtureRoot init --initial-branch=main
        ($LASTEXITCODE) | Should -Be (0) -Because 'Fixture init failed.'
        & git -C $fixtureRoot config core.excludesFile (Join-Path $fixtureRoot '.git/info/exclude')
        ($LASTEXITCODE) | Should -Be (0) -Because 'Fixture exclusion configuration failed.'
        [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'hello.cs'), "// Fullscreen developer fixture`nclass Hello { }`n")
        $assembly = $gitCommand.ImplementingType.Assembly
        $runner = [Activator]::CreateInstance($assembly.GetType('fullscreen.Development.CommandRunner'))
        $backend = $assembly.GetType('fullscreen.Development.GitBackend').GetConstructors()[0].Invoke([object[]]@([string]$fixtureRoot, $runner.PSObject.BaseObject, [int]50))
        foreach ($gitView in 0..7) {
            $page = $backend.LoadAsync($gitView, 0, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
            [bool]($gitView -eq 0 -and $page.Items.Count -ne 1) | Should -BeFalse -Because 'Expected one untracked fixture file.'
            [bool]($gitView -eq 2 -and $page.Items.Count -ne 0) | Should -BeFalse -Because 'Expected empty history on a new repository.'
            [bool]($gitView -eq 5 -and $page.Items.Count -ne 1) | Should -BeFalse -Because 'Expected the main worktree.'
        }
        foreach ($newView in @('Worktrees', 'Stashes', 'Tags')) {
            ([Enum]::GetNames($gitCommand.Parameters['View'].ParameterType)) | Should -Contain ($newView) -Because "Missing Git view $newView"
        }
    }
}
