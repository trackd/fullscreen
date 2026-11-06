BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\output\fullscreen.psd1') -Force
    $example = Join-Path $PSScriptRoot '..\examples\ChartShowcase.ps1'
    $capture = [System.Collections.Generic.List[hashtable]]::new()
    function Read-ShowcaseSixel {
        param([string]$Payload)
        $header = [regex]::Match($Payload, 'q"1;1;(\d+);(\d+)')
        if (-not $header.Success) {
            throw 'Sixel payload has no raster dimensions.'
        }
        $width = [int]$header.Groups[1].Value
        $height = [int]$header.Groups[2].Value
        $pixels = [byte[]]::new($width * $height)
        $x = 0
        $y = 0
        $color = 0
        $painted = 0
        $position = $header.Index + $header.Length
        while ($position -lt $Payload.Length - 2) {
            $character = $Payload[$position]
            $position++
            if ($character -eq '#') {
                $start = $position
                while ($position -lt $Payload.Length -and [char]::IsDigit($Payload[$position])) {
                    $position++
                }
                $color = [int]$Payload.Substring($start, $position - $start)
                while ($position -lt $Payload.Length -and ($Payload[$position] -eq ';' -or [char]::IsDigit($Payload[$position]))) {
                    $position++
                }
                continue
            }
            if ($character -eq '$') {
                $x = 0
                continue
            }
            if ($character -eq '-') {
                $x = 0
                $y += 6
                continue
            }
            $repeat = 1
            if ($character -eq '!') {
                $start = $position
                while ($position -lt $Payload.Length -and [char]::IsDigit($Payload[$position])) {
                    $position++
                }
                $repeat = [int]$Payload.Substring($start, $position - $start)
                $character = $Payload[$position]
                $position++
            }
            $mask = [int]$character - 63
            if ($mask -lt 0 -or $mask -gt 63) {
                throw 'Unexpected Sixel data character.'
            }
            for ($column = 0; $column -lt $repeat; $column++) {
                for ($bit = 0; $bit -lt 6; $bit++) {
                    if (($mask -band (1 -shl $bit)) -ne 0 -and $x -lt $width -and ($y + $bit) -lt $height) {
                        $pixels[($y + $bit) * $width + $x] = $color
                        $painted++
                    }
                }
                $x++
            }
        }
        [pscustomobject]@{
            Width = $width
            Height = $height
            Pixels = $pixels
            Painted = $painted
        }
    }
}

