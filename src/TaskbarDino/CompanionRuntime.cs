using System.Diagnostics;

using TaskbarDino.Core.Animation;
using TaskbarDino.Core.Behavior;
using TaskbarDino.Core.Content;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Game;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;
using TaskbarDino.Core.Settings;
using TaskbarDino.Core.Time;
using TaskbarDino.Rendering;
using TaskbarDino.Win32;

namespace TaskbarDino;

/// <summary>
/// The companion orchestrator. Each frame it arbitrates, in priority order: Game Mode, dragging,
/// relocating (follow the active app / return home), hovering (gaze) and idle personality — then
/// draws <em>only if the picture changed</em> and tells the loop exactly how long it may sleep.
/// Input arrives out-of-band from the hooks and tracker (on this thread, while the loop pumps);
/// handlers only record state and wake the loop, so nothing heavy ever runs inside a hook.
/// </summary>
internal sealed class CompanionRuntime : IDisposable
{
    private const float DebounceSeconds = 0.35f;        // collapses rapid app switches
    private const float MaxAnimationDelta = 1f / 30f;   // animation never jumps more than this per frame
    private const float IdlePollSeconds = 6f;           // re-check the icon position / full-screen state when settled
    private const float FullscreenPollSeconds = 2f;     // while hidden behind a full-screen app
    private const float ReturnHomeDelaySeconds = 1.4f;  // dropped dino looks around, then heads home
    private const float TaskbarSettleSeconds = 1.5f;    // Explorer needs a moment after restarting
    private const int GazeDeadZone = 14;                // sprite px × scale: cursor straight above = look ahead
    private const int TrayIconSize = 32;
    private const int MarginSprite = 16;                // sprite px of air around the dino for effects

    private readonly OverlayWindow _overlay;
    private readonly MouseHook _mouse;
    private readonly KeyboardHook _keyboard;
    private readonly ForegroundTracker _foreground;
    private readonly TrayIcon _tray;
    private readonly SettingsStore _settings;
    private readonly MessageLoop _loop;
    private readonly HomeResolver _home;
    private readonly GameSession _game;
    private readonly DinoBrain _brain;
    private readonly Debouncer<ActiveWindow?> _follow = new(DebounceSeconds);

    private Renderer? _renderer;
    private Texture? _dinoTexture;
    private Texture? _artTexture;
    private SpriteSheet _dinoSheet = null!;
    private SpriteSheet? _artSheet;
    private AnimationController _animation = null!;
    private MoveController _move = null!;
    private PixelRect[] _opaque = [];       // per-frame tight bounds (sprite px), indexed by frame cell X

    private MonitorInfo _monitor = null!;
    private float _spriteScale = 1f;
    private int _dinoWidth, _dinoHeight, _margin, _hitPadding, _groundY;
    private int _windowWidth, _windowHeight;

    private Mode _mode = Mode.Companion;
    private bool _dragging;
    private bool _hovering;
    private PixelPoint _cursor;
    private Facing _gaze = Facing.Right;
    private bool _gazeAhead = true;
    private Rgb _accent;

    private HomePosition? _nextLeg;         // cross-monitor move: where to continue after reaching the edge
    private float _returnHomeIn = -1f;      // > 0: countdown after a drop
    private float _idlePoll = IdlePollSeconds;
    private float _rehomeIn = -1f;          // > 0: countdown to a forced re-home (Explorer restart, resume)

    // Pending requests from hooks/tray, applied on the loop thread.
    private bool _menuRequested, _enterGame, _exitGame, _layoutDirty;

    // Last drawn picture, to skip redundant frames.
    private PixelRect _drawnWindow;
    private AnimationFrame _drawnFrame;
    private bool _drawnFlip;
    private bool _drawnValid;
    private TimeSpan _lastDrawAt;

    private enum Mode
    {
        Companion,
        Game,
        Hidden,      // "Hide for N hours"
        Suspended,   // a full-screen app is in front
    }

