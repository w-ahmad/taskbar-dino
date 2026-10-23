using TaskbarDino.Core.Animation;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;

using static TaskbarDino.Core.Content.DinoSpriteSheet;

namespace TaskbarDino.Core.Content;

/// <summary>
/// Builds the dino's animation clips from the sprite sheet: static postures for sustained states,
/// looping cycles for locomotion and sleep, and short one-shots for reactions. Pure data, so a new
/// behaviour is just a new clip. Effect offsets are in sprite pixels from the frame's top-left.
/// </summary>
public static class DinoAnimations
{
    // Effect anchors (sprite px, relative to the 48×48 frame).
    private const int HeadX = 34;      // above and to the right of the head
    private const int HeadY = -18;
    private const int GroundFxY = 36;  // things that sit on the ground in front of the dino

    public static IReadOnlyDictionary<DinoState, AnimationClip> BuildStateClips(SpriteSheet sheet)
    {
        AtlasRegion zzz1 = sheet[FxZzz1];
        AtlasRegion zzz2 = sheet[FxZzz2];
        return new Dictionary<DinoState, AnimationClip>
        {
            [DinoState.Idle] = AnimationClip.Static("idle", sheet[Idle]),
            [DinoState.Held] = AnimationClip.Static("held", sheet[Jump]),
            [DinoState.Jumping] = AnimationClip.Static("jump", sheet[Jump]),
            [DinoState.Walking] = new AnimationClip("walk", [F(sheet[Walk1], 0.14f), F(sheet[Walk2], 0.14f), F(sheet[Walk3], 0.14f), F(sheet[Walk4], 0.14f)], loop: true),
            [DinoState.Running] = new AnimationClip("run", [F(sheet[Walk1], 0.07f), F(sheet[Walk2], 0.07f), F(sheet[Walk3], 0.07f), F(sheet[Walk4], 0.07f)], loop: true),
            [DinoState.Sleeping] = new AnimationClip(
                "sleep",
                [
                    new AnimationFrame(sheet[Sleep], 0.9f, zzz1, HeadX, HeadY + 8),
                    new AnimationFrame(sheet[Sleep], 0.9f, zzz2, HeadX + 2, HeadY + 4),
                ],
                loop: true),
        };
    }

