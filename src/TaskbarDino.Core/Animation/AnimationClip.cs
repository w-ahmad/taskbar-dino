using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Core.Animation;

/// <summary>
/// One frame of an animation: the atlas region to show, for how long, and an optional effect sprite
/// (heart, zzz, "?") drawn at an offset from the dino's top-left corner in sprite pixels.
/// </summary>
public readonly record struct AnimationFrame(AtlasRegion Region, float DurationSeconds, AtlasRegion Effect = default, int EffectX = 0, int EffectY = 0)
{
    public bool HasEffect => !Effect.IsEmpty;
}

/// <summary>
/// A named sequence of frames. A single-frame clip is a static pose; multi-frame clips animate.
/// Looping clips repeat; non-looping clips are one-shots (reactions).
/// </summary>
public sealed class AnimationClip
{
    public AnimationClip(string name, AnimationFrame[] frames, bool loop)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (frames.Length == 0)
        {
            throw new ArgumentException("An animation clip needs at least one frame.", nameof(frames));
        }

        Name = name;
        Frames = frames;
        Loop = loop;
    }

    public string Name { get; }

    public AnimationFrame[] Frames { get; }

    public bool Loop { get; }

    public bool IsAnimated => Frames.Length > 1;

    /// <summary>Convenience factory for a static, single-frame pose.</summary>
    public static AnimationClip Static(string name, AtlasRegion region) =>
        new(name, [new AnimationFrame(region, float.PositiveInfinity)], loop: true);
}
