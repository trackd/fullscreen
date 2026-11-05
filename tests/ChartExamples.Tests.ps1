BeforeDiscovery {
    $chartExamples = @(
        @{ File = 'ChartCombinations.ps1'; Arguments = @{ GraphType = 'ColumnLine'; SeriesRole = @('Column', 'Line', 'Line') }; Count = 12 }
        @{ File = 'StackedAreas.ps1'; Arguments = @{ GraphType = 'PercentStackedArea' }; Count = 8 }
        @{ File = 'RadialCharts.ps1'; Arguments = @{ GraphType = 'Radar'; Maximum = 120 }; Count = 4 }
        @{ File = 'WaterfallHeatmap.ps1'; Arguments = @{ GraphType = 'Heatmap' }; Count = 6 }
        @{ File = 'MarketCharts.ps1'; Arguments = @{ GraphType = 'Kagi'; ReversalAmount = 7 }; Count = 9 }
        @{ File = 'Flamegraph.ps1'; Arguments = @{ FlameReverse = $true; FlameFrameRows = 3 }; Count = 41 }
        @{ File = 'Gantt.ps1'; Arguments = @{}; Count = 8 }
        @{ File = 'RelationshipCharts.ps1'; Arguments = @{ GraphType = 'NetworkDiagram' }; Count = 1 }
    )
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\output\fullscreen.psd1') -Force
}

