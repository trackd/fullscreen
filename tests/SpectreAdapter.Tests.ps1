BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'SpectreAdapter' -Tag 'TextMate' {
    It 'Matches the TextMate writer for Markdown, numbered code and sliced HighlightedText' {
        Import-Module TextMate -ErrorAction Stop
        $markdown = "# Contract heading`n`nA paragraph with **bold** and a [link](https://example.com).`n`n| Name | State |`n| --- | --- |`n| Fixture | Ready |"
        $code = "function Get-Fixture {`n    # preserve indentation`n    'hello world'`n}"
        $document = $markdown | TextMate\Format-TextMate -Language markdown
        $numbered = $code | TextMate\Format-TextMate -Language powershell -LineNumbers
        $slice = $numbered.Slice(1, 2)
        $assembly = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
        $viewType = $assembly.GetType('fullscreen.Custom.SpectreView', $true)
        $flags = [Reflection.BindingFlags]'Static,NonPublic'
        $render = $viewType.GetMethod('RenderToAnsi', $flags)
        $parse = $viewType.GetMethod('ParseAnsi', $flags)
        foreach ($content in $document, $numbered, $slice) {
            $content.GetType().FullName | Should -Be 'PSTextMate.Core.HighlightedText'
            $writer = $content.GetType().Assembly.GetType('PSTextMate.Utilities.Writer').GetMethod('WriteToString', $flags)
            foreach ($width in 35, 80) {
                $expected = $writer.Invoke($null, [object[]]@($content.PSObject.BaseObject, $width))
                $actual = $render.Invoke($null, [object[]]@($content, $width))
                $expectedText = $parse.Invoke($null, [object[]]@($expected)).Item1.TrimEnd()
                $actualText = $parse.Invoke($null, [object[]]@($actual)).Item1.TrimEnd()
                $actualText | Should -BeExactly $expectedText
            }
        }
        $documentText = $parse.Invoke($null, [object[]]@($render.Invoke($null, [object[]]@($document, 80)))).Item1
        $documentText | Should -Match 'Contract heading'
        $documentText | Should -Match 'Fixture'
        $sliceText = $parse.Invoke($null, [object[]]@($render.Invoke($null, [object[]]@($slice, 80)))).Item1
        $sliceText | Should -Match '2\s+│\s+# preserve indentation'
        $sliceText | Should -Not -Match 'Get-Fixture'
    }
    It 'Renders an actual TextMate object through the private adapter' {
        $TextMatePath = $env:FULLSCREEN_TEST_TEXTMATE_PATH
        if (-not $TextMatePath) { Set-ItResult -Skipped -Because 'Set FULLSCREEN_TEST_TEXTMATE_PATH to the TextMate module manifest'; return }
        Import-Module $TextMatePath -ErrorAction Stop
        $code = 'Get-Process | Sort-Object CPU' | Format-TextMate -Language powershell
        $assembly = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
        $viewType = $assembly.GetType('fullscreen.Custom.SpectreView', $true)
        $flags = [Reflection.BindingFlags]'Static,NonPublic'
        $ansi = $viewType.GetMethod('RenderToAnsi', $flags).Invoke($null, [object[]]@($code, 60))
        $parsed = $viewType.GetMethod('ParseAnsi', $flags).Invoke($null, [object[]]@($ansi))
        [bool]($parsed.Item1.Contains('Get-Process')) | Should -BeTrue -Because 'TextMate renderable lost its source text.'
        [bool]($parsed.Item1.Contains('Sort-Object')) | Should -BeTrue -Because 'TextMate renderable lost its source text.'
        [bool]($ansi.Contains([char]27 + '[')) | Should -BeTrue -Because 'TextMate renderable lost its ANSI styling.'
        $layout = & "$PSScriptRoot/../examples/SpectreContent.ps1" -DefinitionOnly
        $null = $layout | New-TuiDefinition
        ($layout.Root.Children[1].Type) | Should -Be ('Spectre') -Because 'Spectre example lost the adapter control.'
    }
}
