namespace TaskbarDino.Core.Dino;

/// <summary>
/// The dino's high-level behavioural states, produced by the brain (finite state machine) and
/// mapped to animation clips by the animation layer. This enum is the shared vocabulary between
/// the AI, the animation system and the app — none of which know how the others are implemented.
/// </summary>
public enum DinoState
{
    /// <summary>Resting at the Home Position — the default calm state.</summary>
    Sitting,

    /// <summary>Standing, subtly alive (breathing, blinking) between idle behaviours.</summary>
    Idle,

    /// <summary>Noticed something and is paying attention.</summary>
    Curious,

    /// <summary>Tracking the mouse cursor with its gaze/head.</summary>
    WatchingMouse,

    /// <summary>Walking toward a nearby target.</summary>
    Walking,

    /// <summary>Running toward a farther target.</summary>
    Running,

    /// <summary>Airborne during a jump.</summary>
    Jumping,

    /// <summary>Touching down after a jump.</summary>
    Landing,

    /// <summary>Asleep after prolonged inactivity.</summary>
    Sleeping,

    /// <summary>Waking back up.</summary>
    Waking,

    /// <summary>Doing something playful (e.g. batting a ball).</summary>
    Playing,

    /// <summary>A brief happy reaction (chirp, tail-wag, hop).</summary>
    Celebrating,

    /// <summary>Running/jumping back to the Home Position after being displaced.</summary>
    ReturningHome,

    /// <summary>Being held and moved by the mouse.</summary>
    Dragged,

    /// <summary>Running the Chrome-Dino style game.</summary>
    GameMode,

    /// <summary>Game over — the crashed pose before returning to companion mode.</summary>
    GameOver,
}

/// <summary>Which way the sprite faces. Rendering flips horizontally for <see cref="Left"/>.</summary>
public enum Facing
{
    Right,
    Left,
}
