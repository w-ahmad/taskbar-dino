using TaskbarDino.Core.Animation;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;

using static TaskbarDino.Core.Content.DinoSpriteSheet;

namespace TaskbarDino.Core.Content;

/// <summary>
/// Builds the dino's animation clips from the sprite sheet: static postures for sustained states,
/// looping cycles for locomotion and sleep, and short one-shots for reactions. Pure data, so a new
/// behaviour is just a new clip.
/// </summary>
public static class DinoAnimations
{
    // Effect anchor: just above and to the right of the head (sprite pixels, relative to the frame's top-left).
    private const int FxX = 17;
    private const int FxY = -9;

    public static IReadOnlyDictionary<DinoState, AnimationClip> BuildStateClips(SpriteSheet sheet)
    {
        AtlasRegion zzz1 = sheet[FxZzz1];
        AtlasRegion zzz2 = sheet[FxZzz2];
        return new Dictionary<DinoState, AnimationClip>
        {
            [DinoState.Idle] = AnimationClip.Static("idle", sheet[Idle]),
            [DinoState.Held] = AnimationClip.Static("held", sheet[Jump]),
            [DinoState.Jumping] = AnimationClip.Static("jump", sheet[Jump]),
            [DinoState.Walking] = new AnimationClip("walk", [F(sheet[Walk1], 0.16f), F(sheet[Walk2], 0.16f)], loop: true),
            [DinoState.Running] = new AnimationClip("run", [F(sheet[Walk1], 0.09f), F(sheet[Walk2], 0.09f)], loop: true),
            [DinoState.Sleeping] = new AnimationClip(
                "sleep",
                [
                    new AnimationFrame(sheet[Sleep], 0.8f, zzz1, FxX, FxY + 3),
                    new AnimationFrame(sheet[Sleep], 0.8f, zzz2, FxX, FxY + 3),
                ],
                loop: true),
        };
    }

    public static IReadOnlyDictionary<DinoReaction, AnimationClip> BuildReactionClips(SpriteSheet sheet)
    {
        AtlasRegion idle = sheet[Idle];
        AtlasRegion tail = sheet[TailUp];
        AtlasRegion heart = sheet[FxHeart];
        AtlasRegion question = sheet[FxQuestion];
        AtlasRegion exclaim = sheet[FxExclaim];

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

            [DinoReaction.Yawn] = new AnimationClip(
                "yawn",
                [F(sheet[Yawn], 1.1f), F(sheet[Blink], 0.25f)],
                loop: false),

            [DinoReaction.Scratch] = new AnimationClip(
                "scratch",
                [
                    F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f), F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f),
                    F(sheet[Scratch1], 0.12f), F(sheet[Scratch2], 0.12f), F(idle, 0.1f),
                ],
                loop: false),

            [DinoReaction.Stretch] = new AnimationClip(
                "stretch",
                [F(sheet[Stretch], 0.9f), F(idle, 0.1f)],
                loop: false),

            [DinoReaction.Curious] = new AnimationClip(
                "curious",
                [
                    new AnimationFrame(sheet[LookRight], 0.9f, question, FxX + 2, FxY),
                    new AnimationFrame(sheet[LookLeft], 0.5f, question, FxX + 2, FxY),
                    F(idle, 0.1f),
                ],
                loop: false),

            [DinoReaction.Happy] = new AnimationClip(
                "happy",
                [
                    new AnimationFrame(sheet[Happy], 0.14f, heart, FxX, FxY),
                    new AnimationFrame(tail, 0.14f, heart, FxX, FxY - 1),
                    new AnimationFrame(sheet[Happy], 0.14f, heart, FxX, FxY - 2),
                    new AnimationFrame(tail, 0.14f, heart, FxX, FxY - 3),
                    new AnimationFrame(sheet[Happy], 0.14f, heart, FxX, FxY - 4),
                    new AnimationFrame(sheet[Happy], 0.2f, heart, FxX, FxY - 5),
                    F(idle, 0.05f),
                ],
                loop: false),

            [DinoReaction.Surprised] = new AnimationClip(
                "surprised",
                [new AnimationFrame(sheet[LookRight], 0.6f, exclaim, FxX + 3, FxY), F(idle, 0.05f)],
                loop: false),
        };
    }

    private static AnimationFrame F(AtlasRegion region, float seconds) => new(region, seconds);
}
