#Requires -Version 7.6
<#
.SYNOPSIS
Browse Windows event logs in an Event Viewer-style fullscreen workbench.
.DESCRIPTION
Shows enabled Windows logs, a bounded newest-first event table, server-side filters,
and read-only General/XML details. Queries run in a background PowerShell pipeline;
messages and XML are fetched only for the selected event. Nothing is cleared or changed.
Search filters the loaded metadata snapshot, not every message in the log.
.PARAMETER Session
Optional dedicated open PSSession connected to Windows, through WinRM or SSH.
Use this from Linux/macOS to view a Windows machine's logs. The caller owns the session.
The session is not closed; it must not be used by another command while this UI is open.
.PARAMETER LogName
Initial Windows log name. Defaults to System. The log browser also lists enabled channels.
.PARAMETER MaxEvents
Maximum events per query, 25 through 2000. Defaults to 250. Change the UI limit to load more.
.PARAMETER Hours
Initial lookback in hours, 1 through 720. Defaults to 24.
.PARAMETER Theme
Built-in fullscreen theme name or custom palette.
.PARAMETER DefinitionOnly
Return the portable layout without accessing logs, opening a session or starting a worker.
.EXAMPLE
./EventViewer.ps1 -LogName System -Theme 'Dark Modern'
.EXAMPLE
$session = New-PSSession -ComputerName server01
try { ./EventViewer.ps1 -Session $session -LogName Application -MaxEvents 500 }
finally { Remove-PSSession -Session $session }
.EXAMPLE
$session = New-PSSession -HostName windows-server -UserName reader
try { ./EventViewer.ps1 -Session $session }
finally { Remove-PSSession -Session $session }
.OUTPUTS
A layout with DefinitionOnly; otherwise no output.
.NOTES
Import fullscreen first. Local collection requires Windows. Remote targets require
Get-WinEvent (Windows PowerShell 5.1 or PowerShell 7); no fullscreen installation is needed there.
Use an account permitted to read the chosen channel, such as an Event Log Readers member.
Queries time out after 30 seconds; Stop cancels only this example's current pipeline.
Drag the splitters to resize panes. FTable supports mouse, right-click, sorting and / search.
Click a channel to open it immediately; keyboard selection and Enter also work.
Scroll the channel list with the mouse wheel or navigation keys. Select an event
to preview its details; press Enter or double-click to show the General tab.
F10 exits. No logs are cleared, enabled, exported or otherwise modified.
#>
[CmdletBinding()]
param(
    [System.Management.Automation.Runspaces.PSSession]$Session,
    [ValidateNotNullOrEmpty()]
    [string]$LogName = 'System',
    [ValidateRange(25, 2000)]
    [int]$MaxEvents = 250,
    [ValidateRange(1, 720)]
    [int]$Hours = 24,
    [object]$Theme,
    [switch]$DefinitionOnly
)
$ErrorActionPreference = 'Stop'
$themeParameters = @{}
if ($PSBoundParameters.ContainsKey('Theme')) { $themeParameters.Theme = $Theme }
$targetText = if ($Session) { $Session.ComputerName } else { 'Local' }

