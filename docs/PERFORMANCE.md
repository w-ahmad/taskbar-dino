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

First run (before the allocation-free taskbar scan): private bytes crept from 37.3 MB to 40.2 MB
over 10 minutes (≈0.3 MB/min) with CPU at 1–3 ms/s, handles 297–313 and threads 5–8. The growth was
isolated by bisection (WARP, 1 MB gen0 budget, idle poll on/off) to the managed allocations of the
periodic UI Automation scan — strings, lists and tuples created every 6 s that the AOT garbage
collector had not yet collected — not to a native leak. The scan was rewritten to allocate nothing
(spans over the BSTRs, a ref-struct tokenizer, reused lists, cached AUMID).

Final build, 5 minutes idle, GPU path, 30 s samples:

| | first sample | last sample |
|---|---|---|
| Private bytes | 36.9 MB | 36.9 MB |
| Working set | 61.1 MB | 61.3 MB |
| Handles | 307 | 296 |
| Threads | 8 | 6 |
| CPU | 1.0–2.6 ms/s (avg 2.0 ms/s = 0.009 % of the machine) | |

Four-minute WARP control runs: private 7.0 → 6.9 MB with the idle poll on, 6.9 → 6.7 MB with it
off (0.0–1.0 ms/s CPU without the poll, i.e. the poll accounts for about 2 ms/s). Raw CSVs are in
the measurement scratch folder; the numbers above are copied from them.

Repeated-cycle tests (game enter/exit, drag, colour and setting changes) were exercised in the
Sandbox and host sessions during development but were not run as counted long-duration loops; see
the validation report for what was and was not automated.

## Game Mode (autopilot, 40 s, logging on)

`TaskbarDino.exe --game --autopilot --log`, 40 s, full-width stage on a 1920 px monitor, dino size
Small (1.5×), 60 Hz display, vsync present. "fps" is the per-second count of game frames drawn while
the run was in the Running state (one-second windows containing a crash/restart read low).

| | GPU (default) | WARP (Low-Memory Rendering) |
|---|---|---|
| Frame rate, average / max | 56.8 / 58.9 fps | 58.4 / 59.1 fps |
| CPU, % of one core | 0.12 % | 0.62 % |
| CPU, % of the machine | 0.005 % | 0.028 % |
| Peak private bytes | 41.0 MB | 7.8 MB |
| Peak working set | 65.3 MB | 34.9 MB |
| Threads | 8 | 8 |

Before the high-resolution pacing timer the same run averaged 46.9 fps (GPU) / 51.4 fps (WARP): the
plain `MsgWaitForMultipleObjectsEx` timeout rounds up to the 15.6 ms system tick, so a frame that
needed a 6 ms wait slept 15 ms. Pacing now arms a `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` timer and
waits on it (plus messages), which lands frames on the 16.7 ms budget without changing the global
timer resolution.

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

## After the art rework (48 px dino, Fluent menu icons, new moods)

Same scripts, same machine, published build of the second pass:

| Metric | Value |
|---|---|
| Startup → dino visible | 0.46 s |
| Idle CPU (20 s) | 0.014 % (3.1 ms/s) |
| Working set / private (GPU) | 65.3 / 41.0 MB |
| Threads | 11 |
| Game, GPU: fps avg / max, CPU of one core | 57.8 / 58.9 fps, 0.77 % |
| Game, WARP: fps avg / max, CPU of one core | 58.4 / 59.0 fps, 0.41 % |
| Executable | 2.57 MB |

The larger sprite sheet (384×240 instead of 192×24 texels) and 4× larger game sprites raise the
per-frame draw cost slightly during play; idle cost is unchanged because nothing is drawn while
the dino stands still.
