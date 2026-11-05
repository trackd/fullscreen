BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'Out-Chart pipeline contract' {
    It 'Accepts separate diagram collections and validates references before terminal startup' {
        $command = Get-Command fullscreen\Out-Chart
        foreach ($field in 'Nodes', 'Edges') {
            $command.Parameters[$field].ParameterType | Should -Be ([System.Management.Automation.PSObject[]])
            $command.Parameters[$field].ParameterSets.Keys | Should -Contain 'Diagram'
        }
        $command.Parameters['InputObject'].ParameterSets.Keys | Should -Contain 'Objects'
        $graph = & (Join-Path $PSScriptRoot '..\examples\RelationshipCharts.ps1') -DataOnly
        $graph.Nodes.Count | Should -Be 6
        $graph.Edges.Count | Should -Be 6
        $network = & (Join-Path $PSScriptRoot '..\examples\RelationshipCharts.ps1') -GraphType NetworkDiagram -DataOnly
        $network.Nodes.Count | Should -Be 8
        @($network.Edges | Where-Object Directed -EQ $false).Count | Should -Be 2
        $badEdge = [pscustomobject]@{
            SourceId = 'start'
            TargetId = 'missing'
        }
        { Out-Chart -Nodes $graph.Nodes -Edges @($badEdge) -ErrorAction Stop } | Should -Throw '*existing SourceId/TargetId*'
        { $graph.Nodes | Out-Chart -GraphType FlowChart -ErrorAction Stop } | Should -Throw '*separate*'
        { Out-Chart -Nodes $graph.Nodes -Edges $graph.Edges -GraphType Bars -ErrorAction Stop } | Should -Throw '*FlowChart or NetworkDiagram*'
        $node = New-TuiControl Chart -Id network -Bind nodes -Property @{
            GraphKind = 'NetworkDiagram'
            EdgeBind = 'edges'
            SelectionBind = 'selected'
        }
        $node.EdgeBind | Should -Be 'edges'
        { New-TuiControl Chart -Id bad -Bind nodes -Property @{ GraphKind = 'FlowChart' } } | Should -Throw '*EdgeBind*'
    }
    It 'Exposes explicit Gantt selectors and rejects invalid interval data before terminal startup' {
        $command = Get-Command fullscreen\Out-Chart
        [Enum]::GetNames($command.Parameters['GraphType'].ParameterType) | Should -Contain 'Gantt'
        foreach ($field in 'StartProperty', 'EndProperty', 'TaskNameProperty', 'ProgressProperty') {
            $command.Parameters[$field].ParameterType | Should -Be ([string])
        }
        $tasks = & (Join-Path $PSScriptRoot '..\examples\Gantt.ps1') -DataOnly
        $tasks.Count | Should -Be 8
        $tasks[0].Start | Should -BeOfType ([datetimeoffset])
        $tasks[3].Progress | Should -BeNullOrEmpty
        $tasks[4].Start | Should -Be $tasks[4].End
        $node = New-TuiControl Chart -Id schedule -Bind tasks -Property @{
            GraphKind = 'Gantt'
            StartProperty = 'From'
            EndProperty = 'To'
            TaskNameProperty = 'Label'
            ProgressProperty = 'Done'
        }
        $node.StartProperty | Should -Be 'From'
        { $tasks | Out-Chart -GraphType Gantt -ProgressProperty Progress -Minimum 0 -ErrorAction Stop } | Should -Throw '*Y bounds*'
        { $tasks | Out-Chart -GraphType Gantt -YProperty Progress -ErrorAction Stop } | Should -Throw '*not X/Y*'
        $bad = [pscustomobject]@{
            Task = 'Invalid'
            Start = '2026-01-12T09:00:00'
            End = '2026-01-12T10:00:00Z'
        }
        { $bad | Out-Chart -GraphType Gantt -ErrorAction Stop } | Should -Throw '*timezone*'
    }
    It 'Validates frame hierarchies before terminal startup and keeps sample records readable' {
        $command = Get-Command fullscreen\Out-Chart
        [Enum]::GetNames($command.Parameters['GraphType'].ParameterType) | Should -Contain 'Flamegraph'
        foreach ($field in 'FrameIdProperty', 'ParentIdProperty', 'FrameNameProperty') {
            $command.Parameters[$field].ParameterType | Should -Be ([string])
        }
        $command.Parameters['FlameReverse'].ParameterType | Should -Be ([System.Management.Automation.SwitchParameter])
        $command.Parameters['FlameFrameRows'].ParameterType | Should -Be ([int])
        $node = New-TuiControl Chart -Id flame -Bind frames -Property @{
            GraphKind = 'Flamegraph'
            FlameFrameRows = 3
        }
        $node.FlameFrameRows | Should -Be 3
        $frames = & (Join-Path $PSScriptRoot '..\examples\Flamegraph.ps1') -DataOnly
        $frames.Count | Should -BeGreaterThan 20
        ($frames.Weight | Measure-Object -Sum).Sum | Should -BeGreaterThan 0
        $byId = @{}
        foreach ($frame in $frames) {
            $byId[$frame.FrameId] = $frame
        }
        $depth = 0
        $leaf = $byId['zeromemory']
        while ($null -ne $leaf) {
            $depth++
            $leaf = if ($leaf.ParentId) {
                $byId[$leaf.ParentId]
            }
            else {
                $null
            }
        }
        $depth | Should -Be 20
        { $frames | Out-Chart -GraphType Flamegraph -FrameNameProperty Name -YProperty Missing -ErrorAction Stop } | Should -Throw '*Missing*'
        { $frames | Out-Chart -GraphType Flamegraph -FrameNameProperty Name -Minimum 0 -ErrorAction Stop } | Should -Throw '*numeric bounds*'
        { [pscustomobject]@{ FrameId = 'a'; ParentId = 'a'; Weight = 1 } | Out-Chart -GraphType Flamegraph -ErrorAction Stop } | Should -Throw '*cycle*'
        { [pscustomobject]@{ FrameId = 'a'; ParentId = ''; Weight = -1 } | Out-Chart -GraphType Flamegraph -ErrorAction Stop } | Should -Throw '*nonnegative*'
        $radial = & (Join-Path $PSScriptRoot '..\examples\RadialCharts.ps1') -DataOnly
        ($radial.Category -join ',') | Should -Be 'CPU,Memory,Disk,Network'
        ($radial.Target -join ',') | Should -Be '50,60,70,80'
        $changes = & (Join-Path $PSScriptRoot '..\examples\WaterfallHeatmap.ps1') -DataOnly
        $changes[0].Change | Should -BeOfType ([int])
        $changes[3].Change | Should -BeNullOrEmpty
    }
    It 'Exposes market kinds and validates price reversals before terminal startup' {
        $command = Get-Command fullscreen\Out-Chart
        $command.Parameters['ReversalAmount'].ParameterType | Should -Be ([double])
        foreach ($kind in 'Marimekko', 'Kagi') {
            [Enum]::GetNames($command.Parameters['GraphType'].ParameterType) | Should -Contain $kind
            $node = New-TuiControl Chart -Id market -Bind samples -Property @{ GraphKind = $kind; ReversalAmount = 5 }
            $node.ReversalAmount | Should -Be 5
        }
        { 100, 110 | Out-Chart -GraphType Kagi -ReversalAmount 0 -ErrorAction Stop } | Should -Throw '*ReversalAmount*'
        { 100, 0 | Out-Chart -GraphType Kagi -ErrorAction Stop } | Should -Throw '*positive*'
        { -1, 2 | Out-Chart -GraphType Marimekko -ErrorAction Stop } | Should -Throw '*nonnegative*'
        $markets = & (Join-Path $PSScriptRoot '..\examples\MarketCharts.ps1') -DataOnly
        $markets.Count | Should -Be 3
        { $markets | Out-Chart -GraphType Marimekko -XProperty Market -YProperty Alpha, Beta -Minimum 0 -ErrorAction Stop } | Should -Throw '*fixed*100*'
        $prices = & (Join-Path $PSScriptRoot '..\examples\MarketCharts.ps1') -GraphType Kagi -DataOnly
        $prices.Count | Should -Be 9
    }
    It 'Supports column line roles, explicit false legends and the public custom chart property' {
        $command = Get-Command fullscreen\Out-Chart
        $command.Parameters['GraphType'].ParameterType.FullName | Should -Be 'fullscreen.Charts.GraphKind'
        [Enum]::GetNames($command.Parameters['GraphType'].ParameterType) | Should -Contain 'ColumnLine'
        foreach ($parameterName in 'GraphType', 'Aggregate', 'SeriesRole') {
            @($command.Parameters[$parameterName].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).Count | Should -Be 0
        }
        $command.Parameters['SeriesRole'].ParameterType.FullName | Should -Be 'fullscreen.Charts.ChartSeriesRole[]'
        $command.Parameters['ShowLegend'].ParameterType | Should -Be ([System.Management.Automation.SwitchParameter])
        $items = @(
            [pscustomobject]@{ Sample = 'one'; Actual = 8; Target = 6 }
            [pscustomobject]@{ Sample = 'two'; Actual = -2; Target = 5 }
        )
        { $items | Out-Chart -XProperty Sample -YProperty Actual, Target -GraphType ColumnLine -SeriesRole Column -ErrorAction Stop } |
            Should -Throw '*one*role*selected Y property*'
        $node = New-TuiControl Chart -Id combination -Bind samples -Property @{ GraphKind = 'ColumnLine'; ShowLegend = $true }
        $node.GraphKind | Should -Be 'ColumnLine'
        $node.ShowLegend | Should -BeTrue
        $example = Join-Path $PSScriptRoot '..\examples\ChartCombinations.ps1'
        $data = & $example -DataOnly
        $data.Count | Should -Be 12
        $data[0].PSObject.Properties.Name | Should -Contain 'Actual'
        $data[5].Forecast | Should -BeNullOrEmpty
    }
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
        { 1 | Out-Chart -GraphType Unknown -ErrorAction Stop } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException])
        { 1 | Out-Chart -GraphType 999 -ErrorAction Stop } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException])
        { 1 | Out-Chart -SeriesRole Unknown -ErrorAction Stop } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException])
        { 1 | Out-Chart -Aggregate Unknown -ErrorAction Stop } | Should -Throw -ExceptionType ([System.Management.Automation.ParameterBindingException])
    }
    It 'Validates radial input before opening a terminal and exposes the custom radial kinds' {
        $items = & (Join-Path $PSScriptRoot '..\examples\RadialCharts.ps1') -DataOnly
        $items.Count | Should -Be 4
        ($items.Load -join ',') | Should -Be '25,50,75,100'
        foreach ($kind in 'Pie', 'Gauge', 'RadialBars', 'Radar') {
            $node = New-TuiControl Chart -Id radial -Bind loads -Property @{ GraphKind = $kind; ShowLegend = $true }
            $node.GraphKind | Should -Be $kind
        }
        { $items | Out-Chart -GraphType Pie -YProperty Load, Target -ErrorAction Stop } | Should -Throw '*requires one Y*'
        { -1, 2, 3 | Out-Chart -GraphType Gauge -ErrorAction Stop } | Should -Throw '*negative*'
        { 1, 2 | Out-Chart -GraphType Radar -ErrorAction Stop } | Should -Throw '*three category*'
        { $items | Out-Chart -GraphType Gauge -YProperty Load -Minimum 100 -Maximum 100 -ErrorAction Stop } | Should -Throw '*Minimum less than Maximum*'
    }
    It 'Handles an empty pipeline without opening a terminal or emitting objects' {
        @(@() | Out-Chart).Count | Should -Be 0
        @(@() | Out-Chart -PassThru).Count | Should -Be 0
    }
    It 'Validates waterfall and matrix input without opening a terminal' {
        $items = & (Join-Path $PSScriptRoot '..\examples\WaterfallHeatmap.ps1') -DataOnly
        $items.Count | Should -Be 6
        $items[2].Change | Should -Be 0
        $items[3].Change | Should -BeNullOrEmpty
        foreach ($kind in 'Waterfall', 'Heatmap', 'Marimekko', 'Kagi') {
            $node = New-TuiControl Chart -Id grid -Bind samples -Property @{ GraphKind = $kind; ShowLegend = $true }
            $node.GraphKind | Should -Be $kind
        }
        { $items | Out-Chart -GraphType Waterfall -YProperty Change, Target -ErrorAction Stop } | Should -Throw '*requires one Y*'
        { [double]::MaxValue, [double]::MaxValue | Out-Chart -GraphType Waterfall -ErrorAction Stop } | Should -Throw '*finite double*'
        { $items | Out-Chart -GraphType Heatmap -YProperty Change, Target -Minimum 20 -Maximum -20 -ErrorAction Stop } | Should -Throw '*Minimum*Maximum*'
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
if (-not [Console]::IsInputRedirected -or -not [Console]::IsOutputRedirected -or -not [Console]::IsErrorRedirected) {
    throw 'Chart host tests require redirected input, output and error streams.'
}
$frames = @(
    [pscustomobject]@{ FrameId = 'main'; ParentId = ''; Name = 'main'; Weight = 2 }
    [pscustomobject]@{ FrameId = 'child'; ParentId = 'main'; Name = 'worker'; Weight = 3 }
)
try {
    $frames | Out-Chart -GraphType Flamegraph -FrameNameProperty Name -FlameReverse -ErrorAction Stop
    throw 'The redirected host should reject interactive terminal startup.'
}
catch {
    if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
}
$radialRows = 1..4 | ForEach-Object {
    [pscustomobject]@{ Category = "Resource $_"; Load = $_ * 25; Target = 50 }
}
foreach ($kind in 'Pie', 'Gauge', 'RadialBars', 'Radar') {
    $options = @{
        XProperty = 'Category'
        YProperty = if ($kind -eq 'Radar') { @('Load', 'Target') } else { @('Load') }
        GraphType = $kind
        ShowLegend = $true
    }
    if ($kind -ne 'Pie') { $options.Minimum = 0; $options.Maximum = 100 }
    try {
        $radialRows | Out-Chart @options -ErrorAction Stop
        throw 'The redirected host should reject interactive terminal startup.'
    }
    catch {
        if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
    }
}
foreach ($kind in 'Waterfall', 'Heatmap') {
    try {
        $radialRows | Out-Chart -GraphType $kind -XProperty Category -YProperty Load -ShowLegend -ErrorAction Stop
        throw 'The redirected host should reject interactive terminal startup.'
    }
    catch {
        if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
    }
}
$combinations = @(
    [pscustomobject]@{ Sample = 'one'; Actual = 8; Target = 6 }
    [pscustomobject]@{ Sample = 'two'; Actual = -2; Target = 5 }
)
try {
    $combinations | Out-Chart -XProperty Sample -YProperty Actual, Target -GraphType ColumnLine -SeriesRole Column, Line -ShowLegend:$false -ErrorAction Stop
    throw 'The redirected host should reject interactive terminal startup.'
}
catch {
    if ($_.Exception.Message -notlike '*requires an interactive terminal*') { throw }
}
$tasks = @(
    [pscustomobject]@{
        Task = 'Build'
        Start = [datetimeoffset]'2026-01-12T08:00:00Z'
        End = [datetimeoffset]'2026-01-12T12:00:00Z'
        Progress = 0.5
    }
)
try {
    $tasks | Out-Chart -GraphType Gantt -ProgressProperty Progress -ErrorAction Stop
    throw 'The redirected host should reject interactive terminal startup.'
}
catch {
    if ($_.Exception.Message -notlike '*requires an interactive terminal*') {
        throw
    }
}
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
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $scriptPath, '-ModulePath', (Join-Path $PSScriptRoot '../output/fullscreen.psd1'))) {
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