Describe 'Chart showcase' {
    BeforeEach {
        $capture.Clear()
        Mock Invoke-Tui {
            $capture.Add(@{} + $PesterBoundParameters)
        }
    }

    It 'Returns all chart kinds as portable data without entering the host' {
        Mock Invoke-Tui { throw 'DefinitionOnly must not open a host.' }
        $layout = & $example -DefinitionOnly
        $pages = $layout.Root.Children[0]
        $pages.Type | Should -Be 'TabControl'
        $pages.Bind | Should -Be 'currentChart'
        $kinds = @($pages.Children | ForEach-Object { $_.Children[2].GraphKind })
        ($kinds -join ',') | Should -Be ([enum]::GetNames([fullscreen.Charts.GraphKind]) -join ',')
        $layout.State.secondsPerChart | Should -Be 3
        $layout.State.currentChart | Should -Be 0
        $layout.State.slideStarted | Should -Be 0
        (Get-Command $example).Parameters['DefinitionOnly'].Aliases.Count | Should -Be 0
        foreach ($extension in 'json', 'psd1') {
            $path = Join-Path $TestDrive "showcase.$extension"
            $layout | Export-TuiDefinition -Path $path -Script:$false
            $copy = Import-TuiDefinition -Path $path
            $copy.Root.Children[0].Children.Count | Should -Be $kinds.Count
            $errorChart = $copy.Root.Children[0].Children[11].Children[2]
            $errorChart.LowerProperty | Should -Be 'Low'
            $errorChart.UpperProperty | Should -Be 'High'
        }
    }

    It 'Cycles every chart in one module app with supported Sixel or native fallback (<Sixel>)' -ForEach @(
        @{ Sixel = $true }
        @{ Sixel = $false }
    ) {
        & $example
        $invocation = $capture[0]
        $invocation.ContainsKey('Theme') | Should -BeFalse
        $invocation.ContainsKey('Minimal') | Should -BeFalse
        $invocation.TickInterval | Should -Be 100
        $tick = $invocation.OnTick
        $callback = [Func[fullscreen.Custom.TuiEvent, hashtable, fullscreen.Custom.TuiReply]] {
            param($tuiEvent, $state)
            [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $tuiEvent, $state)
        }
        $document = [fullscreen.Custom.TuiDocument]::FromObject($invocation.Definition)
        $app = [fullscreen.Custom.CustomTuiApp]::new($document, $null, $null, $callback, 16)
        $uiAssembly = $app.Root.GetType().Assembly
        $context = [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($uiAssembly)
        $terminalAssembly = $context.LoadFromAssemblyName([Reflection.AssemblyName]::new('XenoAtom.Terminal'))
        $terminalSize = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalSize'), [object[]]@(120, 40))
        $metrics = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalPixelMetrics'),
            [object[]]@(960, 640, 8, 16, 120, 40))
        $graphics = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalGraphicsCapabilities'))
        $protocol = [Enum]::Parse($graphics.GetType().GetProperty('PreferredProtocol').PropertyType, 'Sixel')
        $protocols = [Array]::CreateInstance($protocol.GetType(), [int]$Sixel)
        if ($Sixel) {
            $protocols.SetValue($protocol, 0)
            $graphics.PreferredProtocol = $protocol
        }
        $graphics.SupportedProtocols = $protocols
        $graphics.SupportState = [Enum]::Parse($graphics.GetType().GetProperty('SupportState').PropertyType,
            $(if ($Sixel) { 'Confirmed' } else { 'Unsupported' }))
        $graphics.SupportsStaticImages = $Sixel
        $graphics.PixelMetrics = $metrics
        $capabilities = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalCapabilities'))
        $capabilities.AnsiEnabled = $true
        $capabilities.Graphics = $graphics
        $capabilities.IsOutputRedirected = $false
        $capabilities.IsInputRedirected = $false
        $backend = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.Backends.InMemoryTerminalBackend'),
            [object[]]@($terminalSize, $capabilities))
        $terminalOptions = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalOptions'))
        $terminalOptions.ImplicitStartInput = $true
        $terminalOptions.Graphics.DisableProbing = $true
        $terminalOptions.Graphics.PreferredProtocol = $protocol
        $terminalOptions.Graphics.ForcedPixelMetrics = $metrics
        $session = $terminalAssembly.GetType('XenoAtom.Terminal.Terminal').GetMethod('Open').Invoke($null,
            [object[]]@($backend, $terminalOptions, $true))
        $appOptions = [Activator]::CreateInstance($uiAssembly.GetType('XenoAtom.Terminal.UI.TerminalAppOptions'))
        $appOptions.HostKind = [Enum]::Parse($appOptions.GetType().GetProperty('HostKind').PropertyType, 'Fullscreen')
        $presenter = $null
        if ($Sixel) {
            $presenter = [Activator]::CreateInstance([fullscreen.Charts.Chart].Assembly.GetType('fullscreen.Charts.SixelChartPresenter'),
                [object[]]@($graphics, $metrics))
            [fullscreen.Charts.ChartPresenters].GetMethod('Register', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null,
                [object[]]@($session.Instance, $presenter))
            $appOptions.GraphicsPresenter = $presenter
        }
        $terminal = [Activator]::CreateInstance($uiAssembly.GetType('XenoAtom.Terminal.UI.TerminalApp'),
            [object[]]@($app.Root, $session.Instance, $appOptions))
        $flags = [Reflection.BindingFlags]'Instance,NonPublic'
        $clock = @{ Time = [System.Diagnostics.Stopwatch]::GetTimestamp() }
        $renderTick = {
            $clock.Time += [long]([System.Diagnostics.Stopwatch]::Frequency / 100)
            $null = $terminal.GetType().GetMethod('Tick', $flags).Invoke($terminal, @($clock.Time))
        }.GetNewClosure()
        try {
            $null = $terminal.GetType().GetMethod('BeginRun', $flags).Invoke($terminal, $null)
            & $renderTick
            & $renderTick
            $renderBuffer = $terminal.GetType().GetField('_renderBuffer', $flags)
            $assembly = [fullscreen.Charts.Chart].Assembly
            $draw = [fullscreen.Charts.Chart].GetMethod('DrawPixels', [Reflection.BindingFlags]'Instance,NonPublic')
            $plot = [Activator]::CreateInstance($assembly.GetType('fullscreen.Charts.SixelPlot'), $true)
            $encoder = [Activator]::CreateInstance($assembly.GetType('fullscreen.Charts.SixelEncoder'), $true)
            $encode = $encoder.GetType().GetMethod('EncodePlot', [Reflection.BindingFlags]'Instance,NonPublic',
                $null, [type[]]@($plot.GetType()), $null)
            $pageControl = $app.Controls['pages']
            $start = [fullscreen.Custom.TuiEvent]::new('', 'Tick')
            $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $start, $app.State)
            foreach ($size in @(@(120, 40), @(80, 24))) {
                $terminalSize = [Activator]::CreateInstance($terminalAssembly.GetType('XenoAtom.Terminal.TerminalSize'), [object[]]@($size[0], $size[1]))
                $backend.SetSize($terminalSize)
                $app.SetMinimal($size[0] -eq 80)
                for ($index = 0; $index -lt $app.State.chartCount; $index++) {
                    $app.State.currentChart = $index
                    $app.State.slideStarted = [System.Diagnostics.Stopwatch]::GetTimestamp()
                    $app.Tick() | Should -BeTrue
                    & $renderTick
                    $snapshot = $renderBuffer.GetValue($terminal)
                    $kind = $document.Root.Children[0].Children[$index].Children[2].GraphKind
                    ($snapshot.ToMarkupLines() -join "`n") | Should -Match ([regex]::Escape($kind))
                    $app.Controls["title_$kind"].Bounds.Height | Should -Be 1
                    $chart = $app.Controls["chart_$kind"]
                    [object]::ReferenceEquals($chart.App, $terminal) | Should -BeTrue -Because "$kind must be attached to the running terminal app"
                    $chart.Bounds.Height | Should -BeGreaterThan 4
                    $chart.Bounds.Bottom | Should -BeLessOrEqual $size[1]
                    $draw.Invoke($chart, [object[]]@($plot, 640, 320))
                    $painted = $plot.GetType().GetField('_lastColumns', $flags).GetValue($plot) | Measure-Object -Maximum
                    $painted.Maximum | Should -BeGreaterThan 0 -Because "$kind must contain painted geometry, not only a Sixel palette header"
                    $payload = $encode.Invoke($encoder, @($plot))
                    $payload.Length | Should -BeGreaterThan 100
                    $payload | Should -Match ([regex]::Escape([string][char]27 + 'P'))
                    $beforeOutput = $backend.GetOutText().Length
                    $app.State.slideStarted -= [long]([System.Diagnostics.Stopwatch]::Frequency * 3.1)
                    [System.Threading.Thread]::Sleep(20)
                    $app.Tick() | Should -BeTrue
                    & $renderTick
                    $app.State.currentChart | Should -Be (($index + 1) % $app.State.chartCount)
                    [object]::ReferenceEquals($app.Controls['pages'], $pageControl) | Should -BeTrue
                    $app.Controls['pages'].SelectedIndex | Should -Be $app.State.currentChart
                    $nextKind = $document.Root.Children[0].Children[$app.State.currentChart].Children[2].GraphKind
                    $emitted = $backend.GetOutText().Substring($beforeOutput)
                    if ($Sixel) {
                        $emitted | Should -Match ([regex]::Escape([string][char]27 + 'P0;1q')) -Because "The transition to $nextKind must use the actual Sixel presenter writer"
                        $presenter.GetDiagnosticsSnapshot().Name | Should -Be "Sixel charts · $nextKind"
                        $payloads = [regex]::Matches($emitted, '(?s)\x1bP0;1q.*?\x1b\\')
                        $payloads.Count | Should -Be 1 -Because 'Only the newly active chart should be presented.'
                        $decoded = Read-ShowcaseSixel $payloads[0].Value
                        $decoded.Width | Should -BeGreaterThan 0
                        $decoded.Height | Should -BeGreaterThan 0
                        $decoded.Painted | Should -BeGreaterThan 0
                    }
                    else {
                        $emitted | Should -Not -Match ([regex]::Escape([string][char]27 + 'P0;1q'))
                    }
                    $app.Controls["chart_$kind"].App | Should -BeNullOrEmpty -Because 'The replaced page must detach its previous graphics registration.'
                }
            }
        }
        finally {
            $null = $terminal.GetType().GetMethod('EndRun', $flags).Invoke($terminal, $null)
            $terminal.DisposeAsync().AsTask().GetAwaiter().GetResult()
            if ($presenter) {
                $presenter.Dispose()
            }
            $session.Dispose()
            $backend.Dispose()
            $app.Dispose()
        }
    }

    It 'Preserves presentation settings, pause/manual navigation and one-cycle completion' {
        & $example -Theme Monokai -Minimal:$false -SecondsPerChart 5 -Once
        $invocation = $capture[0]
        $invocation.Theme | Should -Be 'Monokai'
        $invocation.ContainsKey('Minimal') | Should -BeTrue
        [bool]$invocation.Minimal | Should -BeFalse
        $state = @{} + $invocation.Definition.State
        $tick = $invocation.OnTick
        $event = $invocation.OnEvent
        $tickEvent = [fullscreen.Custom.TuiEvent]::new('', 'Tick')
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $tickEvent, $state)
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $tickEvent, $state)
        $state.currentChart | Should -Be 0 -Because 'Ticks before the dwell time must not advance.'
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($event, [fullscreen.Custom.TuiEvent]::new('pause', 'Click'), $state)
        $state.paused | Should -BeTrue
        $state.playback | Should -Be 'Paused'
        $state.slideStarted = 1
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $tickEvent, $state)
        $state.currentChart | Should -Be 0
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($event, [fullscreen.Custom.TuiEvent]::new('next', 'Click'), $state)
        $state.currentChart | Should -Be 1
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($event, [fullscreen.Custom.TuiEvent]::new('previous', 'Click'), $state)
        $state.currentChart | Should -Be 0
        $null = [fullscreen.Commands.InvokeTuiCommand]::Callback($event, [fullscreen.Custom.TuiEvent]::new('pause', 'Click'), $state)
        $state.paused | Should -BeFalse
        $state.playback | Should -Be 'Playing - 5 seconds per chart'
        $state.currentChart = $state.chartCount - 1
        $state.slideStarted = 1
        $reply = [fullscreen.Commands.InvokeTuiCommand]::Callback($tick, $tickEvent, $state)
        $reply.Close | Should -BeTrue
        $state.currentChart | Should -Be ($state.chartCount - 1)
        Should -Invoke Invoke-Tui -Exactly -Times 1
    }
}
