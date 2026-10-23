using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Game;

/// <summary>
/// The taskbar runner: the dino holds its spot on the active app's icon and hops or ducks to clear
/// cacti and pterodactyls that scroll in from the right, over a transparent stage with a thin
/// scrolling ground line and drifting clouds. Speed ramps up; a crash freezes the scene in a
/// game-over state until the player restarts (Space/Enter) or exits (Esc). Score is the number of
/// obstacles dodged; the best is kept. Pure logic on a fixed 60 Hz step, deterministic under a
/// seeded <see cref="Random"/>.
/// </summary>
public sealed class DinoGame
{
    private const float MaxDeltaSeconds = 0.05f; // clamp to avoid a spiral of death after a stall
    private const float CollisionInset = 0.16f;  // shrink both boxes for forgiving hit detection

    private readonly Random _rng;
    private readonly List<Obstacle> _obstacles = [];
    private readonly List<Cloud> _clouds = [];

    private float _accumulator;
    private float _fieldWidth = 400f;
    private float _speed;
    private float _crashTimer;
    private float _graceTimer;
    private float _nextObstacleGap;
    private float _nextCloudGap;
    private long _stepCount;

    public DinoGame(Random? rng = null)
    {
        _rng = rng ?? new Random();
        Reset(dinoCenterX: 60f, fieldWidth: _fieldWidth);
    }

    public GameState State { get; private set; }

    public Trex Trex { get; } = new();

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    public IReadOnlyList<Cloud> Clouds => _clouds;

    public int Score { get; private set; }

    public int HighScore { get; private set; }

    /// <summary>Horizontal scroll phase of the ground tile pattern, in [0, tile width).</summary>
    public float GroundOffset { get; private set; }

    /// <summary>True once the post-crash lockout has passed and a jump press will restart the run.</summary>
    public bool RestartReady => State == GameState.Crashed && _crashTimer <= 0f;

    /// <summary>Running-leg / duck-leg frame (0/1).</summary>
    public int RunFrame => (int)(_stepCount / GameConfig.RunFrameInterval % 2);

    /// <summary>Pterodactyl wing frame (0/1).</summary>
    public int WingFrame => (int)(_stepCount / GameConfig.WingFrameInterval % 2);

    /// <summary>
    /// Obstacles always travel from +X toward the dino in game space. When the dino sits in the right
    /// half of the screen the renderer mirrors the stage so they enter from the left instead, which
    /// keeps the longer run-up (and reaction time) whichever side of the taskbar the dino is on.
    /// </summary>
    public static bool ShouldMirror(float dinoCenterX, float fieldWidth) => dinoCenterX > fieldWidth / 2f;

    /// <summary>Seeds the high score from persisted settings before the first game.</summary>
    public void SeedHighScore(int highScore) => HighScore = Math.Max(HighScore, Math.Max(0, highScore));

    /// <summary>(Re)starts a run: the dino sits at <paramref name="dinoCenterX"/>; obstacles cross a field of the given width.</summary>
    public void Reset(float dinoCenterX, float fieldWidth)
    {
        _fieldWidth = MathF.Max(240f, fieldWidth);
        Trex.X = dinoCenterX - (GameConfig.DinoWidth / 2f);
        _clouds.Clear();
        _nextCloudGap = RandomCloudGap() * 0.3f;
        GroundOffset = 0f;
        RestartRun();
    }

    /// <summary>Space / Up / Enter pressed: jump, or restart after a crash.</summary>
    public void OnJumpDown()
    {
        if (State == GameState.Crashed)
        {
            if (RestartReady)
            {
                RestartRun();
            }

            return;
        }

        Trex.StartJump();
    }

    public void OnJumpUp() => Trex.SetJumpHeld(false);

    /// <summary>Down arrow pressed/released: duck on the ground, drop fast in the air.</summary>
    public void OnDuck(bool held)
    {
        if (State == GameState.Running)
        {
            Trex.SetDuck(held);
        }
    }

    public void Update(float deltaSeconds)
    {
        _accumulator += MathF.Min(deltaSeconds, MaxDeltaSeconds);

        int steps = 0;
        while (_accumulator >= GameConfig.FrameSeconds && steps < 4)
        {
            _accumulator -= GameConfig.FrameSeconds;
            Step();
            steps++;
        }

        if (steps == 4)
        {
            _accumulator = 0f;
        }
    }

    private void Step()
    {
        if (State == GameState.Crashed)
        {
            _crashTimer = MathF.Max(0f, _crashTimer - GameConfig.FrameSeconds);
            return; // the scene is frozen on the game-over screen
        }

        _stepCount++;
        _speed = MathF.Min(GameConfig.MaxSpeed, _speed + GameConfig.Acceleration);

        Trex.Step();
        AdvanceObstacles();
        AdvanceDecor();

        if (HasCollision())
        {
            Trex.Kill();
            Trex.SetDuck(false);
            State = GameState.Crashed;
            _crashTimer = GameConfig.RestartLockoutSeconds;
        }
    }