    public CompanionRuntime(OverlayWindow overlay, MouseHook mouse, KeyboardHook keyboard, ForegroundTracker foreground, TrayIcon tray, SettingsStore settings, MessageLoop loop, HomeResolver home)
    {
        _overlay = overlay;
        _mouse = mouse;
        _keyboard = keyboard;
        _foreground = foreground;
        _tray = tray;
        _settings = settings;
        _loop = loop;
        _home = home;
        _brain = new DinoBrain(new Random(), settings.Current.Activity);
        _game = new GameSession(new Random());
    }

    public void Run()
    {
        _accent = AccentColor.Read();
        _dinoSheet = DinoSpriteSheet.Create(_settings.Current.Theme, _accent);
        ComputeOpaqueBounds();

        _foreground.Start();
        HomePosition home = _home.Resolve(_foreground.Current);
        SetMonitor(home.Monitor);
        CreateRenderer();

        _animation = new AnimationController(DinoAnimations.BuildStateClips(_dinoSheet), DinoAnimations.BuildReactionClips(_dinoSheet), DinoState.Idle);
        _move = new MoveController(home.Monitor.DpiScale);
        _move.Teleport(home.Anchor.X);

        _foreground.Changed += OnForegroundChanged;
        _overlay.CloseRequested += () => _loop.Quit();
        _overlay.LayoutChanged += () => { _layoutDirty = true; _loop.Wake(); };
        _overlay.TaskbarCreated += OnTaskbarCreated;
        _overlay.AccentColorChanged += () => { RefreshAccent(); _loop.Wake(); };
        _overlay.Resumed += OnResumed;
        _overlay.ShowRequested += () => { if (_mode == Mode.Hidden) ShowNow(); _loop.Wake(); };
        SubscribeInput();

        _tray.StateProvider = () => new TrayMenuState(_settings.Current, _mode == Mode.Hidden, _mode == Mode.Game);
        _tray.Command += OnTrayCommand;
        _tray.Install(DinoSpriteSheet.CreateIconPixels(_settings.Current.Theme, TrayIconSize, _accent), TrayIconSize, "Taskbar Dino");
        if (_settings.Current.StartWithWindows != StartupRegistration.IsEnabled)
        {
            StartupRegistration.SetEnabled(_settings.Current.StartWithWindows);
        }

        _mouse.HitTest = HitTest;
        _mouse.Start();

        if (_settings.Current.IsHiddenAt(DateTimeOffset.UtcNow))
        {
            _mode = Mode.Hidden;
            Log.Info($"Starting hidden until {_settings.Current.HiddenUntil:O}.");
        }
        else
        {
            _overlay.Show();
        }

        Log.Info($"Companion running; following '{_foreground.Current?.ProcessName ?? "<desktop>"}' at {home.Anchor} (precise: {home.Precise}).");
        _loop.Run(Tick);
    }

    public void Dispose()
    {
        _keyboard.Disable();
        _dinoTexture?.Dispose();
        _artTexture?.Dispose();
        _renderer?.Dispose();
    }

    // ------------------------------------------------------------------ frame

    private FrameOutcome Tick(FrameTime time)
    {
        if (_renderer is null)
        {
            return FrameOutcome.Idle;
        }

        if (_renderer.DeviceLost)
        {
            RecreateRenderer();
        }

        if (_layoutDirty)
        {
            _layoutDirty = false;
            OnLayoutChanged();
        }

        if (_menuRequested)
        {
            _menuRequested = false;
            _tray.ShowMenu();
        }

        if (_enterGame)
        {
            _enterGame = false;
            EnterGame();
        }

        if (_exitGame)
        {
            _exitGame = false;
            ExitGame();
        }

        switch (_mode)
        {
            case Mode.Game:
                return _game.Tick(time);

            case Mode.Hidden:
                return TickHidden();

            case Mode.Suspended:
                return TickSuspended();

            default:
                return TickCompanion(time);
        }
    }

    private FrameOutcome TickHidden()
    {
        TimeSpan remaining = _settings.Current.HiddenRemaining(DateTimeOffset.UtcNow);
        if (remaining > TimeSpan.Zero)
        {
            return FrameOutcome.WakeIn(remaining < TimeSpan.FromMinutes(1) ? remaining : TimeSpan.FromMinutes(1));
        }

        Log.Info($"Hidden period elapsed; dino restored.");
        ShowNow();
        return FrameOutcome.Continuous;
    }

