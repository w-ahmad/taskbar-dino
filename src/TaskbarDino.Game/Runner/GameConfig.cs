namespace TaskbarDino.Game.Runner;

/// <summary>
/// Tunable constants for the taskbar runner, in <em>logical units</em> (1 unit = 1 sprite-atlas pixel)
/// at a fixed 60 Hz step. Adapted from Chromium's <c>t-rex-runner</c> (gravity/speed/acceleration/gap)
/// and re-scaled to this game's shape: the dino stays put on its taskbar icon and jumps in place while
/// obstacles scroll in from the right screen edge. The renderer multiplies everything by a device
/// scale (dino size × DPI), so a bigger dino means bigger, faster-feeling obstacles.
/// </summary>
public static class GameConfig
{
    public const float Fps = 60f;
    public const float FrameSeconds = 1f / Fps;

    /// <summary>Logical height of the transparent play strip above the taskbar (jump arc + score cloud).</summary>
    public const float Height = 90f;

    /// <summary>Feet baseline — the dino and ground obstacles rest on the taskbar (overlay bottom).</summary>
    public const float GroundY = 90f;

    // --- Dino (uses the companion sprite, a 24×24 frame) ---
    public const float DinoWidth = 24f;
    public const float DinoHeight = 24f;
    public const float Gravity = 0.72f;              // downward accel, units/frame²
    public const float InitialJumpVelocity = 6.4f;   // a tap peaks ≈ 28 units
    public const float HoldGravityFactor = 0.42f;    // weaker gravity while Space is held and rising…
    public const float MaxHoldHeight = 46f;          // …up to this hard ceiling (a full "long jump" clears clusters)

    // --- Difficulty / scrolling ---
    public const float StartSpeed = 3.4f;            // units/frame
    public const float MaxSpeed = 8.5f;
    public const float Acceleration = 0.0011f;
    public const float GapCoefficient = 0.7f;

    // --- Obstacles ---
    public const float CactusSmallWidth = 12f;
    public const float CactusSmallHeight = 18f;
    public const float CactusLargeWidth = 16f;
    public const float CactusLargeHeight = 24f;
    public const float PterodactylWidth = 24f;
    public const float PterodactylHeight = 16f;
    public const int MaxCactusGroup = 2;             // at most 2 cacti clustered (a held jump clears two)
    public const int BirdMinScore = 5;               // birds start appearing after a few dodges

    /// <summary>Pterodactyl flight altitudes (feet/bottom Y). Both are clearable by jumping (no ducking).</summary>
    public static readonly float[] BirdBottoms = [GroundY - 4f, GroundY - 22f];

    // --- Scoring: the score is the number of obstacles successfully dodged (not distance). ---

    /// <summary>How long the crashed dino pauses before the run auto-restarts.</summary>
    public const float CrashPauseSeconds = 0.9f;

    // --- Animation cadence (frames per sprite swap) ---
    public const int RunFrameInterval = 6;
    public const int WingFrameInterval = 10;
}
