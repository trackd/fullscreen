# System monitor

Run `ftop` for processes or `ftop -View Dashboard` for hardware history. See
[Show-SystemMonitor](../en-US/Show-SystemMonitor.md) for all parameters.

## Views and controls

| Key | View | Main use |
| --- | --- | --- |
| 1 | Processes | Sort/filter processes, inspect details and process trees |
| 2 | Services | Inspect state and available service actions |
| 3 | Network | Interface rates, history and addresses |
| 4 | GPU | Device and process activity where exposed |
| 5 | Disks | Read/write throughput, IOPS, capacity and latency |
| 6 | Users | Logged-in users and their processes |
| 7 | Dashboard | CPU, memory, network and GPU overview |

Press **?** for keys. **D** opens details, **P** pauses display, **T** toggles trees,
**C** chooses columns, and **A** or right-click opens actions. Actions require
confirmation and use your existing permissions. Logging off a Windows session
closes applications and may lose unsaved work.

In Network, **[** and **]** select aggregate or individual-interface history.
In Disks, arrow keys select a drive or switch activity/latency details. Drive
cards combine mapped filesystem capacity with device activity. Identified stacked
storage layers are excluded from overview totals.

## Reading the metrics

- A missing counter is unknown, not zero. Some rates need a second sample.
- CPU load describes logical processors. Temperatures are physical sensors;
  package readings are not duplicated across threads. Missing temperatures stay
  hidden. Windows/macOS CPU temperature currently needs another sensor provider.
- Available memory includes reclaimable memory. Cache, free, commit and swap
  have different meanings and must not be added together. Commit is reserved
  virtual memory, not physical RAM use; Linux may allow it to exceed its limit.
- Disk capacity describes mapped filesystems; device I/O covers the whole disk.
  Busy percentage is not a reliable saturation measure for parallel storage.
  Queue and latency semantics vary by OS. A filesystem spanning several disks
  may appear separately rather than receive guessed I/O attribution.
- GPU engine counters can overlap. Shared/unified memory is not dedicated VRAM;
  summing process allocations may double-count buffers. Detection of a display
  adapter does not guarantee acceleration counters.
- VMs show guest devices and counters. Hyper-V/VMware synthetic displays do not
  imply access to the host GPU; assigned hardware needs a guest driver.
- Users lists login sessions, not every service account owning a process.

## Remote monitoring

The caller owns the session:

```powershell
$session = New-PSSession -ComputerName server01
try {
    Show-SystemMonitor -Session $session -View Dashboard -SampleInterval 2000
}
finally {
    Remove-PSSession -Session $session
}
```

Use a larger SampleInterval on slow links. Errors retain the last snapshot and
show stale/error status while retrying. Support and permissions are those of the
monitored computer, not the client displaying the TUI.

## Permissions and optional Linux elevation

Start without elevation. Some driver counters need additional access. `-Sudo`
opts into non-interactive fallback on the collector host. For interactive use,
`-SudoPrompt` accepts a callback returning one SecureString; no output cancels.

```powershell
ftop -View Gpu -Sudo -SudoPrompt {
    param($request)
    Read-Host -Prompt "Sudo on $($request.Target): $($request.Reason)" -AsSecureString
}
```

Authentication happens before the UI starts. Failed/cancelled prompts continue
with unprivileged telemetry. Omit SudoPrompt in unattended scripts. See the cmdlet
parameters for the full callback contract.

## Troubleshooting

Use **F12** for timing, terminal geometry and renderer diagnostics. See
[terminal graphics](terminal-graphics.md) for chart display issues. Missing sensor
values may mean that the OS/driver does not expose them to this process.
Provider implementation and validation limits are documented for
[contributors](../maintainers/telemetry.md).