    private FrameOutcome TickSuspended()
    {
        if (!_settings.Current.PauseInFullscreen || !Fullscreen.IsActive(out _))
        {
            Log.Info($"Full-screen app gone; dino resumes.");
            _mode = Mode.Companion;
            _overlay.Show();
            _drawnValid = false;
            RequestHome(_foreground.Current);
            return FrameOutcome.Continuous;
        }

        return FrameOutcome.WakeIn(TimeSpan.FromSeconds(FullscreenPollSeconds));
    }

    private FrameOutcome TickCompanion(FrameTime time)
    {
        float dt = time.DeltaSeconds;
        float animDt = MathF.Min(dt, MaxAnimationDelta);
        FrameOutcome outcome = FrameOutcome.Idle;

        // Scheduled re-home (Explorer restart / resume) and the drop → return-home countdown.
        if (Countdown(ref _rehomeIn, dt))
        {
            RequestHome(_foreground.Current);
        }

        if (Countdown(ref _returnHomeIn, dt))
        {
            RequestHome(_foreground.Current);
        }

        if (_follow.Update(dt, out ActiveWindow? active) && !_dragging)
        {
            CommitFollow(active);
        }

        if (!_dragging)
        {
            _move.Update(animDt);
            if (!_move.IsBusy && _nextLeg is { } leg)
            {
                ContinueLeg(leg);
            }
        }

        AnimationFrame frame;
        bool flip;
        PixelRect window;

        if (_dragging)
        {
            _animation.SetState(DinoState.Held);
            frame = _animation.CurrentFrame;
            flip = false;
            window = new PixelRect(_cursor.X - (_windowWidth / 2), _cursor.Y - _margin - (_dinoHeight / 2), _windowWidth, _windowHeight);
            // Draws happen on pointer events (the hook wakes the loop); nothing to schedule.
        }
        else if (_move.IsBusy)
        {
            _animation.SetState(_move.State);
            _animation.Update(animDt);
            frame = _animation.CurrentFrame;
            flip = _move.Facing == Facing.Left;
            window = GroundedWindow();
            outcome = FrameOutcome.Continuous;
        }
        else
        {
            _brain.Update(dt);
            _animation.SetState(_brain.State);
            while (_brain.TryDequeueReaction(out DinoReaction reaction))
            {
                _animation.Trigger(reaction);
            }

            _animation.Update(animDt);
            frame = _animation.CurrentFrame;
            flip = _hovering && !_animation.ReactionActive && _brain.State == DinoState.Idle ? _gaze == Facing.Left : _move.Facing == Facing.Left;
            if (_hovering && !_animation.ReactionActive && _brain.State == DinoState.Idle)
            {
                // Head follows the cursor: look toward it (or straight ahead when it is right above).
                frame = new AnimationFrame(_dinoSheet[_gazeAhead ? DinoSpriteSheet.Idle : DinoSpriteSheet.LookRight], float.PositiveInfinity);
                flip = _gaze == Facing.Left;
            }

            window = GroundedWindow();

            outcome = Soonest(outcome, _brain.TimeUntilNextEvent);
            outcome = Soonest(outcome, _animation.TimeUntilNextFrame);

            // Quiet periodic checks while settled: icon drift (rearranged taskbar) and full-screen apps.
            if (!_follow.Pending && _returnHomeIn <= 0f && !_hovering)
            {
                _idlePoll -= dt;
                if (_idlePoll <= 0f)
                {
                    _idlePoll = IdlePollSeconds;
                    IdlePoll();
                }

                outcome = Soonest(outcome, _idlePoll);
            }
        }

        if (_follow.Pending) outcome = Soonest(outcome, _follow.Remaining);
        if (_returnHomeIn > 0f) outcome = Soonest(outcome, _returnHomeIn);
        if (_rehomeIn > 0f) outcome = Soonest(outcome, _rehomeIn);

        return Present(time, window, frame, flip, outcome);
    }

