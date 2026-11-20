#Requires -Version 7.6
<#
.SYNOPSIS
Monitor PowerShell Universal from a Fullscreen terminal dashboard.
.DESCRIPTION
The TUI checks /api/v1/status and tracks the duration and outcomes of its own
HTTP probes. With a PSU app token, it also reads recent jobs and endpoint
inventory using the installed Devolutions.PowerShellUniversal or Universal module.

Only the PSU status endpoint is probed by default. Add known read-only GET
routes with ProbePath. The monitor sends these probes without credentials.
Probe statistics cover this session's latest 60 checks per route.

Optionally supply a Prometheus URL for server-side HTTP request counts from
PSU OpenTelemetry metrics. See the PSU documentation for telemetry setup.
.PARAMETER Server
PSU base URL. Defaults to http://localhost:5000.
.PARAMETER Token
PSU app token with permission to read jobs and endpoints. Defaults to the
PSU_MONITOR_APP_TOKEN environment variable if present. Use HTTPS for remote PSU.
.PARAMETER ProbePath
Additional read-only GET routes on the PSU server, such as /api/ping.
.PARAMETER RefreshSeconds
Minimum seconds between polls. Defaults to 15.
.PARAMETER JobLimit
Maximum number of recent jobs to display. Defaults to 50.
.PARAMETER Prometheus
Base URL of an existing Prometheus collector. Optional.
.PARAMETER RequestQuery
PromQL instant-vector query. Scope it to PSU and retain the route and status
labels. The default estimates request counts by route and status over five minutes.
Adjust the metric name and labels for your exporter.
.PARAMETER DefinitionOnly
Return the TUI layout without polling or loading the PSU module.
.EXAMPLE
Import-Module ./output/fullscreen.psd1
./examples/PowerShellUniversalMonitor.ps1 -Server https://psu.example.com
.EXAMPLE
$token = Read-Host -Prompt 'PSU app token' -AsSecureString
./examples/PowerShellUniversalMonitor.ps1 -Server https://psu.example.com -Token $token -ProbePath /api/ping
.OUTPUTS
A TUI definition with DefinitionOnly. Otherwise no output.
.NOTES
F10 exits. Polling continues while you navigate the dashboard.
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingConvertToSecureStringWithPlainText', '', Justification = 'The documented environment-variable fallback already supplies plaintext. It is never logged; remote token use requires HTTPS. Prefer the SecureString Token parameter.')]
[CmdletBinding()]
param(
    [uri]$Server = 'http://localhost:5000',
    [securestring]$Token,
    [string[]]$ProbePath = @(),
    [ValidateRange(5, 300)]
    [int]$RefreshSeconds = 15,
    [ValidateRange(1, 200)]
    [int]$JobLimit = 50,
    [uri]$Prometheus,
    [ValidateNotNullOrEmpty()]
    [string]$RequestQuery = 'sum by (http_route, http_response_status_code) (increase(http_server_request_duration_seconds_count[5m]))',
    [switch]$DefinitionOnly
)

