<#
.SYNOPSIS
Browse login sessions with optional account process expansion.
.DESCRIPTION
UserList runs independently of the performance dashboard. Session data loads only
while visible. IncludeProcesses adds account process totals and confirmed actions.
.PARAMETER IncludeProcesses
Load account-owned processes as well as login sessions. L toggles this at runtime.
.PARAMETER Theme
fullscreen palette name or custom palette.
.PARAMETER DefinitionOnly
Return the layout without opening a terminal or starting a collector.
.EXAMPLE
./UserSessions.ps1
.EXAMPLE
./UserSessions.ps1 -IncludeProcesses -Theme Monokai
.EXAMPLE
./UserSessions.ps1 -DefinitionOnly | Show-TuiDesigner
.OUTPUTS
A TUI definition with DefinitionOnly; otherwise the selected UserViewRow when accepted.
.NOTES
Import fullscreen first. Use 130 by 40 cells. D shows all session details, L toggles
process loading, + expands an account, and Enter on a process opens confirmed actions.
Processes are grouped by account rather than attributed to individual login sessions.
#>
[CmdletBinding()]
param(
    [switch]$IncludeProcesses,
    [object]$Theme = 'Dark Modern',
    [switch]$DefinitionOnly
)

$users = New-TuiControl UserList -Id users -Property @{
    SelectionBind = 'selectedUser'
    IncludeProcesses = $IncludeProcesses.IsPresent
    Height = 30
}
$accept = New-TuiControl Button -Id accept -Text 'Return selected user'
$root = New-TuiControl VStack -Id root -Child @(
    $users
    $accept
)
$layout = New-TuiDefinition -Title 'User sessions' -Root $root
if ($DefinitionOnly.IsPresent) {
    return $layout
}

$result = $layout |
    Invoke-Tui -Theme $Theme -Handler @{
        'accept:Click' = {
            param(
                $tuiEvent,
                $state
            )
            @{
                Close = $true
                Value = $state.selectedUser
            }
        }
    }
if (-not $result.Cancelled) {
    $result.Value
}