    /// <summary>Draws if the picture changed, throttled to the frame cap; otherwise leaves the last frame on screen.</summary>
    private FrameOutcome Present(FrameTime time, PixelRect window, AnimationFrame frame, bool flip, FrameOutcome outcome)
    {
        bool changed = !_drawnValid || window != _drawnWindow || flip != _drawnFlip ||
                       frame.Region != _drawnFrame.Region || frame.Effect != _drawnFrame.Effect ||
                       frame.EffectX != _drawnFrame.EffectX || frame.EffectY != _drawnFrame.EffectY;
        if (!changed)
        {
            return outcome;
        }

        TimeSpan sinceLast = time.Total - _lastDrawAt;
        var budget = TimeSpan.FromSeconds(1.0 / MessageLoop.TargetFps);
        if (_drawnValid && sinceLast < budget)
        {
            return FrameOutcome.Min(outcome, FrameOutcome.WakeIn(budget - sinceLast)); // coalesce bursts (drag at 1 kHz)
        }

        if (window != _drawnWindow || !_drawnValid)
        {
            _overlay.SetBounds(window);
        }

        Draw(frame, flip);
        _drawnWindow = window;
        _drawnFrame = frame;
        _drawnFlip = flip;
        _drawnValid = true;
        _lastDrawAt = time.Total;
        return outcome;
    }

    private void Draw(in AnimationFrame frame, bool flip)
    {
        var dino = new RectF(_margin, _margin, _dinoWidth, _dinoHeight);
        _renderer!.Begin();
        _renderer.Draw(_dinoTexture!, frame.Region, dino, flip);
        if (frame.HasEffect)
        {
            float w = frame.Effect.Width * _spriteScale;
            float h = frame.Effect.Height * _spriteScale;
            float x = flip ? dino.Right - (frame.EffectX * _spriteScale) - w : dino.X + (frame.EffectX * _spriteScale);
            float y = dino.Y + (frame.EffectY * _spriteScale);
            _renderer.Draw(_dinoTexture!, frame.Effect, new RectF(x, y, w, h));
        }

        if (!_renderer.End())
        {
            _drawnValid = false;
        }
    }

    private PixelRect GroundedWindow()
    {
        int feetX = (int)MathF.Round(_move.FeetX);
        int jump = (int)MathF.Round(_move.JumpHeight);
        return new PixelRect(feetX - (_windowWidth / 2), _groundY - jump - _dinoHeight - _margin, _windowWidth, _windowHeight);
    }

    private static bool Countdown(ref float timer, float dt)
    {
        if (timer <= 0f)
        {
            return false;
        }

        timer -= dt;
        if (timer > 0f)
        {
            return false;
        }

        timer = -1f;
        return true;
    }

    private static FrameOutcome Soonest(FrameOutcome current, float seconds) =>
        float.IsFinite(seconds) ? FrameOutcome.Min(current, FrameOutcome.WakeIn(TimeSpan.FromSeconds(Math.Max(0f, seconds)))) : current;

    private static FrameOutcome Soonest(FrameOutcome current, TimeSpan delay) => FrameOutcome.Min(current, FrameOutcome.WakeIn(delay));

    // ------------------------------------------------------------------ hit test

    /// <summary>
    /// The clickable area hugs the drawn frame's opaque pixels (plus a tiny margin) so clicks in the
    /// transparent space around the dino reach the taskbar beneath. Called from the mouse hook.
    /// </summary>
    private bool HitTest(PixelPoint screen)
    {
        if (_mode != Mode.Companion || !_drawnValid)
        {
            return false;
        }

        PixelRect o = _opaque[_drawnFrame.Region.Source.X / DinoSpriteSheet.FrameWidth];
        float left = _drawnFlip ? DinoSpriteSheet.FrameWidth - o.Right : o.Left;
        float lx = screen.X - _drawnWindow.X - _margin;
        float ly = screen.Y - _drawnWindow.Y - _margin;
        float x0 = (left * _spriteScale) - _hitPadding;
        float x1 = ((left + o.Width) * _spriteScale) + _hitPadding;
        float y0 = (o.Top * _spriteScale) - _hitPadding;
        float y1 = (o.Bottom * _spriteScale) + _hitPadding;
        return lx >= x0 && lx <= x1 && ly >= y0 && ly <= y1;
    }

