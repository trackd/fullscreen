BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'DesignerScaffold' -Tag 'Integration' {
    It 'Generates compact scaffolding with runtime overrides and presentation options' {
        $path = Join-Path $TestDrive 'Compact.ps1'
        $layout = New-TuiDefinition -Title 'Compact tool' -State @{ name = 'Default' } -Root @{
            Id = 'name'; Type = 'TextBox'; Text = 'Initial'; Bind = 'name'; Enabled = $false; Width = 32
        }
        $layout | Export-TuiDefinition -Path $path -Script
        $copy = & $path -DefinitionOnly -Minimal -TickInterval 500 -State @{ name = 'Override' }
        $copy.Root.Enabled | Should -BeFalse
        $copy.Root.Width | Should -Be 32
        $copy.State.name | Should -BeExactly 'Default' -Because 'DefinitionOnly must not merge live state into portable data.'
        $source = Get-Content -LiteralPath $path -Raw
        $source | Should -Not -Match "'RefreshInterval'"
        Mock Invoke-Tui {
            $State.name | Should -BeExactly 'Override'
            $Minimal.IsPresent | Should -BeTrue
            $TickInterval | Should -Be 500
            @{ Cancelled = $false; Value = 'Accepted' }
        }
        & $path -State @{ name = 'Override' } -Minimal -TickInterval 500 | Should -BeExactly 'Accepted'
    }
    It 'Exports a runnable scaffold with styling, state and event references' {
        $temporaryPath = Join-Path ($TestDrive) ('FullscreenDesigner-' + [Guid]::NewGuid().ToString('N') + '.ps1')
        $layout = & "$PSScriptRoot/../examples/DesignerStarter.ps1" -DefinitionOnly
        $layout.Root.Children += @(
            @{ Id = 'choice'; Type = 'Select'; Bind = 'choice'; Items = @('One', 'Two') }
            @{ Id = 'enabled'; Type = 'CheckBox'; Bind = 'enabled' }
            @{ Id = 'progress'; Type = 'Progress'; Bind = 'progress' }
            @{ Id = 'table'; Type = 'FTable'; Bind = 'rows'; SelectionBind = 'selected' }
            @{ Id = 'logs'; Type = 'FLogView'; Bind = 'logs'; SelectionBind = 'selectedLog' }
            @{ Id = 'source'; Type = 'Spectre'; Bind = 'source' }
            @{ Id = 'files'; Type = 'FileBrowser'; SelectionBind = 'selectedPath' }
        )
        $layout.State["odd`nkey"] = 'Comment references must stay on one line'
        $layout | Export-TuiDefinition -Path $temporaryPath -Script -WhatIf
        [bool](Test-Path -LiteralPath $temporaryPath) | Should -BeFalse -Because 'Scaffold export ignored WhatIf.'
        $layout | Export-TuiDefinition -Path $temporaryPath -Script
        $tokens = $null; $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($temporaryPath, [ref]$tokens, [ref]$parseErrors)
        [bool]($parseErrors.Count) | Should -BeFalse -Because "Generated script is invalid: $($parseErrors.Message -join '; ')"
        $roundTrip = & $temporaryPath -DefinitionOnly
        ($roundTrip.Root.Children[0].Foreground) | Should -Be ('#58D5E6') -Because 'Generated script lost styling.'
        [bool]($roundTrip.Root.Children[0].Bold) | Should -BeTrue -Because 'Generated script lost styling.'
        ($roundTrip.Root.Children[3].GraphKind) | Should -Be ('Area') -Because 'Generated script lost chart configuration.'
        $source = Get-Content -LiteralPath $temporaryPath -Raw
        [bool]($source.Contains("'greet:Click'")) | Should -BeTrue -Because 'Generated script lacks per-control event scaffolding.'
        [bool]($source.Contains('$definitionJson')) | Should -BeFalse -Because 'Scaffold must embed native PowerShell data and callback stubs.'
        [bool]($source.Contains('$definition =')) | Should -BeTrue -Because 'Scaffold must embed native PowerShell data and callback stubs.'
        [bool]($source.Contains('$onTick')) | Should -BeTrue -Because 'Scaffold must embed native PowerShell data and callback stubs.'
        foreach ($reference in @(
                'STATE REFERENCE', '$state[''name'']', '$state[''phase'']', '$state[''selected'']',
                'boolean ($true / $false)', 'selected string from this control', 'number from 0 to 1',
                'numeric collection with NaN/null gaps', 'collection of row objects', 'runtime Spectre IRenderable',
                'selected file path string or $null', 'odd\u000akey',
                "'enabled:Change'", "'table:Selection'", "'table:Submit'", "'files:Close'",
                'Tick is delivered to OnTick', 'Cancelled = $true', 'LastEvent = last processed control ID'
            )) {
            [bool]($source.Contains($reference)) | Should -BeTrue -Because "Generated state/event reference is missing: $reference"
        }
    }
}
