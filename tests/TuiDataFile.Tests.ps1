BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'TuiDataFile' -Tag 'Integration' {
    It 'Round-trips native values through PSD1/JSON and rejects executable data files' {
        $temporaryRoot = Join-Path ($TestDrive) ('Fullscreen-DataFile-' + [Guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $temporaryRoot
        # Native objects and nested PSCustomObjects both use the same definition path.
        $specialText = 'Unicode ' + [char]0x754C + ' ' + [char]::ConvertFromUtf32(0x1F44B) + ' ' + [char]0xB7 + ' curly ' + [char]0x2018 + 'quotes' + [char]0x2019 + ' and ''quotes'' ' + [char]0xB7 + ' $() ` backtick' + "`r`nsecond line"
        $definition = [pscustomobject]@{
            Title = 'PowerShell native layout'
            State = @{
                empty = @(); single = @('one'); nested = @(@(1, 2), @(3))
                enabled = $false; missing = $null; fraction = -1.25e-7; large = [long]9007199254740993
            }
            Root = [pscustomobject]@{ Id = 'root'; Type = 'VStack'; Children = @(
                    [pscustomobject]@{ Id = 'label'; Type = 'Text'; Text = $specialText; Foreground = '#58D5E6'; Bold = $true }
                    [pscustomobject]@{ Id = 'chart'; Type = 'Chart'; Bind = 'fraction'; GraphKind = 'Step'; Height = 8 }
                ) }
        }
        $native = $definition | New-TuiDefinition
        $hashLayout = New-TuiDefinition -Root @{ Id = 'root'; Type = 'Text'; Text = 'Hashtable layout' }
        ($native.Root.Children.Count) | Should -Be (2) -Because 'Native object construction failed.'
        ($hashLayout.Root.Text) | Should -Be ('Hashtable layout') -Because 'Native object construction failed.'
        $target = Join-Path $temporaryRoot 'layout-one.PSD1'
        $native | Export-TuiDefinition -LiteralPath $target -WhatIf
        [bool](Test-Path -LiteralPath $target) | Should -BeFalse -Because 'PSD1 export ignored WhatIf.'
        $returned = $native | Export-TuiDefinition -LiteralPath $target -PassThru
        ($returned.Title) | Should -Be ($native.Title) -Because 'PassThru lost the native definition.'
        $loaded = Get-Item -LiteralPath $target | Import-TuiDefinition
        $builtin = Import-PowerShellDataFile -LiteralPath $target
        foreach ($item in @($loaded, $builtin)) {
            ($item.Root.Children[0].Text) | Should -BeExactly ($specialText) -Because 'String quoting or newlines did not survive PSD1.'
            ($item.State.empty.Count) | Should -Be (0) -Because 'PSD1 array shape changed.'
            ($item.State.single.Count) | Should -Be (1) -Because 'PSD1 array shape changed.'
            ($item.State.nested.Count) | Should -Be (2) -Because 'PSD1 array shape changed.'
            ($item.State.nested[0].Count) | Should -Be (2) -Because 'PSD1 array shape changed.'
            ($item.State.nested[1].Count) | Should -Be (1) -Because 'PSD1 array shape changed.'
            ($item.State.enabled) | Should -Be ($false) -Because 'PSD1 scalar value changed.'
            ($null) | Should -Be ($item.State.missing) -Because 'PSD1 scalar value changed.'
            ($item.State.large) | Should -Be ([long]9007199254740993) -Because 'PSD1 scalar value changed.'
            ($item.State.fraction) | Should -Be (-1.25e-7) -Because 'PSD1 scalar value changed.'
        }
        $json = Join-Path $temporaryRoot 'layout.json'
        $loaded | Export-TuiDefinition -Path $json
        ((Import-TuiDefinition -Path $json).Root.Children[0].Text) | Should -BeExactly ($specialText) -Because 'JSON compatibility changed.'
        # Larger layouts keep Fullscreen's bounds rather than PowerShell's small default key limit.
        $many = @{}; foreach ($number in 1..550) { $many['key' + $number] = $number }
        $large = New-TuiDefinition -State $many
        $largePath = Join-Path $temporaryRoot 'large.psd1'
        $large | Export-TuiDefinition -Path $largePath
        ((Import-TuiDefinition -Path $largePath).State.Count) | Should -Be (550) -Because 'Large PSD1 layout did not round-trip.'
        $marker = Join-Path $temporaryRoot 'should-not-exist'
        foreach ($bad in @(
                ('Set-Content -LiteralPath ''' + $marker.Replace("'", "''") + ''' -Value executed; @{ Root = @{ Id = ''root''; Type = ''Text'' } }'),
                '@{ Root = @{ Id = ''root''; Type = ''Text'' }; State = @{ value = $(Get-Process) } }',
                '@{ Root = @{ Id = ''root''; Type = ''Text'' }; State = @{ value = [Environment]::MachineName } }',
                '@{ Root = @{ Id = ''root''; Type = ''Text'' }'
            )) {
            $invalidPath = Join-Path $temporaryRoot 'invalid.psd1'
            [IO.File]::WriteAllText($invalidPath, $bad)
            { Import-TuiDefinition -LiteralPath $invalidPath -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
            [bool](Test-Path -LiteralPath $marker) | Should -BeFalse -Because 'PSD1 import executed code.'
        }
        # Exercise the actual runtime and designer loaders without starting a terminal.
        $assembly = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
        $documentType = $assembly.GetType('fullscreen.Custom.TuiDocument', $true)
        $document = $documentType.GetMethod('Load').Invoke($null, [string[]]@($target))
        ($document.Root.Children[1].GraphKind) | Should -Be ('Step') -Because 'Runtime/designer loader lost the chart style.'
    }
}
