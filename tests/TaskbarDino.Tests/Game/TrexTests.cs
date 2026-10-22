using TaskbarDino.Core.Primitives;
using TaskbarDino.Game.Runner;

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
    public void StartJump_Rises_ThenLands()
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
    public void HoldingJump_GoesHigher_ThanATap()
    {
        float tapPeak = RunJumpTrackingPeak(holdKey: false);
        float holdPeak = RunJumpTrackingPeak(holdKey: true);

        Assert.True(holdPeak > tapPeak + 2f, $"hold {holdPeak} should exceed tap {tapPeak}");
        Assert.True(holdPeak <= GameConfig.MaxHoldHeight + 2f);
    }

    [Fact]
    public void StartJump_MidAir_IsIgnored_NoDoubleBoost()
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
    public void Bounds_SitOnGround_AtX()
    {
        var trex = new Trex { X = 40f };
        RectF bounds = trex.Bounds;

        Assert.Equal(40f, bounds.Left, precision: 3);
        Assert.Equal(GameConfig.GroundY, bounds.Bottom, precision: 3);
        Assert.Equal(GameConfig.DinoHeight, bounds.Height, precision: 3);
    }

    [Fact]
    public void Kill_SetsDeadPose()
    {
        var trex = new Trex();
        trex.Kill();

        Assert.False(trex.IsAlive);
        Assert.Equal(TrexPose.Dead, trex.Pose);
    }
}
