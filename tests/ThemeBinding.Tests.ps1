Describe 'Theme binding at the real terminal boundary' -Tag Integration {
    It 'Binds <ThemeCase> in an isolated redirected PowerShell child' -ForEach @(
        'Name','Hashtable','Palette','Ordered','PSCustomObject' | ForEach-Object { @{ ThemeCase = $_ } }
    ) {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'fixtures/ThemeBinding.ps1'), '-ModulePath', ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../output/fullscreen.psd1'))), '-ThemeCase', $ThemeCase)) {
            $start.ArgumentList.Add($argument)
        }
        $child = [Diagnostics.Process]::Start($start)
        try {
            $child.StandardInput.Close()
            $outputTask = $child.StandardOutput.ReadToEndAsync()
            $errorTask = $child.StandardError.ReadToEndAsync()
            $child.WaitForExit(15000) | Should -BeTrue -Because 'The redirected probe must complete without opening a TUI'
            $errorText = $errorTask.GetAwaiter().GetResult()
            $child.ExitCode | Should -Be 0 -Because $errorText
            $result = $outputTask.GetAwaiter().GetResult() | ConvertFrom-Json
            $result.InputRedirected | Should -BeTrue
            $result.OutputRedirected | Should -BeTrue
            $result.Message | Should -BeLike '*requires an interactive terminal*'
        }
        finally {
            if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
            $child.Dispose()
        }
    }
}
