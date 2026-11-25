# Telemetry providers

Implementation reference for contributors. User setup and metric interpretation are in the [system monitor guide](../guides/system-monitor.md). Availability depends on hardware, driver and permissions; implementation support does not imply live validation on every platform.

Linux GPU discovery identifies DRM devices independently of available metrics.
Intel i915 device utilization uses the kernel's PMU engine counters when available,
including PCI-addressed PMUs on passed-through GPUs. This measures the busiest
engine even when no application has a readable GPU client. PMU access can require
root or CAP_PERFMON. Sudo is disabled by default. With `-Sudo`, when GPU access is
denied the collector tries non-interactive sudo (`sudo -n`) if available. A shared isolated collector runs the requested sampler for elevated device
metrics; the UI, process inspection and control actions retain the original user's
permissions. No password prompt is shown and no system permissions are changed.
The reader is reused, closed when device collection stops, and exits after 15
seconds without a request. Startup and responses have bounded waits. If sudo
cannot run unattended, collection continues with available unprivileged readings
and an availability message; failed startup is not retried every refresh.
With `-Sudo -SudoPrompt`, an optional callback runs on the pipeline thread before
the TUI starts, only if sudo reports that authentication is required. It receives
a request with Target and Reason properties and returns one SecureString, or no
output to cancel. Passwords go through sudo's standard input, never arguments or
logs. The supplied SecureString remains caller-owned; the monitor clears its
temporary copy. Remote callbacks run on the client and pass SecureString through
the existing PowerShell remoting transport to authenticate on the target.
Authentication uses sudo's normal credential cache. It is not repeated while the
TUI owns terminal input; if authorization later expires, restart or authenticate
outside the monitor. A failed or cancelled callback does not enable elevation.
The first sample is unknown while counters warm up.
AMD device utilization and memory use driver sysfs counters. GPU-local hwmon
temperature is read when available. NVIDIA continues to use NVML.
Intel i915/xe and AMD amdgpu client accounting supplies process GPU activity and
resident memory when exposed by the driver. Without a device activity provider,
the device view can show the busiest engine across readable clients, identified
as client activity. New GPU clients are discovered every five seconds in the
device view; process GPU sampling scans each refresh to identify shared clients.
Restricted procfs access can limit coverage. Shared DRM clients have no process
attribution; process memory also requires residency and zero shared-buffer counts
for every reported region. Missing or ambiguous values remain unknown.
Client-memory totals may count shared buffers more than once and exclude hidden
clients or kernel allocations; they are not physical VRAM usage. Integrated GPU
buffers occupy system memory, and no dedicated VRAM capacity is invented.
On kernels exposing Xe engine-active/total PMU events, engine topology is queried
from the matching render node and each engine's active/total ticks are sampled.
The collector discovers PMU field layouts and scales both counters for perf
multiplexing. Missing kernel events or topology leave the DRM-client fallback
available; render-node and perf permissions still apply.
GPU activity columns distinguish Render, Compute, Copy, Video and VideoEnhance
where those engine classes are exposed. Intel Video is combined encode/decode;
it is not split into invented encoder and decoder readings. AMD's potentially
unified video rings are also shown as Video. Compute may run on render hardware;
a missing separate compute counter does not mean compute work is impossible.
The provider identifies client-derived engine data with `clients`; that data
covers readable clients and uses each class's reported capacity. PMU activity
uses the busiest physical engine instance in each class.
NVIDIA NVML additionally reports separate Encode and Decode activity, memory
read/write activity (MemoryActivity), graphics clock (Clock, MHz) and power
(Power, watts) where supported. Linux clock and power use GPU-associated GT and
hwmon interfaces. Power from hwmon may cover a GPU/SoC, including the CPU on an
AMD APU; the status identifies its scope. Energy-derived power needs two samples.
Host CPU temperature and RAPL readings are not attributed to a passed-through GPU.
Left/Right scrolls GPU columns; C selects them and header menus filter values.
Engine columns appear before capacity/sensor/provider columns to expose active
work quickly. Missing counters remain unknown and unsupported columns are hidden.
Virtual display adapters may expose no accounting even when a separate
passed-through GPU supports telemetry. Card and render-node numbers are not paired.
Hyper-V synthetic video is identified as Microsoft Hyper-V Video, including
Linux VMBus DRM devices and older `hyperv_fb` framebuffers. Windows also checks
the display-device inventory when DXGI omits the synthetic adapter. Synthetic
display rows report unavailable acceleration counters. Assigned GPUs in Hyper-V
or Azure guests use the Intel, AMD or NVIDIA provider exposed by the guest driver;
host GPU telemetry is not implied by detection of a synthetic display.
Windows GPU memory includes dedicated and shared usage from WDDM counters,
including integrated GPUs. Missing readings remain unknown rather than zero.
Windows adapters are identified by DXGI LUID and matched to NVML through their
PCI address. WDDM engine classes retain Render, Compute, Copy, Encode and Decode
activity per adapter rather than collapsing all GPUs into one row. DedicatedMemory
and SharedMemory show independently reported device usage. Process columns
GpuDedicatedMemory and GpuSharedMemory separate the corresponding WDDM counters.
The combined process GPU memory value requires both counters; a partial reading
is not shown as a complete total. Identity and MemoryKind columns identify the
adapter and explain whether bytes represent framebuffer usage, shared memory or
readable-client residency.