    private void ComputeOpaqueBounds()
    {
        _opaque = new PixelRect[DinoSpriteSheet.AtlasWidth / DinoSpriteSheet.FrameWidth * (DinoSpriteSheet.AtlasHeight / DinoSpriteSheet.FrameHeight + 1)];
        foreach (AtlasRegion region in _dinoSheet.Regions.Values)
        {
            if (region.Width == DinoSpriteSheet.FrameWidth && region.Height == DinoSpriteSheet.FrameHeight)
            {
                int index = (region.Source.Y / DinoSpriteSheet.FrameHeight * (DinoSpriteSheet.AtlasWidth / DinoSpriteSheet.FrameWidth)) + (region.Source.X / DinoSpriteSheet.FrameWidth);
                if (index < _opaque.Length)
                {
                    _opaque[index] = _dinoSheet.OpaqueBounds(region);
                }
            }
        }
    }

    // ------------------------------------------------------------------ input

    private void SubscribeInput()
    {
        _mouse.PointerHover += p =>
        {
            if (_dragging)
            {
                return;
            }

            int dx = p.X - (int)MathF.Round(_move.FeetX);
            int dead = (int)(GazeDeadZone * _spriteScale);
            bool ahead = Math.Abs(dx) <= dead;
            Facing gaze = dx < 0 ? Facing.Left : Facing.Right;
            if (!_hovering || ahead != _gazeAhead || gaze != _gaze)
            {
                _hovering = true;
                _gazeAhead = ahead;
                _gaze = gaze;
                _brain.Poke();
                _loop.Wake();
            }
        };
        _mouse.PointerLeft += () =>
        {
            if (_hovering)
            {
                _hovering = false;
                _loop.Wake();
            }
        };
        _mouse.Clicked += _ =>
        {
            _brain.Poke();
            _animation.Trigger(DinoReaction.Happy);
            _move.MoveTo(_move.FeetX, jump: true); // a happy hop in place
            _loop.Wake();
        };
        _mouse.DoubleClicked += _ => RequestGame();
        _mouse.RightClicked += _ =>
        {
            _menuRequested = true; // shown on the loop thread, never inside the hook
            _loop.Wake();
        };
        _mouse.DragStarted += p =>
        {
            _dragging = true;
            _hovering = false;
            _cursor = p;
            _returnHomeIn = -1f;
            _nextLeg = null;
            _follow.Cancel();
            _brain.Poke();
            _animation.CancelReaction();
            _loop.Wake();
        };
        _mouse.DragMoved += p =>
        {
            _cursor = p;
            _loop.Wake();
        };
        _mouse.DragReleased += OnDragReleased;

        _keyboard.JumpChanged += down => { _game.OnJump(down); _loop.Wake(); };
        _keyboard.DuckChanged += down => { _game.OnDuck(down); _loop.Wake(); };
        _keyboard.ExitPressed += () => { _exitGame = true; _loop.Wake(); };
    }

    /// <summary>Dropped: land on whatever monitor it is over, look around for a moment, then head home.</summary>
    private void OnDragReleased(PixelPoint p)
    {
        _dragging = false;
        MonitorInfo drop = Monitors.FromPoint(p);
        SetMonitor(drop);
        _move = new MoveController(drop.DpiScale);
        _move.Teleport(p.X);
        _move.FallFrom(MathF.Max(0f, _groundY - (p.Y + (_dinoHeight / 2f))));
        _animation.SetState(DinoState.Idle);
        _animation.Trigger(DinoReaction.Surprised);
        _returnHomeIn = ReturnHomeDelaySeconds;
        _loop.Wake();
    }

    // ------------------------------------------------------------------ following

    private void OnForegroundChanged(ActiveWindow? active)
    {
        if (_mode == Mode.Companion && _settings.Current.PauseInFullscreen && Fullscreen.IsActive(out _))
        {
            Suspend();
            return;
        }

        _brain.Poke();
        RequestHome(active);
    }

