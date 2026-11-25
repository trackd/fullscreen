#Requires -Version 7.6
<#
.SYNOPSIS
Trace the route to a target and plot latency and missed replies for each hop.
.DESCRIPTION
Each sweep sends ICMP probes with increasing TTL values. The route table shows
the latest responder, reply status, latency and rolling loss per hop. Select
a row in the route grid for its full address, reply status and timelines.
A missing reply from an intermediate router can reflect ICMP filtering or
rate limiting. Compare it with downstream hops before interpreting it as loss.
The timelines are indexed by sweep. When a TTL starts reporting a different
address, its history starts again so statistics describe the current responder.
The chart shows one RTT line per hop. Missed replies appear as gaps. Select a
row to highlight its line. Background bands begin at 0, 50 and 150 ms.
N counts probes for the current responder. Statistics and the graph use the
retained window. Pause freezes the display while collection continues. Older
and Newer browse retained sweeps. Tabs show RTT, loss by hop, a latency
distribution and route changes. The selected hop shows p95 and jitter.
.PARAMETER Target
DNS name or IP address. Defaults to 127.0.0.1.
.PARAMETER MaxHop
Maximum TTL to probe. Defaults to 24.
.PARAMETER IntervalMilliseconds
Minimum time between sweep starts. Defaults to 2000.
.PARAMETER TimeoutMilliseconds
Timeout for each ICMP probe. Defaults to 1000.
.PARAMETER HistorySize
Number of sweeps retained per hop. Defaults to 300.
.PARAMETER DefinitionOnly
Return the layout without sending probes or opening the terminal.
.PARAMETER SessionPath
Optional JSON file written by Save session. Contains the retained sweeps.
.PARAMETER ReplayPath
Read a previously saved session without sending network traffic.
.EXAMPLE
Import-Module ./output/fullscreen.psd1
./examples/PingPlotter.ps1
.EXAMPLE
./examples/PingPlotter.ps1 -Target 1.1.1.1 -MaxHop 32 -HistorySize 120
.EXAMPLE
./examples/PingPlotter.ps1 -Target 1.1.1.1 -SessionPath ./route-session.json
Choose Save session to export the currently retained sweeps.
.EXAMPLE
./examples/PingPlotter.ps1 -ReplayPath ./route-session.json
Use Older and Newer to inspect the saved samples.
.OUTPUTS
A TUI definition with DefinitionOnly. Otherwise no output.
.NOTES
F10 exits. Intermediate-hop timing uses elapsed time on the client for ICMP TTL
replies. Hop addresses depend on what the platform reports for those replies.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Target = '127.0.0.1',
    [ValidateRange(1, 64)]
    [int]$MaxHop = 24,
    [ValidateRange(500, 60000)]
    [int]$IntervalMilliseconds = 2000,
    [ValidateRange(100, 30000)]
    [int]$TimeoutMilliseconds = 1000,
    [ValidateRange(10, 1200)]
    [int]$HistorySize = 300,
    [switch]$DefinitionOnly,
    [string]$SessionPath,
    [string]$ReplayPath
)

