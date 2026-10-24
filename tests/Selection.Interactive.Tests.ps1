BeforeAll { Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force }

Describe 'Interactive selection' -Tag Interactive {
    It 'Returns the initial file path when accepted with Enter' {
        if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { Set-ItResult -Skipped -Because 'Requires an interactive terminal'; return }
        $readmePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../README.md'))
        $selected = @(Show-FileManager -Path $readmePath -PassThru)
        $selected.Count | Should -Be 1
        $selected[0] | Should -Be $readmePath
    }

    It 'Returns the original selected object when accepted with Enter' {
        if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { Set-ItResult -Skipped -Because 'Requires an interactive terminal'; return }
        $first = [pscustomobject]@{ Name = 'alpha'; Count = 10 }
        $second = [pscustomobject]@{ Name = 'beta'; Count = 2 }
        $chosen = @($first, $second) | Show-ObjectTable -Property Name,Count -PassThru
        [object]::ReferenceEquals($chosen, $first) | Should -BeTrue
    }
}
