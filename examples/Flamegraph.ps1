<#
.SYNOPSIS
Inspect exclusive profiling samples in a hierarchical flamegraph.
.DESCRIPTION
Each record has a unique FrameId, ParentId and exclusive self Weight.
Matching sibling Name prefixes aggregate; widths include all descendants.
Frames use configurable depth rows and alphabetically sorted call paths; X is not time.
Click zooms into a frame and selects its first original row. Hover shows details.
F/U/0 focuses/returns to parent/all roots, R reverses depth direction, and /
searches function names. Warm colors distinguish frames, not sample counts.
This synthetic server profile includes shallow request paths and a 20-frame
JSON/buffer-allocation path. Depth and individual row height remain independent.
.PARAMETER FlameReverse
Start roots at the top instead of the bottom.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return a complete portable TUI definition without opening a terminal.
.PARAMETER DataOnly
Return the original frame records without opening a terminal.
.PARAMETER FlameFrameRows
Terminal rows per depth, from 1 through 8. Default 2; 1 provides a compact view.
.EXAMPLE
.\Flamegraph.ps1
.EXAMPLE
.\Flamegraph.ps1 -FlameReverse
.EXAMPLE
.\Flamegraph.ps1 -DataOnly
.EXAMPLE
.\Flamegraph.ps1 -FlameFrameRows 3
.EXAMPLE
.\Flamegraph.ps1 -DefinitionOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Original frames with DataOnly; a complete TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. No profiler or additional modules are required.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [switch]$FlameReverse,
    [ValidateRange(1, 8)]
    [int]$FlameFrameRows = 2,
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'

$frames = @(
    [pscustomobject]@{
        FrameId = 'main'
        ParentId = ''
        Name = 'main'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'http'
        ParentId = 'main'
        Name = 'HandleRequest'
        Weight = 4
    }
    [pscustomobject]@{
        FrameId = 'auth'
        ParentId = 'http'
        Name = 'Authenticate'
        Weight = 3
    }
    [pscustomobject]@{
        FrameId = 'token'
        ParentId = 'auth'
        Name = 'ValidateToken'
        Weight = 8
    }
    [pscustomobject]@{
        FrameId = 'crypto'
        ParentId = 'token'
        Name = 'VerifySignature'
        Weight = 12
    }
    [pscustomobject]@{
        FrameId = 'route'
        ParentId = 'http'
        Name = 'RouteRequest'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'catalog'
        ParentId = 'route'
        Name = 'GetCatalog'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'cache'
        ParentId = 'catalog'
        Name = 'LookupCache'
        Weight = 7
    }
    [pscustomobject]@{
        FrameId = 'cachehash'
        ParentId = 'cache'
        Name = 'HashKey'
        Weight = 6
    }
    [pscustomobject]@{
        FrameId = 'db'
        ParentId = 'catalog'
        Name = 'QueryDatabase'
        Weight = 3
    }
    [pscustomobject]@{
        FrameId = 'decode'
        ParentId = 'db'
        Name = 'DecodeRows'
        Weight = 12
    }
    [pscustomobject]@{
        FrameId = 'utf8'
        ParentId = 'decode'
        Name = 'DecodeUtf8'
        Weight = 9
    }
    [pscustomobject]@{
        FrameId = 'sort'
        ParentId = 'catalog'
        Name = 'SortResults'
        Weight = 8
    }
    [pscustomobject]@{
        FrameId = 'compare'
        ParentId = 'sort'
        Name = 'CompareKeys'
        Weight = 6
    }
    [pscustomobject]@{
        FrameId = 'serialize'
        ParentId = 'http'
        Name = 'SerializeResponse'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'json'
        ParentId = 'serialize'
        Name = 'WriteJson'
        Weight = 10
    }
    [pscustomobject]@{
        FrameId = 'escape'
        ParentId = 'json'
        Name = 'EscapeString'
        Weight = 7
    }
    [pscustomobject]@{
        FrameId = 'send'
        ParentId = 'serialize'
        Name = 'SendBytes'
        Weight = 9
    }
    [pscustomobject]@{
        FrameId = 'batch'
        ParentId = 'main'
        Name = 'RunBackgroundJobs'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'read'
        ParentId = 'batch'
        Name = 'ReadQueue'
        Weight = 8
    }
    [pscustomobject]@{
        FrameId = 'process'
        ParentId = 'batch'
        Name = 'ProcessJob'
        Weight = 5
    }
    [pscustomobject]@{
        FrameId = 'parsejob'
        ParentId = 'process'
        Name = 'ParsePayload'
        Weight = 11
    }
    [pscustomobject]@{
        FrameId = 'validatejob'
        ParentId = 'parsejob'
        Name = 'ValidateFields'
        Weight = 7
    }
    [pscustomobject]@{
        FrameId = 'parsejson'
        ParentId = 'parsejob'
        Name = 'ParseJson'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'readvalue'
        ParentId = 'parsejson'
        Name = 'ReadValue'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'readobject'
        ParentId = 'readvalue'
        Name = 'ReadObject'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'readproperty'
        ParentId = 'readobject'
        Name = 'ReadProperty'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'readtoken'
        ParentId = 'readproperty'
        Name = 'ReadToken'
        Weight = 3
    }
    [pscustomobject]@{
        FrameId = 'decodestring'
        ParentId = 'readtoken'
        Name = 'DecodeString'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'readutf8'
        ParentId = 'decodestring'
        Name = 'ReadUtf8'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'decoderune'
        ParentId = 'readutf8'
        Name = 'DecodeRune'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'classifybyte'
        ParentId = 'decoderune'
        Name = 'ClassifyByte'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'decodesequence'
        ParentId = 'classifybyte'
        Name = 'DecodeSequence'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'validatecodepoint'
        ParentId = 'decodesequence'
        Name = 'ValidateCodePoint'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'appendrune'
        ParentId = 'validatecodepoint'
        Name = 'AppendRune'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'growbuffer'
        ParentId = 'appendrune'
        Name = 'GrowBuffer'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'rentarray'
        ParentId = 'growbuffer'
        Name = 'RentArray'
        Weight = 2
    }
    [pscustomobject]@{
        FrameId = 'allocatearray'
        ParentId = 'rentarray'
        Name = 'AllocateArray'
        Weight = 1
    }
    [pscustomobject]@{
        FrameId = 'zeromemory'
        ParentId = 'allocatearray'
        Name = 'ZeroMemory'
        Weight = 28
    }
    [pscustomobject]@{
        FrameId = 'save'
        ParentId = 'process'
        Name = 'SaveResult'
        Weight = 10
    }
    [pscustomobject]@{
        FrameId = 'idle'
        ParentId = ''
        Name = 'Idle'
        Weight = 5
    }
)
if ($DataOnly) {
    $frames
    return
}

$options = @{
    GraphType = 'Flamegraph'
    FrameNameProperty = 'Name'
    FlameReverse = $FlameReverse.IsPresent
    FlameFrameRows = $FlameFrameRows
    ShowLegend = $true
    Title = 'Synthetic server CPU samples'
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($DefinitionOnly.IsPresent) {
    return New-TuiDefinition -Title $options.Title -Root @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'frames'
        GraphKind = 'Flamegraph'
        FrameNameProperty = 'Name'
        FlameReverse = $FlameReverse.IsPresent
        FlameFrameRows = $FlameFrameRows
        Text = $options.Title
        ShowLegend = $true
        ShowAxes = $true
        Height = 48
    } -State @{ frames = $frames }
}
$frames | Out-Chart @options
