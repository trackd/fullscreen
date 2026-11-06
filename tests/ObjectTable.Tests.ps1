BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'ObjectTable' -Tag 'Integration' {
    It 'Bounds FileInfo JSON and marks cycles without emitting depth warnings' {
        $views = (Get-Command Show-ObjectTable).ImplementingType.Assembly.GetType('fullscreen.Objects.PowerShellObjectViews')
        $item = [pscustomobject]@{ Name = 'outer'; Child = [pscustomobject]@{ Answer = 42 } }
        $item | Add-Member -NotePropertyName Self -NotePropertyValue $item
        $json = $views.GetMethod('Json').Invoke($null, @($item, 4)) | ConvertFrom-Json
        $json.Child.Answer | Should -Be 42
        $json.Self | Should -Be '<circular reference>'
        $file = New-Item -ItemType File -Path (Join-Path $TestDrive 'sample.txt')
        $warnings = @()
        $text = & { $views.GetMethod('Json').Invoke($null, @($file, 4)) } -WarningVariable warnings
        $warnings | Should -BeNullOrEmpty
        ($text | ConvertFrom-Json).Name | Should -Be 'sample.txt'
    }
    It 'Preserves object identity, display metadata, lazy views and JSON caching' {
        $command = Get-Command -Name Show-ObjectTable -ErrorAction Stop
        $snapshot = $command.ImplementingType.Assembly.GetType('fullscreen.Objects.ObjectSnapshot')
        $first = [pscustomobject]@{ Name = 'alpha'; Count = 10; Nested = [pscustomobject]@{ Answer = 42 } }
        $second = [pscustomobject]@{ Name = 'beta'; Count = 2; Extra = 'other' }
        $second | Add-Member -MemberType ScriptProperty -Name Broken -Value { throw 'getter failed' }
        $items = [System.Collections.Generic.List[psobject]]::new()
        $items.Add($first); $items.Add($second)
        $model = $snapshot.GetMethod('Create').Invoke($null, @($items, $null))
        ($model.Rows.Count) | Should -Be (2) -Because 'Heterogeneous schema failed.'
        ($model.Columns) | Should -Contain ('Extra') -Because 'Heterogeneous schema failed.'
        ($model.Columns) | Should -Contain ('Broken') -Because 'Heterogeneous schema failed.'
        $brokenColumn = [Array]::IndexOf($model.Columns, 'Broken')
        ($model.Rows[1].Cells[$brokenColumn]) | Should -Be ('<property error>') -Because 'Throwing property was not isolated.'
        $model.SortColumn = [Array]::IndexOf($model.Columns, 'Count'); $model.Rebuild()
        ($model.Visible[0].Cells[0]) | Should -Be ('beta') -Because 'Typed numerical sorting failed.'
        $model.Filter = 'alpha'; $model.Rebuild()
        ($model.Visible.Count) | Should -Be (1) -Because 'Filtering lost original object identity.'
        [bool]([object]::ReferenceEquals($model.Visible[0].Original, $first)) | Should -BeTrue -Because 'Filtering lost original object identity.'
        $nested = $snapshot.GetMethod('Inspect').Invoke($null, @($first.Nested))
        ($nested.Rows[0].Cells[0]) | Should -Be ('Answer') -Because 'Nested inspection failed.'
        ($nested.Rows[0].Values[1]) | Should -Be (42) -Because 'Nested inspection failed.'
        ($command.Parameters['InputObject'].Attributes.ValueFromPipeline) | Should -Contain ($true) -Because 'Pipeline binding missing.'

        # Verify display metadata and lazy tabs in the caller's actual runspace.
        $assembly = $command.ImplementingType.Assembly
        $views = $assembly.GetType('fullscreen.Objects.PowerShellObjectViews')
        $appType = $assembly.GetType('fullscreen.Objects.ObjectTableApp')
        $optionsType = $assembly.GetType('fullscreen.Objects.ObjectTableOptions')
        $viewType = $assembly.GetType('fullscreen.Objects.ObjectView')
        $formattedView = [Enum]::Parse($viewType, 'Formatted')
        $inspectView = [Enum]::Parse($viewType, 'Inspect')
        [Enum]::GetNames($viewType) | Should -Be @('Formatted', 'Inspect', 'Json')
        $jsonView = [Enum]::Parse($viewType, 'Json')
        $global:fullscreenFormatHits = 0
        $global:fullscreenHiddenHits = 0
        $formatPath = Join-Path -Path ($TestDrive) -ChildPath ('Fullscreen-ObjectView-' + [guid]::NewGuid().ToString('N') + '.format.ps1xml')
        try {
            $formatXml = @'
<Configuration><ViewDefinitions><View><Name>FullscreenLazyView</Name><ViewSelectedBy><TypeName>fullscreen.Tests.LazyView</TypeName></ViewSelectedBy><TableControl><TableHeaders><TableColumnHeader><Label>Display name</Label><Width>22</Width></TableColumnHeader><TableColumnHeader><Label>Double count</Label><Alignment>Right</Alignment></TableColumnHeader></TableHeaders><TableRowEntries><TableRowEntry><TableColumnItems><TableColumnItem><PropertyName>Name</PropertyName></TableColumnItem><TableColumnItem><ScriptBlock>$global:fullscreenFormatHits++; $_.Count * 2</ScriptBlock><FormatString>N2</FormatString></TableColumnItem></TableColumnItems></TableRowEntry></TableRowEntries></TableControl></View></ViewDefinitions></Configuration>
'@
            [System.IO.File]::WriteAllText($formatPath, $formatXml)
            Update-FormatData -PrependPath $formatPath
            $lazyItems = [System.Collections.Generic.List[psobject]]::new()
            foreach ($number in @(10, 2)) {
                $item = [pscustomobject]@{ PSTypeName = 'fullscreen.Tests.LazyView'; Name = "item $number"; Count = $number }
                $item | Add-Member -MemberType ScriptProperty -Name Hidden -Value { $global:fullscreenHiddenHits++; 'hidden value' }
                $lazyItems.Add($item)
            }
            $options = [Activator]::CreateInstance($optionsType)
            $options.PassThru = $true
            $app = [Activator]::CreateInstance($appType, @($lazyItems, $options))
            try {
                ($app.View) | Should -Be ($formattedView) -Because 'Display metadata labels were not applied.'
                ($app.Surface.Model.Columns[0]) | Should -Be ('Display name') -Because 'Display metadata labels were not applied.'
                ($app.Surface.Model.Columns[1]) | Should -Be ('Double count') -Because 'Display metadata labels were not applied.'
                ($app.Surface.Model.Rows[0].Values[1]) | Should -Be (20) -Because 'Script display entry, formatting or raw sort value failed.'
                ($app.Surface.Model.Rows[0].Cells[1]) | Should -Be ((20).ToString('N2')) -Because 'Script display entry, formatting or raw sort value failed.'
                ($global:fullscreenFormatHits) | Should -Be (2) -Because 'Unopened views did work.'
                ($global:fullscreenHiddenHits) | Should -Be (0) -Because 'Unopened views did work.'
                [bool]($app.IsLoaded($inspectView)) | Should -BeFalse -Because 'Unopened views did work.'
                [bool]($app.IsLoaded($jsonView)) | Should -BeFalse -Because 'Unopened views did work.'
                $app.Surface.Model.Select(1, 20)
                $app.Surface.Model.Marked.Add(0) | Out-Null
                $app.SwitchView($inspectView)
                ($global:fullscreenHiddenHits) | Should -Be (1) -Because 'Inspect reads only the selected object.'
                ($app.Surface.Model.Rows | Where-Object { $_.Cells[2] -eq 'Name' }).Values[3] | Should -Be 'item 2'
                $app.SwitchView($formattedView)
                ($app.Surface.Model.Current.Id) | Should -Be 1
                [bool]($app.Surface.Model.Marked.Contains(0)) | Should -BeTrue
                ($global:fullscreenFormatHits) | Should -Be (2) -Because 'Reopening a cached format tab did extra work.'
                $app.SwitchView($jsonView)
                [bool]($app.IsLoaded($jsonView)) | Should -BeTrue -Because 'JSON was not serialized lazily for the selected object.'
                ($app.CachedJsonCount) | Should -Be (1) -Because 'JSON was not serialized lazily for the selected object.'
                $hitsAfterJson = $global:fullscreenHiddenHits
                $app.SwitchView($jsonView)
                ($global:fullscreenHiddenHits) | Should -Be ($hitsAfterJson) -Because 'Cached JSON was serialized again.'
            }
            finally { $app.Dispose() }
            $global:fullscreenFormatHits = 0; $global:fullscreenHiddenHits = 0
            $options = [Activator]::CreateInstance($optionsType); $options.View = $inspectView
            $app = [Activator]::CreateInstance($appType, @($lazyItems, $options))
            try {
                [bool]($app.IsLoaded($formattedView)) | Should -BeFalse -Because 'Opening Inspect eagerly constructed another tab.'
                [bool]($app.IsLoaded($jsonView)) | Should -BeFalse -Because 'Opening Inspect eagerly constructed another tab.'
                ($global:fullscreenFormatHits) | Should -Be (0) -Because 'Opening Inspect eagerly constructed another tab.'
                ($global:fullscreenHiddenHits) | Should -Be (1) -Because 'Inspect reads only the selected object.'
            }
            finally { $app.Dispose() }
            $global:fullscreenFormatHits = 0; $global:fullscreenHiddenHits = 0
            $options = [Activator]::CreateInstance($optionsType); $options.View = $jsonView
            $app = [Activator]::CreateInstance($appType, @($lazyItems, $options))
            try {
                [bool]($app.IsLoaded($formattedView)) | Should -BeFalse -Because 'Opening JSON eagerly constructed another tab.'
                [bool]($app.IsLoaded($inspectView)) | Should -BeFalse -Because 'Opening JSON eagerly constructed another tab.'
                ($global:fullscreenFormatHits) | Should -Be (0) -Because 'Opening JSON eagerly constructed another tab.'
                ($app.CachedJsonCount) | Should -Be (1) -Because 'Opening JSON eagerly constructed another tab.'
            }
            finally { $app.Dispose() }

            $defaultItem = [pscustomobject]@{ Name = 'default'; Count = 3; Secret = 'not displayed' }
            $displaySet = [System.Management.Automation.PSPropertySet]::new('DefaultDisplayPropertySet', [string[]]@('Name', 'Count'))
            $defaultItem | Add-Member -MemberType MemberSet -Name PSStandardMembers -Value ([System.Management.Automation.PSMemberInfo[]]@($displaySet))
            $defaultItems = [System.Collections.Generic.List[psobject]]::new(); $defaultItems.Add($defaultItem)
            $display = $views.GetMethod('Format').Invoke($null, @($defaultItems, $null))
            ($display.Columns.Count) | Should -Be (2) -Because 'DefaultDisplayPropertySet fallback failed.'
            ($display.Columns) | Should -Not -Contain ('Secret') -Because 'DefaultDisplayPropertySet fallback failed.'
            $processItems = [System.Collections.Generic.List[psobject]]::new(); $processItems.Add((Get-Process -Id $PID))
            $processDisplay = $views.GetMethod('Format').Invoke($null, @($processItems, $null))
            ($processDisplay.Columns) | Should -Contain ('NPM(K)') -Because 'Built-in process format metadata failed.'
            ($processDisplay.Columns) | Should -Contain ('CPU(s)') -Because 'Built-in process format metadata failed.'
            $fileItems = [System.Collections.Generic.List[psobject]]::new(); $fileItems.Add((Get-Item -LiteralPath "$PSScriptRoot/../README.md"))
            $fileDisplay = $views.GetMethod('Format').Invoke($null, @($fileItems, $null))
            ($fileDisplay.Columns) | Should -Contain ('LastWriteTime') -Because 'File metadata failed.'
            ($fileDisplay.Columns) | Should -Contain ('Name') -Because 'File metadata failed.'
            $dateColumn = [Array]::IndexOf($fileDisplay.Columns, 'LastWriteTime')
            ($fileDisplay.Rows[0].Values[$dateColumn]) | Should -BeOfType ([datetime]) -Because 'File timestamp must sort by the actual date.'
            $scalarItems = [System.Collections.Generic.List[psobject]]::new(); $scalarItems.Add('hello')
            $scalar = $views.GetMethod('Format').Invoke($null, @($scalarItems, $null))
            ($scalar.Rows[0].Cells[0]) | Should -Be ('hello') -Because 'Scalar display failed.'
        }
        finally {
            # Format registrations outlive TestDrive. Unregister this fixture before
            # deleting it, so a later module import can rebuild the format database.
            $formatEntries = [Management.Automation.Runspaces.Runspace]::DefaultRunspace.InitialSessionState.Formats
            for ($index = $formatEntries.Count - 1; $index -ge 0; $index--) {
                if ($formatEntries[$index].FileName -eq $formatPath) {
                    $formatEntries.RemoveItem($index)
                }
            }
            Update-FormatData
            if ([System.IO.Path]::GetFileName($formatPath).StartsWith('Fullscreen-ObjectView-') -and [System.IO.Path]::GetDirectoryName($formatPath) -eq [System.IO.Path]::TrimEndingDirectorySeparator($TestDrive)) { Remove-Item -LiteralPath $formatPath -Force -ErrorAction SilentlyContinue }
            Remove-Variable -Name fullscreenFormatHits, fullscreenHiddenHits -Scope Global -ErrorAction SilentlyContinue
        }
    }
}
