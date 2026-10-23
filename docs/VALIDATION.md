# Validation report

Date: 2026-10-23. Build under test: `TaskbarDino.exe` 1.0.0, Native AOT win-x64, commit on branch
`release-readiness`.

## Environments

| | Host | Windows Sandbox (guest) |
|---|---|---|
| OS | Windows 11 Pro 10.0.26100 | Windows 11 (Sandbox image), 1520×823, 100 % DPI |
| GPU | Intel integrated (hardware Direct3D 11) | none → WARP software rasteriser (automatic fallback) |
| Monitors | 2 (1920×1080 primary + secondary) | 1 |
| Tooling | PowerShell measurement scripts | `winapp` CLI 0.7.1 (`--on sandbox`), screenshots and recordings via `winapp target` |
| Apps available for app-following tests | the user's normal apps | File Explorer, PowerShell, cmd (no Notepad/Paint/Store apps in the Sandbox image) |

Automated unit tests: **75 passed, 0 failed** (`dotnet test`, net10.0, TaskbarDino.Core only; they
cover animation sequencing, the brain, movement, debouncing, the runner's physics/obstacles/scoring/
game-over/mirroring/frame-rate independence, sprite sheets and settings/hide expiry).

## Sandbox test log

Evidence files are in `artifacts/validation/` (screenshots `*.png`, recordings
`sandbox-interactions.mp4`, `sandbox-game.mp4` with per-frame JPEGs in the `.frames` folders).

| # | Scenario | Result | Evidence |
|---|---|---|---|
| 1 | Launch; dino appears, transparent, no window chrome | Pass | `sandbox-01-launch.png` — dino on the Start button (no app active) |
| 2 | Taskbar re-centres when a button is added/removed; dino re-homes on the idle poll | Pass | `sandbox-02-notepad-active.png`, `sandbox-03-paint-active.png` |
| 3 | Open File Explorer → dino hops onto its taskbar button (exact, via UIA AUMID match) | Pass | `sandbox-04-explorer-active.png`, guest log `precise: True` |
| 4 | Click → happy hop with heart | Pass | `06-click.png`, `sandbox-click-drag-contact-sheet.png` (frames at t≈5.2–5.7 s) |
| 5 | Drag to a point above the desktop and release → falls to the ground, looks around, runs home | Pass | `07-dropped.png` (on the ground at the drop X), `08-returned.png` (back on the button, overlay rect identical to before) |
| 6 | Right-click → context menu with all items, correct check states | Pass | `09-context-menu.png` |
| 7 | Menu takes keyboard focus and hands focus back to the app afterwards | Pass (after fix) | first build: menu stayed open and keys went to Explorer (`10-game-running.png` of run 1); fixed with `AttachThreadInput`, verified in run 2 (menu dismissed, Explorer title bar active again) |
| 8 | Start Game from the menu | Pass | `16-game-started.png`; log `Game Mode entered (1520x135)` |
| 9 | Game stage: transparent strip, ground line, clouds, cacti, HI/score bubble above the dino | Pass | `16-game-started.png`, `19-game-restarted.png` |
| 10 | Mirrored stage when the dino is right of centre (obstacles enter from the left, dino faces left) | Pass | `16-game-started.png`, `19-game-restarted.png` |
| 11 | Jump (Space via SendInput) | Pass | `17-game-jump.png`, recording `sandbox-game.mp4` |
| 12 | Collision → GAME OVER text, scene frozen, restart arrow appears after the lockout | Pass (after fix) | `18-game-over.png`; first build never drew the arrow (redraw bug, fixed) |
| 13 | Restart with Space after game over; a Space inside the 0.45 s lockout is ignored | Pass | `19-game-restarted.png` (fresh obstacles, score 0) |
| 14 | Esc leaves Game Mode; small overlay restored on the app's button | Pass | `20-after-esc.png`; log `Game Mode left` |
| 15 | Duck (↓ held) | Not tested in Sandbox | `send-keys` cannot hold a key; covered by unit tests (`Duck_LowersTheCollisionBox_OnTheGroundOnly`, `DuckMidAir_LandsSooner`) |
| 16 | Double-click starts the game | Not tested in Sandbox | `winapp ui click --double` refuses a non-focusable target; the handler is the same `RequestGame` path the menu uses (verified) |
| 17 | Hover gaze | Pass (recording) | cursor glides over the dino before each click in `sandbox-interactions.mp4` |
| 18 | Hide for 1 h / 4 h, Show Dino Now, restore after restart | Not tested end-to-end | logic unit-tested (`AppSettingsTests`); the menu items are present |
| 19 | Colour / size / personality change | Not tested in Sandbox | exercised on the host during development (texture re-upload path) |
| 20 | Tray icon present | Pass | hidden in the Sandbox's overflow area; the owner window and `Shell_NotifyIcon` succeed (no warning in the log) |
| 21 | Single instance: second launch exits and un-hides the first | Pass (host) | second `TaskbarDino.exe` returns immediately; mutex `Local\TaskbarDino.SingleInstance` |
| 22 | Clean exit, no orphan process | Pass | `taskkill`/menu Exit leaves no `TaskbarDino.exe` in `tasklist` |

