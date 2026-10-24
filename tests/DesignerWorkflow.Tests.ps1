BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    $command = Get-Command fullscreen\Invoke-Tui
    $assembly = $command.ImplementingType.Assembly
    $documentType = $assembly.GetType('fullscreen.Custom.TuiDocument')
    $eventType = $assembly.GetType('fullscreen.Custom.TuiEvent')
    $createDispatcher = $command.ImplementingType.GetMethod('CreateDispatcher', [Reflection.BindingFlags]'Static,NonPublic')
    $definition = @{
        Root = @{ Id = 'root'; Type = 'VStack'; Children = @(
            @{ Id = 'name'; Type = 'TextBox'; Bind = 'name' }
            @{ Id = 'save'; Type = 'Button'; Text = 'Save' }
        ) }
    }
    $document = $documentType.GetMethod('FromObject').Invoke($null, [object[]]@($definition))
}

Describe 'Designer workflow' {
    It 'Dispatches named handlers with shared state and uses OnEvent for unmatched events' {
        $state = @{ name = 'Sam'; fallback = 0; tick = 0 }
        $handlers = @{
            ':Start' = { param($event, $state) $state.started = $true }
            'SAVE:Click' = { param($event, $state) @{ State = @{ greeting = "Hello $($state.name)" }; Close = $true; Value = $state.name } }
        }
        $fallback = { param($event, $state) $state.fallback++ }
        $onTick = { param($event, $state) $state.tick++ }
        $dispatch = $createDispatcher.Invoke($null, [object[]]@($document, $handlers, $fallback, $onTick))
        $start = [Activator]::CreateInstance($eventType, [object[]]@('', 'Start', $null))
        $null = $dispatch.Invoke($start, $state)
        $state.started | Should -BeTrue
        $click = [Activator]::CreateInstance($eventType, [object[]]@('save', 'Click', $null))
        $reply = $dispatch.Invoke($click, $state)
        $reply.State.greeting | Should -Be 'Hello Sam'
        $reply.Close | Should -BeTrue
        $reply.Value | Should -Be 'Sam'
        $state.fallback | Should -Be 0
        $change = [Activator]::CreateInstance($eventType, [object[]]@('name', 'Change', 'Sam'))
        $null = $dispatch.Invoke($change, $state)
        $state.fallback | Should -Be 1
        $tick = [Activator]::CreateInstance($eventType, [object[]]@('', 'Tick', $null))
        $null = $dispatch.Invoke($tick, $state)
        $state.tick | Should -Be 1
        $state.fallback | Should -Be 1
    }
    It 'Validates handler IDs, supported events, scriptblocks and duplicate keys before opening a terminal' {
        foreach ($invalid in @(@{ 'missing:Click' = { } }, @{ 'name:Click' = { } }, @{ 'save:Click' = 'not a scriptblock' })) {
            { $createDispatcher.Invoke($null, [object[]]@($document, $invalid, $null, $null)) } | Should -Throw
        }
        $duplicate = [hashtable]::new([StringComparer]::Ordinal)
        $duplicate['save:Click'] = { }
        $duplicate['SAVE:CLICK'] = { }
        { $createDispatcher.Invoke($null, [object[]]@($document, $duplicate, $null, $null)) } | Should -Throw '*Duplicate TUI handler*'
    }
    It 'Routes a Tick handler instead of OnTick and retains reply validation' {
        $state = @{ tick = 0; fallback = 0 }
        $dispatch = $createDispatcher.Invoke($null, [object[]]@($document, @{ ':Tick' = { param($event, $state) $state.tick++ } }, $null, { param($event, $state) $state.fallback++ }))
        $tick = [Activator]::CreateInstance($eventType, [object[]]@('', 'Tick', $null))
        $null = $dispatch.Invoke($tick, $state)
        $state.tick | Should -Be 1
        $state.fallback | Should -Be 0
        $invalidReply = $createDispatcher.Invoke($null, [object[]]@($document, @{ ':Tick' = { 'unexpected output' } }, $null, $null))
        { $invalidReply.Invoke($tick, $state) } | Should -Throw '*handlers must emit*'
    }
    It 'Exports a linked layout and preserves the script byte for byte when the layout changes' {
        $scriptPath = Join-Path $TestDrive 'My tool [one].ps1'
        $layoutPath = [IO.Path]::ChangeExtension($scriptPath, '.tui.psd1')
        $definition | Export-TuiDefinition -Path $scriptPath -Script -LinkedLayout -WhatIf
        Test-Path -LiteralPath $scriptPath | Should -BeFalse
        Test-Path -LiteralPath $layoutPath | Should -BeFalse
        $definition | Export-TuiDefinition -Path $scriptPath -Script -LinkedLayout
        Add-Content -LiteralPath $scriptPath -Value '# My hand-written handler changes'
        $before = [IO.File]::ReadAllBytes($scriptPath)
        $tokens = $null
        $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
        $parseErrors | Should -BeNullOrEmpty
        $first = & $scriptPath -DefinitionOnly
        $first.Root.Children.Count | Should -Be 2
        $changed = $definition | New-TuiDefinition
        $changed.Title = 'Updated layout'
        $changed.Root.Children += @{ Id = 'status'; Type = 'Text'; Text = 'Ready' }
        $changed | Export-TuiDefinition -Path $scriptPath -Script -LinkedLayout
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($scriptPath)) | Should -Be ([Convert]::ToBase64String($before))
        $next = & $scriptPath -DefinitionOnly
        $next.Title | Should -Be 'Updated layout'
        $next.Root.Children.Count | Should -Be 3
    }
    It 'Refuses to repurpose an unrelated existing script and leaves its contents untouched' {
        $scriptPath = Join-Path $TestDrive 'Existing.ps1'
        Set-Content -LiteralPath $scriptPath -Value '# Important user code'
        $before = Get-Content -LiteralPath $scriptPath -Raw
        { $definition | Export-TuiDefinition -Path $scriptPath -Script -LinkedLayout -ErrorAction Stop } | Should -Throw '*not linked*'
        Get-Content -LiteralPath $scriptPath -Raw | Should -BeExactly $before
        Test-Path -LiteralPath ([IO.Path]::ChangeExtension($scriptPath, '.tui.psd1')) | Should -BeFalse
        { $definition | Export-TuiDefinition -Path $scriptPath -LinkedLayout -ErrorAction Stop } | Should -Throw '*requires Script*'
    }
    It 'Exposes typed property metadata with choices and defaults as native PowerShell data' {
        $chart = Get-TuiControl Chart
        $maximum = $chart.PropertyDetails | Where-Object Name -EQ Maximum
        $maximum.Type | Should -Be 'Double?'
        $maximum.DefaultValue | Should -BeNullOrEmpty
        $minimum = $chart.PropertyDetails | Where-Object Name -EQ Minimum
        $minimum.Type | Should -Be 'Double?'
        $minimum.DefaultValue | Should -BeNullOrEmpty
        $graph = $chart.PropertyDetails | Where-Object Name -EQ GraphKind
        $graph.Choices | Should -Contain 'BoxPlot'
        $monitor = Get-TuiControl PerformanceMonitor
        ($monitor.PropertyDetails | Where-Object Name -EQ GraphKind).Choices | Should -Not -Contain 'BoxPlot'
        $chart.PropertyDetails[0].GetType().FullName | Should -Be 'System.Management.Automation.PSCustomObject'
    }
    It 'Preserves tab captions across PSD1 export without changing content or event IDs' {
        $path = Join-Path $TestDrive 'captions.psd1'
        $layout = New-TuiDefinition -Root @{
            Id = 'tabs'; Type = 'Tabs'; Children = @(
                @{ Id = 'general_settings'; Type = 'Text'; TabTitle = 'General settings'; Text = 'Independent content' }
            )
        }
        $layout | Export-TuiDefinition -LiteralPath $path
        $copy = Import-TuiDefinition -LiteralPath $path
        $copy.Root.Children[0].TabTitle | Should -BeExactly 'General settings'
        $copy.Root.Children[0].Id | Should -BeExactly 'general_settings'
        $copy.Root.Children[0].Text | Should -BeExactly 'Independent content'
        (Get-TuiControl Text).Properties | Should -Contain 'TabTitle'
    }
    It 'WhatIf leaves an existing sibling layout unchanged when exporting a new linked script' {
        $scriptPath = Join-Path $TestDrive 'new-app.ps1'
        $layoutPath = [IO.Path]::ChangeExtension($scriptPath, '.tui.psd1')
        [IO.File]::WriteAllText($layoutPath, 'Keep existing layout')
        $definition | Export-TuiDefinition -Path $scriptPath -Script -LinkedLayout -WhatIf
        [IO.File]::ReadAllText($layoutPath) | Should -BeExactly 'Keep existing layout'
        Test-Path -LiteralPath $scriptPath | Should -BeFalse
    }
}
