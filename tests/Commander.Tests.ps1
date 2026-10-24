BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Commander' -Tag 'Integration' {
    It 'Preserves native paths, completion, text rendering and assembly isolation' {
        $command = Get-Command -Name fullscreen\Show-Commander -ErrorAction Stop
        ((Get-Command -Name fmc).Definition) | Should -Be ('Show-Commander') -Because 'Commander alias is missing.'
        foreach ($name in @('LeftPath', 'RightPath', 'Theme', 'Minimal', 'ShowHidden', 'PassThru', 'SetLocation')) {
            [bool]($command.Parameters.ContainsKey($name)) | Should -BeTrue -Because "Missing Commander parameter: $name"
            [bool]((Get-Help -Name Show-Commander -Parameter $name).Description.Text) | Should -BeTrue -Because "Missing Commander parameter help: $name"
        }
        $missing = Join-Path -Path ($TestDrive) -ChildPath "Fullscreen-Missing-$([Guid]::NewGuid().ToString('N'))"
        { Show-Commander -LeftPath (Get-Item -LiteralPath $PSScriptRoot) -RightPath $missing -ErrorAction Stop } | Should -Throw '*existing FileSystem directory*' -ErrorId 'FullscreenCommanderFailed*'
        $line = 'Show-Commander -Theme Dark'
        $themeCompletions = [System.Management.Automation.CommandCompletion]::CompleteInput($line, $line.Length, $null).CompletionMatches
        ($themeCompletions.ListItemText) | Should -Contain ('Dark Modern') -Because 'Commander theme completion is missing.'
        # Internal rendering boundary: consumers use fmc/ffile rather than private XenoAtom types.
        $syntaxType = $command.ImplementingType.Assembly.GetType('fullscreen.Files.SyntaxPreview', $true)
        $visual = $syntaxType.GetMethod('Code').Invoke($null, [object[]]@('Get-Process | Select-Object -First 1', 'example.ps1', $false))
        $context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($command.ImplementingType.Assembly)
        $uiAssembly = $context.Assemblies | Where-Object { $_.GetName().Name -eq 'XenoAtom.Terminal.UI' }
        $renderer = $uiAssembly.GetType('XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer', $true)
        $null = $renderer.GetMethod('Render').Invoke($null, [object[]]@($visual, 100, 20, $null))
        [bool]([System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | Where-Object { $_.GetName().Name -match '^(XenoAtom|TextMateSharp|Onigwrap)' }) | Should -BeFalse -Because 'Commander dependencies escaped the private ALC.'
    }
}
