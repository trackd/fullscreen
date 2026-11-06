BeforeDiscovery {
    $paletteCases = @(
        @{ ThemeName = 'Light 2026'; Expected = 'FFFFFF,3B3B3B,FAFAFD,606060,0069CC,DADADA,202020,587C0C,795E26,AD0707,800080' }
        @{ ThemeName = 'Dark 2026'; Expected = '191A1B,CCCCCC,191A1B,8C8C8C,2F708C,383939,EDEDED,73C991,DCDCAA,F48771,C586C0' }
        @{ ThemeName = 'Dark+'; Expected = '1E1E1E,D4D4D4,1E1E1E,D4D4D4,569CD6,04395E,D4D4D4,6A9955,DCDCAA,F48771,C586C0' }
        @{ ThemeName = 'Dark Modern'; Expected = '1F1F1F,CCCCCC,181818,9D9D9D,0078D4,04395E,CCCCCC,6A9955,DCDCAA,F85149,C586C0' }
        @{ ThemeName = 'Light+'; Expected = 'FFFFFF,000000,FFFFFF,000000,005FB8,ADD6FF,000000,008000,795E26,A31515,800080' }
        @{ ThemeName = 'Light Modern'; Expected = 'FFFFFF,3B3B3B,F8F8F8,3B3B3B,005FB8,E8E8E8,000000,008000,795E26,F85149,800080' }
        @{ ThemeName = 'Dark (Visual Studio)'; Expected = '1E1E1E,D4D4D4,1E1E1E,D4D4D4,569CD6,04395E,D4D4D4,6A9955,DCDCAA,F48771,C586C0' }
        @{ ThemeName = 'Light (Visual Studio)'; Expected = 'FFFFFF,000000,FFFFFF,000000,005FB8,ADD6FF,000000,008000,795E26,A31515,800080' }
        @{ ThemeName = 'Dark High Contrast'; Expected = '000000,FFFFFF,000000,FFFFFF,569CD6,FFFFFF,FFFFFF,6A9955,DCDCAA,F48771,C586C0' }
        @{ ThemeName = 'Light High Contrast'; Expected = 'FFFFFF,333333,FFFFFF,333333,005FB8,ADD6FF,333333,008000,795E26,A31515,800080' }
        @{ ThemeName = 'Abyss'; Expected = '000C18,6688CC,060621,6688CC,596F99,08286B,6688CC,D1F1A9,FFEEAD,FF9DA4,EBBBFF' }
        @{ ThemeName = 'Kimbie Dark'; Expected = '221A0F,D3AF86,362712,D3AF86,A57A4C,7C5021,D3AF86,6A9955,DCDCAA,F48771,C586C0' }
        @{ ThemeName = 'Monokai'; Expected = '272822,F8F8F2,1E1F1C,F8F8F2,99947C,75715E,F8F8F2,86B42B,B3B42B,C4265E,8C6BC8' }
        @{ ThemeName = 'Monokai Dimmed'; Expected = '1E1E1E,C5C8C6,272727,C5C8C6,3655B5,707070,C5C8C6,86B42B,B3B42B,C4265E,8C6BC8' }
        @{ ThemeName = 'Quiet Light'; Expected = 'F5F5F5,333333,F2F2F2,333333,9769DC,C4D9B1,6C6C6C,008000,795E26,F1897F,800080' }
        @{ ThemeName = 'Red'; Expected = '390000,F8F8F8,330000,F8F8F8,BD4444,880000,F8F8F8,6A9955,DCDCAA,FFEAEA,C586C0' }
        @{ ThemeName = 'Solarized Dark'; Expected = '002B36,839496,00212B,839496,197271,005A6F,839496,859900,B58900,DC322F,D33682' }
        @{ ThemeName = 'Solarized Light'; Expected = 'FDF6E3,657B83,EEE8D5,657B83,B49471,DFCA88,6C6C6C,859900,B58900,DC322F,D33682' }
        @{ ThemeName = 'Tomorrow Night Blue'; Expected = '002451,FFFFFF,001C40,FFFFFF,BBDAFF,607693,FFFFFF,D1F1A9,FFEEAD,FF9DA4,EBBBFF' }
    )
    $nameCases = './theme.json', 'https://example.com/theme.json', 'missing' | ForEach-Object { @{ ThemeName = $_ } }
    $duplicate = [Collections.Hashtable]::new([StringComparer]::Ordinal)
    $duplicate.Add('Accent', '#fff'); $duplicate.Add('accent', '#000')
    $invalidCases = @(
        @{ Label = 'unknown color key'; Colors = @{ Acccent = '#fff' } }
        @{ Label = 'numeric color'; Colors = @{ Accent = 123 } }
        @{ Label = 'null color'; Colors = @{ Accent = $null } }
        @{ Label = 'invalid hex'; Colors = @{ Accent = '#xyz' } }
        @{ Label = 'alpha channel'; Colors = @{ Accent = '#ffffff80' } }
        @{ Label = 'unknown base'; Colors = @{ Base = 'missing' } }
        @{ Label = 'empty name'; Colors = @{ Name = '' } }
        @{ Label = 'nonstring key'; Colors = @{ 1 = '#fff' } }
        @{ Label = 'duplicate color keys'; Colors = $duplicate }
    )
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    $definition = New-TuiDefinition
}