Describe 'Chart example entrypoint contracts' {
    It '<File> preserves optional presentation values and its original DataOnly shape' -ForEach $chartExamples {
        $path = Join-Path $PSScriptRoot "..\examples\$File"
        $command = Get-Command $path
        $command.Parameters['Theme'].ParameterType | Should -Be ([object])
        $command.Parameters['Minimal'].ParameterType | Should -Be ([System.Management.Automation.SwitchParameter])
        Mock Out-Chart {
            @{} + $PesterBoundParameters
        }

        $hostCalls = @(& $path @Arguments)
        $hostCalls.Count | Should -BeGreaterThan 0
        $hostCalls[0].ContainsKey('Theme') | Should -BeFalse -Because 'Omission must allow the real host to choose its configured theme.'
        $hostCalls[0].ContainsKey('Minimal') | Should -BeFalse

        $palette = @{ Background = '#102030'; Foreground = '#F0E0D0' }
        $hostCalls = @(& $path @Arguments -Theme $palette -Minimal)
        [object]::ReferenceEquals($hostCalls[0].Theme, $palette) | Should -BeTrue
        [bool]$hostCalls[0].Minimal | Should -BeTrue

        $hostCalls = @(& $path @Arguments -Theme Default -Minimal:$false -DefinitionOnly:$false -DataOnly:$false)
        $hostCalls[0].Theme | Should -Be 'Default'
        $hostCalls[0].ContainsKey('Minimal') | Should -BeTrue
        [bool]$hostCalls[0].Minimal | Should -BeFalse

        $data = @(& $path @Arguments -DataOnly -Theme Monokai -Minimal)
        $data.Count | Should -Be $Count
        $data[0].PSObject.Properties.Name | Should -Not -Contain 'InputObject' -Because 'DataOnly must return original records, not host invocation output.'
        if ($File -eq 'RelationshipCharts.ps1') {
            $data[0].Nodes.Count | Should -Be 8
            $data[0].Edges.Count | Should -Be 8
        }
    }

    It '<File> returns a runnable portable definition using the established DefinitionOnly switch' -ForEach $chartExamples {
        $path = Join-Path $PSScriptRoot "..\examples\$File"
        $command = Get-Command $path
        $command.Parameters['DefinitionOnly'].ParameterType | Should -Be ([System.Management.Automation.SwitchParameter])
        $command.Parameters['DefinitionOnly'].Aliases.Count | Should -Be 0
        Mock Out-Chart { throw 'DefinitionOnly entered Out-Chart.' }
        Mock Invoke-Tui { throw 'DefinitionOnly entered Invoke-Tui.' }
        $layouts = @(& $path @Arguments -DefinitionOnly -Theme Monokai -Minimal)
        $layouts.Count | Should -Be 1
        $document = [fullscreen.Custom.TuiDocument]::FromObject($layouts[0])
        $document.Root.ShowAxes | Should -BeTrue -Because 'Portable examples must retain the normal Out-Chart coordinate axes.'
        $document.Root.Bind | Should -Not -BeNullOrEmpty
        $document.State.ContainsKey($document.Root.Bind) | Should -BeTrue
        $document.Root.Minimal | Should -BeFalse -Because 'Presentation options must not mutate portable data.'
        if ($File -eq 'ChartCombinations.ps1') {
            ($document.Root.YProperty -join ',') | Should -Be 'Actual,Target,Forecast'
            ($document.Root.SeriesRole -join ',') | Should -Be 'Column,Line,Line'
        }
        if ($File -eq 'RadialCharts.ps1') {
            ($document.Root.YProperty -join ',') | Should -Be 'Load,Target'
            $document.Root.Maximum | Should -Be 120
        }
        if ($File -eq 'WaterfallHeatmap.ps1') {
            ($document.Root.YProperty -join ',') | Should -Be 'Change,Target'
        }
        if ($File -eq 'MarketCharts.ps1') {
            ($document.Root.YProperty -join ',') | Should -Be 'Close'
            $document.Root.ReversalAmount | Should -Be 7
        }
        if ($File -eq 'Flamegraph.ps1') {
            $document.Root.FlameReverse | Should -BeTrue
            $document.Root.FlameFrameRows | Should -Be 3
        }
        if ($File -eq 'Gantt.ps1') {
            $document.Root.ProgressProperty | Should -Be 'Progress'
        }
        if ($File -eq 'RelationshipCharts.ps1') {
            $document.Root.EdgeBind | Should -Be 'edges'
            $document.State.ContainsKey('edges') | Should -BeTrue
        }
        foreach ($extension in 'json', 'psd1') {
            $export = Join-Path $TestDrive "$File.$extension"
            $layouts[0] | Export-TuiDefinition -Path $export -Script:$false
            $roundTrip = [fullscreen.Custom.TuiDocument]::Load($export)
            $roundTrip.Root.GraphKind | Should -Be $document.Root.GraphKind
            $app = [fullscreen.Custom.CustomTuiApp]::new($roundTrip)
            try {
                $app.Controls.ContainsKey('chart') | Should -BeTrue
                $app.State[$roundTrip.Root.Bind].Count | Should -Be $document.State[$document.Root.Bind].GetArrayLength()
                $svg = $app.Controls['chart'].ExportSvg(100, 32)
                ([xml]$svg).DocumentElement.LocalName | Should -Be 'svg'
                $app.Controls['chart'].ShowAxes | Should -BeTrue
                if ($File -eq 'StackedAreas.ps1') {
                    $svg | Should -Match '100%'
                    $svg | Should -Match '66.67%'
                    $svg | Should -Match 'Next Mon'
                }
            }
            finally {
                $app.Dispose()
            }
        }
        if ($command.Parameters.ContainsKey('GraphType')) {
            $kinds = ($command.Parameters['GraphType'].Attributes |
                Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }).ValidValues
            foreach ($kind in $kinds) {
                $variantArguments = @{} + $Arguments
                $variantArguments.GraphType = $kind
                $variant = [fullscreen.Custom.TuiDocument]::FromObject((& $path @variantArguments -DefinitionOnly))
                $app = [fullscreen.Custom.CustomTuiApp]::new($variant)
                try {
                    ([xml]$app.Controls['chart'].ExportSvg(100, 32)).DocumentElement.LocalName | Should -Be 'svg'
                    $app.Controls['chart'].GraphKind.ToString() | Should -Be $kind
                }
                finally {
                    $app.Dispose()
                }
            }
        }
        @(& $path @Arguments -DataOnly -DefinitionOnly).Count | Should -Be $Count -Because 'DataOnly compatibility takes precedence.'
    }
}
