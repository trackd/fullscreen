[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', 'ModulePath', Justification = 'Pester container data is consumed by BeforeAll.')]
[CmdletBinding()]
param([string]$ModulePath = (Join-Path $PSScriptRoot '../output/fullscreen.psd1'))

BeforeAll {
    Import-Module $ModulePath -Force
    $example = Join-Path $PSScriptRoot '../examples/EventViewer.ps1'
    $null = & $example -DefinitionOnly
    function Wait-EventQuery {
        param($Tick, $Working)
        $deadline = [datetime]::UtcNow.AddSeconds(15)
        do {
            $null = & $Tick @{ Kind = 'Tick' } $Working
            if ([datetime]::UtcNow -gt $deadline) { throw "Worker did not finish: $($Working.status)" }
            Start-Sleep -Milliseconds 20
        } while ($Working.runtime.shell -or $Working.runtime.desired)
    }
    function Get-EventState {
        param($Definition, $State)
        $working = @{}
        foreach ($key in $Definition.State.Keys) { $working[$key] = $Definition.State[$key] }
        foreach ($key in $State.Keys) { $working[$key] = $State[$key] }
        return $working
    }
    function Invoke-EventUiInput {
        param($Ui, [string]$Kind, [int]$X = 0, [int]$Y = 0, [int]$WheelDelta = 0, [string]$Key)
        if ($Key) {
            $inputEvent = [Activator]::CreateInstance($Ui.TerminalAssembly.GetType('XenoAtom.Terminal.TerminalKeyEvent'))
            $inputEvent.Key = [Enum]::Parse($inputEvent.GetType().GetProperty('Key').PropertyType, $Key)
        }
        else {
            $inputEvent = [Activator]::CreateInstance($Ui.TerminalAssembly.GetType('XenoAtom.Terminal.TerminalMouseEvent'))
            $inputEvent.Kind = [Enum]::Parse($inputEvent.GetType().GetProperty('Kind').PropertyType, $Kind)
            $inputEvent.Button = [Enum]::Parse($inputEvent.GetType().GetProperty('Button').PropertyType, 'Left')
            $inputEvent.X = $X; $inputEvent.Y = $Y; $inputEvent.WheelDelta = $WheelDelta
        }
        $Ui.Backend.PushEvent($inputEvent)
        & $Ui.Tick
        & $Ui.Tick
    }
}

Describe 'Event Viewer example' {
    It 'Leaves theme defaults to Invoke-Tui and forwards only explicitly supplied themes' -Skip:(-not $IsWindows) {
        Mock Invoke-Tui {}
        & $example
        Should -Invoke Invoke-Tui -Times 1 -Exactly -ParameterFilter { $null -eq $Theme }
        & $example -Theme 'Light Modern'
        Should -Invoke Invoke-Tui -Times 1 -Exactly -ParameterFilter { $Theme -eq 'Light Modern' }
        & $example -Theme @{ Base = 'Light Modern'; Panel = '#abc' }
        Should -Invoke Invoke-Tui -Times 1 -Exactly -ParameterFilter { $Theme -is [Collections.IDictionary] -and $Theme.Panel -eq '#abc' }
    }

    It 'Starts the published Event Viewer layout through the real Invoke-Tui host (<Minimal>)' -Tag Interactive -ForEach @(
        @{ Minimal = $false }
        @{ Minimal = $true }
    ) {
        $definition = & $example -DefinitionOnly
        $definition.State.summary = '2000 shown / 2000 loaded'
        $result = $definition | Invoke-Tui -Minimal:$Minimal -TickInterval 16 -OnTick {
            param($tuiEvent, $state)
            if ($tuiEvent.Kind -eq 'Tick' -and $state.ContainsKey('logs')) {
                @{ Close = $true; Value = 'Event Viewer startup completed' }
            }
        }
        $result.Cancelled | Should -BeFalse
        $result.Value | Should -Be 'Event Viewer startup completed'
    }

    It 'Renders its complete portable layout and wrapped detail panes at small and large sizes' {
        $definition = & $example -DefinitionOnly
        $definition.State.summary = '2000 shown / 2000 loaded'
        $assembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'fullscreen' -and $_.GetType('fullscreen.Custom.TuiDocument') } | Select-Object -First 1
        $document = $assembly.GetType('fullscreen.Custom.TuiDocument').GetMethod('FromObject').Invoke($null, @($definition))
        $app = [Activator]::CreateInstance($assembly.GetType('fullscreen.Custom.CustomTuiApp'),
            [object[]]@($document, $null, $null, $null, 250, $true, $null, [Threading.CancellationToken]::None))
        try {
            foreach ($minimal in @($false, $true, $false)) {
                $app.SetMinimal($minimal)
                $app.Controls['root'].IsVisible | Should -BeTrue
            }
            $app.Controls['workspace'].First | Should -Not -BeNullOrEmpty
            $app.Controls['general'].Content.GetType().Name | Should -Be 'Paragraph'
            $ui = $app.Root.GetType().Assembly
            $renderer = $ui.GetType('XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer').GetMethod('Render')
            foreach ($size in @(@(140, 42), @(80, 24))) {
                $snapshot = $renderer.Invoke($null, [object[]]@($app.Root, $size[0], $size[1], $null))
                $snapshot.Width | Should -Be $size[0]
                ($snapshot.ToMarkupLines() -join "`n") | Should -Match 'EVENT VIEWER'
                $app.Controls['events'].Bounds.Height | Should -BeGreaterThan 0
                $app.Controls['general'].Bounds.Height | Should -BeGreaterThan 0
                $app.Controls['general'].Bounds.Bottom | Should -BeLessOrEqual $size[1]
                $app.Controls['footer'].Bounds.Height | Should -Be 1
                $app.Controls['summary'].Bounds.Right | Should -Be $app.Controls['searchRow'].Bounds.Right
                $app.Controls['search'].Bounds.Width | Should -BeGreaterOrEqual 12
                $app.Controls['search'].Bounds.Right | Should -BeLessOrEqual $app.Controls['summary'].Bounds.X
                $app.Controls['search'].Placeholder | Should -Be 'Provider, ID, level or time'
                @($app.Controls.Values | Where-Object { $_.GetType().Name -eq 'Button' -and $_.Content.Text -eq 'Find' }).Count | Should -Be 1
                if ($size[0] -eq 140) {
                    $app.Controls['title'].Bounds.Y | Should -Be $app.Controls['menu'].Bounds.Y
                    $app.Controls['target'].Bounds.X | Should -BeGreaterThan $app.Controls['title'].Bounds.X
                    $app.Controls['target'].Bounds.Right | Should -BeLessOrEqual $app.Controls['menu'].Bounds.X
                }
                $app.Controls['eventPanes'].Bounds.X | Should -BeGreaterThan $app.Controls['navigation'].Bounds.X
            }
        }
        finally { $app.Dispose() }
        $custom = & $example -DefinitionOnly -LogName 'Fixture/Operational'
        $custom.State.logs | Should -Contain 'Fixture/Operational'
        $custom.State.selectedLog | Should -Be 'Fixture/Operational'
        $searchNode = $definition.Root.Children[0].Children[1].Children[0].Children[1]
        $searchNode.Background | Should -Be ''
        $searchNode.Foreground | Should -Be ''
    }

    It 'Rejects invalid filters, applies literal metadata search, and keeps only the newest queued request' -Skip:(-not $IsWindows) {
        Mock Invoke-Tui {
            $working = Get-EventState $Definition $State
            $working.runtime.desired = $null
            $working.eventIds = '1,invalid'
            $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
            $working.status | Should -Match 'comma-separated'
            $working.runtime.desired | Should -BeNullOrEmpty
            $working.eventIds = '1,42'
            $working.level = 'Critical + Error'
            $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
            $working.runtime.desired.Ids | Should -Be @(1, 42)
            $working.runtime.desired.Level | Should -Be @(1, 2)
            $working.level = 'All levels'
            $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
            @($working.runtime.desired.Level).Count | Should -Be 0
            $working.runtime.allRecords = @(
                [pscustomobject]@{ Time = 'Today'; Level = 'Error'; Provider = 'Source[1]'; Id = 42; RecordId = 99 }
                [pscustomobject]@{ Time = 'Today'; Level = 'Error'; Provider = 'Source1'; Id = 43; RecordId = 100 }
            )
            $working.search = '[1]'
            $null = & $OnEvent @{ Id = 'find'; Kind = 'Click' } $working
            @($working.records).Count | Should -Be 1
            $working.records[0].Id | Should -Be 42

            # A slow superseded request cannot replace the final snapshot.
            $working.runtime.query = 'param($request) if ($request.LogName -eq "slow") { Start-Sleep -Seconds 10 }; [pscustomobject]@{ Provider=$request.LogName; Id=42 }'
            $working.logName = 'slow'
            $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
            $null = & $OnTick @{ Kind = 'Tick' } $working
            $working.logName = 'latest'
            $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
            $working.search = ''
            Wait-EventQuery $OnTick $working
            $working.records[0].Provider | Should -Be 'latest'
            $working.runtime.shell | Should -BeNullOrEmpty
            $null = & $OnEvent @{ Id = 'menu'; Kind = 'Click'; Value = 'Help' } $working
            $working.detailTab | Should -Be 2
            $working.selectedLog = 'slow'
            $null = & $OnEvent @{ Id = 'openLog'; Kind = 'Click' } $working
            $working.runtime.desired.LogName | Should -Be 'slow'
            $null = & $OnTick @{ Kind = 'Tick' } $working
            $null = & $OnEvent @{ Id = 'stop'; Kind = 'Click' } $working
            Wait-EventQuery $OnTick $working
            $working.status | Should -Match '^Query cancelled'
            $working.records[0].Provider | Should -Be 'latest'
            $reply = & $OnEvent @{ Id = 'menu'; Kind = 'Click'; Value = 'Close' } $working
            $reply.Close | Should -BeTrue
        }
        & $example
    }

    It 'Opens channels by mouse, scrolls the source viewport and opens events with Enter and double click' -Skip:(-not $IsWindows) {
        Mock Invoke-Tui {
            $working = Get-EventState $Definition $State
            $working.runtime.desired = $null
            $working.logs = @('Application', 'Security', 'System') + @(1..80 | ForEach-Object { "Fixture-Channel-$_" })
            $row = [pscustomobject]@{
                Time = 'Today'; Level = 'Error'; Provider = 'Fixture'; Id = 42; RecordId = 99
                LogName = 'Application'; CreatedUtc = [datetime]::UtcNow
            }
            $working.records = @(1..100 | ForEach-Object {
                [pscustomobject]@{ Time = 'Today'; Level = 'Error'; Provider = 'Fixture'; Id = 42; RecordId = $_; LogName = 'Application'; CreatedUtc = $row.CreatedUtc }
            })
            $working.runtime.loadedGeneration = $working.runtime.generation
            $assembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'fullscreen' -and $_.GetType('fullscreen.Custom.TuiDocument') } | Select-Object -First 1
            $document = $assembly.GetType('fullscreen.Custom.TuiDocument').GetMethod('FromObject').Invoke($null, @($Definition))
            $appType = $assembly.GetType('fullscreen.Custom.CustomTuiApp')
            $dispatcher = $assembly.GetType('fullscreen.Commands.InvokeTuiCommand').GetMethod('CreateDispatcher', [Reflection.BindingFlags]'Static,NonPublic')
            $handler = $dispatcher.Invoke($null, @($document, $null, $OnEvent, $null))
            $app = [Activator]::CreateInstance($appType, [object[]]@($document, $working, $handler, $null, 100, $false, $null, [Threading.CancellationToken]::None))
            $uiAssembly = $app.Root.GetType().Assembly
            $context = [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($uiAssembly)
            $terminalAssembly = $context.LoadFromAssemblyName([Reflection.AssemblyName]::new('XenoAtom.Terminal'))
            $size = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalSize'), [object[]]@(100, 34))
            $backend = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.Backends.InMemoryTerminalBackend'), [object[]]@($size, $null))
            $options = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalOptions'))
            $options.ImplicitStartInput = $true
            $terminalSession = $terminalAssembly.GetType('XenoAtom.Terminal.Terminal').GetMethod('Open').Invoke($null, [object[]]@($backend, $options, $true))
            $appOptions = [Activator]::CreateInstance($uiAssembly.GetType('XenoAtom.Terminal.UI.TerminalAppOptions'))
            $appOptions.EnableMouse = $true
            $appOptions.HostKind = [Enum]::Parse($appOptions.GetType().GetProperty('HostKind').PropertyType, 'Fullscreen')
            $terminal = [Activator]::CreateInstance($uiAssembly.GetType('XenoAtom.Terminal.UI.TerminalApp'), [object[]]@($app.Root, $terminalSession.Instance, $appOptions))
            $flags = [Reflection.BindingFlags]'Instance,NonPublic'
            $clock = @{ Time = [Diagnostics.Stopwatch]::GetTimestamp() }
            $tick = {
                $null = $app.Tick()
                $clock.Time += [long]([Diagnostics.Stopwatch]::Frequency / 100)
                $null = $terminal.GetType().GetMethod('Tick', $flags).Invoke($terminal, @($clock.Time))
            }.GetNewClosure()
            $ui = @{ Backend = $backend; TerminalAssembly = $terminalAssembly; Tick = $tick }
            try {
                $null = $terminal.GetType().GetMethod('BeginRun', $flags).Invoke($terminal, $null)
                & $tick; & $tick
                $logs = $app.Controls['logs']
                $logs.Bounds.Height | Should -BeLessThan 30 -Because 'The source list needs a bounded viewport, not its full natural channel count.'
                $app.Controls['general'].Bounds.Bottom | Should -BeLessOrEqual 34 -Because 'Event details must remain inside the terminal.'
                Invoke-EventUiInput -Ui $ui -Kind Down -X ($logs.Bounds.X + 3) -Y $logs.Bounds.Y
                Invoke-EventUiInput -Ui $ui -Kind Up -X ($logs.Bounds.X + 3) -Y $logs.Bounds.Y
                $app.State.logName | Should -Be 'Application'
                $app.State.runtime.desired.LogName | Should -Be 'Application'
                $generation = $app.State.runtime.generation
                Invoke-EventUiInput -Ui $ui -Kind Down -X ($logs.Bounds.X + 3) -Y $logs.Bounds.Y
                Invoke-EventUiInput -Ui $ui -Kind Up -X ($logs.Bounds.X + 3) -Y $logs.Bounds.Y
                $app.State.runtime.generation | Should -Be $generation -Because 'A redundant click must not restart the query.'
                $terminal.Focus($logs)
                Invoke-EventUiInput -Ui $ui -Key End
                $app.State.logName | Should -Be 'Fixture-Channel-80'
                Invoke-EventUiInput -Ui $ui -Kind Wheel -X ($logs.Bounds.X + 3) -Y ($logs.Bounds.Y + 1) -WheelDelta 1
                $app.State.logName | Should -Be 'Fixture-Channel-79'
                Invoke-EventUiInput -Ui $ui -Kind Wheel -X ($logs.Bounds.X + 3) -Y ($logs.Bounds.Y + 1) -WheelDelta -1
                $app.State.logName | Should -Be 'Fixture-Channel-80'
                Invoke-EventUiInput -Ui $ui -Key PageUp
                $logs.SelectedIndex | Should -BeLessThan 81
                $app.State.runtime.desired = $null
                $app.State.runtime.loadedGeneration = $app.State.runtime.generation
                $app.State.detailTab = 2
                $table = $app.Controls['events'].Surface
                $scrollBar = @($app.Controls['events'].EnumerateVisualsDepthFirst() | Where-Object { $_.GetType().Name -eq 'VScrollBar' })[0]
                $scrollBar.IsVisible | Should -BeTrue
                $scrollBar.Bounds.X | Should -Be $table.Bounds.Right
                Invoke-EventUiInput -Ui $ui -Kind Wheel -X ($table.Bounds.X + 4) -Y ($table.Bounds.Y + 2) -WheelDelta -1
                $terminal.Focus($table)
                Invoke-EventUiInput -Ui $ui -Key End
                $table.Model.Offset | Should -BeGreaterThan 0
                $scrollBar.Value | Should -Be $table.Model.Offset
                $offsetBeforeClick = $table.Model.Offset
                Invoke-EventUiInput -Ui $ui -Kind Down -X $scrollBar.Bounds.X -Y ($scrollBar.Bounds.Y + 1)
                Invoke-EventUiInput -Ui $ui -Kind Up -X $scrollBar.Bounds.X -Y ($scrollBar.Bounds.Y + 1)
                $table.Model.Offset | Should -BeLessThan $offsetBeforeClick
                $terminal.Focus($table)
                Invoke-EventUiInput -Ui $ui -Key Home
                $app.State.runtime.detailKey = ''
                $app.State.runtime.desired = $null
                $terminal.Focus($table)
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.detailTab | Should -Be 0
                $app.State.runtime.desired.Operation | Should -Be 'Detail'
                $detailKey = $app.State.runtime.detailKey
                $app.State.runtime.desired = $null
                $app.State.detailTab = 2
                Invoke-EventUiInput -Ui $ui -Kind Down -X ($table.Bounds.X + 5) -Y ($table.Bounds.Y + 1)
                Invoke-EventUiInput -Ui $ui -Kind Up -X ($table.Bounds.X + 5) -Y ($table.Bounds.Y + 1)
                Invoke-EventUiInput -Ui $ui -Kind Down -X ($table.Bounds.X + 5) -Y ($table.Bounds.Y + 1)
                Invoke-EventUiInput -Ui $ui -Kind Up -X ($table.Bounds.X + 5) -Y ($table.Bounds.Y + 1)
                $app.State.detailTab | Should -Be 0
                $app.State.runtime.detailKey | Should -Be $detailKey
                $app.State.runtime.desired | Should -BeNullOrEmpty -Because 'Opening an already requested event must not duplicate the detail read.'
                $menu = $app.Controls['menu']
                $terminal.Focus($menu)
                Invoke-EventUiInput -Ui $ui -Key Right
                Invoke-EventUiInput -Ui $ui -Key Right
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.detailTab | Should -Be 2
                $app.Controls['help'].Bounds.Height | Should -BeGreaterThan 0
                $app.State.detailTab = 0
                & $tick; & $tick
                $helpMenu = @($menu.EnumerateVisualsDepthFirst() | Where-Object { $_.GetType().Name -eq 'MenuBarItem' })[2]
                Invoke-EventUiInput -Ui $ui -Kind Down -X ($helpMenu.Bounds.X + 1) -Y $helpMenu.Bounds.Y
                Invoke-EventUiInput -Ui $ui -Kind Up -X ($helpMenu.Bounds.X + 1) -Y $helpMenu.Bounds.Y
                $app.State.detailTab | Should -Be 2 -Because 'Help must visibly open from an actual mouse click.'
                Invoke-EventUiInput -Ui $ui -Key Left
                Invoke-EventUiInput -Ui $ui -Key Left
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.runtime.desired.Operation | Should -Be 'Logs'
                Invoke-EventUiInput -Ui $ui -Key Right
                $app.State.eventIds = '42'
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.eventIds | Should -Be ''
                $app.State.runtime.desired.Operation | Should -Be 'Events'
                foreach ($id in @('refresh', 'openLog', 'apply', 'reset')) {
                    $app.Controls['navigation'].SelectedIndex = [int]($id -in 'apply', 'reset')
                    & $tick; & $tick
                    $terminal.Focus($app.Controls[$id])
                    Invoke-EventUiInput -Ui $ui -Key Enter
                    $app.State.runtime.desired.Operation | Should -Be 'Events' -Because "$id must run an event query."
                    $app.State.runtime.desired = $null
                }
                $terminal.Focus($app.Controls['stop'])
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.status | Should -Match 'cancelled'
                $app.State.runtime.allRecords = @($row)
                $terminal.Focus($app.Controls['search'])
                $textEvent = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalTextEvent'))
                $textEvent.Text = 'absent'
                $backend.PushEvent($textEvent)
                & $tick; & $tick
                $app.State.search | Should -Be 'absent'
                $terminal.Focus($app.Controls['find'])
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.State.records.Count | Should -Be 0
                $terminal.Focus($menu)
                Invoke-EventUiInput -Ui $ui -Key Right
                Invoke-EventUiInput -Ui $ui -Key Right
                Invoke-EventUiInput -Ui $ui -Key Enter
                $app.Tick() | Should -BeFalse -Because 'The actual public callback adapter must preserve the Close menu action.'
            }
            finally {
                $null = $terminal.GetType().GetMethod('EndRun', $flags).Invoke($terminal, $null)
                $terminal.DisposeAsync().AsTask().GetAwaiter().GetResult()
                $terminalSession.Dispose()
                $backend.Dispose()
                $app.Dispose()
            }
        }
        & $example
    }

    It 'Formats valid XML with built-in APIs and reports unavailable malformed and bounded XML' -Skip:(-not $IsWindows) {
        Mock Invoke-Tui {
            $format = $State.runtime.formatXml
            $pretty = & $format '<Event><System><EventID>42</EventID></System><EventData><Data Name="X">value</Data></EventData></Event>'
            $pretty | Should -Match "(?m)^  <System>"
            $pretty | Should -Match "(?m)^    <EventID>42</EventID>"
            $Definition.State.xml = $pretty
            $Definition.State.detailTab = 1
            $assembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'fullscreen' -and $_.GetType('fullscreen.Custom.TuiDocument') } | Select-Object -First 1
            $document = $assembly.GetType('fullscreen.Custom.TuiDocument').GetMethod('FromObject').Invoke($null, @($Definition))
            $app = [Activator]::CreateInstance($assembly.GetType('fullscreen.Custom.CustomTuiApp'),
                [object[]]@($document, $null, $null, $null, 250, $true, $null, [Threading.CancellationToken]::None))
            try {
                $snapshot = $app.Root.GetType().Assembly.GetType('XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer').GetMethod('Render').Invoke($null, @($app.Root, 140, 42, $null))
                $app.Controls['xml'].Text | Should -Be $pretty
                $app.Controls['xml'].Bounds.Height | Should -BeGreaterThan 1
                $lines = @($snapshot.ToMarkupLines())
                @($lines | Where-Object { $_ -match '<System>' -and $_ -notmatch 'EventID' }).Count | Should -BeGreaterThan 0
                @($lines | Where-Object { $_ -match '<EventID>42</EventID>' -and $_ -notmatch '<System>' }).Count | Should -BeGreaterThan 0
            }
            finally { $app.Dispose() }
            ([xml]$pretty).Event.EventData.Data.InnerText | Should -Be 'value'
            (& $format '<Event><bad></Event>') | Should -Match 'XML could not be formatted:'
            (& $format '') | Should -Match 'unavailable'
            (& $format ('<Event>' + ('x' * 70000) + '</Event>')) | Should -Match 'Truncated at 65536'
            (& $format ('<Event>' + ('<Data/>' * 7000) + '</Event>')).Length | Should -BeLessThan 65700
            (& $format '<!DOCTYPE Event SYSTEM "file:///unavailable"><Event/>') | Should -Match 'XML could not be formatted:'
        }
        & $example
    }

    It 'Reads bounded real Windows events and exact selected details through <Transport>' -ForEach @(
        @{ Transport = 'local runspace' }
        @{ Transport = 'out-of-process PSRP' }
        @{ Transport = 'Windows PowerShell 5.1 PSRP' }
    ) -Skip:(-not $IsWindows) {
        $script:eventTransport = $Transport
        Mock Invoke-Tui {
            $working = Get-EventState $Definition $State
            $remote = $null
            $child = $null
            try {
                if ($script:eventTransport -ne 'local runspace') {
                    $child = if ($script:eventTransport -eq 'Windows PowerShell 5.1 PSRP') {
                        [Management.Automation.Runspaces.PowerShellProcessInstance]::new([version]'5.1', $null, $null, $false)
                    }
                    else { [Management.Automation.Runspaces.PowerShellProcessInstance]::new() }
                    $remote = [Management.Automation.Runspaces.RunspaceFactory]::CreateOutOfProcessRunspace($null, $child)
                    $remote.Open()
                    $working.runtime.runspace = $remote
                }
                Wait-EventQuery $OnTick $working
                @($working.logs) | Should -Contain 'System'
                $working.hours = '720'; $working.limit = '25'
                $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
                Wait-EventQuery $OnTick $working
                $working.status | Should -Match '^Loaded '
                @($working.records).Count | Should -BeGreaterThan 0
                @($working.records).Count | Should -BeLessOrEqual 25
                $row = $working.records[0]
                $null = & $OnEvent @{ Id = 'events'; Kind = 'Selection'; Value = $row } $working
                Wait-EventQuery $OnTick $working
                $working.status | Should -Be 'Event details loaded. No log changes were made.'
                $working.detail | Should -Match "Record: $($row.RecordId)"
                $working.xml | Should -Match '<Event '

                $working.eventIds = '65535'; $working.provider = 'Nonexistent-EventViewer-Test-Provider'
                $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
                Wait-EventQuery $OnTick $working
                # Invalid providers are errors, not a success-shaped empty snapshot.
                $working.status | Should -Match '^Read failed:'
                $working.records[0].RecordId | Should -Be $row.RecordId
                $working.provider = $row.Provider
                $null = & $OnEvent @{ Id = 'apply'; Kind = 'Click' } $working
                Wait-EventQuery $OnTick $working
                $working.status | Should -Match '^Loaded 0 '
                @($working.records).Count | Should -Be 0
            }
            finally {
                if ($remote) { $remote.Dispose() }
                if ($child) { $child.Dispose() }
            }
        }
        & $example
    }
}