## Second pass (48 px art, moods, menu icons, movement)

Host captures after the art and behaviour rework, taken without injecting input (the app's own
`--menu-preview` and `--game --autopilot` switches):

| Scenario | Result | Evidence |
|---|---|---|
| 48 px T-rex on the active app's button, Medium size, accent colour | Pass | `docs/images/companion.png` |
| Context menu with Fluent icons and colour swatches | Pass | `docs/images/menu.png` |
| Game Mode at the new scale (dino, cacti, clouds, ground, HI bubble) | Pass | `docs/images/game.png` |
| All 20 poses + 12 effect sprites render inside their cells, same footprint for standing poses | Pass | unit tests `SpriteSheetTests`, `docs/images/sprites.png` |
| Walk / run / leap selection by distance | Pass | unit test `Travel_WalksShort_RunsMedium_LeapsFar` |
| Sleep (lying down + zzz), eat, play, celebrate moods | Reviewed in the sprite preview; timing exercised by `DinoBrainTests` | not captured live (random timing) |

## Windows scenarios

| Scenario | Result | Notes |
|---|---|---|
| Explorer restart | Not tested | code path: `TaskbarCreated` → tray re-add, re-topmost, re-home after 1.5 s |
| Taskbar auto-hide | Not tested | geometry: dino stands on the screen bottom edge when the taskbar reserves no space |
| DPI change / resolution change / monitor disconnect | Not tested | `WM_DPICHANGED`/`WM_DISPLAYCHANGE` → layout refresh and re-home; sizes are recomputed from the monitor's DPI |
| Multi-monitor following | Pass (host) | the dino moves between the two host monitors when the active app's button is on the other taskbar; cross-monitor moves run off one edge and continue from the other |
| Sleep/resume | Not tested | `WM_POWERBROADCAST` resume → foreground refresh and re-home; hide uses an absolute time |
| Full-screen application | Partially tested (host) | detection relies on `SHQueryUserNotificationState` and a covers-monitor check; the suspend/resume loop was exercised by maximising a borderless window |
| Relaunch after abnormal termination | Pass | killed with `taskkill /F`, relaunched cleanly; settings intact |

## Host runs

| Run | Result |
|---|---|
| Baseline (as received, framework-dependent) | started, dino on the Claude app's button; measured for the performance report |
| Restructured FD build | started, correct button, idle 0.028 % |
| AOT build | started, correct button; 20 s idle measurement; menu/game verified in the Sandbox instead of by injecting input on the host desktop |
| Long-run idle (10 min GPU, then 4–6 min WARP bisection runs, then 5 min GPU on the final build) | flat private memory on the final build (36.9 → 36.9 MB over 5 min); see `docs/PERFORMANCE.md` |
| Game Mode benchmark (`--game --autopilot`, 40 s, GPU and WARP) | 57–58 fps average at the 60 fps cap, 0.12 % / 0.62 % of one core |

## Limitations of this validation

- The Sandbox has no GPU, so Sandbox runs exercise the WARP path; the GPU path was exercised on the
  host only.
- Sandbox input injection cannot hold keys, double-click a non-focusable window, or act while no
  window is foreground, so duck, double-click and some menu paths rely on unit tests and code review.
- No multi-monitor, DPI-change, Explorer-restart or sleep scenario was automated.

## Code-review fixes (2026-10-23, later)

A two-axis review (standards + spec) of the whole change set found eight behavioural defects and a
few interop risks; all confirmed ones were fixed and re-tested (76 unit tests, host smoke run,
Native AOT publish):

- Hit test used only the frame's column, so poses on the second and third sheet shelves (walk3/4,
  jump, tail-up, yawn, sleep, scratch, stretch, duck, eat) tested against the wrong box.
- A DPI/resolution/work-area change teleported the dino to the monitor's left edge (fresh controller
  read before it was positioned).
- Full-screen detection treated any maximised window as full screen on monitors whose taskbar
  reserves no space (auto-hide, or none); now requires a borderless, non-maximised window there.
- A click no longer retargets a relocation in progress; the happy hop plays only when the dino is
  settled.
- A single click is reported only after the double-click interval passes (with the system
  double-click distance), so a double-click no longer also plays the click reaction.
- A drag whose button-up never reached the hook ends itself when the button is seen released.
- The hover flag clears while the dino is walking, so the idle poll resumes without a mouse move.
- Full-screen detection and re-homing moved out of the WinEvent callback into the frame loop.
- The high score is written at game over and on any exit path, not only on Esc.
- `MouseHook.Start` is idempotent; submenu icons are attached by position instead of a truncated
  HMENU; the menu-preview key timer does nothing once the menu has closed.
- Test names follow Method_Scenario_Expectation; dead members removed; shared `Random.NextRange`.
