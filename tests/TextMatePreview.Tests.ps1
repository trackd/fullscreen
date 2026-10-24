BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    $assembly = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
    $render = $assembly.GetType('fullscreen.Files.SyntaxPreview').GetMethod('Render', [Reflection.BindingFlags]'Static,NonPublic')
}

Describe 'Optional TextMate previews' -Tag 'TextMate' {
    It 'Renders a multiline Markdown document inside the ffile preview pane' {
        $path = Join-Path $TestDrive 'layout.md'
        $markdown = "# Preview heading`n`nA paragraph in the preview.`n`n## Second heading`n`nFinal preview paragraph."
        Set-Content -LiteralPath $path -Value $markdown
        $surface = [Activator]::CreateInstance($assembly.GetType('fullscreen.Files.FileManagerSurface'))
        $kind = [Enum]::Parse($assembly.GetType('fullscreen.Files.PreviewKind'), 'Markdown')
        $preview = [Activator]::CreateInstance($assembly.GetType('fullscreen.Files.FilePreview'), [object[]]@([string]$path, $kind, $markdown))
        $surface.SetPreview($preview)
        $ui = $surface.GetType().BaseType.BaseType.Assembly
        $renderer = $ui.GetType('XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer').GetMethod('Render')
        foreach ($width in 100, 140) {
            $snapshot = $renderer.Invoke($null, [object[]]@($surface, $width, 40, $null))
            $lines = @($snapshot.ToMarkupLines())
            ($lines -join "`n") | Should -Match 'Preview heading'
            ($lines -join "`n") | Should -Match 'Second heading'
            ($lines -join "`n") | Should -Match 'Final preview paragraph'
            @($lines | Where-Object { $_ -match 'Preview heading|Second heading|Final preview paragraph' }).Count | Should -Be 3
        }
    }
    It 'Imports TextMate when needed and returns a renderable across the ALC boundary' {
        Remove-Module TextMate -ErrorAction SilentlyContinue
        $arguments = [object[]]@('public class PreviewFixture { }', 'csharp', $null, $true, $null)
        $result = $render.Invoke($null, $arguments)
        Get-Module TextMate | Should -Not -BeNullOrEmpty
        $view = $assembly.GetType('fullscreen.Custom.SpectreView')
        $ansi = $view.GetMethod('RenderToAnsi', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($result, 80))
        $ansi | Should -Match 'PreviewFixture'
        $ansi | Should -Match ([regex]::Escape([string][char]27 + '['))
    }
    It 'Explains that previews require TextMate if the module cannot be imported' {
        Remove-Module TextMate -ErrorAction SilentlyContinue
        $savedModulePath = $env:PSModulePath
        try {
            $env:PSModulePath = $TestDrive
            $arguments = [object[]]@('# Heading', 'markdown', $null, $false, $null)
            $result = $render.Invoke($null, $arguments)
            $result | Should -BeNullOrEmpty
            $arguments[4] | Should -BeExactly 'Install-Module TextMate for syntax highlighting.'
        }
        finally { $env:PSModulePath = $savedModulePath }
    }
    It 'Passes a real Markdown path through TextMate' {
        $path = Join-Path $TestDrive 'preview.md'
        Set-Content -LiteralPath $path -Value '# Path preview fixture'
        $arguments = [object[]]@('bounded fallback', 'markdown', [string]$path, $false, $null)
        $result = $render.Invoke($null, $arguments)
        $view = $assembly.GetType('fullscreen.Custom.SpectreView')
        $ansi = $view.GetMethod('RenderToAnsi', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, [object[]]@($result, 80))
        $ansi | Should -Match 'Path preview fixture'
    }
}
