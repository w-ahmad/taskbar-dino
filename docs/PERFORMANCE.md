# Performance report

All numbers were measured on the development machine (Windows 11 Pro 26100, Intel integrated GPU,
8 logical cores, 2 monitors) with the scripts described at the end. "Before" is the application as
received (framework-dependent .NET 10 build, Vortice renderer, DI + console logging, 6 projects).
"After" is the Native AOT build of this release.

## Headline

| Metric | Before | After (GPU, default) | After (Low-Memory Rendering) |
|---|---|---|---|
| Startup → dino visible | 1.10 s | 0.51–0.71 s | 0.42 s |
| Idle CPU (20 s, stationary) | 0.153 % (33.6 ms/s) | 0.014–0.024 % (3–5 ms/s) | 0.000 % (0 ms/s measured) |
| Working set | 95.3 MB | 61–64 MB | 30.4 MB |
| Private bytes | 50.7 MB | 37–41 MB | 7.2 MB |
| Threads | 40 | 11 (5 once settled) | 11 |
| Handles | 454 | 309–341 | 320 |
| Loaded modules | 120 | 61 | 50 |
| Executable | 163 KB (+ 41 files) | 2.5 MB single file | same |
| Deployment | 42 files, 4.25 MB (+ .NET 10 runtime required) | 1 file, 2.5 MB, nothing to install | same |

Idle CPU is the total processor time of the process over 20 s with the dino stationary (it still
blinks and glances, and re-checks its taskbar button every 6 s), divided by wall time and cores.

### Where the remaining memory goes

With the GPU path the process maps the Intel driver (`igc-default64.dll` 80 MB, `igd10umt64xe.dll`
31 MB; shared, file-backed) and the driver keeps roughly 30 MB of private allocations. That is the
cost of creating a hardware Direct3D 11 device at all; the app's own managed heap and buffers are
a few megabytes (the WARP number is the ceiling: 7.2 MB private for the whole process). The
**Low-Memory Rendering (CPU)** menu setting switches to WARP live. Machines whose driver is lighter
will sit between the two columns.

`D3D11_CREATE_DEVICE_PREVENT_INTERNAL_THREADING_OPTIMIZATIONS` + `SINGLETHREADED` brought the
thread count from 35 to 11 on the GPU path; the remaining threads belong to the runtime (GC,
finalizer), COM/UIA and the driver, all idle.

### Deployment flavours compared (same code)

| Flavour | Files | Size | Needs .NET runtime | Startup |
|---|---|---|---|---|
| Framework-dependent | 5 | 1.74 MB (exe 164 KB) | yes (10.0) | ≈ 1.0 s |
| Self-contained, trimmed, single-file | 1 | 12.07 MB | no | not measured |
| **Native AOT (shipped)** | 1 | 2.52 MB | no | 0.4–0.7 s |

## Long-run idle (10 minutes, 30 s samples, GPU path)

LONGRUN_PLACEHOLDER

## Game Mode (autopilot, 40 s, logging on)

GAME_PLACEHOLDER

## What made the difference

1. **Change-driven rendering.** A frame is drawn only when the window rectangle, sprite frame,
   flip or effect differs from the last drawn one; otherwise the loop sleeps until the next
   scheduled change (next animation frame, next brain event, debounce expiry, idle poll). Hover
   and drag are event-driven too, coalesced to the 60 FPS cap. Before: hovering or any reaction ran
   the loop at 60 FPS continuously.
2. **Zero background machinery.** No DI container, no console/debug logger thread, no event bus,
   no periodic timers except the one bounded "window is still launching" poll. Logging is an
   interpolated-string handler that formats nothing unless enabled.
3. **Native AOT, no third-party runtime code.** Vortice (eight assemblies, ≈1.8 MB) and
   Microsoft.Extensions (nine assemblies) replaced by CsWin32-generated raw COM/Win32; the JIT,
   assembly loading and type loading disappear from startup.
4. **Cheaper taskbar lookups.** UI Automation queries are filtered to Button elements with the
   three needed properties cached in the same call (one cross-process round trip per taskbar
   instead of three per element), and the idle re-check runs every 6 s instead of 2.5 s.
5. **Game-over costs nothing.** The frozen scene is drawn twice (crash, restart hint) and then the
   loop blocks until a key arrives.
6. **GPU driver threading disabled** (above), and the D2D context pinned to 96 DPI so sprites are
   scaled once by the app with nearest-neighbour sampling.

## How it was measured

- `scratchpad/measure.ps1`: starts the exe, polls `FindWindow("TaskbarDinoOverlay")` + `IsWindowVisible`
  for the startup time, waits a warm-up, then samples `TotalProcessorTime`, `WorkingSet64`,
  `PrivateMemorySize64`, `HandleCount`, `Threads.Count` over the idle window.
- `scratchpad/longrun.ps1`: same counters every 30 s for 10 minutes, CSV + summary.
- Game Mode: `TaskbarDino.exe --game --autopilot --log` (benchmark-only flags: starts in Game Mode
  and a bot plays) with the per-second frame-rate lines from the log and the same process counters.
- Deployment sizes: `dotnet publish` with the three configurations, `.pdb`/`.xml` excluded.
