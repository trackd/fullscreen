BeforeDiscovery {
    $completionCases = @(
        @{ Text = 'Show-SystemMonitor -View '; Expected = @('Processes','Dashboard','Services','Network','Gpu') }
        @{ Text = 'ftop -GraphType '; Expected = @('Line','Area','Bars','Step','Scatter','Stem') }
        @{ Text = 'Show-SystemMonitor -Column Cpu'; Expected = @('Cpu','CpuTime') }
        @{ Text = 'Show-GitManager -View '; Expected = @('Branches','Worktrees','Stashes','Tags') }
        @{ Text = 'Show-GitHubManager -View '; Expected = @('Issues','PullRequests','Runs','Workflows') }
        @{ Text = 'Show-AzureDevOpsManager -StoryType '; Expected = @('User Story','Product Backlog Item') }
        @{ Text = 'Show-LogViewer -MinimumLevel '; Expected = @('Warning','Error','Critical') }
        @{ Text = 'Show-SystemMonitor -ConfigurationName '; Expected = @('PowerShell.7','Microsoft.PowerShell') }
        @{ Text = 'Show-ObjectTable -InputObject $global:fullscreenCompletionItems -Property Di'; Expected = @('Display Name') }
        @{ Text = 'Show-ObjectTable -InputObject $global:fullscreenCompletionItems -ExcludeProperty Da'; Expected = @('Dangerous') }
        @{ Text = 'Show-ObjectTable -Property Name,Count -SortBy Co'; Expected = @('Count') }
        @{ Text = 'Show-ObjectTable -Property Name,Count -GroupBy Na'; Expected = @('Name') }
    )
    $invalidCases = @(
        @{ Name = 'undefined monitor view'; Call = { Show-SystemMonitor -View 999 -ErrorAction Stop } }
        @{ Name = 'distribution monitor plot'; Call = { Show-SystemMonitor -GraphType Histogram -ErrorAction Stop } }
        @{ Name = 'undefined monitor plot'; Call = { Show-SystemMonitor -GraphType 999 -ErrorAction Stop } }
        @{ Name = 'undefined column'; Call = { Show-SystemMonitor -Column Name,999 -ErrorAction Stop } }
        @{ Name = 'zero refresh rate'; Call = { Show-SystemMonitor -RefreshRate 0 -ErrorAction Stop } }
        @{ Name = 'undefined file sort'; Call = { Show-FileManager -SortBy 999 -ErrorAction Stop } }
        @{ Name = 'undefined log level'; Call = { Show-LogViewer -LiteralPath ignored -MinimumLevel 999 -ErrorAction Stop } }
        @{ Name = 'undefined object view'; Call = { Show-ObjectTable -View 999 -ErrorAction Stop } }
        @{ Name = 'undefined Git view'; Call = { Show-GitManager -View 999 -ErrorAction Stop } }
        @{ Name = 'malformed GitHub repository'; Call = { Show-GitHubManager -Repository invalid -ErrorAction Stop } }
        @{ Name = 'undefined Azure view'; Call = { Show-AzureDevOpsManager -Organization fixture -Project fixture -View 999 -ErrorAction Stop } }
        @{ Name = 'invalid color'; Call = { Invoke-Tui -Definition @{} -Theme @{ Accent = 'invalid' } -ErrorAction Stop } }
        @{ Name = 'theme file'; Call = { Invoke-Tui -Definition @{} -Theme './theme.json' -ErrorAction Stop } }
        @{ Name = 'oversized title'; Call = { New-TuiDefinition -Title ('x' * 513) -ErrorAction Stop } }
    )
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    $global:fullscreenCompletionItems = @([pscustomobject]@{ 'Display Name' = 'fixture'; Count = 1 })
    $global:fullscreenCompletionItems[0] | Add-Member -MemberType ScriptProperty -Name Dangerous -Value { throw 'Completion evaluated a property getter.' }
}

AfterAll { Remove-Variable fullscreenCompletionItems -Scope Global -ErrorAction SilentlyContinue }

Describe 'Parameter binding and passive completion' {
    It 'Offers only enabled chart renderers for <CommandName>' -ForEach @(
        @{ CommandName = 'Out-Chart' }
        @{ CommandName = 'Invoke-Tui' }
        @{ CommandName = 'Show-SystemMonitor' }
    ) {
        $completionText = "$CommandName -ChartRenderer "
        $names = [Management.Automation.CommandCompletion]::CompleteInput($completionText, $completionText.Length, $null).CompletionMatches.ListItemText
        ($names | Sort-Object) -join ',' | Should -BeExactly 'Auto,Native,Regis,Sixel'
    }

    It 'Rejects disabled DRCS renderer <Name> before opening a terminal' -ForEach @(
        @{ Name = 'Out-Chart Drcs'; Call = { 1 | Out-Chart -ChartRenderer Drcs -ErrorAction Stop } }
        @{ Name = 'Out-Chart DrcsUniversal'; Call = { 1 | Out-Chart -ChartRenderer DrcsUniversal -ErrorAction Stop } }
        @{ Name = 'Invoke-Tui Drcs'; Call = { Invoke-Tui -Definition @{} -ChartRenderer Drcs -ErrorAction Stop } }
        @{ Name = 'Invoke-Tui DrcsUniversal'; Call = { Invoke-Tui -Definition @{} -ChartRenderer DrcsUniversal -ErrorAction Stop } }
        @{ Name = 'Show-SystemMonitor Drcs'; Call = { Show-SystemMonitor -ChartRenderer Drcs -ErrorAction Stop } }
        @{ Name = 'Show-SystemMonitor DrcsUniversal'; Call = { Show-SystemMonitor -ChartRenderer DrcsUniversal -ErrorAction Stop } }
    ) {
        { & $Call } | Should -Throw '*ChartRenderer*'
    }

    It 'Completes <Text> without evaluating property getters or returning duplicates' -ForEach $completionCases {
        $completions = [Management.Automation.CommandCompletion]::CompleteInput($Text, $Text.Length, $null).CompletionMatches
        foreach ($completionName in $Expected) { $completions.ListItemText | Should -Contain $completionName }
        @($completions.ListItemText | Select-Object -Unique).Count | Should -Be $completions.Count
    }

    It 'Rejects <Name> during binding before opening a terminal' -ForEach $invalidCases {
        { & $Call } | Should -Throw -ExceptionType ([Management.Automation.ParameterBindingException])
    }

    It 'Still transforms the legacy Overview view before validating Theme' {
        { Show-SystemMonitor -View Overview -Theme @{ Accent = 'invalid' } -ErrorAction Stop } | Should -Throw "*parameter 'Theme'*"
    }

    It 'Preserves literal file names containing brackets' {
        $target = Join-Path $TestDrive 'native[1].tui.json'
        New-TuiDefinition -Title NativePath | Export-TuiDefinition -Path $target
        (Import-TuiDefinition -Path $target).Title | Should -Be 'NativePath'
    }

    It 'Validates every piped log path, not only the last one' {
        $log = Join-Path $TestDrive 'app.log'; Set-Content -LiteralPath $log -Value 'line'
        { $TestDrive, $log | Show-LogViewer -ErrorAction Stop } | Should -Throw '*Expected a file*'
    }

    It 'Rejects more than one piped file manager path' {
        { $TestDrive, $TestDrive | Show-FileManager -ErrorAction Stop } | Should -Throw '*Pipe one path*'
    }

}
