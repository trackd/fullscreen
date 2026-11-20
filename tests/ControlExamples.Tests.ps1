BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module "$PSScriptRoot/../output/fullscreen.psd1" -Force
    function Get-ExampleNode {
        param($Node)
        $Node
        foreach ($child in $Node.Children) {
            Get-ExampleNode $child
        }
    }
}
Describe 'Custom control examples' {
    It 'Covers every supported control in the copyable gallery and round-trips its data' {
        $layout = & "$PSScriptRoot/../examples/ControlGallery.ps1" -DefinitionOnly
        $nodes = @(Get-ExampleNode $layout.Root)
        Compare-Object @(Get-TuiControl | ForEach-Object Name | Sort-Object) @($nodes.Type | Sort-Object -Unique) | Should -BeNullOrEmpty
        $path = Join-Path $TestDrive 'gallery.psd1'
        $layout | Export-TuiDefinition -Path $path
        $copy = Import-TuiDefinition -Path $path
        $copy.State.rows.Count | Should -Be 2
        $copy.Root.Children[0].TabTitle | Should -BeExactly 'Form'
        @(Get-ExampleNode $copy.Root).Count | Should -Be $nodes.Count
    }
    It 'Runs gallery bindings, selection, simulated ticks and accepted results through the example handler map' {
        Mock Invoke-Tui {
            $State.name = 'Ada'
            $State.notes = "Line one`nLine two"
            $State.environment = 'Production'
            $State.enabled = $true
            $null = & $Handler['greet:Click'] @{
                Id = 'greet'
                Kind = 'Click'
            } $State
            $State.message | Should -BeExactly 'Hello Ada, from Production!'
            $State.enabled = $false
            $null = & $Handler['greet:Click'] @{
                Id = 'greet'
                Kind = 'Click'
            } $State
            $State.message | Should -BeExactly 'Greeting disabled'
            $row = [pscustomobject]@{
                Name = 'original'
                Environment = 'Production'
            }
            $null = & $Handler['inventory:Selection'] @{
                Value = $row
            } $State
            $State.details | Should -BeExactly "# original`nEnvironment: Production"
            $reply = & $Handler['inventory:Submit'] @{
                Value = $row
            } $State
            [object]::ReferenceEquals($reply.Value, $row) | Should -BeTrue
            $reply.Close | Should -BeTrue
            $State.phase = 0
            $null = & $OnTick @{
                Kind = 'Tick'
            } $State
            $State.load | Should -BeGreaterThan 45
            $reply = & $Handler['accept:Click'] @{
                Kind = 'Click'
            } $State
            $reply.Close | Should -BeTrue
            @{
                Cancelled = $false
                Value = $reply.Value
            }
        }
        $value = & "$PSScriptRoot/../examples/ControlGallery.ps1"
        $value.Name | Should -BeExactly 'Ada'
        $value.Notes | Should -BeExactly "Line one`nLine two"
        $value.Enabled | Should -BeFalse
        $value.Environment | Should -BeExactly 'Production'
    }
}