    private void RestartRun()
    {
        Trex.Reset();
        _obstacles.Clear();
        _speed = GameConfig.StartSpeed;
        Score = 0;
        _crashTimer = 0f;
        _graceTimer = GameConfig.FirstObstacleGraceSeconds;
        State = GameState.Running;
        _nextObstacleGap = 0f;
    }

    private void AdvanceObstacles()
    {
        float rightmostEdge = float.NegativeInfinity;
        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle o = _obstacles[i];
            float step = o.Type == ObstacleType.Pterodactyl ? _speed * 1.3f : _speed; // birds fly faster
            o.X -= step;

            // Score a point the moment an obstacle has fully passed the dino (it was dodged).
            if (!o.Counted && o.X + o.TotalWidth < Trex.X)
            {
                o.Counted = true;
                Score++;
                HighScore = Math.Max(HighScore, Score);
            }

            if (o.X + o.TotalWidth < -16f)
            {
                _obstacles.RemoveAt(i);
            }
            else
            {
                rightmostEdge = MathF.Max(rightmostEdge, o.X + o.TotalWidth);
            }
        }

        if (_graceTimer > 0f)
        {
            _graceTimer -= GameConfig.FrameSeconds;
            return;
        }

        if (_obstacles.Count == 0 || _fieldWidth - rightmostEdge >= _nextObstacleGap)
        {
            SpawnObstacle();
            _nextObstacleGap = RandomObstacleGap();
        }
    }

    private void AdvanceDecor()
    {
        GroundOffset = (GroundOffset + _speed) % GameConfig.GroundTileWidth;

        float cloudStep = _speed * GameConfig.CloudSpeedFactor;
        float rightmost = float.NegativeInfinity;
        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            Cloud c = _clouds[i];
            c.X -= cloudStep;
            if (c.X < -80f)
            {
                _clouds.RemoveAt(i);
            }
            else
            {
                rightmost = MathF.Max(rightmost, c.X);
            }
        }

        if (_clouds.Count < GameConfig.MaxClouds && (_clouds.Count == 0 || _fieldWidth - rightmost >= _nextCloudGap))
        {
            _clouds.Add(new Cloud { X = _fieldWidth, Y = Range(GameConfig.CloudMinY, GameConfig.CloudMaxY) });
            _nextCloudGap = RandomCloudGap();
        }
    }

    private void SpawnObstacle()
    {
        bool bird = Score >= GameConfig.BirdMinScore && _rng.NextDouble() < 0.25;
        if (bird)
        {
            float bottom = GameConfig.BirdBottoms[_rng.Next(GameConfig.BirdBottoms.Length)];
            _obstacles.Add(new Obstacle
            {
                Type = ObstacleType.Pterodactyl,
                X = _fieldWidth,
                Y = bottom - GameConfig.PterodactylHeight,
                Width = GameConfig.PterodactylWidth,
                Height = GameConfig.PterodactylHeight,
            });
            return;
        }

        bool large = _rng.NextDouble() < 0.45;
        float width = large ? GameConfig.CactusLargeWidth : GameConfig.CactusSmallWidth;
        float height = large ? GameConfig.CactusLargeHeight : GameConfig.CactusSmallHeight;
        int count = 1 + _rng.Next(large ? 1 : GameConfig.MaxCactusGroup); // large cacti stay single
        _obstacles.Add(new Obstacle
        {
            Type = large ? ObstacleType.CactusLarge : ObstacleType.CactusSmall,
            X = _fieldWidth,
            Y = GameConfig.GroundY - height,
            Width = width,
            Height = height,
            Count = count,
        });
    }

    private float RandomObstacleGap()
    {
        // Wider gaps at higher speed keep obstacles reachable; a randomised extra span adds variety.
        float baseGap = 120f + (_speed * 12f);
        return baseGap + ((float)_rng.NextDouble() * baseGap * GameConfig.GapCoefficient);
    }

    private float RandomCloudGap() => Range(GameConfig.CloudMinGap, GameConfig.CloudMaxGap);

    private float Range(float min, float max) => min + ((float)_rng.NextDouble() * (max - min));

    private bool HasCollision()
    {
        RectF trex = Inset(Trex.Bounds);
        foreach (Obstacle o in _obstacles)
        {
            if (trex.Intersects(Inset(o.Bounds)))
            {
                return true;
            }
        }

        return false;
    }

    private static RectF Inset(RectF box) => box.Deflate(box.Width * CollisionInset, box.Height * CollisionInset);
}