Process columns GpuAdapter and GpuEngine identify reported adapters and active
engine classes. Process details retain per-adapter activity and memory rather
than collapsing different GPUs into one unexplained number. A complete process
total requires complete readings for all attributed adapters. Shared DRM clients,
missing counters and ambiguous NVIDIA GPU partitions stay unknown. NVIDIA SM
activity is labeled SM because streaming multiprocessors can execute graphics
as well as compute work. The GPU Availability column explains provider-specific
warm-up, denied, unsupported or incomplete readings.

AMD Windows sensors are read opportunistically from the driver-installed ADL
library in the Windows system directory. Supported PMLog sensors provide graphics
clock, temperature, memory activity and board/ASIC power. No tuning API is used.
The driver may omit or reject this deprecated query; WDDM readings continue when
ADL is missing or unsupported. A read-only Overdrive6 temperature query provides
a fallback when PMLog omits temperature. No additional AMD SDK or monitoring tool is required.

Intel Windows GPU temperatures use the installed graphics driver's IGCL API
when available in 64-bit processes. Sensors are matched to adapters by PCI
address; GPU sensors take precedence over device-wide sensors, and memory
sensors are not presented as GPU temperature. No wrapper DLL or SDK is bundled.
This does not provide CPU package temperatures. On Windows, CPU register access
requires a supported kernel provider; ordinary chipset-driver installation does
not guarantee a public CPU-temperature interface.

On native Linux, NVML compute/graphics process lists supply NVIDIA process memory
without double-counting processes present in both lists. Available process samples
supply SM/encoder/decoder activity; initial or missing samples remain unknown.
WSL and detected containers do not attribute NVML host PIDs to local processes.
Device telemetry remains available there. Native-Linux NVIDIA process telemetry
requires driver support and sufficient process visibility.

Apple Silicon uses unprivileged IORegistry reads through the macOS `ioreg` tool;
no sudo, powermetrics or third-party monitoring package is required. Available
IOAccelerator statistics supply device/renderer activity and unified GPU allocation
bytes. Allocation bytes are not resident VRAM, and physical system RAM is not
reported as dedicated GPU capacity. Metal-client GPU time deltas supply process
GPU activity, tracking each IORegistry client separately. The first sample warms
up; closed, reset or missing clients do not produce negative activity. Apple's
built-in footprint tool supplies process graphics dirty/swapped charges when
permitted. Bounded queries run on a background worker; results are tied to process
start identity and become unknown when stale. Graphics footprint bytes are not
dedicated VRAM or ordinary process RSS. Other users' processes may be inaccessible
without elevation; the collector does not elevate footprint queries. Power,
temperature and separate compute/video engines remain unknown.
These IORegistry properties vary by macOS/driver version; missing values stay unknown.

CPU is a percentage of total processor capacity; RSS includes shared pages.

Remote metrics and actions apply to the target computer. Targets support
PowerShell 7 or Windows PowerShell 5.1 with .NET Framework 4.7.2 or later and
must allow in-memory assembly loading. If sampling fails, the last data remains
visible while the monitor retries.

## Validation scope

Windows collection and Debian/WSL collection have been exercised on the available
hosts, alongside parser, accounting and native-layout fixtures. This is not a
cross-vendor hardware certification. Apple Silicon, AMD ADL, Windows Intel IGCL,
Xe PMU hardware and active native Linux NVIDIA workloads still need matching
live-hardware validation. The standard Windows storage-temperature query returned
no reading on the available host. Fixture success verifies parsing/selection, not
that an arbitrary installed driver exposes a particular sensor.
