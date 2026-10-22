using System.Diagnostics;

using Microsoft.Extensions.Logging;

using TaskbarDino.Animation;
using TaskbarDino.App.Content;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Hosting;
using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;
using TaskbarDino.Core.Time;
using TaskbarDino.Game;
using TaskbarDino.Game.Runner;

namespace TaskbarDino.App;

/// <summary>
/// Companion-mode orchestrator. The dino lives in a small, always-click-through window that is
/// repositioned to follow it (so the overlay never covers — and never blocks — more than a sliver of
/// the screen, and can move freely across monitors). Each frame it arbitrates between four drivers
/// in priority order: dragging, following (<see cref="MoveController"/>), hovering (gaze), and idle
/// personality (the brain). Pointer input arrives out-of-band via <see cref="IInputSource"/>
/// (a global mouse hook). The loop rests at ~0% CPU whenever nothing is moving/animating.
/// </summary>
public sealed class CompanionRuntime(
    IOverlayWindow overlay,
    IInputSource input,
    IRendererFactory rendererFactory,
    IHomePositionProvider homePositionProvider,
    IActiveWindowTracker activeWindowTracker,
    ITaskbarTracker taskbarTracker,
    IMonitorService monitors,
    IDinoBrain brain,
    ISettingsService settings,
    ITrayIcon tray,
    IStartupService startup,
    IMessageLoop loop,
    GameSession game,
    IAccentColorProvider accentColors,
    ILogger<CompanionRuntime> logger) : IDisposable
{
    private const float DebounceSeconds = 0.35f; // snappier relocation between app icons
    private const float MaxAnimationDeltaSeconds = 1f / 30f;
    private const float PositionPollInterval = 2.5f; // re-query icon position when idle (rearranged icons)
    private const int GazeDeadZone = 14;
    private const int TrayIconSize = 32;

    private readonly IOverlayWindow _overlay = overlay;
    private readonly IInputSource _input = input;
    private readonly IRendererFactory _rendererFactory = rendererFactory;
    private readonly IHomePositionProvider _homePositionProvider = homePositionProvider;
    private readonly IActiveWindowTracker _activeWindowTracker = activeWindowTracker;
    private readonly ITaskbarTracker _taskbarTracker = taskbarTracker;
    private readonly IMonitorService _monitors = monitors;
    private readonly IDinoBrain _brain = brain;
    private readonly ISettingsService _settings = settings;
    private readonly ITrayIcon _tray = tray;
    private readonly IStartupService _startup = startup;
    private readonly IMessageLoop _loop = loop;
    private readonly GameSession _game = game;
    private readonly IAccentColorProvider _accentColors = accentColors;
    private readonly ILogger<CompanionRuntime> _logger = logger;

    private IRenderer? _renderer;
    private SpriteAtlas? _atlas;
    private Bgra32[] _atlasPixels = [];
    private AnimationController? _animation;
    private MoveController? _move;

    private MonitorInfo _monitor = null!;
    private int _dinoWidth;
    private int _dinoHeight;
    private int _hitPadding;
    private int _windowMargin;
    private int _windowWidth;
    private int _windowHeight;
    private int _groundScreenY;
    private PixelRect _windowBounds;
    private Rgba _accent;

    private ActiveWindowInfo? _pendingActive;
    private float _debounceRemaining;
    private float _positionPollRemaining = PositionPollInterval;

    private Interaction _interaction = Interaction.None;
    private PixelPoint _cursor;

    // Last drawn dino rectangle (window-local), read by the hit-test on the same (UI) thread.
    private RectF _lastDestination;

    // Tight bounds of the dino's opaque pixels within the sprite frame, as fractions [0,1] (union over
    // all frames). The hit-test uses these so the clickable area hugs the visible dino, not the whole
    // frame — clicks in the transparent margin around it still reach the taskbar beneath.
    private float _opaqueX0;
    private float _opaqueX1 = 1f;
    private float _opaqueY0;
    private float _opaqueY1 = 1f;

    private DinoTheme _theme;
    private DinoSize _size;
    private float _hideRemaining;
    private bool _menuRequested;

    private Mode _mode = Mode.Companion;
    private bool _enterGameRequested;
    private bool _exitGameRequested;

    private enum Interaction
    {
        None,
        Hover,
        Drag,
    }

    private enum Mode
    {
        Companion,
        Game,
    }

    public void Run()
    {
        _activeWindowTracker.Start();
        HomePosition home = _homePositionProvider.GetHomePosition(_activeWindowTracker.Current);

        _theme = _settings.Current.Theme;
        _size = _settings.Current.Size;
        _accent = _accentColors.GetAccentColor();

        SetMonitor(home.Monitor);
        CreateSurface();

        _atlasPixels = DinoSpriteSheet.CreatePixels(_theme, _accent);
        ComputeDinoOpaqueBounds(); // theme-independent (same mask for every palette), so compute once
        TextureHandle texture = _renderer!.CreateTexture(_atlasPixels, DinoSpriteSheet.AtlasWidth, DinoSpriteSheet.AtlasHeight);
        _atlas = new SpriteAtlas(texture, DinoSpriteSheet.AtlasWidth, DinoSpriteSheet.AtlasHeight, DinoSpriteSheet.Regions);
        _animation = new AnimationController(DinoAnimations.BuildStateClips(_atlas), DinoAnimations.BuildReactionClips(_atlas), DinoState.Idle);

        _move = new MoveController(home.Monitor.DpiScale);
        _move.Teleport(home.Anchor.X);

        _activeWindowTracker.ForegroundChanged += OnForegroundChanged;
        _overlay.CloseRequested += OnCloseRequested;
        _overlay.DpiChanged += OnDpiChanged;
        _overlay.AccentColorChanged += OnAccentColorChanged;
        _game.Exited += OnGameExited;
        SubscribeInput();

        _settings.Update(static s => s); // ensure the settings file exists on disk
        InstallTray();
        SyncStartup();

        _input.SetHitTest(HitTest);
        _input.Start();
        _overlay.Show();
        _logger.LogInformation("Companion running: following '{App}'.", _activeWindowTracker.Current?.ProcessName ?? "<desktop>");

        _loop.Run(Tick);
    }

    public void Dispose()
    {
        _activeWindowTracker.ForegroundChanged -= OnForegroundChanged;
        _overlay.CloseRequested -= OnCloseRequested;
        _overlay.DpiChanged -= OnDpiChanged;
        _overlay.AccentColorChanged -= OnAccentColorChanged;
        _game.Exited -= OnGameExited;
        _renderer?.Dispose();
    }

    private FrameOutcome Tick(FrameTime time)
    {
        if (_renderer is null || _atlas is null || _animation is null || _move is null)
        {
            return FrameOutcome.Idle;
        }

        // Mode switches are deferred out of the input hooks to here (the loop thread) so heavy work
        // like resizing the render surface never runs inside a hook callback.
        if (_enterGameRequested)
        {
            _enterGameRequested = false;
            EnterGame();
        }

        if (_exitGameRequested)
        {
            _exitGameRequested = false;
            ExitGame();
        }

        if (_mode == Mode.Game)
        {
            return _game.Tick(time);
        }

        // Temporarily hidden ("Hide for N hours"): stay dark and asleep until the timer elapses.
        if (_hideRemaining > 0f)
        {
            _hideRemaining -= time.DeltaSeconds;
            if (_hideRemaining > 0f)
            {
                return FrameOutcome.WakeIn(TimeSpan.FromSeconds(_hideRemaining));
            }

            _hideRemaining = 0f;
            _overlay.Show();
            _logger.LogInformation("Hidden period elapsed; dino restored.");
        }

        // Show the context menu here (on the loop thread), never from inside the mouse hook.
        if (_menuRequested)
        {
            _menuRequested = false;
            _tray.ShowMenu();
        }

        float animationDelta = MathF.Min(time.DeltaSeconds, MaxAnimationDeltaSeconds);
        CommitPendingFollow(time.DeltaSeconds);
        _move.Update(animationDelta);

        FrameOutcome outcome;
        AtlasRegion region;
        bool flip;
        PixelRect windowBounds;
        RectF destination;

        if (_interaction == Interaction.Drag)
        {
            region = _atlas["jump"]; // tucked "held" pose
            flip = false;
            (windowBounds, destination) = DragPlacement();
            outcome = FrameOutcome.Continuous;
        }
        else if (_move.IsBusy)
        {
            _animation.SetState(_move.State);
            _animation.Update(animationDelta);
            region = _animation.CurrentRegion;
            flip = _move.Facing == Facing.Left;
            (windowBounds, destination) = GroundedPlacement();
            outcome = FrameOutcome.Continuous;
        }
        else if (_interaction == Interaction.Hover)
        {
            region = GazeRegion();
            flip = false;
            (windowBounds, destination) = GroundedPlacement();
            outcome = FrameOutcome.Continuous;
        }
        else
        {
            _brain.Update(time);
            _animation.SetState(_brain.State);
            while (_brain.TryDequeueReaction(out DinoReaction reaction))
            {
                _animation.Trigger(reaction);
            }

            _animation.Update(animationDelta);
            region = _animation.CurrentRegion;
            flip = _brain.Facing == Facing.Left;
            (windowBounds, destination) = GroundedPlacement();

            // Periodic quiet re-home so rearranged taskbar icons are followed without requiring an
            // app switch.  Only runs when the dino is fully settled (no debounce, not moving).
            if (_debounceRemaining <= 0f)
            {
                _positionPollRemaining -= time.DeltaSeconds;
                if (_positionPollRemaining <= 0f)
                {
                    _positionPollRemaining = PositionPollInterval;
                    RefreshHomeIfMoved();
                }
            }

            outcome = _debounceRemaining > 0f
                ? FrameOutcome.WakeIn(TimeSpan.FromSeconds(_debounceRemaining))
                : _move!.IsBusy || _animation.IsAnimating ? FrameOutcome.Continuous
                : FrameOutcome.WakeIn(_brain.TimeUntilNextEvent);
        }

        if (windowBounds != _windowBounds)
        {
            _windowBounds = windowBounds;
            _overlay.SetBounds(windowBounds);
        }

        Draw(destination, region, flip);
        return outcome;
    }

    /// <summary>
    /// Window rect (screen) + dino rect (window-local) when grounded. The window tightly wraps the
    /// dino and rides UP with the jump, so there is no tall empty region above the dino to block clicks.
    /// </summary>
    private (PixelRect Window, RectF Dino) GroundedPlacement()
    {
        int feetX = (int)MathF.Round(_move!.FeetX);
        int jump = (int)MathF.Round(_move.JumpHeight);
        int windowTop = _groundScreenY - jump - _dinoHeight - _windowMargin;
        var window = new PixelRect(feetX - (_windowWidth / 2), windowTop, _windowWidth, _windowHeight);
        var dino = new RectF(_windowMargin, _windowMargin, _dinoWidth, _dinoHeight);
        return (window, dino);
    }

    /// <summary>Window rect (screen) + dino rect (window-local) while being dragged (centred on the cursor).</summary>
    private (PixelRect Window, RectF Dino) DragPlacement()
    {
        var window = new PixelRect(_cursor.X - (_windowWidth / 2), _cursor.Y - (_windowHeight / 2), _windowWidth, _windowHeight);
        var dino = new RectF(_windowMargin, _windowMargin, _dinoWidth, _dinoHeight);
        return (window, dino);
    }

    private AtlasRegion GazeRegion()
    {
        int dx = _cursor.X - (int)_move!.FeetX;
        string frame = dx < -GazeDeadZone ? "look_left" : dx > GazeDeadZone ? "look_right" : "idle";
        return _atlas![frame];
    }

    private void Draw(RectF destination, AtlasRegion region, bool flip)
    {
        _lastDestination = destination;

        _renderer!.BeginFrame();
        _renderer.DrawSprite(_atlas!.Texture, region, destination, Rgba.White, flip);
        _renderer.EndFrame();
    }

    /// <summary>The window-local rectangle the hit-test treats as "on the dino" (drawn for debugging).</summary>
    private RectF HitBox()
    {
        RectF d = _lastDestination;
        float left = d.X + (_opaqueX0 * d.Width) - _hitPadding;
        float right = d.X + (_opaqueX1 * d.Width) + _hitPadding;
        float top = d.Y + (_opaqueY0 * d.Height) - _hitPadding;
        float bottom = d.Y + (_opaqueY1 * d.Height) + _hitPadding;
        return new RectF(left, top, right - left, bottom - top);
    }

    // The clickable area is a solid box hugging the dino's opaque pixels (so the transparent gaps
    // between legs / around the head still register) plus a tiny margin — but not the whole sprite
    // frame, so clicks in the space around the dino pass through to the taskbar beneath.
    private bool HitTest(PixelPoint screen)
    {
        if (_mode == Mode.Game)
        {
            return false; // the stage is driven by the keyboard; let the mouse pass through
        }

        RectF box = HitBox();
        float lx = screen.X - _windowBounds.X;
        float ly = screen.Y - _windowBounds.Y;
        return lx >= box.Left && lx <= box.Right && ly >= box.Top && ly <= box.Bottom;
    }

    /// <summary>Finds the tight bounds of the dino's opaque pixels (union over all frames), as [0,1] fractions.</summary>
    private void ComputeDinoOpaqueBounds()
    {
        int fw = DinoSpriteSheet.FrameWidth;
        int fh = DinoSpriteSheet.FrameHeight;
        int minX = fw, minY = fh, maxX = 0, maxY = 0;

        for (int f = 0; f < DinoSpriteSheet.FrameCount; f++)
        {
            int offsetX = f * fw;
            for (int y = 0; y < fh; y++)
            {
                for (int x = 0; x < fw; x++)
                {
                    if (_atlasPixels[(y * DinoSpriteSheet.AtlasWidth) + offsetX + x].A != 0)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x + 1);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y + 1);
                    }
                }
            }
        }

        if (maxX <= minX || maxY <= minY)
        {
            (minX, minY, maxX, maxY) = (0, 0, fw, fh); // fallback: whole frame
        }

        _opaqueX0 = (float)minX / fw;
        _opaqueX1 = (float)maxX / fw;
        _opaqueY0 = (float)minY / fh;
        _opaqueY1 = (float)maxY / fh;
    }

    private void SubscribeInput()
    {
        _input.PointerHover += (_, p) =>
        {
            if (_interaction != Interaction.Drag)
            {
                _interaction = Interaction.Hover;
                _cursor = p;
                _loop.Wake();
            }
        };
        _input.PointerLeft += (_, _) =>
        {
            if (_interaction == Interaction.Hover)
            {
                _interaction = Interaction.None;
                _loop.Wake();
            }
        };
        _input.Clicked += (_, _) =>
        {
            _move?.MoveTo(_move.FeetX, jump: true); // happy hop in place
            _loop.Wake();
        };
        _input.DoubleClicked += (_, _) =>
        {
            // Double-click no longer starts the game (use the tray's "Start Game"); just a happy hop.
            _move?.MoveTo(_move.FeetX, jump: true);
            _loop.Wake();
        };
        _input.DragStarted += (_, p) =>
        {
            _interaction = Interaction.Drag;
            _cursor = p;
            _loop.Wake();
        };
        _input.DragMoved += (_, p) =>
        {
            _cursor = p;
            _loop.Wake();
        };
        _input.DragReleased += (_, p) =>
        {
            _interaction = Interaction.None;
            // Cancel any pending follow that built up during the drag so it doesn't yank the dino
            // away from the drop spot.  The next real ForegroundChanged will re-home it properly.
            _debounceRemaining = 0f;

            // Land on whichever monitor it was dropped on, at THAT monitor's ground — the taskbar top,
            // or the screen's bottom edge on a display with no taskbar. It stays there until the next
            // app switch relocates it (with a hop). This is why dropping on a taskbar-less screen no
            // longer leaves the dino floating at a phantom taskbar height.
            MonitorInfo dropMonitor = _monitors.FromPoint(p);
            if (dropMonitor.Handle != _monitor.Handle && MathF.Abs(dropMonitor.DpiScale - _monitor.DpiScale) > 0.001f)
            {
                _move = new MoveController(dropMonitor.DpiScale);
            }

            SetMonitor(dropMonitor);
            _move!.Teleport(p.X);
            _windowBounds = default; // force the overlay to reposition at the drop spot
            _loop.Wake();
        };
    }

    private void CommitPendingFollow(float deltaSeconds)
    {
        if (_debounceRemaining <= 0f)
        {
            return;
        }

        _debounceRemaining -= deltaSeconds;
        if (_debounceRemaining > 0f)
        {
            return;
        }

        _debounceRemaining = 0f;

        if (_interaction == Interaction.Drag)
        {
            return; // don't yank the dino away mid-drag
        }

        HomePosition home = _homePositionProvider.GetHomePosition(_pendingActive);
        float previousDpi = _monitor.DpiScale;
        bool monitorChanged = home.Monitor.Handle != _monitor.Handle;

        SetMonitor(home.Monitor);

        if (monitorChanged)
        {
            if (MathF.Abs(home.Monitor.DpiScale - previousDpi) > 0.001f)
            {
                _move = new MoveController(home.Monitor.DpiScale);
            }

            bool enterFromLeft = home.Anchor.X >= home.Monitor.Bounds.Center.X;
            float entryX = enterFromLeft ? home.Monitor.Bounds.Left + (_dinoWidth / 2f) : home.Monitor.Bounds.Right - (_dinoWidth / 2f);
            _move!.Teleport(entryX);
        }

        // Always hop to the new spot — it reads as "jumping across to the app icon" and, combined with
        // the shorter debounce and faster movement, keeps relocation snappy.
        _move!.MoveTo(home.Anchor.X, jump: true);
    }

    /// <summary>
    /// Periodically re-queries the active app's taskbar-button position and quietly re-homes if it
    /// has moved (e.g. the user rearranged icons).  Runs only when the dino is idle and settled.
    /// </summary>
    private void RefreshHomeIfMoved()
    {
        if (_interaction == Interaction.Drag || _mode == Mode.Game || _move is null) return;

        HomePosition home = _homePositionProvider.GetHomePosition(_activeWindowTracker.Current);
        float drift = MathF.Abs(home.Anchor.X - _move.FeetX);
        if (drift <= 6f) return; // sub-pixel jitter — ignore

        _logger.LogDebug("Icon drifted {Drift:F0}px; re-homing.", drift);
        if (home.Monitor.Handle != _monitor.Handle)
        {
            if (MathF.Abs(home.Monitor.DpiScale - _monitor.DpiScale) > 0.001f)
                _move = new MoveController(home.Monitor.DpiScale);
            SetMonitor(home.Monitor);
        }

        _move.MoveTo(home.Anchor.X, jump: false);
    }

    /// <summary>Sizes the dino + its window for a monitor and sets the ground line to that monitor's taskbar.</summary>
    private void SetMonitor(MonitorInfo monitor)
    {
        _monitor = monitor;
        float dpi = monitor.DpiScale;
        TaskbarInfo taskbar = _taskbarTracker.GetTaskbar(monitor);

        // Sprite magnification = the chosen size × the monitor's DPI scale, so a size looks the same
        // physical size on every display.
        float spriteScale = _size.ScaleFactor() * dpi;
        _dinoWidth = (int)MathF.Round(DinoSpriteSheet.FrameWidth * spriteScale);
        _dinoHeight = (int)MathF.Round(DinoSpriteSheet.FrameHeight * spriteScale);

        // A small DPI-scaled forgiveness margin around the dino's opaque bounds — just enough that
        // poking the edge registers, without the clickable area spilling into the space around it.
        _hitPadding = (int)MathF.Round(2f * dpi);

        // The overlay hugs the dino (a small margin all round) instead of being wide with tall
        // headroom: the window follows the dino UP during a jump rather than reserving empty space
        // above it, so it never covers — and blocks clicks to — the taskbar around the dino.
        _windowMargin = (int)MathF.Round(8f * dpi);
        int newWidth = _dinoWidth + (2 * _windowMargin);
        int newHeight = _dinoHeight + (2 * _windowMargin);

        // Stand on the taskbar where there is one; on a screen with no taskbar, stand on its bottom edge.
        _groundScreenY = !taskbar.IsPresent ? monitor.Bounds.Bottom
            : taskbar.Edge == TaskbarEdge.Bottom ? taskbar.Bounds.Top
            : monitor.WorkArea.Bottom;

        if ((newWidth != _windowWidth || newHeight != _windowHeight) && _renderer is not null)
        {
            _renderer.Resize(newWidth, newHeight, dpi);
        }

        _windowWidth = newWidth;
        _windowHeight = newHeight;
    }

    private void CreateSurface() => _renderer = _rendererFactory.Create(_overlay.Handle, _windowWidth, _windowHeight, _monitor.DpiScale);

    private void InstallTray()
    {
        _tray.CommandInvoked += OnTrayCommand;
        _tray.ThemeSelected += (_, theme) => ApplyTheme(theme);
        _tray.SizeSelected += (_, size) => ApplySize(size);

        // The right-click arrives inside the low-level mouse hook; showing the (modal) menu there
        // would stall the hook and hang input. Defer it to the loop thread via a flag.
        _input.RightClicked += (_, _) =>
        {
            _menuRequested = true;
            _loop.Wake();
        };

        _tray.SetActiveTheme(_theme);
        _tray.SetActiveSize(_size);
        _tray.SetStartupChecked(_settings.Current.StartWithWindows);
        _tray.Install(DinoSpriteSheet.CreateIconPixels(_theme, TrayIconSize, _accent), TrayIconSize, TrayIconSize, "Taskbar Dino");
    }

    private void SyncStartup()
    {
        if (_settings.Current.StartWithWindows != _startup.IsEnabled)
        {
            _startup.SetEnabled(_settings.Current.StartWithWindows);
        }
    }

    private void OnTrayCommand(object? sender, TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.StartGame:
                RequestGame();
                break;
            case TrayCommand.HideOneHour:
                HideFor(TimeSpan.FromHours(1));
                break;
            case TrayCommand.HideFourHours:
                HideFor(TimeSpan.FromHours(4));
                break;
            case TrayCommand.ToggleStartWithWindows:
                ToggleStartup();
                break;
            case TrayCommand.OpenSettings:
                OpenSettings();
                break;
            case TrayCommand.Exit:
                _loop.Quit();
                break;
        }

        _loop.Wake();
    }

    private void ApplyTheme(DinoTheme theme)
    {
        _theme = theme;
        _accent = _accentColors.GetAccentColor(); // refresh in case the OS accent changed
        _atlasPixels = DinoSpriteSheet.CreatePixels(theme, _accent);
        _renderer?.UpdateTexture(_atlas!.Texture, _atlasPixels, DinoSpriteSheet.AtlasWidth, DinoSpriteSheet.AtlasHeight);
        _tray.UpdateIcon(DinoSpriteSheet.CreateIconPixels(theme, TrayIconSize, _accent), TrayIconSize, TrayIconSize);
        _tray.SetActiveTheme(theme);
        _settings.Update(s => s with { Theme = theme });
        _logger.LogInformation("Theme changed to {Theme}.", theme);
        _loop.Wake();
    }

    private void OnAccentColorChanged(object? sender, EventArgs e) => RefreshAccent();

    /// <summary>Re-rasterises the dino when the OS accent colour changes (only while the Accent theme is active).</summary>
    private void RefreshAccent()
    {
        if (_theme != DinoTheme.Accent || _atlas is null)
        {
            return;
        }

        Rgba updated = _accentColors.GetAccentColor();
        if (updated == _accent)
        {
            return;
        }

        _accent = updated;
        _atlasPixels = DinoSpriteSheet.CreatePixels(_theme, _accent);
        _renderer?.UpdateTexture(_atlas.Texture, _atlasPixels, DinoSpriteSheet.AtlasWidth, DinoSpriteSheet.AtlasHeight);
        _tray.UpdateIcon(DinoSpriteSheet.CreateIconPixels(_theme, TrayIconSize, _accent), TrayIconSize, TrayIconSize);
        _logger.LogDebug("Accent colour updated to match Windows.");
        _loop.Wake();
    }

    private void ApplySize(DinoSize size)
    {
        if (size == _size)
        {
            return;
        }

        _size = size;
        _tray.SetActiveSize(size);
        _settings.Update(s => s with { Size = size });
        _logger.LogInformation("Dino size changed to {Size}.", size);

        // In companion mode, apply immediately (recompute dimensions + resize the surface). In game
        // mode the new size takes effect on exit, so we don't disturb the running stage.
        if (_mode == Mode.Companion)
        {
            SetMonitor(_monitor);
            _windowBounds = default; // force the overlay to reposition at the new size
        }

        _loop.Wake();
    }

    private void RequestGame()
    {
        if (_mode == Mode.Companion)
        {
            _enterGameRequested = true;
            _loop.Wake();
        }
    }

    private void OnGameExited(object? sender, EventArgs e)
    {
        _exitGameRequested = true;
        _loop.Wake();
    }

    /// <summary>
    /// Turns the small companion overlay into a transparent, full-width play strip along the taskbar.
    /// The dino keeps its current icon position; obstacles scroll in from the right screen edge.
    /// </summary>
    private void EnterGame()
    {
        if (_mode == Mode.Game)
        {
            return;
        }

        float scale = _size.ScaleFactor() * _monitor.DpiScale;
        int stageHeight = Math.Max(1, (int)MathF.Round(GameConfig.Height * scale));
        int stageWidth = _monitor.Bounds.Width;
        var bounds = new PixelRect(_monitor.Bounds.X, _groundScreenY - stageHeight, stageWidth, stageHeight);

        _hideRemaining = 0f;
        _renderer!.Resize(stageWidth, stageHeight, _monitor.DpiScale);
        _windowBounds = bounds;
        _overlay.SetBounds(bounds);
        _overlay.Show();

        // The dino stays where it is on the taskbar (its feet X, made local to the stage).
        float dinoCenterLogical = (_move!.FeetX - _monitor.Bounds.X) / scale;
        float fieldWidthLogical = stageWidth / scale;

        _mode = Mode.Game;
        _game.Enter(_renderer, _atlas!, dinoCenterLogical, fieldWidthLogical, scale);
        _logger.LogInformation("Entered game mode ({Width}x{Height}).", stageWidth, stageHeight);
    }

    /// <summary>Returns from the game stage to the companion, restoring the small following overlay.</summary>
    private void ExitGame()
    {
        if (_mode != Mode.Game)
        {
            return;
        }

        _game.Leave();
        _mode = Mode.Companion;

        // Force SetMonitor to resize the surface back down from the wide stage, and the next frame to
        // reposition the small overlay.
        _windowWidth = 0;
        _windowHeight = 0;
        SetMonitor(_monitor);
        _windowBounds = default;
        _logger.LogInformation("Left game mode.");
    }

    private void HideFor(TimeSpan duration)
    {
        _hideRemaining = (float)duration.TotalSeconds;
        _overlay.Hide();
        _logger.LogInformation("Hiding the dino for {Hours}h.", duration.TotalHours);
    }

    private void ToggleStartup()
    {
        bool enabled = !_settings.Current.StartWithWindows;
        _startup.SetEnabled(enabled);
        _settings.Update(s => s with { StartWithWindows = enabled });
        _tray.SetStartupChecked(enabled);
        _logger.LogInformation("Start with Windows: {Enabled}.", enabled);
    }

    private void OpenSettings()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo(_settings.Location) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the settings file.");
        }
    }

    private void OnForegroundChanged(object? sender, ActiveWindowInfo? active)
    {
        _pendingActive = active;
        _debounceRemaining = DebounceSeconds;
        _loop.Wake();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _logger.LogInformation("Close requested; shutting down.");
        _loop.Quit();
    }

    private void OnDpiChanged(object? sender, uint dpi) => _logger.LogInformation("Overlay DPI changed to {Dpi}.", dpi);
}
