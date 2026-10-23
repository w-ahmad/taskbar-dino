using TaskbarDino.Core.Dino;

namespace TaskbarDino.Core.Behavior;

/// <summary>
/// The companion's idle personality: while awake it blinks, glances, wags, yawns, scratches,
/// stretches and gets curious on jittered timers (never the same non-blink reaction twice in a
/// row), and after a long stretch without interaction it drifts to sleep, waking later or when
/// poked. Pure logic, fully deterministic under a seeded <see cref="Random"/>.
/// </summary>
public sealed class DinoBrain
{
    private const float MaxDeltaSeconds = 5f; // guard against huge deltas after system sleep

    // Weighted reaction pool while awake. Blink dominates, as it should.
    private static readonly (DinoReaction Reaction, float Weight)[] Pool =
    [
        (DinoReaction.Blink, 10f),
        (DinoReaction.Glance, 4f),
        (DinoReaction.TailWag, 3f),
        (DinoReaction.Scratch, 2f),
        (DinoReaction.Yawn, 1.5f),
        (DinoReaction.Stretch, 1.5f),
        (DinoReaction.Curious, 1.5f),
        (DinoReaction.Eat, 1.2f),
        (DinoReaction.Play, 1.2f),
        (DinoReaction.Celebrate, 0.4f),
    ];

    private readonly Random _rng;

    private float _reactionTimer;
    private float _awakeSeconds;
    private float _sleepThreshold;
    private float _sleepTimer;
    private DinoReaction _lastReaction = DinoReaction.Blink;
    private DinoReaction? _pending;

    public DinoBrain(Random rng, ActivityLevel activity = ActivityLevel.Normal)
    {
        _rng = rng;
        Activity = activity;
        _reactionTimer = NextReactionDelay();
        _sleepThreshold = NextSleepThreshold();
    }

    public ActivityLevel Activity { get; private set; }

    public DinoState State { get; private set; } = DinoState.Idle;

    public bool IsAsleep => State == DinoState.Sleeping;

    /// <summary>How long until the next scheduled change — lets the loop rest exactly that long.</summary>
    public TimeSpan TimeUntilNextEvent
    {
        get
        {
            float seconds = IsAsleep
                ? _sleepTimer
                : MathF.Min(_reactionTimer, _sleepThreshold - _awakeSeconds);
            return TimeSpan.FromSeconds(Math.Max(0f, seconds));
        }
    }

    public void SetActivity(ActivityLevel activity)
    {
        if (activity == Activity)
        {
            return;
        }

        Activity = activity;
        _reactionTimer = MathF.Min(_reactionTimer, NextReactionDelay());
    }

    /// <summary>The user interacted (hover, click, drag) or the app switched: wake up and postpone sleep.</summary>
    public void Poke()
    {
        if (IsAsleep)
        {
            WakeUp();
        }

        _awakeSeconds = 0f;
    }

    public void Update(float deltaSeconds)
    {
        float dt = MathF.Min(deltaSeconds, MaxDeltaSeconds);

        if (IsAsleep)
        {
            _sleepTimer -= dt;
            if (_sleepTimer <= 0f)
            {
                WakeUp();
            }

            return;
        }

        _awakeSeconds += dt;
        _reactionTimer -= dt;

        if (_reactionTimer <= 0f)
        {
            _pending = PickReaction();
            _reactionTimer = NextReactionDelay();
        }

        if (_awakeSeconds >= _sleepThreshold)
        {
            FallAsleep();
        }
    }

    public bool TryDequeueReaction(out DinoReaction reaction)
    {
        if (_pending is { } pending)
        {
            reaction = pending;
            _pending = null;
            return true;
        }

        reaction = default;
        return false;
    }

    private DinoReaction PickReaction()
    {
        // Weighted pick; a non-blink reaction may not immediately repeat.
        float total = 0f;
        foreach ((DinoReaction r, float w) in Pool)
        {
            if (r == DinoReaction.Blink || r != _lastReaction)
            {
                total += w;
            }
        }

        float roll = (float)_rng.NextDouble() * total;
        foreach ((DinoReaction r, float w) in Pool)
        {
            if (r != DinoReaction.Blink && r == _lastReaction)
            {
                continue;
            }

            roll -= w;
            if (roll <= 0f)
            {
                if (r != DinoReaction.Blink)
                {
                    _lastReaction = r; // remember the last non-blink so it isn't repeated back to back
                }

                return r;
            }
        }

        return DinoReaction.Blink;
    }

    private float ActivityScale => Activity switch
    {
        ActivityLevel.Calm => 2.2f,
        ActivityLevel.Lively => 0.55f,
        _ => 1f,
    };

    private float NextReactionDelay() => Range(2.5f, 6.5f) * ActivityScale;

    private float NextSleepThreshold() => Range(50f, 110f) * (Activity == ActivityLevel.Calm ? 0.6f : 1f);

    private void FallAsleep()
    {
        State = DinoState.Sleeping;
        _sleepTimer = Range(20f, 45f);
        _pending = null;
    }

    private void WakeUp()
    {
        State = DinoState.Idle;
        _awakeSeconds = 0f;
        _sleepThreshold = NextSleepThreshold();
        _reactionTimer = Range(1.5f, 3f);
    }

    private float Range(float min, float max) => _rng.NextRange(min, max);
}
