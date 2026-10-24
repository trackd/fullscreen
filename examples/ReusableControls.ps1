<#
.SYNOPSIS
Combine fullscreen's table, Code, chart and multiline input using native control definitions.
.DESCRIPTION
Uses demonstration records and a simulated chart. Selection updates the detail and multiline input;
submitting a table row returns that original native object. No source text is executed.
.PARAMETER Theme
Built-in palette or native custom theme hashtable.
.PARAMETER Minimal
Hide the custom application's surrounding chrome.
.PARAMETER DefinitionOnly
Return the serializable layout without opening a TUI or starting any workers.
.EXAMPLE
./ReusableControls.ps1 -Theme Monokai
.EXAMPLE
./ReusableControls.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
The original selected demonstration record after submitting a row.
.NOTES
Demonstration values are static; the chart tick deliberately simulates a metric.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$names = @(
    'api-gateway',
    'identity',
    'catalog',
    'checkout',
    'payments',
    'notifications',
    'search',
    'inventory',
    'scheduler',
    'telemetry',
    'billing',
    'worker'
)
$records = for ($index = 0; $index -lt $names.Count; $index++) {
    [pscustomobject]@{
        Name = $names[$index]
        Environment = if ($index -lt 8) {
            'production'
        }
        else {
            'staging'
        }
        Instances = 2 + $index % 4
    }
}
$initial = $records[0]
$source = "# Editable deployment draft`nservice: $($initial.Name)`nenvironment: $($initial.Environment)`nreplicas: $($initial.Instances)`nport: 443`nhealthCheck: /health/ready`nstrategy: rolling"
$table = New-TuiControl FTable -Id inventory -Bind records -Text 'DEPLOYMENT INVENTORY' -Property @{
    Columns = @(
        'Name',
        'Environment',
        'Instances'
    )
    SelectionBind = 'selected'
    Height = 20
    Minimal = $true
}
$left = New-TuiControl VStack -Id inventoryPane -Child @(
    (New-TuiControl Text -Id inventoryHeading -Text 'DEPLOYMENT INVENTORY' -Property @{
        Bold = $true
    })
    $table
    (New-TuiControl Text -Id count -Text '12 deployments · 8 production · 4 staging')
    (New-TuiControl Text -Id instructions -Text 'Arrows select · / filter · S sort · Enter choose')
    (New-TuiControl HStack -Id actions -Property @{
        Spacing = 2
    } -Child @(
        (New-TuiControl Button -Id accept -Text 'Use selected deployment')
        (New-TuiControl Button -Id cancel -Text 'Close' -Property @{
            Close = $true
        })
    ))
) -Property @{
    Width = 54
    Spacing = 1
}
$right = New-TuiControl VStack -Id detailPane -Child @(
    (New-TuiControl Code -Id details -Bind details -Property @{
        Height = 6
    })
    (New-TuiControl Chart -Id simulated -Text 'SIMULATED UTILIZATION %' -Bind metric -Property @{
        Height = 9
        Maximum = 100
        GraphKind = 'Area'
    })
    (New-TuiControl Text -Id draftLabel -Text 'EDITABLE DRAFT · returned data is never executed')
    (New-TuiControl TextArea -Id source -Bind source -Property @{
        Height = 9
    })
) -Property @{
    Spacing = 1
}
$root = New-TuiControl HStack -Id root -Child @(
    $left
    $right
) -Property @{
    Spacing = 2
}
$layout = New-TuiDefinition -Title 'Deployment workbench · reusable controls · demonstration data' -Root $root -State @{
    records = $records
    selected = $initial
    details = "# $($initial.Name)`n`n**Environment:** $($initial.Environment) · **Instances:** $($initial.Instances)`n`nHTTPS :443 · rolling updates · readiness check enabled"
    source = $source
    metric = 54.0
    phase = 0.0
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}
$result = $layout |
    Invoke-Tui -Theme $Theme -Minimal:$Minimal.IsPresent -State @{
        records = $records
        selected = $initial
    } -OnEvent {
        param(
            $tuiEvent,
            $state
        )
        if ($tuiEvent.Id -eq 'inventory' -and $tuiEvent.Kind -eq 'Selection' -and $null -ne $tuiEvent.Value) {
            $selected = $tuiEvent.Value
            $state.details = "# $($selected.Name)`n`n**Environment:** $($selected.Environment) · **Instances:** $($selected.Instances)`n`nHTTPS :443 · rolling updates · readiness check enabled"
            $state.source = "# Editable deployment draft`nservice: $($selected.Name)`nenvironment: $($selected.Environment)`nreplicas: $($selected.Instances)`nport: 443`nhealthCheck: /health/ready`nstrategy: rolling"
        }
        elseif (($tuiEvent.Id -eq 'inventory' -and $tuiEvent.Kind -eq 'Submit') -or ($tuiEvent.Id -eq 'accept' -and $tuiEvent.Kind -eq 'Click')) {
            @{
                Close = $true
                Value = $state.selected
            }
        }
    } -OnTick {
        param(
            $tuiEvent,
            $state
        )
        $state.phase += 0.2
        $state.metric = 45 + 25 * [Math]::Sin($state.phase)
    }
if (-not $result.Cancelled) {
    $result.Value
}