    public static IReadOnlyDictionary<DinoReaction, AnimationClip> BuildReactionClips(SpriteSheet sheet)
    {
        AtlasRegion idle = sheet[Idle];
        AtlasRegion tail = sheet[TailUp];
        AtlasRegion happy = sheet[Happy];
        AtlasRegion heart = sheet[FxHeart];
        AtlasRegion question = sheet[FxQuestion];
        AtlasRegion exclaim = sheet[FxExclaim];
        AtlasRegion ball1 = sheet[FxBall1];
        AtlasRegion ball2 = sheet[FxBall2];
        AtlasRegion sprout1 = sheet[FxSprout1];
        AtlasRegion sprout2 = sheet[FxSprout2];
        AtlasRegion sprout3 = sheet[FxSprout3];
        AtlasRegion confetti1 = sheet[FxConfetti1];
        AtlasRegion confetti2 = sheet[FxConfetti2];

        return new Dictionary<DinoReaction, AnimationClip>
        {
            [DinoReaction.Blink] = new AnimationClip("blink", [F(sheet[Blink], 0.12f)], loop: false),

            [DinoReaction.Glance] = new AnimationClip(
                "glance",
                [F(sheet[LookLeft], 0.55f), F(idle, 0.25f), F(sheet[LookRight], 0.55f), F(idle, 0.2f)],
                loop: false),

            [DinoReaction.TailWag] = new AnimationClip(
                "tailwag",
                [F(tail, 0.14f), F(idle, 0.14f), F(tail, 0.14f), F(idle, 0.14f), F(tail, 0.14f), F(idle, 0.14f)],
                loop: false),

            [DinoReaction.Yawn] = new AnimationClip("yawn", [F(sheet[Yawn], 1.1f), F(sheet[Blink], 0.25f)], loop: false),

            [DinoReaction.Scratch] = new AnimationClip(
                "scratch",
                [
                    F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f), F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f),
                    F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f), F(idle, 0.1f),
                ],
                loop: false),

            [DinoReaction.Stretch] = new AnimationClip("stretch", [F(sheet[Stretch], 0.9f), F(idle, 0.1f)], loop: false),

            [DinoReaction.Curious] = new AnimationClip(
                "curious",
                [
                    new AnimationFrame(sheet[LookRight], 0.9f, question, HeadX + 4, HeadY),
                    new AnimationFrame(sheet[LookLeft], 0.5f, question, HeadX + 4, HeadY),
                    F(idle, 0.1f),
                ],
                loop: false),

            [DinoReaction.Happy] = new AnimationClip(
                "happy",
                [
                    new AnimationFrame(happy, 0.14f, heart, HeadX, HeadY),
                    new AnimationFrame(tail, 0.14f, heart, HeadX, HeadY - 2),
                    new AnimationFrame(happy, 0.14f, heart, HeadX, HeadY - 4),
                    new AnimationFrame(tail, 0.14f, heart, HeadX, HeadY - 6),
                    new AnimationFrame(happy, 0.14f, heart, HeadX, HeadY - 8),
                    new AnimationFrame(happy, 0.2f, heart, HeadX, HeadY - 10),
                    F(idle, 0.05f),
                ],
                loop: false),

            [DinoReaction.Surprised] = new AnimationClip(
                "surprised",
                [new AnimationFrame(sheet[LookRight], 0.6f, exclaim, HeadX + 6, HeadY), F(idle, 0.05f)],
                loop: false),

            // A sprout appears in front of the dino; it bends down and eats it in three bites.
            [DinoReaction.Eat] = new AnimationClip(
                "eat",
                [
                    new AnimationFrame(sheet[LookRight], 0.4f, sprout1, 46, GroundFxY),
                    new AnimationFrame(sheet[Eat], 0.45f, sprout1, 46, GroundFxY),
                    new AnimationFrame(idle, 0.3f, sprout2, 46, GroundFxY),
                    new AnimationFrame(sheet[Eat], 0.45f, sprout2, 46, GroundFxY),
                    new AnimationFrame(idle, 0.3f, sprout3, 46, GroundFxY),
                    new AnimationFrame(sheet[Eat], 0.45f, sprout3, 46, GroundFxY),
                    new AnimationFrame(happy, 0.5f),
                ],
                loop: false),

            // A ball bounces in front of the dino, which hops along (the runtime adds the hop).
            [DinoReaction.Play] = new AnimationClip(
                "play",
                [
                    new AnimationFrame(sheet[LookRight], 0.2f, ball1, 50, GroundFxY),
                    new AnimationFrame(sheet[LookRight], 0.12f, ball2, 50, GroundFxY - 10),
                    new AnimationFrame(happy, 0.12f, ball1, 50, GroundFxY - 18),
                    new AnimationFrame(happy, 0.12f, ball2, 50, GroundFxY - 22),
                    new AnimationFrame(tail, 0.12f, ball1, 50, GroundFxY - 18),
                    new AnimationFrame(tail, 0.12f, ball2, 50, GroundFxY - 10),
                    new AnimationFrame(sheet[LookRight], 0.14f, ball1, 50, GroundFxY),
                    new AnimationFrame(sheet[LookRight], 0.12f, ball2, 50, GroundFxY - 8),
                    new AnimationFrame(happy, 0.12f, ball1, 50, GroundFxY - 12),
                    new AnimationFrame(happy, 0.12f, ball2, 50, GroundFxY - 8),
                    new AnimationFrame(sheet[LookRight], 0.3f, ball1, 50, GroundFxY),
                    F(idle, 0.05f),
                ],
                loop: false),

            // Confetti over a wagging, hopping dino (new high score, or just a good mood).
            [DinoReaction.Celebrate] = new AnimationClip(
                "celebrate",
                [
                    new AnimationFrame(happy, 0.15f, confetti1, 12, -16),
                    new AnimationFrame(tail, 0.15f, confetti2, 12, -12),
                    new AnimationFrame(happy, 0.15f, confetti1, 12, -8),
                    new AnimationFrame(tail, 0.15f, confetti2, 12, -4),
                    new AnimationFrame(happy, 0.15f, confetti1, 12, 0),
                    new AnimationFrame(tail, 0.15f, confetti2, 12, 4),
                    new AnimationFrame(happy, 0.15f, confetti1, 12, 8),
                    new AnimationFrame(happy, 0.2f, confetti2, 12, 12),
                    F(idle, 0.05f),
                ],
                loop: false),
        };
    }

    private static AnimationFrame F(AtlasRegion region, float seconds) => new(region, seconds);
}
