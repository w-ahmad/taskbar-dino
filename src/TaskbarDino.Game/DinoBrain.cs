using Microsoft.Extensions.Logging;

using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Time;

namespace TaskbarDino.Game;

/// <summary>
/// The companion behaviour model for idle life: the dino rests, blinks and glances around on
/// randomised timers, and drifts to sleep after a long stretch of inactivity, waking again later.
/// Every interval is jittered so the rhythm never repeats obviously. Pure logic and fully
/// deterministic when constructed with a seeded <see cref="Random"/>, which the tests rely on.
/// </summary>
public sealed class DinoBrain : IDinoBrain
{
    private const float MaxDeltaSeconds = 5f; // guard against huge deltas after system sleep

    private readonly Random _rng;
    private readonly ILogger<DinoBrain>? _logger;

    private float _reactionTimer;
    private float _awakeAccumulator;
    private float _sleepThreshold;
    private float _sleepTimer;
    private DinoReaction? _pendingReaction;

    public DinoBrain(ILogger<DinoBrain> logger)
        : this(new Random(), logger)
    {
    }

    public DinoBrain(Random rng, ILogger<DinoBrain>? logger = null)
    {
        _rng = rng;
        _logger = logger;
        State = DinoState.Idle;
        _reactionTimer = Range(2f, 5f);
        _sleepThreshold = Range(25f, 45f);
    }

    public DinoState State { get; private set; }

    public Facing Facing => Facing.Right;

    public TimeSpan TimeUntilNextEvent
    {
        get
        {
            float seconds = State == DinoState.Sleeping
                ? _sleepTimer
                : MathF.Min(_reactionTimer, _sleepThreshold - _awakeAccumulator);
            return TimeSpan.FromSeconds(Math.Max(0f, seconds));
        }
    }

    public void Update(in FrameTime time)
    {
        float dt = MathF.Min(time.DeltaSeconds, MaxDeltaSeconds);

        if (State == DinoState.Sleeping)
        {
            _sleepTimer -= dt;
            if (_sleepTimer <= 0f)
            {
                WakeUp();
            }

            return;
        }

        _awakeAccumulator += dt;

        _reactionTimer -= dt;
        if (_reactionTimer <= 0f)
        {
            _pendingReaction = _rng.NextDouble() < 0.72 ? DinoReaction.Blink : DinoReaction.Glance;
            _reactionTimer = Range(2.5f, 6f);
        }

        if (_awakeAccumulator >= _sleepThreshold)
        {
            FallAsleep();
        }
    }

    public bool TryDequeueReaction(out DinoReaction reaction)
    {
        if (_pendingReaction is { } pending)
        {
            reaction = pending;
            _pendingReaction = null;
            return true;
        }

        reaction = default;
        return false;
    }

    private void FallAsleep()
    {
        State = DinoState.Sleeping;
        _sleepTimer = Range(12f, 25f);
        _pendingReaction = null;
        _logger?.LogDebug("Dino fell asleep for {Seconds:0.0}s.", _sleepTimer);
    }

    private void WakeUp()
    {
        State = DinoState.Idle;
        _awakeAccumulator = 0f;
        _sleepThreshold = Range(25f, 45f);
        _reactionTimer = Range(2f, 4f);
        _logger?.LogDebug("Dino woke up.");
    }

    private float Range(float min, float max) => min + ((float)_rng.NextDouble() * (max - min));
}
