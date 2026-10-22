using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Animation;

/// <summary>One frame of an animation: which atlas region to show and for how long.</summary>
public readonly record struct AnimationFrame(AtlasRegion Region, float DurationSeconds);

/// <summary>
/// A named sequence of frames. A single-frame clip is a static pose (used for resting postures);
/// multi-frame clips animate. Looping clips repeat; non-looping clips are one-shots (reactions).
/// </summary>
public sealed class AnimationClip
{
    public AnimationClip(string name, IReadOnlyList<AnimationFrame> frames, bool loop)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (frames.Count == 0)
        {
            throw new ArgumentException("An animation clip needs at least one frame.", nameof(frames));
        }

        Name = name;
        Frames = frames;
        Loop = loop;
    }

    public string Name { get; }

    public IReadOnlyList<AnimationFrame> Frames { get; }

    public bool Loop { get; }

    public bool IsAnimated => Frames.Count > 1;

    /// <summary>Convenience factory for a static, single-frame pose.</summary>
    public static AnimationClip Static(string name, AtlasRegion region) =>
        new(name, [new AnimationFrame(region, float.PositiveInfinity)], loop: true);
}
