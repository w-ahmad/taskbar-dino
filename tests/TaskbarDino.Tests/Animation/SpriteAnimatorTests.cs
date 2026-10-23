using TaskbarDino.Core.Animation;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Tests.Animation;

public class SpriteAnimatorTests
{
    private static AtlasRegion Region(int x) => new(new PixelRect(x, 0, 10, 10));

    private static AnimationClip TwoFrame(bool loop) =>
        new("two", [new AnimationFrame(Region(0), 0.5f), new AnimationFrame(Region(10), 0.5f)], loop);

    [Fact]
    public void Update_StaticClip_NeverAdvancesOrFinishes()
    {
        var animator = new SpriteAnimator(AnimationClip.Static("idle", Region(0)));

        animator.Update(1000f);

        Assert.Equal(Region(0), animator.CurrentFrame.Region);
        Assert.False(animator.Finished);
        Assert.Equal(float.PositiveInfinity, animator.TimeUntilNextFrame);
    }

    [Fact]
    public void Update_AfterFrameDuration_AdvancesToNextFrame()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));

        Assert.Equal(Region(0), animator.CurrentFrame.Region);
        Assert.Equal(0.5f, animator.TimeUntilNextFrame, precision: 3);
        animator.Update(0.3f);
        Assert.Equal(0.2f, animator.TimeUntilNextFrame, precision: 3);
        animator.Update(0.3f);
        Assert.Equal(Region(10), animator.CurrentFrame.Region);
    }

    [Fact]
    public void Update_NonLoopingClip_LatchesOnLastFrameAndFinishes()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));

        animator.Update(2f);

        Assert.True(animator.Finished);
        Assert.Equal(Region(10), animator.CurrentFrame.Region);
        Assert.Equal(float.PositiveInfinity, animator.TimeUntilNextFrame);
    }

    [Fact]
    public void Update_LoopingClip_WrapsAndNeverFinishes()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: true));

        animator.Update(1.0f); // full cycle -> back to frame 0
        Assert.Equal(Region(0), animator.CurrentFrame.Region);
        Assert.False(animator.Finished);
    }

    [Fact]
    public void Play_AfterFinish_ResetsState()
    {
        var animator = new SpriteAnimator(TwoFrame(loop: false));
        animator.Update(2f);
        Assert.True(animator.Finished);

        animator.Play(TwoFrame(loop: false));

        Assert.False(animator.Finished);
        Assert.Equal(Region(0), animator.CurrentFrame.Region);
    }

    [Fact]
    public void Trigger_Reaction_OverridesBaseThenYields()
    {
        var states = new Dictionary<DinoState, AnimationClip> { [DinoState.Idle] = AnimationClip.Static("idle", Region(0)) };
        var reactions = new Dictionary<DinoReaction, AnimationClip> { [DinoReaction.Blink] = new("blink", [new AnimationFrame(Region(10), 0.1f)], loop: false) };
        var controller = new AnimationController(states, reactions, DinoState.Idle);

        controller.Trigger(DinoReaction.Blink);
        Assert.Equal(Region(10), controller.CurrentFrame.Region);
        Assert.True(controller.ReactionActive);

        controller.Update(0.2f);
        Assert.False(controller.ReactionActive);
        Assert.Equal(Region(0), controller.CurrentFrame.Region);
    }

    [Fact]
    public void SetState_MovementState_CancelsReaction()
    {
        var states = new Dictionary<DinoState, AnimationClip>
        {
            [DinoState.Idle] = AnimationClip.Static("idle", Region(0)),
            [DinoState.Walking] = AnimationClip.Static("walk", Region(20)),
        };
        var reactions = new Dictionary<DinoReaction, AnimationClip> { [DinoReaction.Yawn] = new("yawn", [new AnimationFrame(Region(10), 5f)], loop: false) };
        var controller = new AnimationController(states, reactions, DinoState.Idle);

        controller.Trigger(DinoReaction.Yawn);
        controller.SetState(DinoState.Walking);

        Assert.False(controller.ReactionActive);
        Assert.Equal(Region(20), controller.CurrentFrame.Region);
    }
}
