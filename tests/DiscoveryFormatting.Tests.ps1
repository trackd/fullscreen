BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Discovery formatting' {
    It 'Groups native control data by category with concise default columns' {
        $controls = @(Get-TuiControl)
        $controls[0].PSObject.TypeNames | Should -Contain 'fullscreen.TuiControl'
        $actual = $controls | ForEach-Object { "$($_.Category)/$($_.Name)" }
        Compare-Object -ReferenceObject $actual -DifferenceObject @($actual | Sort-Object) -SyncWindow 0 | Should -BeNullOrEmpty
        $text = Get-TuiControl | Out-String -Width 160
        $text | Should -Match 'Category: Application'
        $text | Should -Match 'Category: Data'
        $text | Should -Match 'Binding'
        $text | Should -Not -Match 'PropertyDetails'
        (Get-TuiControl FTable).PropertyDetails.Count | Should -BeGreaterThan 0
    }

    It 'Displays theme colors while retaining editable native color values' {
        $theme = Get-FullscreenTheme 'Dark Modern'
        $theme.PSObject.TypeNames | Should -Contain 'fullscreen.Theme'
        $theme.Colors | Should -BeOfType ([hashtable])
        $theme.Preview | Should -Match ([regex]::Escape("$([char]27)[38;2;0;120;212m"))
        $theme.Preview | Should -Match ([regex]::Escape("$([char]27)[48;2;31;31;31m"))
        $theme.Colors.Accent | Should -Be '#0078D4'
        $text = $theme | Out-String -Width 160
        $text | Should -Match 'Palette'
        $text | Should -Match 'Accent'
        $text | Should -Match 'Success'
        $text | Should -Match 'Warning'
        $text | Should -Match 'Error'
    }

    It 'Keeps formatted and JSON views scoped to the current inspected node' {
        $assembly = (Get-Command Show-ObjectTable).ImplementingType.Assembly
        $options = [Activator]::CreateInstance($assembly.GetType('fullscreen.Objects.ObjectTableOptions'))
        $options.View = [Enum]::Parse($assembly.GetType('fullscreen.Objects.ObjectView'), 'Inspect')
        $rows = [Collections.Generic.List[psobject]]::new()
        $rows.Add([pscustomobject]@{ RootOnly = 'outer'; Child = [pscustomobject]@{ Name = 'inner'; Count = 42 } })
        $app = [Activator]::CreateInstance($assembly.GetType('fullscreen.Objects.ObjectTableApp'), @($rows, $options))
        try {
            $childRow = $app.Surface.Model.Visible | Where-Object { $_.Cells[2] -eq 'Child' }
            $childIndex = $app.Surface.Model.Visible.IndexOf($childRow)
            $app.Surface.Model.Select($childIndex, 20)
            $app.InspectSelected($app.Surface.Model.Current)
            $path = $app.NodePath
            $app.SwitchView([Enum]::Parse($options.View.GetType(), 'Formatted'))
            $app.NodePath | Should -Be '$[0].Child'
            $app.Surface.Model.Rows[0].Cells | Should -Contain 'inner'
            $app.Surface.Model.Columns | Should -Not -Contain 'RootOnly'
            $app.SwitchView([Enum]::Parse($options.View.GetType(), 'Json'))
            $app.NodePath | Should -Be $path
            $app.Surface.DocumentHint | Should -Be $path
            $app.NavigationDepth | Should -Be 1
            $app.GoBack() | Should -BeTrue
            $app.NodePath | Should -Be '$'
        }
        finally { $app.Dispose() }
    }

    It 'Honors display-expression colors through the PowerShell format database' {
        $assembly = (Get-Command Show-ObjectTable).ImplementingType.Assembly
        $rows = [Collections.Generic.List[psobject]]::new()
        $rows.Add((Get-FullscreenTheme 'Dark Modern'))
        $model = $assembly.GetType('fullscreen.Objects.PowerShellObjectViews').GetMethod('Format').Invoke($null, @($rows, $null))
        $model.Columns | Should -Contain 'Palette'
        $model.Rows[0].Cells[1] | Should -Match 'Accent'
        $model.Rows[0].Cells[1] | Should -Not -Match ([regex]::Escape([string][char]27))
        $styles = $model.GetType().GetProperty('CellStyles', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($model)
        $styles.Count | Should -Be 1
    }
}
