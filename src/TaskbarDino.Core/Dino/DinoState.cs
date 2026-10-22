namespace TaskbarDino.Core.Dino;

/// <summary>
/// The dino's sustained posture, mapped to an animation clip by the animation layer. Movement
/// states come from the movement controller; <see cref="Sleeping"/> from the brain;
/// <see cref="Held"/> while the user drags it.
/// </summary>
public enum DinoState
{
    Idle,
    Walking,
    Running,
    Jumping,
    Sleeping,
    Held,
}

/// <summary>
/// A short, one-shot expressive animation played over the current posture — the touches that make
/// the dino feel alive. Reactions come and go without changing what it is fundamentally doing.
/// </summary>
public enum DinoReaction
{
    Blink,
    Glance,
    TailWag,
    Yawn,
    Scratch,
    Stretch,
    Curious,
    Happy,
    Surprised,
}

/// <summary>Which way the sprite faces. Rendering mirrors for <see cref="Left"/>.</summary>
public enum Facing
{
    Right,
    Left,
}

/// <summary>How lively the idle personality is (scales the time between spontaneous reactions).</summary>
public enum ActivityLevel
{
    Calm,
    Normal,
    Lively,
}
