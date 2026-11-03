BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Native theme palettes' {
    It 'Lists built-in palettes, wildcard matches and aliases as native data' {
        @(Get-FullscreenTheme).Count | Should -Be 20
        @(Get-FullscreenTheme -Name 'Dark*').Count | Should -Be 5
        (Get-FullscreenTheme 'Visual Studio Dark').Name | Should -Be 'Dark (Visual Studio)'
        $colors = (Get-FullscreenTheme Monokai).Colors
        $colors | Should -BeOfType ([hashtable])
        $colors.Accent | Should -Match '^#[0-9A-F]{6}$'
    }

    It 'Returns independent copies of built-in colors' {
        $colors = (Get-FullscreenTheme Monokai).Colors
        $colors.Accent = '#123456'
        (Get-FullscreenTheme Monokai).Colors.Accent | Should -Not -Be '#123456'
    }

    It 'Rejects theme files with the documented error ID' {
        { Get-FullscreenTheme './theme.json' -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenThemeNotFound*'
    }

}

Describe 'Native definition construction' {
    It 'Builds a starter without exposing private CLR types' {
        $starter = New-TuiDefinition -Title 'Native tool' -State @{ count = 7 }
        $starter | Should -BeOfType ([pscustomobject])
        $starter.Root | Should -BeOfType ([hashtable])
        $starter.State.count | Should -Be 7
    }

    It 'Accepts nested PSCustomObjects and produces independent clones' {
        $root = [pscustomobject]@{ Id = 'root'; Type = 'VStack'; Children = @([pscustomobject]@{ Id = 'text'; Type = 'Text'; Text = 'Hello'; Bind = 'message' }) }
        $definition = New-TuiDefinition -Title 'Parts' -Root $root -State @{ message = 'World' }
        $clone = $definition | New-TuiDefinition
        $clone.Root.Children[0].Text = 'Changed'
        $definition.Root.Children[0].Text | Should -Be 'Hello'
        $json = $definition | ConvertTo-Json -Depth 64
        ($json | New-TuiDefinition).Root.Children[0].Bind | Should -Be 'message'
        @(@($definition, (New-TuiDefinition)) | New-TuiDefinition).Count | Should -Be 2
    }

    It 'Rejects unknown control types with the documented error ID' {
        { New-TuiDefinition -Root @{ Id = 'root'; Type = 'PrivateClrType' } -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }
}

Describe 'Definition import and export' {
    BeforeEach {
        $definition = New-TuiDefinition -Title 'Parts' -State @{ message = 'World' }
        $target = Join-Path $TestDrive ("form[1].$([Guid]::NewGuid().ToString('N')).tui.json")
    }

    It 'Does not write a file with WhatIf' {
        $definition | Export-TuiDefinition -Path $target -WhatIf
        $target | Should -Not -Exist
    }

    It 'Returns the validated definition and accepts literal FileInfo pipeline input' {
        $saved = $definition | Export-TuiDefinition -Path $target -PassThru
        $saved.Title | Should -Be 'Parts'
        $loaded = Get-Item -LiteralPath $target | Import-TuiDefinition
        $loaded.State.message | Should -Be 'World'
        $loaded.Title | Should -Be 'Parts'
    }

    It 'Replaces an existing definition only with Force' {
        $definition | Export-TuiDefinition -Path $target
        { New-TuiDefinition -Title 'Replacement' | Export-TuiDefinition -Path $target -ErrorAction Stop } | Should -Throw '*Force*'
        New-TuiDefinition -Title 'Replacement' | Export-TuiDefinition -Path $target -Force
        (Import-TuiDefinition -LiteralPath $target).Title | Should -Be 'Replacement'
    }
}

Describe 'Public assembly isolation' {
    It 'Does not load XenoAtom into the default context' {
        @([Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | Where-Object { $_.GetName().Name -like 'XenoAtom*' }).Count | Should -Be 0
    }
}
