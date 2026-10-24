BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'CustomTui' -Tag 'Integration' {
    It 'Preserves native layouts, callbacks, result objects and save/load behavior' {
        foreach ($commandName in @('Show-TuiDesigner', 'Invoke-Tui')) {
            $command = Get-Command -Name "Fullscreen\$commandName" -ErrorAction Stop
            $null = Get-Help -Name $commandName -Full
            [bool]($command.Parameters.ContainsKey('Theme')) | Should -BeTrue -Because "Missing Theme: $commandName"
        }
        $command = Get-Command -Name fullscreen\Invoke-Tui
        $assembly = $command.ImplementingType.Assembly
        $layout = @{
            Version = 1
            Title = 'ALC fixture'
            State = @{ count = 7 }
            Root = @{
                Id = 'root'
                Type = 'VStack'
                Children = @(
                    @{ Id = 'name'; Type = 'TextBox'; Bind = 'name'; Text = 'World' }
                    @{ Id = 'submit'; Type = 'Button'; Text = 'Submit'; Close = $true }
                )
            }
        }
        $plain = $layout | New-TuiDefinition
        ($plain.Root.GetType().FullName) | Should -Be ('System.Collections.Hashtable') -Because 'Definition exposed private control types.'
        ($plain.Root.Children[0].Bind) | Should -Be ('name') -Because 'PowerShell definition lost its binding.'
        $roundTrip = $plain | New-TuiDefinition
        ($roundTrip.Root.Children.Count) | Should -Be (2) -Because 'Plain-object round trip failed.'
        ($roundTrip.State['count']) | Should -Be (7) -Because 'Plain-object round trip failed.'

        $eventType = $assembly.GetType('fullscreen.Custom.TuiEvent', $true)
        # Private callback-boundary tests intentionally use reflection; consumers use Invoke-Tui -OnEvent.
        $tuiEvent = [Activator]::CreateInstance($eventType, [object[]]@('submit', 'Click', $null))
        $state = @{ name = 'Sam' }
        $callingThread = [Environment]::CurrentManagedThreadId
        $handler = {
            param($tuiEvent, $state)
            if ([Environment]::CurrentManagedThreadId -ne $callingThread) { throw 'Handler changed threads.' }
            if ($tuiEvent.GetType().FullName -like 'Fullscreen.*') { throw 'Event exposed a private CLR type.' }
            $state['seen'] = $tuiEvent.Kind
            @{ State = @{ greeting = "Hello $($state.name)" }; Close = $true; Value = $state.name }
        }
        $reply = $command.ImplementingType.GetMethod('Callback').Invoke($null, [object[]]@($handler, $tuiEvent, $state))
        [bool]($reply.Close) | Should -BeTrue -Because 'Handler bridge failed.'
        ($reply.Value) | Should -Be ('Sam') -Because 'Handler bridge failed.'
        ($reply.State['greeting']) | Should -Be ('Hello Sam') -Because 'Handler bridge failed.'
        ($state['seen']) | Should -Be ('Click') -Because 'Handler bridge failed.'
        $richReply = $command.ImplementingType.GetMethod('Callback').Invoke($null, [object[]]@({ @{ Value = [pscustomobject]@{ Name = 'Sam'; Count = 7 } } }, $tuiEvent, $state))
        ($richReply.Value.Name) | Should -Be ('Sam') -Because 'Handler response lost custom object properties.'
        ($richReply.Value.Count) | Should -Be (7) -Because 'Handler response lost custom object properties.'
        {
            $null = $command.ImplementingType.GetMethod('Callback').Invoke($null, [object[]]@({ 'unexpected output' }, $tuiEvent, $state))
        } | Should -Throw '*handlers must emit*'
        { @($layout, $layout) | Invoke-Tui -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenMultipleTuiDefinitions*'
        [bool]([System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | Where-Object { $_.GetName().Name -like 'XenoAtom*' }) | Should -BeFalse -Because 'Custom TUI bridge loaded framework types into the default ALC.'
        $temporaryRoot = Join-Path -Path ($TestDrive) -ChildPath "Fullscreen-CustomTui-$([Guid]::NewGuid().ToString('N'))"
        $null = New-Item -ItemType Directory -Path $temporaryRoot
        $target = Join-Path -Path $temporaryRoot -ChildPath 'form.tui.json'
        $plain | Export-TuiDefinition -Path $target
        $loaded = Import-TuiDefinition -Path $target
        ($loaded.Title) | Should -Be ('ALC fixture') -Because 'Saved layout did not round-trip.'
    }
}
