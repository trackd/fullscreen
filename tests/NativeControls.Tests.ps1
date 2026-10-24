BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module "$PSScriptRoot/../output/fullscreen.psd1" -Force
}
Describe 'Native widget composition' {
    It 'Separates Fullscreen controls from native widgets and exposes their configurable properties' {
        @(Get-TuiControl).Count | Should -Be 90
        ((Get-TuiControl FTable).Binding) | Should -Match 'original'
        ((Get-TuiControl FLogView).Description) | Should -Match 'Severity'
        ((Get-TuiControl Table).Description) | Should -Match 'Native XenoAtom'
        ((Get-TuiControl Slider).NativeProperties.Name) | Should -Contain 'Minimum'
        ((Get-TuiControl Slider).NativeProperties.Name) | Should -Contain 'Step'
        ((Get-TuiControl EnumSelect).PropertyDetails | Where-Object Name -eq EnumType).Choices | Should -Contain 'Orientation'
        { Get-TuiControl CodeEditor -ErrorAction Stop } | Should -Throw '*Unknown TUI control*'
    }
    It 'Preserves native options and enum types through the builder and data-file export' {
        $slider = New-TuiControl Slider -Id slider -Bind amount -Property @{ Options = @{ Minimum = 0; Maximum = 100; Step = 5 } }
        $enum = New-TuiControl EnumSelect -Id direction -Bind direction -Property @{ EnumType = 'Orientation' }
        $root = New-TuiControl VStack -Id root -Child $slider, $enum
        $layout = New-TuiDefinition -Root $root -State @{ amount = 25; direction = 'Vertical' }
        $path = Join-Path $TestDrive 'native.psd1'
        $layout | Export-TuiDefinition -Path $path
        $copy = Import-TuiDefinition -Path $path
        $copy.Root.Children[0].Options.Step | Should -Be 5
        $copy.Root.Children[1].EnumType | Should -BeExactly 'Orientation'
        $copy.State.amount | Should -Be 25
        { New-TuiControl Slider -Id bad -Property @{ Options = @{ ImaginaryProperty = 1 } } -ErrorAction Stop } | Should -Throw '*ImaginaryProperty*'
        { New-TuiControl Slider -Id bad -Property @{ Options = @{ Orientation = 'Diagonal' } } -ErrorAction Stop } | Should -Throw
    }
    It 'Allows a search target to be built separately and validates it when the layout is composed' {
        $search = New-TuiControl SearchReplacePopup -Id search -Bind searching -Property @{ Target = 'log' }
        $log = New-TuiControl LogControl -Id log -Property @{ Items = @('Ready', 'Build complete') }
        $root = New-TuiControl VStack -Id root -Child $search, $log
        $layout = New-TuiDefinition -Root $root
        $layout.Root.Children[0].Target | Should -BeExactly 'log'
        { New-TuiDefinition -Root $search -ErrorAction Stop } | Should -Throw '*Target*'
    }
    It 'Provides all native examples as data-only pages with populated datasets' {
        $layout = & "$PSScriptRoot/../examples/NativeControls.ps1" -All -DefinitionOnly
        $layout.Root.Children.Count | Should -Be 76
        $layout.State.native_records.Count | Should -Be 2
        $layout.State.native_series.Count | Should -Be 8
        $layout.State.native_tree[0].Children.Count | Should -Be 2
        $validated = $layout | New-TuiDefinition
        $validated.Root.Children.Count | Should -Be 76
    }
}
