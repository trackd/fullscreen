<#
.SYNOPSIS
Shows a directed approval flow or a small service network with separate nodes and edges.
.PARAMETER GraphType
FlowChart (default) or NetworkDiagram.
.PARAMETER DataOnly
Returns one object containing the separate Nodes and Edges arrays without opening a viewer.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return a complete portable TUI definition without opening a terminal.
.EXAMPLE
.\RelationshipCharts.ps1
.EXAMPLE
.\RelationshipCharts.ps1 -GraphType NetworkDiagram
.EXAMPLE
$graph = .\RelationshipCharts.ps1 -DataOnly
Out-Chart -Nodes $graph.Nodes -Edges $graph.Edges -GraphType FlowChart
.EXAMPLE
.\RelationshipCharts.ps1 -DefinitionOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Separate Nodes/Edges with DataOnly; a complete TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. DataOnly takes precedence over DefinitionOnly.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [ValidateSet('FlowChart', 'NetworkDiagram')]
    [string] $GraphType = 'FlowChart',
    [switch] $DataOnly
)

if ($GraphType -eq 'FlowChart') {
    $nodes = @(
        [pscustomobject]@{
            NodeId = 'start'
            Name = 'Order received'
            Shape = 'Terminal'
        }
        [pscustomobject]@{
            NodeId = 'check'
            Name = 'Approved?'
            Shape = 'Decision'
        }
        [pscustomobject]@{
            NodeId = 'ship'
            Name = 'Ship order'
            Shape = 'Process'
        }
        [pscustomobject]@{
            NodeId = 'review'
            Name = 'Manual review'
            Shape = 'Process'
        }
        [pscustomobject]@{
            NodeId = 'end'
            Name = 'Complete'
            Shape = 'Terminal'
        }
        [pscustomobject]@{
            NodeId = 'archive'
            Name = 'Archive'
            Shape = 'Process'
        }
    )
    $edges = @(
        [pscustomobject]@{
            SourceId = 'start'
            TargetId = 'check'
        }
        [pscustomobject]@{
            SourceId = 'check'
            TargetId = 'ship'
            Label = 'Yes'
        }
        [pscustomobject]@{
            SourceId = 'check'
            TargetId = 'review'
            Label = 'No'
        }
        [pscustomobject]@{
            SourceId = 'review'
            TargetId = 'check'
            Label = 'Retry'
        }
        [pscustomobject]@{
            SourceId = 'ship'
            TargetId = 'end'
        }
        [pscustomobject]@{
            SourceId = 'ship'
            TargetId = 'ship'
            Label = 'Audit'
        }
    )
    $title = 'Order approval'
}
else {
    $nodes = @(
        [pscustomobject]@{
            NodeId = 'gateway'
            Name = 'Gateway'
        }
        [pscustomobject]@{
            NodeId = 'web'
            Name = 'Web service'
        }
        [pscustomobject]@{
            NodeId = 'worker'
            Name = 'Worker'
        }
        [pscustomobject]@{
            NodeId = 'queue'
            Name = 'Queue'
        }
        [pscustomobject]@{
            NodeId = 'database'
            Name = 'Database'
        }
        [pscustomobject]@{
            NodeId = 'cache'
            Name = 'Cache'
        }
        [pscustomobject]@{
            NodeId = 'monitor'
            Name = 'Monitoring'
        }
        [pscustomobject]@{
            NodeId = 'standby'
            Name = 'Standby'
        }
    )
    $edges = @(
        [pscustomobject]@{
            SourceId = 'gateway'
            TargetId = 'web'
            Label = 'HTTPS'
        }
        [pscustomobject]@{
            SourceId = 'web'
            TargetId = 'queue'
            Label = 'Jobs'
        }
        [pscustomobject]@{
            SourceId = 'queue'
            TargetId = 'worker'
        }
        [pscustomobject]@{
            SourceId = 'web'
            TargetId = 'database'
        }
        [pscustomobject]@{
            SourceId = 'worker'
            TargetId = 'database'
        }
        [pscustomobject]@{
            SourceId = 'web'
            TargetId = 'cache'
        }
        [pscustomobject]@{
            SourceId = 'monitor'
            TargetId = 'web'
            Directed = $false
        }
        [pscustomobject]@{
            SourceId = 'monitor'
            TargetId = 'worker'
            Directed = $false
        }
    )
    $title = 'Service network'
}

if ($DataOnly) {
    [pscustomobject]@{
        Nodes = $nodes
        Edges = $edges
    }
    return
}

$options = @{
    Nodes = $nodes
    Edges = $edges
    GraphType = $GraphType
    Title = $title
    ShowLegend = $true
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($DefinitionOnly.IsPresent) {
    return New-TuiDefinition -Title $title -Root @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'nodes'
        EdgeBind = 'edges'
        GraphKind = $GraphType
        Text = $title
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    } -State @{
        nodes = $nodes
        edges = $edges
    }
}
Out-Chart @options
