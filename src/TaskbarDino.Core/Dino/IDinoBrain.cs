using TaskbarDino.Core.Time;

namespace TaskbarDino.Core.Dino;

/// <summary>
/// A short, one-shot expressive animation the dino plays over its current posture — the little
/// touches that make it feel alive. Distinct from <see cref="DinoState"/> (a sustained posture)
/// because reactions come and go without changing what the dino is fundamentally "doing".
/// </summary>
public enum DinoReaction
{
    Blink,
    Glance,
    Yawn,
}

/// <summary>
/// The dino's "mind": a small behaviour model that decides posture (<see cref="DinoState"/>) and
/// emits spontaneous <see cref="DinoReaction"/>s with randomised timing so it never feels scripted.
/// It is pure logic — no rendering, no Windows — and drives, but is decoupled from, the animation layer.
/// </summary>
public interface IDinoBrain
{
    /// <summary>The dino's current sustained posture.</summary>
    DinoState State { get; }

    /// <summary>Which way the dino faces (rendering mirrors for <see cref="Facing.Left"/>).</summary>
    Facing Facing { get; }

    /// <summary>How long until the next visible change is scheduled — lets the loop rest exactly that long.</summary>
    TimeSpan TimeUntilNextEvent { get; }

    /// <summary>Advances the behaviour model by one frame.</summary>
    void Update(in FrameTime time);

    /// <summary>Retrieves a pending one-shot reaction to play, if the brain raised one this tick.</summary>
    bool TryDequeueReaction(out DinoReaction reaction);
}