    private void RequestHome(ActiveWindow? active)
    {
        _follow.Request(active);
        _loop.Wake();
    }

    /// <summary>Resolves the home for <paramref name="active"/> and starts moving there (hopping across, running when far).</summary>
    private void CommitFollow(ActiveWindow? active)
    {
        HomePosition home = _home.Resolve(active);
        _nextLeg = null;

        if (home.Monitor.Handle != _monitor.Handle)
        {
            // Run off this monitor's edge toward the other one; ContinueLeg picks it up on the far side.
            bool toRight = home.Monitor.Bounds.Left >= _monitor.Bounds.Right || (home.Monitor.Bounds.Center.X > _monitor.Bounds.Center.X && home.Monitor.Bounds.Left > _monitor.Bounds.Left);
            float exitX = toRight ? _monitor.Bounds.Right + (_dinoWidth / 2f) : _monitor.Bounds.Left - (_dinoWidth / 2f);
            _nextLeg = home;
            _move.MoveTo(exitX);
            return;
        }

        _groundY = Monitors.Taskbar(home.Monitor).GroundY(home.Monitor);
        _move.MoveTo(home.Anchor.X, jump: true);
    }

    private void ContinueLeg(HomePosition home)
    {
        _nextLeg = null;
        bool fromLeft = _move.FeetX <= home.Monitor.Bounds.Left;
        SetMonitor(home.Monitor);
        _move = new MoveController(home.Monitor.DpiScale);
        _move.Teleport(fromLeft ? home.Monitor.Bounds.Left - (_dinoWidth / 2f) : home.Monitor.Bounds.Right + (_dinoWidth / 2f));
        _move.MoveTo(home.Anchor.X, jump: true);
    }

    /// <summary>Periodic, cheap checks while settled: did the icon move (rearranged taskbar)? Did a full-screen app appear?</summary>
    private void IdlePoll()
    {
        if (_settings.Current.PauseInFullscreen && Fullscreen.IsActive(out _))
        {
            Suspend();
            return;
        }

        HomePosition home = _home.Resolve(_foreground.Current);
        if (home.Monitor.Handle != _monitor.Handle || MathF.Abs(home.Anchor.X - _move.FeetX) > 6f || home.Anchor.Y != _groundY)
        {
            Log.Info($"Home moved to {home.Anchor}; re-homing.");
            CommitFollow(_foreground.Current);
        }
    }

    // ------------------------------------------------------------------ monitors / layout

    /// <summary>Sizes the dino and its window for a monitor and sets the ground line to that monitor's taskbar.</summary>
    private void SetMonitor(MonitorInfo monitor)
    {
        _monitor = monitor;
        TaskbarInfo taskbar = Monitors.Taskbar(monitor);
        _spriteScale = _settings.Current.Size.ScaleFactor() * monitor.DpiScale;
        _dinoWidth = (int)MathF.Round(DinoSpriteSheet.FrameWidth * _spriteScale);
        _dinoHeight = (int)MathF.Round(DinoSpriteSheet.FrameHeight * _spriteScale);
        _margin = (int)MathF.Round(MarginSprite * _spriteScale);
        _hitPadding = (int)MathF.Round(2f * monitor.DpiScale);
        _groundY = taskbar.GroundY(monitor);

        int w = _dinoWidth + (2 * _margin);
        int h = _dinoHeight + _margin; // no air below the feet: the window bottom is the taskbar top
        if (w != _windowWidth || h != _windowHeight)
        {
            _windowWidth = w;
            _windowHeight = h;
            _renderer?.Resize(w, h);
            _drawnValid = false;
        }
    }

    private void OnLayoutChanged()
    {
        Log.Info($"Display layout changed.");
        MonitorInfo refreshed = Monitors.Refresh(_monitor);
        SetMonitor(refreshed);
        if (_mode == Mode.Game)
        {
            ExitGame();
        }

        if (_mode == Mode.Companion)
        {
            _move = new MoveController(refreshed.DpiScale);
            _move.Teleport(Math.Clamp(_move.FeetX, refreshed.Bounds.Left, refreshed.Bounds.Right));
            _drawnValid = false;
            RequestHome(_foreground.Current);
        }
    }

