using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Time;
using TaskbarDino.Game;

namespace TaskbarDino.Tests.Game;

public class DinoBrainTests
{
    private static void Advance(DinoBrain brain, double seconds, double stepSeconds, Action<DinoBrain>? afterStep = null)
    {
        var step = TimeSpan.FromSeconds(stepSeconds);
        var total = TimeSpan.Zero;
        long index = 0;
        for (double t = 0; t < seconds; t += stepSeconds)
        {
            total += step;
            brain.Update(new FrameTime(total, step, index++));
            afterStep?.Invoke(brain);
        }
    }

    [Fact]
    public void StartsIdle_AndFacesRight()
    {
        var brain = new DinoBrain(new Random(1));
        Assert.Equal(DinoState.Idle, brain.State);
        Assert.Equal(Facing.Right, brain.Facing);
    }

    [Fact]
    public void FreshBrain_HasNoPendingReaction()
    {
        var brain = new DinoBrain(new Random(1));
        Assert.False(brain.TryDequeueReaction(out _));
    }

    [Fact]
    public void EmitsReactions_WhileAwake()
    {
        var brain = new DinoBrain(new Random(4242));
        int reactions = 0;

        Advance(brain, seconds: 20, stepSeconds: 0.1, afterStep: b =>
        {
            if (b.TryDequeueReaction(out _))
            {
                reactions++;
            }
        });

        Assert.True(reactions > 0, "expected the dino to blink/glance during 20s awake");
    }

    [Fact]
    public void FallsAsleep_AfterProlongedIdle()
    {
        var brain = new DinoBrain(new Random(7));
        bool slept = false;

        Advance(brain, seconds: 60, stepSeconds: 0.1, afterStep: b =>
        {
            _ = b.TryDequeueReaction(out _);
            if (b.State == DinoState.Sleeping)
            {
                slept = true;
            }
        });

        Assert.True(slept, "expected the dino to fall asleep within 60s");
    }

    [Fact]
    public void TimeUntilNextEvent_IsNeverNegative()
    {
        var brain = new DinoBrain(new Random(99));
        Advance(brain, seconds: 80, stepSeconds: 0.25, afterStep: b =>
        {
            _ = b.TryDequeueReaction(out _);
            Assert.True(b.TimeUntilNextEvent >= TimeSpan.Zero);
        });
    }
}
