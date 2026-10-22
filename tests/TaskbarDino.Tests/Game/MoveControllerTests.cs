using TaskbarDino.Core.Dino;
using TaskbarDino.Game;

namespace TaskbarDino.Tests.Game;

public class MoveControllerTests
{
    private static MoveController Grounded(float x)
    {
        var mc = new MoveController(uiScale: 1f);
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
}
