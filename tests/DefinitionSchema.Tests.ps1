BeforeAll { Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force }

Describe 'Definition schema contracts' {
    It 'Round-trips the versioned starter through JSON' {
        $starter = New-TuiDefinition
        $clone = ($starter | ConvertTo-Json -Depth 64) | New-TuiDefinition
        $clone.Root.Children[1].Type | Should -Be 'TextBox'
        $clone.Version | Should -Be $starter.Version
    }

    It 'Rejects duplicate control IDs' {
        $definition = New-TuiDefinition
        $definition.Root.Children[1].Id = $definition.Root.Children[0].Id
        { $definition | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }

    It 'Rejects executable layout fields' {
        { '{"Root":{"Id":"root","OnClick":"Remove-Item"}}' | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }

    It 'Rejects cycles in native layouts' {
        $definition = New-TuiDefinition
        $definition.Root.Children += $definition.Root
        { $definition | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }

    It 'Rejects removed editor property <Property>' -ForEach @(
        @{ Property = 'IndentationSize'; Value = 4 }
        @{ Property = 'UseTab'; Value = $false }
        @{ Property = 'NoLineNumber'; Value = $false }
    ) {
        $definition = @{ Root = @{ Id = 'input'; Type = 'TextArea' } }
        $definition.Root[$Property] = $Value
        { $definition | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }

    It 'Rejects removed editor features in <Json>' -ForEach @(
        @{ Json = '{"Root":{"Id":"old","Type":"CodeEditor"}}' }
        @{ Json = '{"Root":{"Id":"input","Type":"TextArea","Language":"powershell"}}' }
    ) {
        { $Json | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }

    It 'Round-trips Code language and Image path definitions' {
        $code = New-TuiControl Code -Id code -Bind source -Property @{ Language = 'powershell' }
        $image = New-TuiControl Image -Id logo -Bind logo -Property @{ Path = 'logo.png' }
        $definition = New-TuiDefinition -Root (New-TuiControl VStack -Id root -Child $code, $image)
        $clone = ($definition | ConvertTo-Json -Depth 64) | New-TuiDefinition
        $clone.Root.Children[0].Language | Should -Be 'powershell'
        $clone.Root.Children[1].Path | Should -Be 'logo.png'
        { New-TuiControl Markdown -Id old } | Should -Throw
    }

    It 'Exposes graph styling in the control catalog' {
        (Get-TuiControl Chart).Properties | Should -Contain 'GraphKind'
    }

    It 'Round-trips <GraphKind> through native chart definitions' -ForEach @(
        'Line','Area','Bars','Step','Scatter','Stem','Histogram','BoxPlot','Pie','Gauge','RadialBars','Radar','Heatmap','Waterfall' | ForEach-Object { @{ GraphKind = $_ } }
    ) {
        $chart = New-TuiControl Chart -Id plot -Property @{ GraphKind = $GraphKind }
        $definition = New-TuiDefinition -Root $chart
        $clone = ($definition | ConvertTo-Json -Depth 64) | New-TuiDefinition
        $clone.Root.GraphKind | Should -Be $GraphKind
    }

    It 'Rejects unsupported chart styles' {
        { '{"Root":{"Id":"plot","Type":"Chart","GraphKind":"NotAChart"}}' | New-TuiDefinition -ErrorAction Stop } | Should -Throw -ErrorId 'FullscreenTuiDefinitionFailed*'
    }
}