$ErrorActionPreference = 'Stop'
$session = $null
if ($ReplayPath -and -not $DefinitionOnly.IsPresent) {
    $sessionFile = Get-Item -LiteralPath $ReplayPath
    if ($sessionFile.Length -gt 32MB) {
        throw 'Replay sessions must be no larger than 32 MiB.'
    }
    $session = Get-Content -LiteralPath $sessionFile.FullName -Raw | ConvertFrom-Json -Depth 32
    if ($session.Version -ne 1 -or @($session.Sweeps).Count -notin 1..1200) {
        throw 'Expected a version 1 PingPlotter session containing 1 to 1200 sweeps.'
    }
    $previousSweep = 0L
    foreach ($snapshot in $session.Sweeps) {
        if ($snapshot.Sweep -ne [long]$snapshot.Sweep -or [long]$snapshot.Sweep -le $previousSweep -or @($snapshot.Replies).Count -notin 1..64 -or
            @($snapshot.Rows).Count -ne @($snapshot.Replies).Count) {
            throw 'Replay sweeps must be increasing and contain 1 to 64 ordered hop replies.'
        }
        $previousSweep = [long]$snapshot.Sweep
        $expectedHop = 1
        foreach ($reply in $snapshot.Replies) {
            if ($reply.Hop -ne $expectedHop -or ($null -ne $reply.Milliseconds -and (
                        -not [double]::IsFinite([double]$reply.Milliseconds) -or $reply.Milliseconds -lt 0
                    ))) {
                throw 'Replay hop numbers or round-trip times are invalid.'
            }
            $row = @($snapshot.Rows)[$expectedHop - 1]
            if ($null -eq $row -or $row.Hop -ne $reply.Hop -or $row.N -lt 1 -or
                $row.'Miss%' -lt 0 -or $row.'Miss%' -gt 100) {
                throw 'Replay table rows must match their hop replies and contain valid probe counts and loss percentages.'
            }
            foreach ($field in 'Last', 'Min', 'Avg', 'Max', 'Miss%', 'N') {
                if ($null -ne $row.$field -and (-not [double]::IsFinite([double]$row.$field) -or [double]$row.$field -lt 0)) {
                    throw "Replay table field '$field' must be a finite nonnegative number or null."
                }
            }
            $expectedHop++
        }
    }
    $Target = [string]$session.Target
    $MaxHop = [int]($session.Sweeps.Replies.Hop | Measure-Object -Maximum).Maximum
}
$hopSeries = @(1..$MaxHop | ForEach-Object {
        "Hop $_"
    })
$latencyChart = @{
    Id = 'latency'
    Type = 'Chart'
    Bind = 'timeline'
    Text = 'RTT by sweep (ms) · gaps mark missed replies'
    GraphKind = 'Line'
    XProperty = 'Sweep'
    YProperty = $hopSeries
    Minimum = 0
    ShowLegend = $true
    ShowAxes = $true
    ShowTooltips = $true
    EnableZoom = $true
}
$latencyChart.SeriesColor = @(for ($index = 0; $index -lt $MaxHop; $index++) {
        $hue = (($index * 137.508) % 360) / 60
        $sector = [int][math]::Floor($hue)
        $fraction = $hue - $sector
        $rising = [int](65 + 165 * $fraction)
        $falling = [int](230 - 165 * $fraction)
        $rgb = switch ($sector) {
            0 {
                230, $rising, 65
            }
            1 {
                $falling, 230, 65
            }
            2 {
                65, 230, $rising
            }
            3 {
                65, $falling, 230
            }
            4 {
                $rising, 65, 230
            }
            5 {
                230, 65, $falling
            }
        }
        '#{0:X2}{1:X2}{2:X2}' -f $rgb[0], $rgb[1], $rgb[2]
    })
