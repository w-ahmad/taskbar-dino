using TaskbarDino.Core.Behavior;
using TaskbarDino.Core.Dino;

namespace TaskbarDino.Tests.Behavior;

public class MoveControllerTests
{
    private static MoveController Grounded(float x, float scale = 1f)
    {
        var mc = new MoveController(scale);
        mc.Teleport(x);
        return mc;
    }

    private static void Advance(MoveController mc, double seconds, double step = 1.0 / 60.0)
    {
        for (double t = 0; t < seconds; t += step)
        {
            mc.Update((float)step);
        }
    }

    [Fact]
    public void Teleport_IsInstant_AndIdle()
    {
        var mc = Grounded(100f);
        Assert.Equal(100f, mc.FeetX);
        Assert.Equal(DinoState.Idle, mc.State);
        Assert.False(mc.IsBusy);
    }

    [Fact]
    public void MoveTo_NearTarget_Walks_FacingRight()
    {
        var mc = Grounded(0f);
        mc.MoveTo(50f);
        mc.Update(0.01f);

        Assert.Equal(DinoState.Walking, mc.State);
        Assert.Equal(Facing.Right, mc.Facing);
        Assert.True(mc.IsBusy);
    }

    [Fact]
    public void MoveTo_FarTarget_Runs()
    {
        var mc = Grounded(0f);
        mc.MoveTo(600f);
        mc.Update(0.01f);

        Assert.Equal(DinoState.Running, mc.State);
    }

    [Fact]
    public void MoveTo_LeftTarget_FacesLeft()
    {
        var mc = Grounded(200f);
        mc.MoveTo(0f);
        mc.Update(0.01f);

        Assert.Equal(Facing.Left, mc.Facing);
    }

    [Fact]
    public void Arrives_AtTarget_ThenIdles()
    {
        var mc = Grounded(0f);
        mc.MoveTo(120f);

        Advance(mc, seconds: 6);

        Assert.Equal(120f, mc.FeetX, precision: 1);
        Assert.False(mc.IsBusy);
        Assert.Equal(DinoState.Idle, mc.State);
    }

    [Fact]
    public void Retargeting_MidMove_ChangesDestination_WithoutQueueing()
    {
        var mc = Grounded(0f);
        mc.MoveTo(500f);
        Advance(mc, 0.2);
        float midway = mc.FeetX;
        Assert.True(midway > 0f && midway < 500f);

        mc.MoveTo(-100f); // user switched apps again
        Advance(mc, 3);

        Assert.Equal(-100f, mc.FeetX, precision: 1);
        Assert.False(mc.IsBusy);
    }

    [Fact]
    public void DoesNotOvershoot_OnLargeDelta()
    {
        var mc = Grounded(0f);
        mc.MoveTo(10f);
        mc.Update(10f); // absurdly large step

        Assert.Equal(10f, mc.FeetX, precision: 3);
    }

    [Fact]
    public void Jump_RisesThenLands()
    {
        var mc = Grounded(0f);
        mc.MoveTo(0f, jump: true);

        mc.Update(0.05f);
        Assert.True(mc.JumpHeight > 0f);
        Assert.True(mc.IsBusy);
        Assert.Equal(DinoState.Jumping, mc.State);

        Advance(mc, seconds: 2);
        Assert.Equal(0f, mc.JumpHeight, precision: 3);
        Assert.False(mc.IsBusy);
    }

    [Fact]
    public void FallFrom_DropsToGround()
    {
        var mc = Grounded(0f);
        mc.FallFrom(200f);
        Assert.True(mc.IsBusy);

        Advance(mc, seconds: 2);
        Assert.Equal(0f, mc.JumpHeight, precision: 3);
        Assert.Equal(DinoState.Idle, mc.State);
    }

    [Fact]
    public void Speeds_ScaleWithDpi()
    {
        var a = Grounded(0f, 1f);
        var b = Grounded(0f, 2f);
        a.MoveTo(1000f);
        b.MoveTo(1000f);
        Advance(a, 0.5);
        Advance(b, 0.5);

        Assert.Equal(a.FeetX * 2f, b.FeetX, precision: 1);
    }
}