$ErrorActionPreference = 'Stop'
$layout = New-TuiDefinition -Title 'PowerShell Universal monitor' -State @{
    overview = 'Waiting for the first status check…'
    jobsInfo = 'Waiting for job history…'
    jobs = @()
    endpointsInfo = 'Waiting for endpoint inventory…'
    endpoints = @()
    probes = @()
    latency = @()
    trafficInfo = 'Optional · use -Prometheus to show server-side HTTP request counts.'
    traffic = @()
} -Root @{
    Id = 'root'
    Type = 'VStack'
    Children = @(
        @{
            Id = 'overview'
            Type = 'Text'
            Bind = 'overview'
        }
        @{
            Id = 'tabs'
            Type = 'Tabs'
            Children = @(
                @{
                    Id = 'httpTab'
                    Type = 'VStack'
                    TabTitle = 'HTTP probes'
                    Children = @(
                        @{
                            Id = 'probes'
                            Type = 'FTable'
                            Bind = 'probes'
                            Text = 'Calls from this monitor · latest 60 per route'
                            Columns = @('Route', 'Latest', 'Calls', '2xx', 'Failed', 'Last ms', 'Avg ms')
                            Height = 9
                            Minimal = $true
                        }
                        @{
                            Id = 'latency'
                            Type = 'Chart'
                            Bind = 'latency'
                            Text = 'PSU status probe · elapsed ms'
                            GraphKind = 'Line'
                            Height = 9
                        }
                    )
                }
                @{
                    Id = 'jobsTab'
                    Type = 'VStack'
                    TabTitle = 'Jobs'
                    Children = @(
                        @{
                            Id = 'jobsInfo'
                            Type = 'Text'
                            Bind = 'jobsInfo'
                        }
                        @{
                            Id = 'jobs'
                            Type = 'FTable'
                            Bind = 'jobs'
                            Columns = @('Id', 'Script', 'Status', 'Started', 'Ended')
                            Height = 18
                            Minimal = $true
                        }
                    )
                }
                @{
                    Id = 'endpointsTab'
                    Type = 'VStack'
                    TabTitle = 'Endpoints'
                    Children = @(
                        @{
                            Id = 'endpointsInfo'
                            Type = 'Text'
                            Bind = 'endpointsInfo'
                        }
                        @{
                            Id = 'endpoints'
                            Type = 'FTable'
                            Bind = 'endpoints'
                            Columns = @('Route', 'Method', 'Enabled', 'Authentication')
                            Height = 18
                            Minimal = $true
                        }
                    )
                }
                @{
                    Id = 'trafficTab'
                    Type = 'VStack'
                    TabTitle = 'Prometheus'
                    Children = @(
                        @{
                            Id = 'trafficInfo'
                            Type = 'Text'
                            Bind = 'trafficInfo'
                        }
                        @{
                            Id = 'traffic'
                            Type = 'FTable'
                            Bind = 'traffic'
                            Columns = @('Route', 'Status', 'Estimated calls')
                            Height = 18
                            Minimal = $true
                        }
                    )
                }
            )
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}

foreach ($address in @($Server, $Prometheus)) {
    if ($null -ne $address -and (
            -not $address.IsAbsoluteUri -or $address.Scheme -notin 'http', 'https' -or
            $address.UserInfo -or $address.Query -or $address.Fragment
        )) {
        throw 'Provide an absolute HTTP(S) base URL without credentials, a query or a fragment.'
    }
}

if ($null -eq $Token -and $env:PSU_MONITOR_APP_TOKEN) {
    $Token = ConvertTo-SecureString -String $env:PSU_MONITOR_APP_TOKEN -AsPlainText -Force
}
if ($null -ne $Token -and $Server.Scheme -ne 'https' -and -not $Server.IsLoopback) {
    throw 'Use an HTTPS PSU URL when sending an app token to a remote server.'
}
$psuModule = if ($null -ne $Token) {
    Get-Module -ListAvailable -Name Devolutions.PowerShellUniversal, Universal |
        Select-Object -First 1 -ExpandProperty Path
}

$statusPath = '/api/v1/status'
$serverBase = $Server.AbsoluteUri.TrimEnd('/')
$routes = [ordered]@{}
foreach ($route in (@($statusPath) + $ProbePath)) {
    if ($route -notmatch '^/(?!/)' -or $route -match '[\\?#\s]') {
        throw "Use a root-relative route without a query or fragment for -ProbePath. Invalid route: '$route'."
    }
    $routes[$route] = [uri]($serverBase + $route)
}

$poll = {
    param($config)

    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    $snapshot = @{
        Checks = @(foreach ($route in $config.Routes.Keys) {
                $clock = [Diagnostics.Stopwatch]::StartNew()
                try {
                    $response = Invoke-WebRequest -Uri $config.Routes[$route] -TimeoutSec 4 -MaximumRedirection 0 -SkipHttpErrorCheck
                    [pscustomobject]@{
                        Route = $route
                        Status = [int]$response.StatusCode
                        Milliseconds = $clock.ElapsedMilliseconds
                    }
                }
                catch {
                    $statusCode = $_.Exception.Response.StatusCode
                    [pscustomobject]@{
                        Route = $route
                        Status = [int]$statusCode
                        Milliseconds = $clock.ElapsedMilliseconds
                    }
                }
            })
        Jobs = @()
        JobsLoaded = $false
        JobsInfo = 'Set PSU_MONITOR_APP_TOKEN or pass -Token to read job history.'
        Endpoints = @()
        EndpointsLoaded = $false
        EndpointsInfo = 'Set PSU_MONITOR_APP_TOKEN or pass -Token to read endpoint inventory.'
        Traffic = @()
        TrafficLoaded = $false
        TrafficInfo = 'Optional · use -Prometheus to show server-side HTTP request counts.'
    }

    if ($null -ne $config.Token) {
        if (-not $config.ModulePath) {
            $snapshot.JobsInfo = $snapshot.EndpointsInfo = 'Install the PSU PowerShell client module to load jobs and endpoints.'
        }
        else {
            try {
                if ($null -eq $config.Connection) {
                    Import-Module -Name $config.ModulePath
                    $config.Connection = @{
                        ComputerName = $config.Server
                        AppToken = [Net.NetworkCredential]::new('', $config.Token).Password
                    }
                }
                $connection = $config.Connection
            }
            catch {
                $snapshot.JobsInfo = $snapshot.EndpointsInfo = "PSU client import failed · $($_.FullyQualifiedErrorId)"
            }

            if ($connection) {
                try {
                    $snapshot.Jobs = @(Get-PSUJob @connection -First $config.JobLimit -OrderBy Id -OrderDirection Descending |
                            ForEach-Object {
                                [pscustomobject][ordered]@{
                                    Id = $_.Id
                                    Script = $_.Script.Name ?? $_.ScriptFullPath
                                    Status = [string]$_.Status
                                    Started = $_.StartTime
                                    Ended = $_.EndTime
                                }
                            })
                    $counts = $snapshot.Jobs | Group-Object -Property Status |
                        ForEach-Object {
                            '{0} {1}' -f $_.Count, $_.Name
                        }
                    $snapshot.JobsInfo = if ($snapshot.Jobs.Count) {
                        "Latest $($snapshot.Jobs.Count) jobs · $($counts -join ' · ')"
                    }
                    else {
                        'No jobs returned.'
                    }
                    $snapshot.JobsLoaded = $true
                }
                catch {
                    $snapshot.JobsInfo = "Job history unavailable · $($_.FullyQualifiedErrorId)"
                }

                try {
                    $snapshot.Endpoints = @(Get-PSUEndpoint @connection | Sort-Object -Property Url | ForEach-Object {
                            [pscustomobject][ordered]@{
                                Route = $_.Url
                                Method = @(($_.Methods ?? $_.Method) | Where-Object {
                                        $_
                                    }) -join ', '
                                Enabled = -not $_.Disabled
                                Authentication = $_.Authentication
                            }
                        })
                    $snapshot.EndpointsInfo = "$($snapshot.Endpoints.Count) configured endpoints"
                    $snapshot.EndpointsLoaded = $true
                }
                catch {
                    $snapshot.EndpointsInfo = "Endpoint inventory unavailable · $($_.FullyQualifiedErrorId)"
                }
            }
        }
    }

    if ($config.Prometheus) {
        try {
            $queryUri = '{0}/api/v1/query?query={1}' -f $config.Prometheus.AbsoluteUri.TrimEnd('/'), [uri]::EscapeDataString($config.RequestQuery)
            $metrics = Invoke-RestMethod -Uri $queryUri -TimeoutSec 5
            if ($metrics.status -ne 'success' -or $metrics.data.resultType -ne 'vector') {
                throw 'Expected a Prometheus instant vector.'
            }
            $snapshot.Traffic = @(foreach ($series in $metrics.data.result) {
                    $count = [double]::Parse($series.value[1], [Globalization.CultureInfo]::InvariantCulture)
                    if ([double]::IsFinite($count)) {
                        [pscustomobject][ordered]@{
                            Route = $series.metric.http_route ?? '(unlabeled)'
                            Status = $series.metric.http_response_status_code ?? $series.metric.http_status_code ?? '(unlabeled)'
                            'Estimated calls' = [math]::Round($count, 1)
                        }
                    }
                })
            $snapshot.Traffic = @($snapshot.Traffic | Sort-Object -Property 'Estimated calls' -Descending)
            $snapshot.TrafficInfo = if ($snapshot.Traffic.Count) {
                'Server traffic · estimated calls over the configured PromQL window'
            }
            else {
                'No request samples matched the PromQL query.'
            }
            $snapshot.TrafficLoaded = $true
        }
        catch {
            $snapshot.TrafficInfo = "Prometheus query failed · $($_.FullyQualifiedErrorId)"
        }
    }
    $snapshot
}

$runtime = @{
    Config = @{
        Server = $serverBase
        ModulePath = $psuModule
        Token = $Token
        Routes = $routes
        JobLimit = $JobLimit
        Prometheus = $Prometheus
        RequestQuery = $RequestQuery
    }
    Pending = $null
    Queue = [Collections.Concurrent.ConcurrentQueue[object]]::new()
    Cancellation = [Threading.CancellationTokenSource]::new()
    LastSuccess = @{}
    History = @{}
}
try {
    $runtime.Pending = Start-ThreadJob -ArgumentList $runtime.Config, $poll.ToString(), $runtime.Queue, $runtime.Cancellation.Token, $RefreshSeconds -ScriptBlock {
        param($config, $pollText, $queue, $token, $interval)
        # Give the worker its own session state instead of rebinding the UI's script block.
        $poll = [scriptblock]::Create($pollText)
        while (-not $token.IsCancellationRequested) {
            $snapshot = & $poll $config
            $previous = $null
            while ($queue.TryDequeue([ref]$previous)) {
            }
            $queue.Enqueue($snapshot)
            if ($token.WaitHandle.WaitOne([timespan]::FromSeconds($interval))) {
                break
            }
        }
    }
    $null = $layout | Invoke-Tui -State @{
        monitor = $runtime
    } -TickInterval 250 -OnTick {
        param($tuiEvent, $state)

        $monitor = $state.monitor
        if ($monitor.Pending.State -eq 'Failed') {
            $state.overview = 'Polling stopped · check the server and restart the monitor.'
        }
        $snapshot = $null
        if (-not $monitor.Queue.TryDequeue([ref]$snapshot)) {
            return
        }
        foreach ($source in 'Jobs', 'Endpoints', 'Traffic') {
            $information = $snapshot["${source}Info"]
            if ($snapshot["${source}Loaded"]) {
                $state[$source] = $snapshot[$source]
                $monitor.LastSuccess[$source] = Get-Date -Format HH:mm:ss
            }
            elseif ($monitor.LastSuccess.ContainsKey($source)) {
                $information += " · stale since $($monitor.LastSuccess[$source])"
            }
            $state["${source}Info"] = $information
        }

        $state.probes = @(foreach ($check in $snapshot.Checks) {
                $history = $monitor.History[$check.Route]
                if ($null -eq $history) {
                    $history = [Collections.Generic.Queue[object]]::new()
                    $monitor.History[$check.Route] = $history
                }
                $history.Enqueue($check)
                if ($history.Count -gt 60) {
                    $null = $history.Dequeue()
                }
                $successful = @($history | Where-Object {
                        $_.Status -ge 200 -and $_.Status -lt 300
                    })
                $latest = if ($check.Status) {
                    "HTTP $($check.Status)"
                }
                else {
                    'Connection error'
                }

                if ($check.Route -eq '/api/v1/status') {
                    $state.overview = "$($monitor.Config.Server) · $latest · $($check.Milliseconds) ms · $(Get-Date -Format HH:mm:ss)"
                    $state.latency = @(foreach ($sample in $history) {
                            if ($sample.Status) {
                                [double]$sample.Milliseconds
                            }
                            else {
                                [double]::NaN
                            }
                        })
                }
                [pscustomobject][ordered]@{
                    Route = $check.Route
                    Latest = $latest
                    Calls = $history.Count
                    '2xx' = $successful.Count
                    Failed = $history.Count - $successful.Count
                    'Last ms' = $check.Milliseconds
                    'Avg ms' = [math]::Round(($history | Measure-Object -Property Milliseconds -Average).Average, 1)
                }
            })
    }
}
finally {
    $runtime.Cancellation.Cancel()
    if ($null -ne $runtime.Pending) {
        Remove-Job -Job $runtime.Pending -Force
    }
    $runtime.Cancellation.Dispose()
}