$latencyChart.SeriesCountBind = 'hopCount'
$latencyChart.HighlightSeriesBind = 'highlight'
$latencyChart.DataVersionBind = 'dataVersion'
$latencyChart.PlotBands = @(
    @{
        From = 0
        Color = '#1FA763'
    }
    @{
        From = 50
        Color = '#D89614'
    }
    @{
        From = 150
        Color = '#E54D55'
    }
)
$lossChart = @{
    Id = 'lossHeatmap'
    Type = 'Chart'
    TabTitle = 'Loss by hop'
    Bind = 'losses'
    GraphKind = 'Heatmap'
    XProperty = 'Sweep'
    YProperty = $hopSeries
    Minimum = 0
    Maximum = 1
    ShowAxes = $true
    ShowLegend = $true
}
$lossChart.SeriesCountBind = 'hopCount'
$lossChart.DataVersionBind = 'dataVersion'
$layout = New-TuiDefinition -Title 'Ping plotter' -State @{
    summary = "Ping plotter · $Target · Waiting for replies…"
    hops = @()
    detail = 'Waiting for hop data…'
    timeline = @()
    hopCount = 0
    highlight = ''
    dataVersion = 0L
    playback = if ($ReplayPath) {
        'Replay'
    }
    else {
        'Live'
    }
    distribution = @()
    losses = @()
    routeChanges = 'No route changes recorded.'
    rangeKey = 'Latency · │ min/max ━ range ● average · select a row to highlight'
} -Root @{
    Id = 'root'
    Type = 'VStack'
    VerticalAlignment = 'Stretch'
    Children = @(
        @{
            Id = 'summary'
            Type = 'Text'
            Bind = 'summary'
        }
        @{
            Id = 'route'
            Type = 'DataGridControl'
            Bind = 'hops'
            VerticalAlignment = 'Stretch'
            SelectionBind = 'selectedHop'
            Columns = @('Hop', 'Address', 'Avg', 'Miss%', 'Latency')
            CellRanges = @('Latency')
            CellColors = @(
                foreach ($column in 'Avg') {
                    @{
                        Column = $column
                        From = 0
                        Color = '#65CD79'
                    }
                    @{
                        Column = $column
                        From = 50
                        Color = '#F4C65D'
                    }
                    @{
                        Column = $column
                        From = 150
                        Color = '#FF7279'
                    }
                }
                @{
                    Column = 'Miss%'
                    From = 0
                    Color = '#65CD79'
                }
                @{
                    Column = 'Miss%'
                    From = 0.1
                    Color = '#F4C65D'
                }
                @{
                    Column = 'Miss%'
                    From = 10
                    Color = '#FF7279'
                }
            )
            Options = @{
                ReadOnly = $true
                SelectionMode = 'Row'
                MaxHeight = 14
            }
        }
        @{
            Id = 'routeKey'
            Type = 'HStack'
            Spacing = 1
            Children = @(
                @{
                    Id = 'goodKey'
                    Type = 'Text'
                    Width = 6
                    Text = '<50ms'
                    Foreground = '#65CD79'
                }
                @{
                    Id = 'warnKey'
                    Type = 'Text'
                    Width = 10
                    Text = '50–<150ms'
                    Foreground = '#F4C65D'
                }
                @{
                    Id = 'badKey'
                    Type = 'Text'
                    Width = 6
                    Text = '≥150ms'
                    Foreground = '#FF7279'
                }
                @{
                    Id = 'statsKey'
                    Type = 'Text'
                    Bind = 'rangeKey'
                }
            )
        }
        @{
            Id = 'detail'
            Type = 'Text'
            Bind = 'detail'
        }
        @{
            Id = 'actions'
            Type = 'HStack'
            Spacing = 1
            Children = @(
                @{
                    Id = 'pause'
                    Type = 'Button'
                    Text = 'Pause / Live'
                }
                @{
                    Id = 'older'
                    Type = 'Button'
                    Text = 'Older'
                }
                @{
                    Id = 'newer'
                    Type = 'Button'
                    Text = 'Newer'
                }
                @{
                    Id = 'save'
                    Type = 'Button'
                    Text = 'Save session'
                }
                @{
                    Id = 'playback'
                    Type = 'Text'
                    Bind = 'playback'
                }
            )
        }
        @{
            Id = 'plots'
            Type = 'Tabs'
            VerticalAlignment = 'Stretch'
            Children = @(
                $latencyChart
                $lossChart
                @{
                    Id = 'distribution'
                    Type = 'Chart'
                    TabTitle = 'Latency distribution'
                    Bind = 'distribution'
                    GraphKind = 'Histogram'
                    ShowAxes = $true
                }
                @{
                    Id = 'changes'
                    Type = 'TextArea'
                    TabTitle = 'Route changes'
                    Bind = 'routeChanges'
                    VerticalAlignment = 'Stretch'
                }
            )
        }
    )
}
if ($DefinitionOnly.IsPresent) {
    return $layout
}

