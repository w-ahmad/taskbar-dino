using TaskbarDino.Animation;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Tests.Animation;

public class SpriteAnimatorTests
{
    private static AtlasRegion Region(int x) => new(new PixelRect(x, 0, 10, 10));

    private static AnimationClip TwoFrame(bool loop) =>
        new("two", [new AnimationFrame(Region(0), 0.5f), new AnimationFrame(Region(10), 0.5f)], loop);

    [Fact]
    public void StaticClip_NeverAdvances_AndNeverFinishes()
    {
        var animator = new SpriteAnimator(AnimationClip.Static("idle", Region(0)));

        animator.Update(1000f);

        Assert.Equal(Region(0), animator.CurrentRegion);
        Assert.False(animator.Finished);
    }

    [Fact]
    public void Advances_ToNextFrame_AfterFrameDuration()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));

        Assert.Equal(Region(0), animator.CurrentRegion);
        animator.Update(0.6f);
        Assert.Equal(Region(10), animator.CurrentRegion);
    }

    [Fact]
    public void NonLoopingClip_LatchesOnLastFrame_AndFinishes()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));

        animator.Update(2f);

        Assert.True(animator.Finished);
        Assert.Equal(Region(10), animator.CurrentRegion);
    }

    [Fact]
    public void LoopingClip_WrapsAround_AndNeverFinishes()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: true));

        animator.Update(1.0f); // full cycle -> back to frame 0
        Assert.Equal(Region(0), animator.CurrentRegion);
        Assert.False(animator.Finished);
    }

    [Fact]
    public void Play_ResetsState()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));
        animator.Update(2f);
        Assert.True(animator.Finished);

        animator.Play(TwoFrame(loop: false));

        Assert.False(animator.Finished);
        Assert.Equal(Region(0), animator.CurrentRegion);
    }
}