    private void OnTaskbarCreated()
    {
        Log.Info($"Explorer restarted; re-adding the tray icon.");
        _tray.Reinstall();
        _overlay.BringToTop();
        _rehomeIn = TaskbarSettleSeconds;
        _loop.Wake();
    }

    private void OnResumed()
    {
        Log.Info($"System resumed.");
        _foreground.Refresh();
        _rehomeIn = TaskbarSettleSeconds;
        _loop.Wake();
    }

    private void Suspend()
    {
        if (_mode != Mode.Companion)
        {
            return;
        }

        Log.Info($"Full-screen app detected; dino hides.");
        _mode = Mode.Suspended;
        _hovering = false;
        _dragging = false;
        _mouse.Reset();
        _overlay.Hide();
        _loop.Wake();
    }

    // ------------------------------------------------------------------ rendering resources

    private void CreateRenderer()
    {
        _renderer = new Renderer(_overlay.Handle, _windowWidth, _windowHeight);
        _dinoTexture = _renderer.CreateTexture(_dinoSheet);
        if (_artSheet is not null)
        {
            _artTexture = _renderer.CreateTexture(_artSheet);
        }

        _drawnValid = false;
    }

    private void RecreateRenderer()
    {
        Log.Warn($"Recreating the renderer after a device loss.");
        _dinoTexture?.Dispose();
        _artTexture?.Dispose();
        _renderer?.Dispose();
        _dinoTexture = null;
        _artTexture = null;
        try
        {
            CreateRenderer();
            if (_mode == Mode.Game)
            {
                _game.Rebind(_renderer!, _dinoTexture!, _artTexture!);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "renderer recreation");
            _loop.Quit();
        }
    }

    private void RebuildDinoSheet()
    {
        _dinoSheet = DinoSpriteSheet.Create(_settings.Current.Theme, _accent);
        _renderer?.Upload(_dinoTexture!, _dinoSheet);
        _tray.UpdateIcon(DinoSpriteSheet.CreateIconPixels(_settings.Current.Theme, TrayIconSize, _accent), TrayIconSize);
        _drawnValid = false;
        _loop.Wake();
    }

    private void RefreshAccent()
    {
        Rgb updated = AccentColor.Read();
        if (updated != _accent)
        {
            _accent = updated;
            if (_settings.Current.Theme == DinoTheme.Accent)
            {
                RebuildDinoSheet();
            }
        }
    }

    // ------------------------------------------------------------------ game mode

    private void RequestGame()
    {
        if (_mode == Mode.Companion)
        {
            _enterGame = true;
            _loop.Wake();
        }
    }

    /// <summary>Turns the small companion overlay into a transparent, full-width play strip along the taskbar.</summary>
    private void EnterGame()
    {
        if (_mode != Mode.Companion || _dragging)
        {
            return;
        }

        _artSheet ??= GameSpriteSheet.Create();
        _artTexture ??= _renderer!.CreateTexture(_artSheet);

        _nextLeg = null;
        _returnHomeIn = -1f;
        _follow.Cancel();
        _move.Teleport(Math.Clamp(_move.FeetX, _monitor.Bounds.Left + _dinoWidth, _monitor.Bounds.Right - _dinoWidth));
        _hovering = false;
        _mouse.Reset();

        int stripHeight = Math.Max(1, (int)MathF.Round(GameConfig.Height * _spriteScale));
        var bounds = new PixelRect(_monitor.Bounds.X, _groundY - stripHeight, _monitor.Bounds.Width, stripHeight);
        _renderer!.Resize(bounds.Width, bounds.Height);
        _overlay.SetBounds(bounds);
        _overlay.Show();

        float dinoCenter = (_move.FeetX - _monitor.Bounds.X) / _spriteScale;
        _game.Enter(_renderer, _dinoTexture!, _dinoSheet, _artTexture, _artSheet, dinoCenter, bounds.Width / _spriteScale, _spriteScale, _settings.Current.HighScore);
        _keyboard.Enable();
        _mode = Mode.Game;
        Log.Info($"Game Mode entered ({bounds.Width}x{bounds.Height}).");
    }

