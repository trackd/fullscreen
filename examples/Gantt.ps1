<#
.SYNOPSIS
Inspect a release schedule with overlapping tasks, progress and milestones.
.DESCRIPTION
Start and End are absolute DateTimeOffset instants. End equals Start for
a milestone. Progress is a fraction from 0 through 1; null means unknown.
Each original task keeps its own lane in input order. All axes use UTC.
Up/Down/PageUp/PageDown scroll tasks, the wheel or +/- zooms time, brackets
pan, and 0 resets. Click selects a task; D/Enter inspects its original object.
Shift-drag selects every task intersecting the chosen time range.
.PARAMETER DataOnly
Return the original task records without opening a terminal.
.PARAMETER Theme
Built-in palette or custom theme.
.PARAMETER Minimal
Hide surrounding application chrome.
.PARAMETER DefinitionOnly
Return a complete portable TUI definition without opening a terminal.
.EXAMPLE
.\Gantt.ps1
.EXAMPLE
.\Gantt.ps1 -DataOnly
.EXAMPLE
.\Gantt.ps1 -DataOnly | Out-Chart -GraphType Gantt -ProgressProperty Progress -PassThru
.EXAMPLE
.\Gantt.ps1 -DefinitionOnly | Invoke-Tui -Theme Monokai -Minimal
.OUTPUTS
Original tasks with DataOnly; a complete TUI definition with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. No scheduler or additional modules are required.
#>
[CmdletBinding()]
param(
    [object]$Theme = 'Default',
    [switch]$Minimal,
    [switch]$DefinitionOnly,
    [switch]$DataOnly
)
$ErrorActionPreference = 'Stop'

$tasks = @(
    [pscustomobject]@{
        Task = 'Prepare release'
        Start = [datetimeoffset]'2026-01-12T08:00:00Z'
        End = [datetimeoffset]'2026-01-12T10:00:00Z'
        Progress = 1
        Owner = 'Release'
    }
    [pscustomobject]@{
        Task = 'Build packages'
        Start = [datetimeoffset]'2026-01-12T09:00:00Z'
        End = [datetimeoffset]'2026-01-12T12:00:00Z'
        Progress = 0.75
        Owner = 'Build'
    }
    [pscustomobject]@{
        Task = 'Integration tests'
        Start = [datetimeoffset]'2026-01-12T11:00:00Z'
        End = [datetimeoffset]'2026-01-12T15:00:00Z'
        Progress = 0.5
        Owner = 'Quality'
    }
    [pscustomobject]@{
        Task = 'Documentation review'
        Start = [datetimeoffset]'2026-01-12T10:00:00Z'
        End = [datetimeoffset]'2026-01-12T14:00:00Z'
        Progress = $null
        Owner = 'Docs'
    }
    [pscustomobject]@{
        Task = 'Approval'
        Start = [datetimeoffset]'2026-01-12T15:00:00Z'
        End = [datetimeoffset]'2026-01-12T15:00:00Z'
        Progress = $null
        Owner = 'Release'
    }
    [pscustomobject]@{
        Task = 'Staged deployment'
        Start = [datetimeoffset]'2026-01-12T15:00:00Z'
        End = [datetimeoffset]'2026-01-12T18:00:00Z'
        Progress = 0.25
        Owner = 'Operations'
    }
    [pscustomobject]@{
        Task = 'Observe production'
        Start = [datetimeoffset]'2026-01-12T17:00:00Z'
        End = [datetimeoffset]'2026-01-12T20:00:00Z'
        Progress = 0
        Owner = 'Operations'
    }
    [pscustomobject]@{
        Task = 'Release complete'
        Start = [datetimeoffset]'2026-01-12T20:00:00Z'
        End = [datetimeoffset]'2026-01-12T20:00:00Z'
        Progress = 0
        Owner = 'Release'
    }
)

if ($DataOnly) {
    $tasks
    return
}

$options = @{
    GraphType = 'Gantt'
    ProgressProperty = 'Progress'
    ShowLegend = $true
    Title = 'Release schedule (UTC)'
}
if ($PSBoundParameters.ContainsKey('Theme')) {
    $options.Theme = $Theme
}
if ($PSBoundParameters.ContainsKey('Minimal')) {
    $options.Minimal = $Minimal.IsPresent
}
if ($DefinitionOnly.IsPresent) {
    $portableTasks = foreach ($task in $tasks) {
        @{
            Task = $task.Task
            Start = $task.Start.ToString('O', [cultureinfo]::InvariantCulture)
            End = $task.End.ToString('O', [cultureinfo]::InvariantCulture)
            Progress = $task.Progress
            Owner = $task.Owner
        }
    }
    return New-TuiDefinition -Title $options.Title -Root @{
        Id = 'chart'
        Type = 'Chart'
        Bind = 'tasks'
        GraphKind = 'Gantt'
        ProgressProperty = 'Progress'
        Text = $options.Title
        ShowLegend = $true
        ShowAxes = $true
        Height = 24
    } -State @{ tasks = @($portableTasks) }
}
$tasks | Out-Chart @options