if (-not $session -and -not ('Fullscreen.Examples.RouteProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Fullscreen.Examples;

public sealed record HopReply(int Hop, string Address, string Status, double Milliseconds, bool Reached);
public sealed record RouteSweep(string Destination, HopReply[] Hops, string Error);

public static class RouteProbe {
    public static async Task<RouteSweep> TraceAsync(
        string target, int maxHop, int timeoutMilliseconds, CancellationToken cancellationToken) {
        IPAddress destination;
        try {
            var addresses = await Dns.GetHostAddressesAsync(target, cancellationToken).ConfigureAwait(false);
            var selected = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
            if (selected is null)
                return new RouteSweep("", [], "No IP addresses returned.");

            destination = selected;
        }
        catch (SocketException error) {
            return new RouteSweep("", [], error.Message);
        }

        var hops = new List<HopReply>(maxHop);
        var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        const int batchSize = 8;
        for (int firstHop = 1; firstHop <= maxHop; firstHop += batchSize) {
            int count = Math.Min(batchSize, maxHop - firstHop + 1);
            var batch = Enumerable.Range(firstHop, count)
                .Select(hop => ProbeAsync(destination, hop, timeout, cancellationToken));
            foreach (var reply in await Task.WhenAll(batch).ConfigureAwait(false)) {
                hops.Add(reply);
                if (reply.Reached)
                    return new RouteSweep(destination.ToString(), hops.ToArray(), "");
            }
        }

        return new RouteSweep(destination.ToString(), hops.ToArray(), "");
    }

    private static async Task<HopReply> ProbeAsync(
        IPAddress target, int hop, TimeSpan timeout, CancellationToken cancellationToken) {
        using var ping = new Ping();
        var started = Stopwatch.GetTimestamp();
        try {
            var reply = await ping.SendPingAsync(
                target, timeout, new byte[32], new PingOptions(hop, false), cancellationToken).ConfigureAwait(false);
            bool reached = reply.Status == IPStatus.Success;
            bool expired = reply.Status is IPStatus.TtlExpired or IPStatus.TimeExceeded;
            string address = reached || expired ? reply.Address?.ToString() ?? "" : "";
            double elapsed = reached ? reply.RoundtripTime
                : expired ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : double.NaN;
            return new HopReply(hop, address, reply.Status.ToString(), elapsed, reached);
        }
        catch (PingException error) {
            return new HopReply(hop, "", error.InnerException?.Message ?? error.Message, double.NaN, false);
        }
    }
}
'@
}

$cancellation = [Threading.CancellationTokenSource]::new()
$runtime = @{
    Target = $Target
    MaxHop = $MaxHop
    Timeout = $TimeoutMilliseconds
    Interval = $IntervalMilliseconds
    HistorySize = $HistorySize
    Clock = [Diagnostics.Stopwatch]::StartNew()
    Token = $cancellation.Token
    NextSweep = 0L
    Pending = $null
    History = @{}
    Archive = [Collections.Generic.Queue[object]]::new()
    Paused = $false
    ViewSweep = 0L
    Replay = $null -ne $session
    SessionPath = $SessionPath
    Destination = ''
    Sweep = 0L
    SelectedHop = $null
    LastHopCount = 1
    ShowHistory = {
        param($trace, $view)
        $archive = $trace.Archive.ToArray()
        if ($archive.Length -eq 0) {
            return
        }
        $end = if ($trace.Paused) {
            $trace.ViewSweep
        }
        else {
            $archive[-1].Sweep
        }
        $window = @($archive | Where-Object {
                $_.Sweep -le $end
            })
        if ($window.Length -eq 0) {
            $window = @($archive[0])
        }
        $latest = $window[-1]
        $trace.ViewSweep = $latest.Sweep
        $largest = ($latest.Rows | Measure-Object -Property Max -Maximum).Maximum
        $rangeScale = [math]::Max(50, [math]::Ceiling([double]$largest / 50) * 50)
        if (-not [object]::ReferenceEquals($trace.DisplaySource, $latest.Rows)) {
            $trace.DisplaySource = $latest.Rows
            $view.hops = @(foreach ($row in $latest.Rows) {
                    $display = [ordered]@{}
                    foreach ($property in $row.PSObject.Properties) {
                        $display[$property.Name] = $property.Value
                    }
                    $display.Latency = [pscustomobject]@{
                        Min = $row.Min
                        Avg = $row.Avg
                        Max = $row.Max
                        Scale = $rangeScale
                    }
                    [pscustomobject]$display
                })
        }
        $view.rangeKey = "0–$rangeScale ms · │ min/max ● avg · click row → highlight"
        $view.hopCount = @($latest.Replies).Count
        $columns = $view.hopCount
        $currentAddress = @{}
        foreach ($snapshot in $window) {
            foreach ($reply in $snapshot.Replies) {
                if ($reply.Address) {
                    $currentAddress[[int]$reply.Hop] = $reply.Address
                }
            }
        }
        $timeline = [Collections.Generic.List[object]]::new()
        $losses = [Collections.Generic.List[object]]::new()
        foreach ($snapshot in $window) {
            $sample = [ordered]@{
                Sweep = [long]$snapshot.Sweep
            }
            $lossSample = [ordered]@{
                Sweep = [long]$snapshot.Sweep
            }
            for ($hop = 1; $hop -le $columns; $hop++) {
                $reply = @($snapshot.Replies)[$hop - 1]
                $available = $null -ne $reply -and (
                    -not $reply.Address -or $reply.Address -eq $currentAddress[$hop]
                )
                $value = if ($available -and $null -ne $reply.Milliseconds) {
                    [double]$reply.Milliseconds
                }
                else {
                    [double]::NaN
                }
                $sample["Hop $hop"] = $value
                $lossSample["Hop $hop"] = if ($available) {
                    [double][double]::IsNaN($value)
                }
                else {
                    [double]::NaN
                }
            }
            $timeline.Add([pscustomobject]$sample)
            $losses.Add([pscustomobject]$lossSample)
        }
        $view.timeline = $timeline.ToArray()
        $view.losses = $losses.ToArray()
        $view.dataVersion++
        $hop = $trace.SelectedHop ?? $view.hopCount
        $row = $view.hops | Where-Object Hop -eq $hop | Select-Object -First 1
        $values = @($view.timeline | ForEach-Object {
                $_."Hop $hop"
            } | Where-Object {
                $null -ne $_ -and [double]::IsFinite($_)
            })
        $view.distribution = $values
        $view.detail = "Hop $hop · $($currentAddress[$hop]) · $($row.Status)"
        $view.detail += " · last $($row.Last) · min $($row.Min) · max $($row.Max) ms · N $($row.N)"
        if ($values.Count) {
            $sorted = @($values | Sort-Object)
            $p95 = $sorted[[math]::Ceiling(.95 * $sorted.Count) - 1]
            $difference = 0.0
            $pairs = 0
            $previous = [double]::NaN
            foreach ($sample in $view.timeline) {
                $value = $sample."Hop $hop"
                if ($null -ne $value -and [double]::IsFinite($value)) {
                    if ([double]::IsFinite($previous)) {
                        $difference += [math]::Abs($value - $previous)
                        $pairs++
                    }
                    $previous = $value
                }
                else {
                    $previous = [double]::NaN
                }
            }
            $jitter = if ($pairs) {
                $difference / $pairs
            }
            else {
                0
            }
            $view.detail += ' · p95 {0:0.#} ms · jitter {1:0.#} ms' -f $p95, $jitter
        }
        $changes = @($window | ForEach-Object {
                $_.Changes
            } | Where-Object {
                $_
            })
        $view.routeChanges = if ($changes.Count) {
            $changes -join "`n"
        }
        else {
            'No route changes recorded.'
        }
        $mode = if ($trace.Replay) {
            'Replay'
        }
        elseif ($trace.Paused) {
            'Paused'
        }
        else {
            'Live'
        }
        $view.playback = "$mode · sweep $($latest.Sweep) · $($latest.Timestamp)"
    }
}
if ($session) {
    foreach ($snapshot in $session.Sweeps) {
        $runtime.Archive.Enqueue($snapshot)
    }
    $runtime.Paused = $true
    $runtime.ViewSweep = $session.Sweeps[-1].Sweep
    $runtime.Sweep = [long]$session.Sweeps[-1].Sweep
}
try {
    $null = $layout | Invoke-Tui -Minimal -State @{
        trace = $runtime
    } -TickInterval 100 -OnEvent {
        param($tuiEvent, $state)
        if ($tuiEvent.Id -eq 'route' -and $tuiEvent.Kind -eq 'Selection' -and $null -ne $tuiEvent.Value) {
            $state.trace.SelectedHop = [int]$tuiEvent.Value.Hop
            $state.highlight = "Hop $($state.trace.SelectedHop)"
            & $state.trace.ShowHistory $state.trace $state
        }
        if ($tuiEvent.Kind -eq 'Click') {
            $trace = $state.trace
            switch ($tuiEvent.Id) {
                'pause' {
                    $trace.Paused = -not $trace.Paused
                    if ($trace.Replay) {
                        $trace.Paused = $true
                        $trace.ViewSweep = $trace.Archive.ToArray()[-1].Sweep
                    }
                }
                'older' {
                    $trace.Paused = $true
                    $trace.ViewSweep -= [math]::Max(1, [int]($trace.HistorySize / 4))
                }
                'newer' {
                    $trace.ViewSweep += [math]::Max(1, [int]($trace.HistorySize / 4))
                    $trace.Paused = $true
                }
                'save' {
                    if ($trace.Archive.Count -eq 0) {
                        $state.playback = 'Waiting for samples to save.'
                        return
                    }
                    if (-not $trace.SessionPath) {
                        $state.playback = 'Use -SessionPath to choose where Save session writes JSON.'
                        return
                    }
                    $sessionData = @{
                        Version = 1
                        Target = $trace.Target
                        Sweeps = @($trace.Archive.ToArray())
                    }
                    $temporary = $null
                    try {
                        $destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($trace.SessionPath)
                        $temporary = "$destination.$([guid]::NewGuid().ToString('N')).tmp"
                        $json = $sessionData | ConvertTo-Json -Depth 16 -Compress
                        if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 32MB) {
                            throw 'Session exceeds 32 MiB. Reduce HistorySize before saving.'
                        }
                        [IO.File]::WriteAllText($temporary, $json)
                        [IO.File]::Move($temporary, $destination, $true)
                        $state.playback = "Saved $($trace.Archive.Count) sweeps"
                    }
                    catch {
                        $state.playback = "Save failed · $($_.Exception.Message)"
                    }
                    finally {
                        if ([IO.File]::Exists($temporary)) {
                            [IO.File]::Delete($temporary)
                        }
                    }
                    return
                }
            }
            & $trace.ShowHistory $trace $state
        }
    } -OnTick {
        param($tuiEvent, $state)
        $trace = $state.trace
        if ($trace.Replay) {
            if ($state.timeline.Count -eq 0) {
                & $trace.ShowHistory $trace $state
            }
            return
        }
        if ($null -eq $trace.Pending) {
            $elapsed = $trace.Clock.ElapsedMilliseconds
            if ($elapsed -ge $trace.NextSweep) {
                $trace.Pending = [Fullscreen.Examples.RouteProbe]::TraceAsync(
                    $trace.Target, $trace.MaxHop, $trace.Timeout, $trace.Token
                )
                $trace.NextSweep = $elapsed + $trace.Interval
            }
            return
        }
        if (-not $trace.Pending.IsCompleted) {
            return
        }

        $sweep = $trace.Pending.GetAwaiter().GetResult()
        $trace.Pending = $null
        if ($sweep.Error) {
            $state.summary = "Ping plotter · $($trace.Target) · Lookup failed: $($sweep.Error)"
            return
        }
        $trace.Sweep++
        $destinationChange = ''
        if ($trace.Destination -ne $sweep.Destination) {
            if ($trace.Destination) {
                $destinationChange = "Sweep $($trace.Sweep) · destination $($trace.Destination) → $($sweep.Destination)"
            }
            $trace.History.Clear()
            $trace.Destination = $sweep.Destination
        }
        $lastHop = $sweep.Hops[-1]
        $previousHopCount = $trace.LastHopCount
        $trace.LastHopCount = $lastHop.Hop
        $reachability = if ($lastHop.Reached) {
            "reached at hop $($lastHop.Hop)"
        }
        else {
            "no destination reply within $($lastHop.Hop) hops"
        }
        $endpoint = if ($trace.Target -eq $trace.Destination) {
            $trace.Target
        }
        else {
            "$($trace.Target) ($($trace.Destination))"
        }
        $state.summary = "Ping plotter · $endpoint · $reachability"
        $formatLatency = {
            param([double]$milliseconds)
            # Keep numeric milliseconds for sorting and threshold colors in every culture.
            [math]::Round($milliseconds, [int]($milliseconds -lt 1000))
        }
        $changes = [Collections.Generic.List[string]]::new()
        if ($destinationChange) {
            $changes.Add($destinationChange)
        }
        if ($trace.Sweep -gt 1 -and $previousHopCount -ne $lastHop.Hop) {
            $changes.Add("Sweep $($trace.Sweep) · route length $previousHopCount → $($lastHop.Hop)")
        }
        $rows = @(foreach ($reply in $sweep.Hops) {
                $history = $trace.History[$reply.Hop]
                $addressChanged = $reply.Address -and $history.Address -and $reply.Address -ne $history.Address
                if (-not $history -or $addressChanged) {
                    if ($addressChanged) {
                        $changes.Add("Sweep $($trace.Sweep) · hop $($reply.Hop) · $($history.Address) → $($reply.Address)")
                    }
                    $history = @{
                        Address = $reply.Address
                        Samples = [Collections.Generic.Queue[double]]::new()
                        ProbeCount = 0L
                    }
                    $trace.History[$reply.Hop] = $history
                }
                if ($reply.Address) {
                    $history.Address = $reply.Address
                }
                $history.Samples.Enqueue($reply.Milliseconds)
                $history.ProbeCount++
                if ($history.Samples.Count -gt $trace.HistorySize) {
                    $null = $history.Samples.Dequeue()
                }

                $valid = @($history.Samples | Where-Object {
                        -not [double]::IsNaN($_)
                    })
                $stats = $valid | Measure-Object -Minimum -Average -Maximum
                $stateLabel = switch ($reply.Status) {
                    'Success' {
                        'Dst'
                    }
                    {
                        $_ -in 'TtlExpired', 'TimeExceeded'
                    } {
                        'Hop'
                    }
                    'TimedOut' {
                        'Tmo'
                    }
                    default {
                        'Err'
                    }
                }
                [pscustomobject][ordered]@{
                    Hop = $reply.Hop
                    Address = if ($history.Address.Length -gt 15) {
                        $history.Address.Substring(0, 14) + '…'
                    }
                    elseif ($history.Address) {
                        $history.Address
                    }
                    else {
                        '—'
                    }
                    St = $stateLabel
                    Status = if ($reply.Reached) {
                        'Destination'
                    }
                    elseif ($reply.Status -in 'TtlExpired', 'TimeExceeded') {
                        'Hop reply'
                    }
                    else {
                        $reply.Status
                    }
                    Last = if (-not [double]::IsNaN($reply.Milliseconds)) {
                        & $formatLatency $reply.Milliseconds
                    }
                    Min = if ($valid.Count) {
                        & $formatLatency $stats.Minimum
                    }
                    Avg = if ($valid.Count) {
                        & $formatLatency $stats.Average
                    }
                    Max = if ($valid.Count) {
                        & $formatLatency $stats.Maximum
                    }
                    'Miss%' = [math]::Round(100.0 * ($history.Samples.Count - $valid.Count) / $history.Samples.Count, 1)
                    N = $history.ProbeCount
                }
            })
        foreach ($hop in @($trace.History.Keys)) {
            if ($hop -gt $lastHop.Hop) {
                $trace.History.Remove($hop)
            }
        }
        if ($trace.SelectedHop -gt $lastHop.Hop) {
            $trace.SelectedHop = $null
            $state.highlight = ''
        }
        $replies = @(foreach ($reply in $sweep.Hops) {
                [pscustomobject]@{
                    Hop = $reply.Hop
                    Address = $reply.Address
                    Status = $reply.Status
                    Milliseconds = if ([double]::IsFinite($reply.Milliseconds)) {
                        $reply.Milliseconds
                    }
                    else {
                        $null
                    }
                }
            })
        $trace.Archive.Enqueue([pscustomobject]@{
                Sweep = $trace.Sweep
                Timestamp = [datetimeoffset]::Now.ToString('o')
                Rows = $rows
                Replies = $replies
                Changes = $changes.ToArray()
            })
        if ($trace.Archive.Count -gt $trace.HistorySize) {
            $null = $trace.Archive.Dequeue()
        }
        if (-not $trace.Paused) {
            & $trace.ShowHistory $trace $state
        }
    }
}
finally {
    $cancellation.Cancel()
    $cancellation.Dispose()
}