    private void ExitGame()
    {
        if (_mode != Mode.Game)
        {
            return;
        }

        _keyboard.Disable();
        if (_game.HighScore > _settings.Current.HighScore)
        {
            _settings.Update(s => s with { HighScore = _game.HighScore });
        }

        _mode = Mode.Companion;
        _windowWidth = _windowHeight = 0; // force SetMonitor to resize the surface back down
        SetMonitor(_monitor);
        _animation.SetState(DinoState.Idle);
        _animation.Trigger(DinoReaction.Happy);
        _drawnValid = false;
        _brain.Poke();
        Log.Info($"Game Mode left (best {_game.HighScore}).");
    }

    // ------------------------------------------------------------------ tray / settings

    private void OnTrayCommand(TrayCommand command, int value)
    {
        switch (command)
        {
            case TrayCommand.StartGame:
                if (_mode == Mode.Game) _exitGame = true; else RequestGame();
                break;
            case TrayCommand.HideOneHour:
                HideFor(TimeSpan.FromHours(1));
                break;
            case TrayCommand.HideFourHours:
                HideFor(TimeSpan.FromHours(4));
                break;
            case TrayCommand.ShowNow:
                ShowNow();
                break;
            case TrayCommand.Theme:
                _settings.Update(s => s with { Theme = (DinoTheme)value });
                RebuildDinoSheet();
                break;
            case TrayCommand.Size:
                _settings.Update(s => s with { Size = (DinoSize)value });
                if (_mode == Mode.Game) ExitGame();
                SetMonitor(_monitor);
                _drawnValid = false;
                break;
            case TrayCommand.Activity:
                _settings.Update(s => s with { Activity = (ActivityLevel)value });
                _brain.SetActivity((ActivityLevel)value);
                break;
            case TrayCommand.TogglePauseInFullscreen:
                _settings.Update(s => s with { PauseInFullscreen = !s.PauseInFullscreen });
                break;
            case TrayCommand.ToggleStartWithWindows:
                bool enabled = !_settings.Current.StartWithWindows;
                StartupRegistration.SetEnabled(enabled);
                _settings.Update(s => s with { StartWithWindows = enabled });
                break;
            case TrayCommand.ResetHighScore:
                _settings.Update(s => s with { HighScore = 0 });
                break;
            case TrayCommand.OpenSettingsFile:
                OpenSettingsFile();
                break;
            case TrayCommand.ResetSettings:
                ResetSettings();
                break;
            case TrayCommand.Exit:
                _loop.Quit();
                break;
        }

        _loop.Wake();
    }

    private void HideFor(TimeSpan duration)
    {
        if (_mode == Mode.Game)
        {
            ExitGame();
        }

        _settings.Update(s => s with { HiddenUntil = DateTimeOffset.UtcNow + duration });
        _mode = Mode.Hidden;
        _hovering = false;
        _dragging = false;
        _mouse.Reset();
        _overlay.Hide();
        Log.Info($"Hiding the dino for {duration.TotalHours}h.");
    }

    private void ShowNow()
    {
        if (_settings.Current.HiddenUntil is not null)
        {
            _settings.Update(s => s with { HiddenUntil = null });
        }

        if (_mode == Mode.Hidden)
        {
            _mode = Mode.Companion;
            _overlay.Show();
            _drawnValid = false;
            _brain.Poke();
            RequestHome(_foreground.Current);
        }
    }

    private void ResetSettings()
    {
        bool wasStartup = _settings.Current.StartWithWindows;
        _settings.Reset();
        if (wasStartup)
        {
            StartupRegistration.SetEnabled(false);
        }

        _brain.SetActivity(_settings.Current.Activity);
        RebuildDinoSheet();
        if (_mode == Mode.Hidden)
        {
            ShowNow();
        }

        SetMonitor(_monitor);
        _drawnValid = false;
    }

    private void OpenSettingsFile()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo(_settings.Location) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open the settings file: {ex.Message}");
        }
    }
}
