BeforeDiscovery {
    $exampleRoot = Join-Path $PSScriptRoot '../examples'
    $scriptCases = Get-ChildItem $exampleRoot -Filter '*.ps1' -File |
        Where-Object { (Get-Command -Name $_.FullName).Parameters.ContainsKey('DefinitionOnly') } |
        ForEach-Object { @{ ExampleName = $_.Name; ExamplePath = $_.FullName } }
    $dataCases = Get-ChildItem $exampleRoot -Filter '*.psd1' -File | ForEach-Object { @{ ExampleName = $_.Name; ExamplePath = $_.FullName } }
    $graphCases = 'Line','Area','Bars','Step','Scatter','Stem','Histogram','BoxPlot','HorizontalBars','StackedBars','Heatmap','ErrorBars' | ForEach-Object { @{ GraphKind = $_ } }
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    $exampleRoot = Join-Path $PSScriptRoot '../examples'
}

Describe 'Custom TUI examples' {
    It 'Keeps user sessions standalone and makes process loading optional (<IncludeProcesses>)' -ForEach @(
        @{ IncludeProcesses = $false }
        @{ IncludeProcesses = $true }
    ) {
        $layout = & (Join-Path $exampleRoot 'UserSessions.ps1') -DefinitionOnly -IncludeProcesses:$IncludeProcesses
        $layout.Root.Children[0].Type | Should -BeExactly 'UserList'
        $layout.Root.Children[0].IncludeProcesses | Should -Be $IncludeProcesses
        $layout.Root.Children[0].SelectionBind | Should -BeExactly 'selectedUser'
        Mock Invoke-Tui {
            $selected = [pscustomobject]@{ UserName = 'fixture'; Logins = @(); Process = $null }
            $reply = & $Handler['accept:Click'] @{ Id = 'accept'; Kind = 'Click' } @{ selectedUser = $selected }
            $reply.Close | Should -BeTrue
            [object]::ReferenceEquals($reply.Value, $selected) | Should -BeTrue
            [pscustomobject]@{ Cancelled = $false; Value = $reply.Value }
        }
        $selectedUser = & (Join-Path $exampleRoot 'UserSessions.ps1') -IncludeProcesses:$IncludeProcesses
        $selectedUser.UserName | Should -BeExactly 'fixture'
        Should -Invoke Invoke-Tui -Exactly -Times 1
    }
    It 'Starts the reusable workbench populated and returns the selected original record' {
        Mock Invoke-Tui {
            $Definition.State.records.Count | Should -Be 12
            $Definition.State.details | Should -Match 'api-gateway'
            $Definition.State.source | Should -Match 'healthCheck: /health/ready'
            $working = @{}
            foreach ($key in $Definition.State.Keys) { $working[$key] = $Definition.State[$key] }
            $chosen = $State.records[2]
            $working.selected = $chosen
            $null = & $OnEvent @{ Id = 'inventory'; Kind = 'Selection'; Value = $chosen } $working
            $working.details | Should -Match $chosen.Name
            $working.source | Should -Match "replicas: $($chosen.Instances)"
            $reply = & $OnEvent @{ Id = 'accept'; Kind = 'Click' } $working
            $reply.Close | Should -BeTrue
            [object]::ReferenceEquals($reply.Value, $chosen) | Should -BeTrue
            $null = & $OnTick @{ Kind = 'Tick' } $working
            $working.metric | Should -BeGreaterThan 45
            $working.metric | Should -BeLessThan 70
            @{ Cancelled = $false; Value = $chosen }
        }
        $result = & (Join-Path $exampleRoot 'ReusableControls.ps1')
        $result.Name | Should -Be 'catalog'
        $result.Instances | Should -Be 4
    }

    It 'Shows inline JSON log data without reading a file' {
        Mock Invoke-Tui {
            $State.sourcePath | Should -BeNullOrEmpty
            $entries = @($Definition.State.content -split '\r?\n' | ConvertFrom-Json)
            $entries.Count | Should -Be 3
            $entries[1].level | Should -BeExactly 'Warning'
            $reply = & $OnTick $null $State
            $reply | Should -BeNullOrEmpty
            @{ Cancelled = $true }
        }
        & (Join-Path $exampleRoot 'LogTail.ps1')
    }
    It 'Still tails an explicitly supplied log file' {
        $logPath = Join-Path $TestDrive 'application.log'
        Set-Content -LiteralPath $logPath -Value 'Live log fixture'
        Mock Invoke-Tui {
            $reply = & $OnTick $null $State
            $reply.State.content | Should -BeExactly 'Live log fixture'
            $reply.State.status | Should -Match '^Read '
            @{ Cancelled = $true }
        }
        & (Join-Path $exampleRoot 'LogTail.ps1') -Path $logPath
    }
    It 'Uses the inline deployment form layout and preserves preview and submit results' {
        Mock Invoke-Tui {
            $Definition.Title | Should -BeExactly 'Deployment request'
            $working = @{ name = 'API'; environment = 'Production'; notify = $true }
            $preview = & $OnEvent @{ Id = 'preview'; Kind = 'Click' } $working
            $preview.State.message | Should -Match 'API.*Production'
            $reply = & $OnEvent @{ Id = 'submit'; Kind = 'Click' } $working
            $reply.Close | Should -BeTrue
            $reply.Value.Name | Should -BeExactly 'API'
            $reply.Value.Environment | Should -BeExactly 'Production'
            $reply.Value.Notify | Should -BeTrue
            [pscustomobject]@{ Cancelled = $false; Value = $reply.Value }
        }
        $result = & (Join-Path $exampleRoot 'Invoke-ContactForm.ps1')
        $result.Value.Name | Should -BeExactly 'API'
    }
    It 'Provides runnable example scripts' {
        @(Get-ChildItem $exampleRoot -Filter '*.ps1' -File).Count | Should -BeGreaterOrEqual 6
    }

    It 'Parses <ExampleName> and returns native layout data without starting jobs' -ForEach $scriptCases {
        $tokens = $null; $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($ExamplePath, [ref]$tokens, [ref]$parseErrors)
        $parseErrors | Should -BeNullOrEmpty
        $before = @(Get-Job).Count
        $definition = & $ExamplePath -DefinitionOnly
        $validated = $definition | New-TuiDefinition
        $validated.Root | Should -BeOfType ([hashtable])
        $validated.Title | Should -Not -BeNullOrEmpty
        @(Get-Job).Count | Should -Be $before
    }

    It 'Applies <GraphKind> to both live charts' -ForEach $graphCases {
        $layout = & (Join-Path $exampleRoot 'LiveChart.ps1') -DefinitionOnly -GraphKind $GraphKind
        $layout.Root.Children[1].GraphKind | Should -Be $GraphKind
        $layout.Root.Children[2].GraphKind | Should -Be $GraphKind
        ,$layout.State.load | Should -BeOfType ([array])
        ,$layout.State.queue | Should -BeOfType ([array])
        { $layout | New-TuiDefinition } | Should -Not -Throw
    }

    It 'Binds all twelve gallery plots to the same signal' {
        $gallery = & (Join-Path $exampleRoot 'GraphGallery.ps1') -DefinitionOnly
        $plots = @($gallery.Root.Children[1].Children)
        $plots.Count | Should -Be 12
        @($plots.GraphKind | Select-Object -Unique).Count | Should -Be 12
        foreach ($plot in $plots) { $plot.Bind | Should -Be 'latency'; $plot.Type | Should -Be 'Chart' }
        ,$gallery.State.latency | Should -BeOfType ([array])
    }

    It 'Loads the native data example <ExampleName>' -ForEach $dataCases {
        { Import-TuiDefinition -LiteralPath $ExamplePath | New-TuiDefinition } | Should -Not -Throw
    }

    It 'Preserves pipeline items, empty input and help in the reusable picker module' {
        Import-Module (Join-Path $exampleRoot 'ToolModule.psm1') -Force
        try {
            $objects = @([pscustomobject]@{ Name = 'One' }, [pscustomobject]@{ Name = 'Two' })
            $layout = $objects | Select-TuiItem -DefinitionOnly
            { $layout | New-TuiDefinition } | Should -Not -Throw
            $layout.Root.Children[0].Items.Count | Should -Be 2
            @(@() | Select-TuiItem).Count | Should -Be 0
            (Get-Help Select-TuiItem -Full).Examples.Example | Should -Not -BeNullOrEmpty
        }
        finally { Remove-Module ToolModule }
    }
}
