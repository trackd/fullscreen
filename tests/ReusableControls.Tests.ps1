BeforeAll {
    $ErrorActionPreference = 'Stop'
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
}

Describe 'ReusableControls' -Tag 'Integration' {
    It 'Preserves catalog metadata, composition, table identity, work budgets and log parsing' {
        foreach ($commandName in @('Get-TuiControl', 'New-TuiControl')) {
            $commandInfo = Get-Command -Name "Fullscreen\$commandName" -ErrorAction Stop
            [bool]((Get-Help -Name $commandName -Full).Description.Text) | Should -BeTrue -Because "Missing control help: $commandName"
        }
        $catalog = @(Get-TuiControl)
        ($catalog.Count) | Should -Be (90) -Because 'Control catalog is incomplete.'
        ((Get-TuiControl FTable).Binding) | Should -BeLike ('*original*') -Because 'Control catalog is incomplete.'
        ((Get-TuiControl Spectre).Binding) | Should -BeLike ('*IRenderable*') -Because 'Control catalog is incomplete.'
        (@(Get-TuiControl '*View').Count) | Should -Be (2) -Because 'Catalog filters or metadata failed.'
        ((Get-TuiControl PerformanceMonitor).Live) | Should -Be ($true) -Because 'Catalog filters or metadata failed.'
        $completionText = 'New-TuiControl -Type File'
        $completion = [System.Management.Automation.CommandCompletion]::CompleteInput($completionText, $completionText.Length, $null)
        ($completion.CompletionMatches.ListItemText) | Should -Contain ('FileBrowser') -Because 'Control type completion failed.'
        $table = New-TuiControl FTable -Id inventory -Bind rows -Property @{ Columns = @('Name', 'Count'); SelectionBind = 'selected'; Height = 12 }
        $editor = New-TuiControl TextArea -Id source -Bind source -Property @{ Height = 8 }
        $root = New-TuiControl HStack -Id root -Child $table, $editor -Property @{ Spacing = 1 }
        $layout = New-TuiDefinition -Title 'Composition fixture' -Root $root
        ($root.GetType().FullName) | Should -Be ('System.Management.Automation.PSCustomObject') -Because 'Control builder leaked types or lost children.'
        ($layout.Root.Children.Count) | Should -Be (2) -Because 'Control builder leaked types or lost children.'
        $layout.Root.Children[0].Columns[0] = 'Changed'
        ($table.Columns[0]) | Should -Be ('Name') -Because 'Control composition mutated the source definition.'
        foreach ($invalid in @(@{ Columns = @('Name','name') }, @{ UnknownOption = 1 }, @{ SelectionBind = 'rows' }, @{ Height = 0 }, @{ Id = 'override' }, @{ RefreshInterval = 100 }, @{ Close = $true })) {
            { New-TuiControl FTable -Id bad -Bind rows -Property $invalid -ErrorAction Stop } | Should -Throw
        }
        # Internal private-ALC adapter tests. Consumers use the native builders and Invoke-Tui.
        $assembly = (Get-Command fullscreen\Invoke-Tui).ImplementingType.Assembly
        $documentType = $assembly.GetType('fullscreen.Custom.TuiDocument', $true)
        $document = $documentType.GetMethod('FromObject').Invoke($null, [object[]]@(New-TuiDefinition -Root $table))
        $tableType = $assembly.GetType('fullscreen.Custom.ReusableTable', $true)
        $records = @([pscustomobject]@{ Name = 'One'; Count = 7 }, [pscustomobject]@{ Name = 'Two'; Count = 12 })
        $widget = [Activator]::CreateInstance($tableType, [object[]]@($document.Root, $records))
        ($widget.Surface.Model.Rows.Count) | Should -Be (0) -Because 'FTable eagerly evaluated its hidden data.'
        $null = $tableType.GetMethod('LoadSnapshot', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($widget, $null)
        ($widget.Surface.Model.Rows.Count) | Should -Be (2) -Because 'Embedded table lost native property values.'
        ($widget.Surface.Model.Rows[0].Cells[1]) | Should -Be ('7') -Because 'Embedded table lost native property values.'
        [bool]([object]::ReferenceEquals($widget.Selection, $records[0])) | Should -BeTrue -Because 'Embedded table lost native identity.'
        $widget.Surface.Model.Enabled[1] = $false
        $widget.Replace(@([pscustomobject]@{ Name = 'Updated'; Count = 21 }))
        $null = $tableType.GetMethod('LoadSnapshot', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($widget, $null)
        ($widget.Surface.Model.Rows.Count) | Should -Be (1) -Because 'FTable snapshot replacement failed.'
        ($widget.Surface.Model.Rows[0].Cells[1]) | Should -Be ('21') -Because 'FTable snapshot replacement failed.'
        [bool]($widget.Surface.Model.Enabled[1]) | Should -BeFalse -Because 'FTable refresh reset hidden columns.'
        $automatic = New-TuiControl FTable -Id automatic -Bind rows
        $automaticDocument = $documentType.GetMethod('FromObject').Invoke($null, [object[]]@(New-TuiDefinition -Root $automatic))
        $automaticWidget = [Activator]::CreateInstance($tableType, [object[]]@($automaticDocument.Root, $records))
        $loadSnapshot = $tableType.GetMethod('LoadSnapshot', [Reflection.BindingFlags]'Instance,NonPublic')
        $null = $loadSnapshot.Invoke($automaticWidget, $null)
        $automaticWidget.Surface.Model.SortColumn = 1
        $automaticWidget.Surface.Model.GroupColumn = 0
        $automaticWidget.Surface.Model.Enabled[1] = $false
        $automaticWidget.Replace(@([pscustomobject][ordered]@{ Count = 42; Name = 'Reordered' }))
        $null = $loadSnapshot.Invoke($automaticWidget, $null)
        ($automaticWidget.Surface.Model.SortColumn) | Should -Be (0) -Because 'Column settings did not follow property names across schema reordering.'
        ($automaticWidget.Surface.Model.GroupColumn) | Should -Be (1) -Because 'Column settings did not follow property names across schema reordering.'
        [bool]($automaticWidget.Surface.Model.Enabled[0]) | Should -BeFalse -Because 'Column settings did not follow property names across schema reordering.'
        Add-Type -TypeDefinition @'
using System.Collections;
public sealed class FullscreenNullSequence : IEnumerable {
    public int Visits;
    public IEnumerator GetEnumerator() { for (int index = 0; index < 20000; index++) { Visits++; yield return null; } }
}
'@
        $nullSequence = [FullscreenNullSequence]::new()
        $automaticWidget.Replace($nullSequence)
        { $null = $loadSnapshot.Invoke($automaticWidget, $null) } | Should -Throw '*10,000*'
        ($nullSequence.Visits) | Should -BeLessOrEqual (10001) -Because 'Null enumeration bypassed the snapshot work budget.'
        $log = New-TuiControl FLogView -Id logs -Bind lines
        $logDocument = $documentType.GetMethod('FromObject').Invoke($null, [object[]]@(New-TuiDefinition -Root $log))
        $lines = @('{"level":"Error","message":"fixture failure"}')
        $logWidget = [Activator]::CreateInstance($tableType, [object[]]@($logDocument.Root, $lines))
        $null = $tableType.GetMethod('LoadSnapshot', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($logWidget, $null)
        ($logWidget.Surface.Model.Rows[0].Cells[1]) | Should -Be ('Error') -Because 'Reusable log parser failed.'
        ($logWidget.Surface.Model.Rows[0].Cells[3]) | Should -Be ('fixture failure') -Because 'Reusable log parser failed.'
        [bool]([System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies | Where-Object { $_.GetName().Name -like 'XenoAtom*' }) | Should -BeFalse -Because 'Reusable controls leaked framework types into the default ALC.'
    }
}
