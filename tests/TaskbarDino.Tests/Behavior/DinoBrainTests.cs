using TaskbarDino.Core.Behavior;
using TaskbarDino.Core.Dino;

namespace TaskbarDino.Tests.Behavior;

public class DinoBrainTests
{
    private static void Advance(DinoBrain brain, double seconds, double step, Action<DinoBrain>? afterStep = null)
    {
        for (double t = 0; t < seconds; t += step)
        {
            brain.Update((float)step);
            afterStep?.Invoke(brain);
        }
    }

    [Fact]
    public void Constructor_Default_StartsIdleWithNoPendingReaction()
    {
        var brain = new DinoBrain(new Random(1));
        Assert.Equal(DinoState.Idle, brain.State);
        Assert.False(brain.TryDequeueReaction(out _));
    }

    [Fact]
    public void Update_WhileAwake_EmitsReactions()
    {
        var brain = new DinoBrain(new Random(4242));
        int reactions = 0;
        Advance(brain, 30, 0.1, b => { if (b.TryDequeueReaction(out _)) reactions++; });
        Assert.InRange(reactions, 3, 15);
    }

    [Fact]
    public void TryDequeueReaction_NonBlink_NeverRepeatsBackToBack()
    {
        var brain = new DinoBrain(new Random(99), ActivityLevel.Lively);
        DinoReaction? last = null;
        Advance(brain, 400, 0.1, b =>
        {
            b.Poke(); // never sleep
            if (b.TryDequeueReaction(out DinoReaction r))
            {
                if (r != DinoReaction.Blink)
                {
                    Assert.NotEqual(last, r);
                    last = r;
                }
            }
        });
    }

    [Fact]
    public void Update_AfterProlongedIdle_SleepsThenWakes()
    {
        var brain = new DinoBrain(new Random(7));
        bool slept = false, wokeAfter = false;
        Advance(brain, 200, 0.1, b =>
        {
            _ = b.TryDequeueReaction(out _);
            if (b.IsAsleep) slept = true;
            else if (slept) wokeAfter = true;
        });

        Assert.True(slept, "expected the dino to fall asleep within 200s");
        Assert.True(wokeAfter, "expected the dino to wake up again");
    }

    [Fact]
    public void Poke_WhileAsleep_Wakes()
    {
        var brain = new DinoBrain(new Random(7));
        Advance(brain, 200, 0.5, b => { if (!b.IsAsleep) { } });
        // Drive until asleep.
        while (!brain.IsAsleep) brain.Update(1f);

        brain.Poke();

        Assert.Equal(DinoState.Idle, brain.State);
    }

    [Fact]
    public void SetActivity_Calm_EmitsFewerReactionsThanLively()
    {
        int Count(ActivityLevel level)
        {
            var brain = new DinoBrain(new Random(11), level);
            int n = 0;
            Advance(brain, 60, 0.1, b => { b.Poke(); if (b.TryDequeueReaction(out _)) n++; });
            return n;
        }

        Assert.True(Count(ActivityLevel.Calm) < Count(ActivityLevel.Lively));
    }

    [Fact]
    public void TimeUntilNextEvent_Always_IsNeverNegative()
    {
        var brain = new DinoBrain(new Random(99));
        Advance(brain, 150, 0.25, b =>
        {
            _ = b.TryDequeueReaction(out _);
            Assert.True(b.TimeUntilNextEvent >= TimeSpan.Zero);
        });
    }

    [Fact]
    public void Update_HugeDelta_IsClamped()
    {
        var brain = new DinoBrain(new Random(3));
        brain.Update(10_000f); // system resume
        Assert.Equal(DinoState.Idle, brain.State);
    }
}
