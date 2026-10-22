using TaskbarDino.Animation;
using TaskbarDino.Core.Dino;
using TaskbarDino.Core.Graphics;

namespace TaskbarDino.App.Content;

/// <summary>
/// Builds the dino's animation clips from a sprite atlas: static postures for sustained states,
/// looping cycles for locomotion, and short one-shots for reactions. Kept as data so new behaviours
/// are just new clips.
/// </summary>
public static class DinoAnimations
{
    public static IReadOnlyDictionary<DinoState, AnimationClip> BuildStateClips(SpriteAtlas atlas) =>
        new Dictionary<DinoState, AnimationClip>
        {
            [DinoState.Idle] = AnimationClip.Static("idle", atlas["idle"]),
            [DinoState.Sleeping] = AnimationClip.Static("sleep", atlas["blink"]),
            [DinoState.Walking] = new AnimationClip(
                "walk",
                [new AnimationFrame(atlas["walk1"], 0.16f), new AnimationFrame(atlas["walk2"], 0.16f)],
                loop: true),
            [DinoState.Running] = new AnimationClip(
                "run",
                [new AnimationFrame(atlas["walk1"], 0.09f), new AnimationFrame(atlas["walk2"], 0.09f)],
                loop: true),
            [DinoState.Jumping] = AnimationClip.Static("jump", atlas["jump"]),
        };

    public static IReadOnlyDictionary<DinoReaction, AnimationClip> BuildReactionClips(SpriteAtlas atlas) =>
        new Dictionary<DinoReaction, AnimationClip>
        {
            [DinoReaction.Blink] = new AnimationClip(
                "blink",
                [new AnimationFrame(atlas["blink"], 0.10f)],
                loop: false),

            [DinoReaction.Glance] = new AnimationClip(
                "glance",
                [
                    new AnimationFrame(atlas["look_left"], 0.55f),
                    new AnimationFrame(atlas["idle"], 0.25f),
                    new AnimationFrame(atlas["look_right"], 0.55f),
                    new AnimationFrame(atlas["idle"], 0.20f),
                ],
                loop: false),
        };
}