$layout = New-TuiDefinition -Title 'Event Viewer' -State @{
    target = $targetText
    logs = @('Application', 'Security', 'System', $LogName | Select-Object -Unique)
    selectedLog = $LogName
    logName = $LogName
    level = 'All levels'
    hours = [string]$Hours
    limit = [string]$MaxEvents
    provider = ''
    eventIds = ''
    search = ''
    records = @()
    selected = $null
    detail = 'Choose a log, then Apply filters. Select an event to read its message.'
    xml = 'Select an event to read its XML.'
    detailTab = 0
    summary = 'No snapshot loaded'
    status = 'Ready | queries are bounded; logs are never modified.'
} -Root @{
    Id = 'root'; Type = 'DockLayout'; VerticalAlignment = 'Stretch'
    Children = @(
        @{
            Id = 'top'; Type = 'VStack'
            Children = @(
                @{
                    Id = 'banner'; Type = 'WrapHStack'
                    Children = @(
                        @{ Id = 'title'; Type = 'Text'; Text = 'EVENT VIEWER  '; Bold = $true; Width = 14 }
                        @{ Id = 'target'; Type = 'Text'; Bind = 'target'; Width = $targetText.Length }
                        @{ Id = 'menu'; Type = 'MenuBar'; Items = @('Refresh logs', 'Clear filters', 'Help', 'Close') }
                        @{ Id = 'refresh'; Type = 'Button'; Text = 'Refresh' }
                        @{ Id = 'stop'; Type = 'Button'; Text = 'Stop query' }
                    )
                }
                @{
                    Id = 'searchRow'; Type = 'Header'
                    Children = @(
                        @{
                            Id = 'searchControls'; Type = 'HStack'; Spacing = 1
                            Children = @(
                                @{ Id = 'searchLabel'; Type = 'Text'; Text = 'Search'; Width = 6 }
                                @{
                                    Id = 'search'; Type = 'TextBox'; Bind = 'search'
                                    Options = @{ Placeholder = 'Provider, ID, level or time'; MinWidth = 12; MaxWidth = 36 }
                                }
                                @{ Id = 'find'; Type = 'Button'; Text = 'Find' }
                            )
                        }
                        @{ Id = 'searchSpacer'; Type = 'Text'; Text = '' }
                        @{ Id = 'summary'; Type = 'Text'; Bind = 'summary'; HorizontalAlignment = 'End' }
                    )
                }
            )
        }
        @{
            Id = 'workspace'; Type = 'HSplitter'; VerticalAlignment = 'Stretch'; Options = @{ Ratio = 0.26; MinFirst = 23; MinSecond = 35 }
            Children = @(
                @{
                    Id = 'navigation'; Type = 'Tabs'; VerticalAlignment = 'Stretch'
                    Children = @(
                        @{
                            Id = 'logsTab'; Type = 'Group'; TabTitle = 'Windows logs'; Text = 'Enabled channels'; VerticalAlignment = 'Stretch'
                            Children = @(
                                @{
                                    Id = 'logBrowser'; Type = 'DockLayout'; VerticalAlignment = 'Stretch'
                                    Children = @(
                                        @{ Id = 'logHint'; Type = 'Text'; Text = 'Click or select a channel to open' }
                                        @{ Id = 'logs'; Type = 'OptionList'; Bind = 'logs'; SelectionBind = 'selectedLog'; VerticalAlignment = 'Stretch' }
                                        @{ Id = 'openLog'; Type = 'Button'; Text = 'Open selected log' }
                                    )
                                }
                            )
                        }
                        @{
                            Id = 'filterTab'; Type = 'ScrollViewer'; TabTitle = 'Filter current log'
                            Children = @(
                                @{
                                    Id = 'filters'; Type = 'VStack'; Spacing = 1
                                    Children = @(
                                        @{ Id = 'logLabel'; Type = 'Text'; Text = 'Log / channel name'; Bold = $true }
                                        @{ Id = 'logName'; Type = 'TextBox'; Bind = 'logName' }
                                        @{ Id = 'levelLabel'; Type = 'Text'; Text = 'Event level' }
                                        @{ Id = 'level'; Type = 'Select'; Bind = 'level'; Items = @('All levels', 'Critical + Error', 'Warning+', 'Information', 'Verbose') }
                                        @{ Id = 'hoursLabel'; Type = 'Text'; Text = 'Lookback hours (1-720)' }
                                        @{ Id = 'hours'; Type = 'TextBox'; Bind = 'hours' }
                                        @{ Id = 'limitLabel'; Type = 'Text'; Text = 'Event limit (25-2000)' }
                                        @{ Id = 'limit'; Type = 'TextBox'; Bind = 'limit' }
                                        @{ Id = 'providerLabel'; Type = 'Text'; Text = 'Provider (optional, * wildcard)' }
                                        @{ Id = 'provider'; Type = 'TextBox'; Bind = 'provider' }
                                        @{ Id = 'idLabel'; Type = 'Text'; Text = 'Event IDs (comma-separated)' }
                                        @{ Id = 'eventIds'; Type = 'TextBox'; Bind = 'eventIds' }
                                        @{ Id = 'apply'; Type = 'Button'; Text = 'Apply filters' }
                                        @{ Id = 'reset'; Type = 'Button'; Text = 'Clear filters' }
                                    )
                                }
                            )
                        }
                    )
                }
                @{
                    Id = 'eventPanes'; Type = 'VSplitter'; VerticalAlignment = 'Stretch'; Options = @{ Ratio = 0.56; MinFirst = 7; MinSecond = 6 }
                    Children = @(
                        @{
                            Id = 'tableGroup'; Type = 'Group'; Text = 'Events | newest first'; VerticalAlignment = 'Stretch'
                            Children = @(
                                @{
                                    Id = 'events'; Type = 'FTable'; Bind = 'records'; SelectionBind = 'selected'
                                    Columns = @('Time', 'Level', 'Provider', 'Id', 'RecordId')
                                    Minimal = $true
                                    VerticalAlignment = 'Stretch'
                                }
                            )
                        }
                        @{
                            Id = 'details'; Type = 'TabControl'; Bind = 'detailTab'; VerticalAlignment = 'Stretch'
                            Children = @(
                                @{
                                    Id = 'general'; Type = 'ScrollViewer'; TabTitle = 'General'; VerticalAlignment = 'Stretch'
                                    Children = @(@{ Id = 'detail'; Type = 'Paragraph'; Bind = 'detail'; Options = @{ Wrap = $true } })
                                }
                                @{
                                    Id = 'xmlTab'; Type = 'ScrollViewer'; TabTitle = 'XML'; VerticalAlignment = 'Stretch'
                                    Children = @(@{ Id = 'xml'; Type = 'Paragraph'; Bind = 'xml'; Options = @{ Wrap = $true } })
                                }
                                @{
                                    Id = 'helpTab'; Type = 'ScrollViewer'; TabTitle = 'Help'; VerticalAlignment = 'Stretch'
                                    Children = @(
                                        @{
                                            Id = 'help'; Type = 'Paragraph'; Options = @{ Wrap = $true }
                                            Text = @'
Click or select a channel under Windows logs to open it immediately. Enter and Open selected log also work.
Scroll the log list with the mouse wheel or navigation keys. Select an event for its message and XML; Enter or double-click shows General details.
You can also type its exact name in Filter current log.
Apply filters runs Get-WinEvent on Windows, newest first, with a time window and event limit.
Level, provider and IDs filter on the Windows target. Disabled/analytic channels are not listed.

Find searches only the loaded event metadata: level, time, provider, event ID and record ID.
It does not scan all messages. Increase the lookback/limit to examine a larger bounded snapshot.
The event table also supports / search, sorting, mouse selection and right-click menus.

Selecting a row loads its message and XML in the background. If the event was cleared or
overwritten, details report that explicitly. Drag either splitter to resize the panes.
The event scrollbar scrolls this snapshot. XML is indented with built-in XML APIs; very large content is truncated.
Refresh reloads the snapshot; Stop cancels a query; F10 exits. The caller's PSSession stays open.

This example reads Windows event logs only. Linux/macOS clients can use an existing PSSession
connected to Windows. No log changes, credentials, elevation or remote module installation occur.
'@
                                        }
                                    )
                                }
                            )
                        }
                    )
                }
            )
        }
        @{
            Id = 'footer'; Type = 'VStack'
            Children = @(
                @{ Id = 'status'; Type = 'Text'; Bind = 'status' }
            )
        }
    )
}
$layout.Title = ''
if ($DefinitionOnly.IsPresent) {
    return $layout
}
if (-not $Session -and -not $IsWindows) {
    throw 'Local Get-WinEvent requires Windows. Supply -Session with an open PSSession connected to Windows.'
}
if ($Session -and ($Session.Runspace.RunspaceStateInfo.State -ne 'Opened' -or
    $Session.Runspace.RunspaceAvailability -ne 'Available')) {
    throw 'Use a dedicated, open and available PSSession connected to Windows.'
}

