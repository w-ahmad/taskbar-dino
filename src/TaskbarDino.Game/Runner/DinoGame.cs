using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Game.Runner;

/// <summary>
/// The taskbar runner: the dino holds its spot on the active app's icon and hops (variable height) to
/// clear cacti and pterodactyls that scroll in from the right screen edge, over a transparent stage —
/// nothing else is drawn but the dino, the obstacles and a small score cloud. Speed ramps up; a crash
/// briefly pauses then auto-restarts the run (endless), keeping the persisted high score. Pure logic
/// on a fixed 60 Hz step, deterministic under a seeded <see cref="Random"/>, and unit-testable.
/// </summary>
public sealed class DinoGame
{
    private const float MaxDeltaSeconds = 0.05f; // clamp to avoid a spiral of death after a stall
    private const float CollisionInset = 0.16f;  // shrink both boxes for forgiving hit detection

    private readonly Random _rng;
    private readonly List<Obstacle> _obstacles = [];

    private float _accumulator;
    private float _fieldWidth = 400f;
    private float _speed;
    private float _crashTimer;
    private float _nextObstacleGap;
    private long _stepCount;

    public DinoGame(Random? rng = null)
    {
        _rng = rng ?? new Random();
        Reset(dinoCenterX: 60f, fieldWidth: _fieldWidth);
    }

    public GameState State { get; private set; }

    public Trex Trex { get; } = new();

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    public int Score { get; private set; }

    public int HighScore { get; private set; }

    /// <summary>Running-leg frame (0/1).</summary>
    public int RunFrame => (int)(_stepCount / GameConfig.RunFrameInterval % 2);

    /// <summary>Pterodactyl wing frame (0/1).</summary>
    public int WingFrame => (int)(_stepCount / GameConfig.WingFrameInterval % 2);

    /// <summary>Seeds the high score from persisted settings before the first game.</summary>
    public void SeedHighScore(int highScore) => HighScore = Math.Max(HighScore, Math.Max(0, highScore));

    /// <summary>(Re)starts a run: the dino sits at <paramref name="dinoCenterX"/>; obstacles cross a field of the given width.</summary>
    public void Reset(float dinoCenterX, float fieldWidth)
    {
        _fieldWidth = MathF.Max(120f, fieldWidth);
        Trex.X = dinoCenterX - (GameConfig.DinoWidth / 2f);
        Trex.Reset();
        _obstacles.Clear();
        _speed = GameConfig.StartSpeed;
        Score = 0;
        _crashTimer = 0f;
        State = GameState.Running;

        // First obstacle starts comfortably far away so the run doesn't ambush you.
        _nextObstacleGap = MathF.Max(140f, (_fieldWidth - Trex.X) * 0.7f);
    }

    public void OnJumpDown() => Trex.StartJump();

    public void OnJumpUp() => Trex.SetJumpHeld(false);

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
        _stepCount++;

        if (State == GameState.Crashed)
        {
            _crashTimer -= GameConfig.FrameSeconds;
            if (_crashTimer <= 0f)
            {
                RestartRun();
            }

            return;
        }

        _speed = MathF.Min(GameConfig.MaxSpeed, _speed + GameConfig.Acceleration);

        Trex.Step();
        AdvanceObstacles();

        if (HasCollision())
        {
            Trex.Kill();
            State = GameState.Crashed;
            _crashTimer = GameConfig.CrashPauseSeconds;
        }
    }

    private void RestartRun()
    {
        Trex.Reset();
        _obstacles.Clear();
        _speed = GameConfig.StartSpeed;
        Score = 0;
        State = GameState.Running;
        _nextObstacleGap = MathF.Max(140f, (_fieldWidth - Trex.X) * 0.7f);
    }

    private void AdvanceObstacles()
    {
        float rightmostEdge = float.NegativeInfinity;
        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle o = _obstacles[i];
            float step = o.Type == ObstacleType.Pterodactyl ? _speed * 1.3f : _speed; // birds fly faster
            o.X -= step;

            // Score a point the moment an obstacle has fully passed the dino (i.e. it was dodged).
            if (!o.Counted && o.X + o.TotalWidth < Trex.X)
            {
                o.Counted = true;
                Score++;
                HighScore = Math.Max(HighScore, Score);
            }

            if (o.X + o.TotalWidth < -8f)
            {
                _obstacles.RemoveAt(i);
            }
            else
            {
                rightmostEdge = MathF.Max(rightmostEdge, o.X + o.TotalWidth);
            }
        }

        if (_obstacles.Count == 0 || _fieldWidth - rightmostEdge >= _nextObstacleGap)
        {
            SpawnObstacle();
            _nextObstacleGap = RandomObstacleGap();
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
        float baseGap = 60f + (_speed * 12f);
        return baseGap + ((float)_rng.NextDouble() * baseGap * GameConfig.GapCoefficient);
    }

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
