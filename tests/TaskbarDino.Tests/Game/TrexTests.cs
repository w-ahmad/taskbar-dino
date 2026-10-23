using TaskbarDino.Core.Game;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Tests.Game;

public class TrexTests
{
    private static float RunJumpTrackingPeak(bool holdKey)
    {
        var trex = new Trex();
        trex.StartJump();
        if (!holdKey)
        {
            trex.SetJumpHeld(false); // release immediately → a short tap
        }

        float peak = 0f;
        for (int i = 0; i < 400 && trex.IsJumping; i++)
        {
            trex.Step();
            peak = MathF.Max(peak, trex.JumpOffset);
        }

        return peak;
    }

    [Fact]
    public void StartJump_WhenGrounded_RisesThenLands()
    {
        var trex = new Trex();
        trex.StartJump();

        trex.Step();
        trex.Step();
        Assert.True(trex.IsJumping);
        Assert.True(trex.JumpOffset > 0f);

        for (int i = 0; i < 400 && trex.IsJumping; i++)
        {
            trex.Step();
        }

        Assert.False(trex.IsJumping);
        Assert.Equal(0f, trex.JumpOffset, precision: 3);
    }

    [Fact]
    public void Step_HeldJump_GoesHigherThanATap()
    {
        float tapPeak = RunJumpTrackingPeak(holdKey: false);
        float holdPeak = RunJumpTrackingPeak(holdKey: true);

        Assert.True(holdPeak > tapPeak + 2f, $"hold {holdPeak} should exceed tap {tapPeak}");
        Assert.True(holdPeak <= GameConfig.MaxHoldHeight + 2f);
    }

    [Fact]
    public void StartJump_MidAir_IsIgnored()
    {
        var trex = new Trex();
        trex.StartJump();

        float peak = 0f;
        for (int i = 0; i < 400 && trex.IsJumping; i++)
        {
            trex.Step();
            trex.StartJump(); // spam mid-air — must be ignored
            peak = MathF.Max(peak, trex.JumpOffset);
        }

        Assert.True(peak <= GameConfig.MaxHoldHeight + 2f, $"double-jump boosted the peak to {peak}");
    }

    [Fact]
    public void SetDuck_MidAir_LandsSooner()
    {
        int Airtime(bool drop)
        {
            var trex = new Trex();
            trex.StartJump();
            int frames = 0;
            while (trex.IsJumping && frames < 400)
            {
                trex.Step();
                frames++;
                if (drop && frames == 8)
                {
                    trex.SetDuck(true);
                }
            }

            return frames;
        }

        Assert.True(Airtime(drop: true) < Airtime(drop: false));
    }

    [Fact]
    public void Bounds_Grounded_SitOnGroundAtX()
    {
        var trex = new Trex { X = 40f };
        RectF bounds = trex.Bounds;

        Assert.Equal(40f, bounds.Left, precision: 3);
        Assert.Equal(GameConfig.GroundY, bounds.Bottom, precision: 3);
        Assert.Equal(GameConfig.DinoHeight, bounds.Height, precision: 3);
    }

    [Fact]
    public void Kill_Always_SetsDeadPoseAndBlocksJumping()
    {
        var trex = new Trex();
        trex.Kill();

        Assert.False(trex.IsAlive);
        Assert.Equal(TrexPose.Dead, trex.Pose);
        trex.StartJump();
        Assert.False(trex.IsJumping);
    }
}