# This script runs on the data host. It returns plain bounded records, never live EventLogRecord handles.
$query = {
    param($request)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'The event-log target must be Windows with Get-WinEvent available.'
    }
    $ErrorActionPreference = 'Stop'
    function Get-BoundedText {
        param([string]$Text, [int]$Limit = 65536)
        if ($Text.Length -gt $Limit) { return $Text.Substring(0, $Limit) + "`n[Truncated at $Limit characters.]" }
        return $Text
    }
    if ($request.Operation -eq 'Logs') {
        $readErrors = @()
        $names = @(Microsoft.PowerShell.Diagnostics\Get-WinEvent -ListLog '*' -ErrorAction SilentlyContinue -ErrorVariable readErrors |
            ForEach-Object {
                $configuration = $_
                try {
                    if ($configuration.IsEnabled -and $configuration.LogType -notin 'Analytical', 'Debug') {
                        [string]$configuration.LogName
                    }
                }
                finally { $configuration.Dispose() }
            } | Sort-Object | Select-Object -First 1000)
        if ($names.Count -eq 0 -and $readErrors.Count -gt 0) { throw $readErrors[0] }
        [pscustomobject]@{
            Logs = $names
            Notice = if ($readErrors.Count -gt 0) { "$($readErrors.Count) channels could not be read with this account." } else { '' }
        }
        return
    }
    if ($request.Operation -eq 'Detail') {
        # Numeric IDs and XML escaping keep channel names out of executable XPath syntax.
        $channel = [Security.SecurityElement]::Escape([string]$request.LogName)
        $recordId = [long]$request.RecordId
        $eventId = [int]$request.Id
        $utc = ([datetime]$request.CreatedUtc).ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        $filter = "<QueryList><Query Id='0'><Select Path='$channel'>*[System[EventRecordID=$recordId and EventID=$eventId and TimeCreated[@SystemTime='$utc']]]</Select></Query></QueryList>"
        $events = @(Microsoft.PowerShell.Diagnostics\Get-WinEvent -FilterXml $filter -MaxEvents 1 -ErrorAction Stop)
        foreach ($eventRecord in $events) {
            try {
                $message = ''
                try { $message = [string]$eventRecord.Message }
                catch { $message = 'Message could not be formatted: ' + $_.Exception.Message }
                if ([string]::IsNullOrWhiteSpace($message)) { $message = 'No formatted message is available. See the XML tab for event data.' }
                [pscustomobject]@{
                    Detail = Get-BoundedText ("Log: $($eventRecord.LogName)`nProvider: $($eventRecord.ProviderName)`nEvent ID: $($eventRecord.Id) | Record: $($eventRecord.RecordId)`nTime: $($eventRecord.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss zzz'))`nLevel: $($eventRecord.LevelDisplayName)`nMachine: $($eventRecord.MachineName)`nProcess: $($eventRecord.ProcessId) | Thread: $($eventRecord.ThreadId)`n`n$message")
                    Xml = Get-BoundedText $eventRecord.ToXml()
                }
            }
            finally { $eventRecord.Dispose() }
        }
        return
    }
    $filter = @{
        LogName = [string]$request.LogName
        StartTime = (Get-Date).AddHours(-[int]$request.Hours)
    }
    if ($request.Level.Count -gt 0) { $filter.Level = [int[]]$request.Level }
    if ($request.Provider) { $filter.ProviderName = [string]$request.Provider }
    if ($request.Ids.Count -gt 0) { $filter.Id = [int[]]$request.Ids }
    try {
        Microsoft.PowerShell.Diagnostics\Get-WinEvent -FilterHashtable $filter -MaxEvents ([int]$request.Limit) -ErrorAction Stop |
            ForEach-Object {
                $record = $_
                try {
                    [pscustomobject]@{
                        Time = $record.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')
                        Level = if ($record.Level -eq 0) { 'Audit / Always' } else { [string]$record.LevelDisplayName }
                        Provider = [string]$record.ProviderName
                        Id = [int]$record.Id
                        RecordId = [long]$record.RecordId
                        LogName = [string]$record.LogName
                        CreatedUtc = $record.TimeCreated.ToUniversalTime()
                    }
                }
                finally { $record.Dispose() }
            }
    }
    catch {
        if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { throw }
    }
}.ToString()

