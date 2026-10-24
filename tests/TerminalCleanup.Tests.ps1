BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'TerminalCleanup' -Tag 'Interactive' {
    It 'Restores console modes after closing each interactive application' {
        if (-not $IsWindows -or [Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { Set-ItResult -Skipped -Because 'Requires an interactive Windows terminal'; return }
        [bool]($IsWindows) | Should -BeTrue -Because 'This terminal-mode regression test uses Windows console APIs.'
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FullscreenConsoleProbe {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetConsoleMode(IntPtr handle, out uint mode);
}
'@

        $null = Get-Command Show-SystemMonitor -ErrorAction Stop
        $null = Get-Help Show-SystemMonitor -Full
        $inputHandle = [FullscreenConsoleProbe]::GetStdHandle(-10)
        $outputHandle = [FullscreenConsoleProbe]::GetStdHandle(-11)
        $beforeInput = [uint32]0
        $beforeOutput = [uint32]0
        [bool]([FullscreenConsoleProbe]::GetConsoleMode($inputHandle, [ref]$beforeInput)) | Should -BeTrue -Because 'Run this test in an interactive Windows terminal.'
        [bool]([FullscreenConsoleProbe]::GetConsoleMode($outputHandle, [ref]$beforeOutput)) | Should -BeTrue -Because 'Run this test in an interactive Windows terminal.'
        foreach ($monitorView in @('Processes', 'Dashboard', 'Files', 'Objects', 'Logs')) {
            if ($monitorView -eq 'Files') { Show-FileManager -Path (Join-Path $PSScriptRoot '../README.md') }
            elseif ($monitorView -eq 'Objects') { Get-Process | Show-ObjectTable -Property ProcessName, Id, CPU }
            elseif ($monitorView -eq 'Logs') { Show-LogViewer -LiteralPath (Join-Path $PSScriptRoot '../README.md') }
            else { Show-SystemMonitor -View $monitorView -SampleInterval 100 -RefreshRate 60 }
            $afterInput = [uint32]0
            $afterOutput = [uint32]0
            $null = [FullscreenConsoleProbe]::GetConsoleMode($inputHandle, [ref]$afterInput)
            $null = [FullscreenConsoleProbe]::GetConsoleMode($outputHandle, [ref]$afterOutput)
            ($afterInput) | Should -Be ($beforeInput) -Because "Console modes were not restored: input $beforeInput/$afterInput, output $beforeOutput/$afterOutput"
            ($afterOutput) | Should -Be ($beforeOutput) -Because "Console modes were not restored: input $beforeInput/$afterInput, output $beforeOutput/$afterOutput"
        }
    }
}
