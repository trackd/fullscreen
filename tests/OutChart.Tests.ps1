BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Out-Chart pipeline contract' {
    It 'Accepts whole objects and numeric input and stops at the declared limit before opening a terminal' {
        $command = Get-Command fullscreen\Out-Chart
        $command.Parameters['InputObject'].ParameterType | Should -Be ([psobject[]])
        $command.Parameters['InputObject'].Attributes.ValueFromPipeline | Should -Contain $true
        $command.Parameters['PassThru'].ParameterType | Should -Be ([System.Management.Automation.SwitchParameter])
        $command.OutputType.Name | Should -Contain 'System.Management.Automation.PSObject'
        { 1, 2, 3 | Out-Chart -MaximumCount 2 -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenChartLimit*'
        { @([pscustomobject]@{ A = 1; B = 2 }, [pscustomobject]@{ A = 3; B = 4 }) | Out-Chart -YProperty A, B -MaximumCount 1 -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenChartLimit*'
    }
    It 'Rejects missing properties and invalid graph types before opening a terminal' {
        { [pscustomobject]@{ Amount = 3 } | Out-Chart -YProperty Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { [pscustomobject]@{ Amount = 3 } | Out-Chart -XProperty Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { 1 | Out-Chart -GraphType Unknown -ErrorAction Stop } | Should -Throw
    }
    It 'Handles an empty pipeline without opening a terminal or emitting objects' {
        @(@() | Out-Chart).Count | Should -Be 0
        @(@() | Out-Chart -PassThru).Count | Should -Be 0
    }
    It 'Validates grouping error bounds and bounded refresh before opening a terminal' {
        $command = Get-Command fullscreen\Out-Chart
        $command.Parameters['Refresh'].ParameterType | Should -Be ([scriptblock])
        $command.Parameters['Aggregate'].ParameterType.FullName | Should -Be 'fullscreen.Charts.ChartAggregate'
        { [pscustomobject]@{ Y = 1 } | Out-Chart -GroupBy Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { [pscustomobject]@{ Y = 1 } | Out-Chart -LowerProperty Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { [pscustomobject]@{ Y = 1 } | Out-Chart -UpperProperty Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { Out-Chart -Refresh { 1, 2, 3 } -MaximumCount 2 -ErrorAction Stop } | Should -Throw '*MaximumCount*'
        { Out-Chart -RefreshInterval 99 -ErrorAction Stop } | Should -Throw
    }
    It 'Evaluates calculated X and Y parameters with original objects and caller closures before terminal startup' {
        $scriptPath = Join-Path $TestDrive 'calculated-chart.ps1'
        @'
param([string]$ModulePath)
$ErrorActionPreference = 'Stop'
Import-Module $ModulePath
$factor = 4
$seen = [Collections.Generic.List[double]]::new()
$expression = { $seen.Add($_.Amount * $factor); $_.Amount * $factor }.GetNewClosure()
$items = @([pscustomobject]@{ Name = 'one'; Amount = 3 }, [pscustomobject]@{ Name = 'two'; Amount = 5 })
try {
    $items | Out-Chart -XProperty @{ Label = 'Caption'; Expression = { $PSItem.Name.ToUpperInvariant() } } -YProperty Amount, @{ n = 'Scaled'; e = $expression } -ErrorAction Stop
    throw 'The redirected host should reject interactive terminal startup.'
}
catch {
    if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
}
if (($seen -join ',') -ne '12,20') { throw "Calculated expression lost caller scope or input identity: $seen" }
if ($items[0].Amount -ne 3 -or $items[0].PSObject.Properties['Scaled']) { throw 'Original input objects were changed.' }
$refreshSeen = [Collections.Generic.List[object]]::new()
$refresh = { $null; $items[0]; $null; $items[1] }.GetNewClosure()
$capture = { $refreshSeen.Add($_); $_.Amount * $factor }.GetNewClosure()
try {
    Out-Chart -Refresh $refresh -MaximumCount 2 -YProperty @{ Name = 'Read'; Expression = $capture } -ErrorAction Stop
    throw 'The redirected host should reject interactive terminal startup.'
}
catch {
    if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
}
if ($refreshSeen.Count -ne 2 -or -not [object]::ReferenceEquals($items[0], $refreshSeen[0]) -or -not [object]::ReferenceEquals($items[1], $refreshSeen[1])) { throw 'Refresh changed object identity or null handling.' }
if ($items[0].PSObject.Properties['Read']) { throw 'Refresh calculations changed the original object.' }
'calculated properties evaluated'
'@ | Set-Content -LiteralPath $scriptPath
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-File', $scriptPath, '-ModulePath', (Join-Path $PSScriptRoot '../output/fullscreen.psd1'))) {
            $start.ArgumentList.Add($argument)
        }
        $child = [Diagnostics.Process]::Start($start)
        try {
            $child.StandardInput.Close()
            $outputTask = $child.StandardOutput.ReadToEndAsync()
            $errorTask = $child.StandardError.ReadToEndAsync()
            $child.WaitForExit(30000) | Should -BeTrue
            $outputText = $outputTask.GetAwaiter().GetResult()
            $errorText = $errorTask.GetAwaiter().GetResult()
            $child.ExitCode | Should -Be 0 -Because $errorText
            $outputText | Should -Match 'calculated properties evaluated'
        }
        finally {
            if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
            $child.Dispose()
        }
    }
    It 'Stops refresh producers after the first excess non-null object and reports overflow' {
        $seen = [Collections.Generic.List[int]]::new()
        $refresh = {
            for ($sample = 1; $sample -le 1000000; $sample++) {
                $seen.Add($sample)
                $null
                [pscustomobject]@{ Y = $sample }
            }
        }.GetNewClosure()
        { Out-Chart -Refresh $refresh -MaximumCount 3 -ErrorAction Stop } | Should -Throw '*MaximumCount*'
        $seen.Count | Should -Be 4
        ($seen -join ',') | Should -Be '1,2,3,4'
    }
}