$ownedRunspace = $null
if ($Session) {
    $runspace = $Session.Runspace
}
else {
    $ownedRunspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace(
        [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2())
    $ownedRunspace.Open()
    $runspace = $ownedRunspace
}
$runtime = @{
    runspace = $runspace
    shell = $null
    pending = $null
    operation = $null
    request = $null
    stopping = $false
    generation = 0
    workerGeneration = 0
    loadedGeneration = -1
    desired = $null
    detailKey = ''
    allRecords = @()
    query = $query
    started = [datetime]::MinValue
    start = $null
    apply = $null
    search = $null
    formatXml = $null
}
$runtime.formatXml = {
    param([string]$text)
    if ([string]::IsNullOrWhiteSpace($text)) { return 'XML is unavailable for this event.' }
    if ($text.Length -gt 65536) { return $text.Substring(0, 65536) + "`n[Truncated at 65536 characters; XML formatting skipped.]" }
    $reader = $null; $writer = $null; $inputText = [IO.StringReader]::new($text)
    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 65536
        $reader = [Xml.XmlReader]::Create($inputText, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        $output = [Text.StringBuilder]::new()
        $format = [Xml.XmlWriterSettings]::new()
        $format.Indent = $true; $format.IndentChars = '  '; $format.NewLineChars = "`n"
        $format.OmitXmlDeclaration = $document.FirstChild -isnot [Xml.XmlDeclaration]
        $writer = [Xml.XmlWriter]::Create($output, $format)
        $document.Save($writer)
        $writer.Flush()
        $formatted = $output.ToString()
        if ($formatted.Length -gt 65536) { return $formatted.Substring(0, 65536) + "`n[Truncated at 65536 characters.]" }
        return $formatted
    }
    catch [Xml.XmlException] { return "XML could not be formatted: $($_.Exception.Message)`n`n$text" }
    finally {
        if ($writer) { $writer.Dispose() }
        if ($reader) { $reader.Dispose() }
        $inputText.Dispose()
    }
}
$runtime.start = {
    param($state, $request)
    $state.runtime.desired = $request
    if ($state.runtime.shell -and -not $state.runtime.pending.IsCompleted) {
        if (-not $state.runtime.stopping) {
            $null = $state.runtime.shell.BeginStop($null, $null)
            $state.runtime.stopping = $true
        }
    }
    $state.status = 'Loading ' + $request.Operation.ToLowerInvariant() + '...'
}
$runtime.search = {
    param($state)
    $text = ([string]$state.search).Trim()
    $rows = @(
        if ($text) {
            $state.runtime.allRecords | Where-Object {
                ($_.Time + ' ' + $_.Level + ' ' + $_.Provider + ' ' + $_.Id + ' ' + $_.RecordId).IndexOf($text, [StringComparison]::OrdinalIgnoreCase) -ge 0
            }
        }
        else { $state.runtime.allRecords }
    )
    $state.records = $rows
    $state.summary = "$($rows.Count) shown / $($state.runtime.allRecords.Count) loaded"
}
$runtime.apply = {
    param($state)
    $lookback = 0; $limit = 0
    if ([string]::IsNullOrWhiteSpace($state.logName) -or
        -not [int]::TryParse([string]$state.hours, [ref]$lookback) -or $lookback -lt 1 -or $lookback -gt 720 -or
        -not [int]::TryParse([string]$state.limit, [ref]$limit) -or $limit -lt 25 -or $limit -gt 2000) {
        $state.status = 'Choose a log, lookback 1-720 hours and limit 25-2000.'
        return
    }
    $ids = [Collections.Generic.List[int]]::new()
    if (-not [string]::IsNullOrWhiteSpace($state.eventIds)) {
        foreach ($part in ([string]$state.eventIds).Split(',')) {
            $idValue = 0
            if (-not [int]::TryParse($part.Trim(), [ref]$idValue) -or $idValue -lt 0 -or $idValue -gt 65535 -or $ids.Count -ge 50) {
                $state.status = 'Event IDs must be up to 50 comma-separated numbers from 0 to 65535.'
                return
            }
            $ids.Add($idValue)
        }
    }
    $levels = @(switch ($state.level) {
        'Critical + Error' { @(1, 2) }
        'Warning+' { @(1, 2, 3) }
        'Information' { @(4) }
        'Verbose' { @(5) }
        default { @() }
    })
    $state.runtime.generation++
    $state.runtime.loadedGeneration = -1
    $state.runtime.detailKey = ''
    $state.selected = $null
    $state.detail = 'Loading snapshot. Select a row after the query completes.'
    $state.xml = ''
    & $state.runtime.start $state @{
        Operation = 'Events'; LogName = $state.logName.Trim(); Hours = $lookback; Limit = $limit
        Level = @($levels); Provider = ([string]$state.provider).Trim(); Ids = $ids.ToArray()
    }
}
$onEvent = {
    param($tuiEvent, $state)
    $action = if ($tuiEvent.Id -eq 'menu') { [string]$tuiEvent.Value } else { $tuiEvent.Id }
    if ($action -eq 'Close') { return @{ Close = $true } }
    if ($action -in 'Help') {
        $state.detailTab = 2
        $state.status = 'Help tab: read-only Windows logs | metadata search | drag splitters | F10 closes.'
        return
    }
    if ($action -eq 'stop') {
        $state.runtime.desired = $null
        $state.runtime.detailKey = ''
        $state.runtime.loadedGeneration = $state.runtime.generation
        if ($state.runtime.shell -and -not $state.runtime.stopping) {
            $null = $state.runtime.shell.BeginStop($null, $null)
            $state.runtime.stopping = $true
        }
        $state.status = 'Query cancelled. The previous snapshot remains available.'
        $state.detail = $state.status
        $state.xml = ''
        return
    }
    if ($action -in 'Refresh logs') {
        & $state.runtime.start $state @{ Operation = 'Logs' }
        return
    }
    if ($action -in 'reset', 'Clear filters') {
        $state.level = 'All levels'; $state.provider = ''; $state.eventIds = ''; $state.search = ''
        $state.hours = '24'
    }
    if ($action -eq 'openLog' -or ($action -eq 'logs' -and $tuiEvent.Kind -in 'Selection', 'Submit')) {
        $name = if ($action -eq 'openLog') { $state.selectedLog } else { $tuiEvent.Value }
        if (-not $name) { return }
        if ($action -eq 'logs' -and [string]::Equals([string]$name, [string]$state.logName, [StringComparison]::OrdinalIgnoreCase)) { return }
        $state.logName = [string]$name
    }
    elseif ($action -in 'find', 'search') {
        & $state.runtime.search $state
        return
    }
    elseif ($action -eq 'events' -and $tuiEvent.Kind -in 'Selection', 'Submit') {
        $row = $tuiEvent.Value
        if (-not $row -or $state.runtime.loadedGeneration -ne $state.runtime.generation) { return }
        if ($tuiEvent.Kind -eq 'Submit') { $state.detailTab = 0 }
        $key = "$($state.runtime.generation):$($row.LogName):$($row.RecordId):$($row.CreatedUtc)"
        if ($key -eq $state.runtime.detailKey) { return }
        $state.runtime.detailKey = $key
        $state.detail = 'Loading event message...'; $state.xml = 'Loading XML...'
        & $state.runtime.start $state @{
            Operation = 'Detail'; LogName = $row.LogName; RecordId = $row.RecordId; Id = $row.Id; CreatedUtc = $row.CreatedUtc
        }
        return
    }
    elseif ($action -notin 'apply', 'refresh', 'Refresh events', 'reset', 'Clear filters') { return }
    & $state.runtime.apply $state
}
$onTick = {
    param($tuiEvent, $state)
    if ($tuiEvent.Kind -ne 'Tick') { return }
    $worker = $state.runtime
    if ($worker.shell -and -not $worker.pending.IsCompleted -and
        -not $worker.stopping -and ([datetime]::UtcNow - $worker.started).TotalSeconds -ge 30) {
        $null = $worker.shell.BeginStop($null, $null)
        $worker.stopping = $true
        $worker.detailKey = ''
        $worker.loadedGeneration = $worker.generation
        $state.status = 'Query timed out after 30 seconds. Narrow the filter or retry.'
        $state.detail = $state.status
        $state.xml = ''
    }
    if ($worker.shell -and $worker.pending.IsCompleted) {
        try {
            $output = @($worker.shell.EndInvoke($worker.pending))
            if ($worker.shell.Streams.Error.Count -gt 0) { throw $worker.shell.Streams.Error[0] }
            if (-not $worker.stopping -and -not $worker.desired -and $worker.workerGeneration -eq $worker.generation) {
                switch ($worker.operation) {
                    'Logs' {
                        $state.logs = @(@($output[0].Logs) + $state.logName | Select-Object -Unique)
                        $state.status = "Found $($state.logs.Count) enabled logs. $($output[0].Notice)"
                        if ($worker.allRecords.Count -eq 0) { & $worker.apply $state }
                    }
                    'Events' {
                        $worker.allRecords = $output
                        $worker.loadedGeneration = $worker.generation
                        & $worker.search $state
                        $state.status = "Loaded $($output.Count) newest events | $($worker.request.LogName) | last $($worker.request.Hours)h | limit $($worker.request.Limit)."
                        $state.detail = 'Select a row to read its message and XML.'
                    }
                    'Detail' {
                        if ($output.Count -eq 0) { throw 'The event is no longer available; refresh the log.' }
                        $state.detail = [string]$output[0].Detail
                        $state.xml = & $worker.formatXml ([string]$output[0].Xml)
                        $state.status = 'Event details loaded. No log changes were made.'
                    }
                }
            }
        }
        catch {
            if (-not $worker.stopping -and -not $worker.desired) {
                $state.status = 'Read failed: ' + $_.Exception.Message
                if ($worker.operation -eq 'Detail') {
                    $worker.detailKey = ''
                    $state.detail = $state.status
                    $state.xml = 'Unavailable. The event may have been cleared or overwritten, or access denied.'
                }
                elseif ($worker.operation -eq 'Events') {
                    $worker.loadedGeneration = $worker.generation
                    $state.detail = 'Query failed. The table still shows the previous snapshot. Correct the filter and refresh.'
                    $state.xml = ''
                }
            }
        }
        finally {
            $worker.shell.Dispose()
            $worker.shell = $null; $worker.pending = $null; $worker.stopping = $false
        }
    }
    if (-not $worker.shell -and $worker.desired) {
        $request = $worker.desired; $worker.desired = $null
        try {
            $worker.shell = [System.Management.Automation.PowerShell]::Create()
            $worker.shell.Runspace = $worker.runspace
            $null = $worker.shell.AddScript($worker.query).AddArgument($request)
            $worker.operation = $request.Operation
            $worker.request = $request
            $worker.workerGeneration = $worker.generation
            $worker.started = [datetime]::UtcNow
            $worker.pending = $worker.shell.BeginInvoke()
        }
        catch {
            $state.status = 'Could not start query: ' + $_.Exception.Message
            if ($worker.shell) { $worker.shell.Dispose(); $worker.shell = $null }
        }
    }
}
$runtime.desired = @{ Operation = 'Logs' }
try {
    $null = $layout | Invoke-Tui @themeParameters -TickInterval 100 -State @{ runtime = $runtime } -OnEvent $onEvent -OnTick $onTick
}
finally {
    if ($runtime.shell) {
        try {
            $runtime.shell.Stop()
        }
        finally { $runtime.shell.Dispose() }
    }
    if ($ownedRunspace) { $ownedRunspace.Dispose() }
}
