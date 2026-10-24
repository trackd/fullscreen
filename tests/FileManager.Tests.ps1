BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'FileManager' -Tag 'Integration' {
    It 'Loads an initial file and its metadata preview on the worker' {
        $modulePath = Join-Path $PSScriptRoot '../output/fullscreen.psd1'

        $command = Get-Command -Name fullscreen\Show-FileManager -ErrorAction Stop
        $help = Get-Help -Name Show-FileManager -Full
        ($help.Synopsis) | Should -Match ('file manager') -Because 'File manager help missing.'
        $appType = $command.ImplementingType.Assembly.GetType('fullscreen.Files.FileManagerApp', $true)
        $sortType = $command.Parameters['SortBy'].ParameterType
        $imagePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../docs/images/dashboard.png'))
        $app = [Activator]::CreateInstance($appType, @($imagePath, $false, [Enum]::Parse($sortType, 'Name'), $false, $true, $false))
        try {
            $surface = $app.Root
            $uiAssembly = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($appType.Assembly).Assemblies |
                Where-Object { $_.GetName().Name -eq 'XenoAtom.Terminal.UI' }
            $renderer = $uiAssembly.GetType('XenoAtom.Terminal.UI.Rendering.VisualSnapshotRenderer', $true)
            $null = $renderer.GetMethod('Render').Invoke($null, @($surface, 120, 30, $null))
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while ($null -eq $surface.Preview -and [DateTime]::UtcNow -lt $deadline) {
                $null = $app.Tick()
                Start-Sleep -Milliseconds 10
            }
            ($null) | Should -Not -Be ($surface.Preview) -Because "Image metadata preview failed: $($surface.Preview.Text) $($surface.Status)"
            ($surface.Preview.Kind.ToString()) | Should -Be ('Image') -Because "Image metadata preview failed: $($surface.Preview.Text) $($surface.Status)"
            ($app.ChosenPaths[0]) | Should -Be ($imagePath) -Because 'Initial file selection mismatch.'
        }
        finally { $app.Dispose() }
    }
}