Describe 'Theme color contracts' {
    It 'Exports editable syntax and chart roles that round-trip without losing colors' {
        $catalog = (Get-Command Get-FullscreenTheme).ImplementingType.Assembly.GetType('fullscreen.Themes.ThemeCatalog')
        $roles = 'DefaultToken','Comment','Keyword','String','Operator','Variable','Command','Parameter','Type','Number','Property','Function','Punctuation',
            'ChartBlue','ChartOrange','ChartPurple','ChartGreen','ChartCyan','ChartRed','ChartYellow','ChartPink',
            'ChartGrid','ChartAxis','ChartLabel','ChartSequentialLow','ChartSequentialHigh','ChartNegative','ChartNeutral','ChartPositive'
        foreach ($theme in Get-FullscreenTheme) {
            $palette = $catalog.GetMethod('FromDictionary').Invoke($null, @($theme.Colors))
            foreach ($role in $roles) {
                $theme.Colors[$role] | Should -Match '^#[0-9A-F]{6}$'
                $property = switch ($role) { String { 'StringLiteral' }; ChartGrid { 'ChartGridColor' }; default { $role } }
                $color = $palette.$property
                ('#{0:X2}{1:X2}{2:X2}' -f $color.R,$color.G,$color.B) | Should -BeExactly $theme.Colors[$role]
            }
        }
        $custom = $catalog.GetMethod('FromDictionary').Invoke($null, @(@{ Base = 'Monokai'; ChartBlue = '#123'; Keyword = '#456' }))
        $custom.ChartBlue.R | Should -Be 17
        $custom.Keyword.R | Should -Be 68
        $original = (Get-FullscreenTheme Monokai).Colors
        ('#{0:X2}{1:X2}{2:X2}' -f $custom.Success.R,$custom.Success.G,$custom.Success.B) | Should -BeExactly $original.Success
    }

    It 'Preserves every color role in <ThemeName>' -ForEach $paletteCases {
        $colors = (Get-FullscreenTheme -Name $ThemeName).Colors
        $roles = 'Background','Foreground','Panel','Muted','Accent','Selection','SelectedText','Success','Warning','Error','Purple'
        $expectedColors = $Expected.Split(',')
        for ($index = 0; $index -lt $roles.Count; $index++) {
            $colors[$roles[$index]] | Should -BeExactly ('#' + $expectedColors[$index]) -Because "$ThemeName must retain its $($roles[$index]) color"
        }
    }

    It 'Rejects unsupported theme name/path <ThemeName>' -ForEach $nameCases {
        { Get-FullscreenTheme -Name $ThemeName -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenThemeNotFound*'
    }

    It 'Rejects <Label> before opening a terminal' -ForEach $invalidCases {
        { Invoke-Tui -Definition $definition -Theme $Colors -ErrorAction Stop } | Should -Throw -ExceptionType ([Management.Automation.ParameterBindingException])
    }
}
