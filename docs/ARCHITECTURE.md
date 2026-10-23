# Architecture

Taskbar Dino is two projects plus tests. The split is the only boundary that matters: logic that can
run anywhere versus code that touches Windows.

```
src/TaskbarDino.Core   net10.0          pure logic, no Windows references, unit-tested
src/TaskbarDino        net10.0-windows  the executable: Win32, Direct2D/DirectComposition, orchestration
tests/TaskbarDino.Tests                 xunit, runs on any OS
```

There is no DI container, no logging framework and no third-party runtime dependency. The only
package is the CsWin32 source generator, which runs at compile time. The app publishes as one
Native AOT executable.

## Core (`TaskbarDino.Core`)

| Area | Types | Role |
|---|---|---|
| `Primitives`, `Graphics` | `PixelRect`, `RectF`, `Bgra32`, `Rgb`, `AtlasRegion`, `SpriteSheet` | Geometry and pixel buffers shared by everything. |
| `Content` | `DinoSpriteSheet`, `GameSpriteSheet`, `DinoAnimations`, `PixelArt` | All art, generated at runtime from character grids. One body grid plus eye/leg/effect variants gives 17 dino frames; a theme is a three-tone palette. The game sheet draws a one-pixel contrasting outline around every sprite so it reads on any taskbar. |
| `Animation` | `AnimationClip`, `SpriteAnimator`, `AnimationController` | Frame sequencing. A reaction one-shot overrides the sustained posture, then yields. `TimeUntilNextFrame` tells the loop how long it may sleep. |
| `Behavior` | `DinoBrain`, `MoveController`, `Debouncer<T>` | Idle personality (weighted reactions, sleep/wake), walking/running/hopping along the taskbar, and the burst-collapsing debounce used for app switching. |
| `Game` | `DinoGame`, `Trex`, `Obstacle`, `Cloud`, `GameConfig` | The runner, on a fixed 60 Hz step with a seeded `Random`: variable-height jump, duck and fast drop, three bird altitudes, cactus groups, parallax clouds, scrolling ground, game-over with a restart lockout. Frame-rate independent by construction. |
| `Settings` | `AppSettings`, `DinoTheme`, `DinoSize` | The persisted record. The hide timer is an absolute UTC instant so it survives restarts and sleep. |
| `Time` | `FrameTime`, `FrameOutcome` | The contract between a frame and the loop: `Idle`, `WakeIn(t)` or `Continuous`. |

## Executable (`TaskbarDino`)

### Frame loop and adaptive rendering

`Win32/MessageLoop` pumps messages, calls the frame callback, then does exactly what the returned
`FrameOutcome` says: block in `WaitMessage` (0% CPU), wait at most *t* in
`MsgWaitForMultipleObjectsEx`, or pace to the 60 FPS cap. Any input or posted message ends a wait
early. Nothing else in the process has a timer or a thread of its own.

`CompanionRuntime.Tick` computes the picture for this instant (window rectangle, frame, flip,
effect) and only calls the renderer if it differs from the last drawn picture. Draws are also
coalesced to the frame budget, so a 1 kHz mouse during a drag still produces at most 60 frames a
second. The outcome it returns is the soonest of: the next animation frame change, the brain's next
scheduled reaction, the follow debounce, the drop-then-return countdown and the idle poll. A static
dino therefore wakes only for the next blink.

Game Mode returns `Continuous` while running and stops rendering entirely on the game-over screen
(one frame at the crash, one more when the restart hint appears, then `Idle` until a key arrives).

### Rendering

`Rendering/Renderer` owns a Direct3D 11 device (single-threaded, no driver worker threads), a DXGI
composition swap chain with premultiplied alpha, a DirectComposition visual bound to the overlay
HWND, and a Direct2D device context that draws sprites with nearest-neighbour sampling. Pixel art is
scaled by `size × DPI` so whole-number magnifications stay crisp. `End()` reports device loss, and
the runtime recreates the renderer and re-uploads both sheets. `TASKBARDINO_WARP=1` forces the WARP
software rasteriser (used automatically where no GPU exists, such as Windows Sandbox).

### Windows integration (`Win32/`)

| Type | Mechanism |
|---|---|
| `OverlayWindow` | Tiny `WS_POPUP` tool window: top-most, no-activate, always click-through (`WS_EX_TRANSPARENT`), `WS_EX_NOREDIRECTIONBITMAP` for composition. It hugs the dino and is repositioned every frame the dino moves, so it never covers more than a sliver of the taskbar. It is also the sink for `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, work-area and accent changes, power resume and Explorer's `TaskbarCreated` broadcast. |
| `MouseHook` | `WH_MOUSE_LL`. The overlay never receives mouse messages itself; the hook asks the runtime "is this point on the dino?" (a rectangle test against the drawn frame's opaque bounds) and consumes only clicks and drags that land on it. Hover, click, double-click, right-click, drag start/move/release are derived here. |
| `KeyboardHook` | `WH_KEYBOARD_LL`, installed only while a game is running. Space/Up/Enter (jump, restart), Down (duck), Esc (exit) are consumed during play; every other key passes through, and outside Game Mode no key is observed at all. |
| `ForegroundTracker` | `EVENT_SYSTEM_FOREGROUND` WinEvent hook. Shell surfaces (task switcher, Start, search, tool windows, our own windows) are ignored; `Progman` means "desktop". A just-launched app that takes foreground before it is visible is re-checked by a short bounded `SetTimer` poll. |
| `TaskbarButtonLocator` | UI Automation over every `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`, one cached, Button-filtered `FindAllBuildCache` per taskbar. Match order: the window's App User Model ID against the button's AutomationId (works for PWAs, Store and pinned apps), then shared significant words between button label and window title/process; a button on the window's own monitor wins ties. |
| `HomeResolver` | Exact button → Start button (no active app or unknown button) → active window's centre projected onto its taskbar. |
| `Monitors` | Monitor and taskbar geometry from `GetMonitorInfo` work-area insets (robust on every Windows version); auto-hide and taskbar-less monitors detected by enumerating taskbar windows. |
| `Fullscreen` | `SHQueryUserNotificationState` plus a window-covers-monitor check. When a full-screen app is in front (and the setting is on) the dino hides and the loop sleeps in 2 s steps. |
| `TrayIcon` | Notification-area icon built from the dino's own pixels; the context menu is the entire settings UI. The menu briefly attaches to the foreground thread's input so it can take keyboard focus, then hands focus straight back to the app the user was in. |
| `StartupRegistration`, `AccentColor` | Per-user Run key; accent colour from the Explorer accent palette (not the DWM colorization tint). |

### Priorities

Each frame arbitrates in this order: Game Mode › dragging › relocating (follow the active app or
return home) › hovering (gaze) › idle personality. Higher states cancel reactions; movement
re-targets instead of queueing, so rapid app switching never backs up animations.

### Lifecycle

`Program` enforces a single instance with a named mutex (a second launch just tells the running
instance to un-hide), initialises COM apartment-threaded for UI Automation, wires the dozen objects
by hand and runs the loop. Every native resource (hooks, HWNDs, GDI icons, COM pointers, GPU
objects) is owned by exactly one `IDisposable` and released in reverse order on exit.
