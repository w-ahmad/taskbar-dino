namespace TaskbarDino.Core.Game;

/// <summary>
/// Tunable constants for the taskbar runner, in <em>logical units</em> (1 unit = 1 sprite pixel) at a
/// fixed 60 Hz step. Adapted from the Chromium <c>t-rex-runner</c> feel (speed ramp, gravity, drop,
/// gap coefficient, cloud parallax) and re-scaled to this game's shape: the dino keeps its taskbar
/// spot and obstacles scroll in from the right screen edge. The renderer multiplies everything by a
/// device scale (dino size × DPI), so a bigger dino means bigger, faster-feeling obstacles.
/// </summary>
public static class GameConfig
{
    public const float Fps = 60f;
    public const float FrameSeconds = 1f / Fps;

    /// <summary>Logical height of the transparent play strip above the taskbar.</summary>
    public const float Height = 90f;

    /// <summary>Feet baseline — the dino and ground obstacles rest on the taskbar (strip bottom).</summary>
    public const float GroundY = 90f;

    // --- Dino (the companion sprite, a 24×24 frame) ---
    public const float DinoWidth = 24f;
    public const float DinoHeight = 24f;
    public const float DuckHeight = 14f;
    public const float Gravity = 0.72f;              // downward accel, units/frame²
    public const float InitialJumpVelocity = 6.4f;   // a tap peaks ≈ 28 units
    public const float HoldGravityFactor = 0.42f;    // weaker gravity while the key is held and rising…
    public const float MaxHoldHeight = 46f;          // …up to this hard ceiling
    public const float DropVelocity = -7f;           // pressing Down mid-air slams the dino down

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
    public const int MaxCactusGroup = 2;             // a held jump clears two
    public const int BirdMinScore = 5;               // birds start appearing after a few dodges

    /// <summary>
    /// Pterodactyl altitudes (bottom edge). Low: jump over. Mid: duck under (or a high jump). High:
    /// run under — a jump would hit it.
    /// </summary>
    public static readonly float[] BirdBottoms = [GroundY - 3f, GroundY - 17f, GroundY - 30f];

    // --- Decor ---
    public const float CloudSpeedFactor = 0.2f;      // parallax: clouds drift at a fifth of the ground speed
    public const int MaxClouds = 5;
    public const float CloudMinGap = 90f;
    public const float CloudMaxGap = 260f;
    public const float CloudMinY = 4f;
    public const float CloudMaxY = 36f;
    public const float GroundTileWidth = 48f;

    /// <summary>Restart presses are ignored this long after a crash so the game-over screen is seen.</summary>
    public const float RestartLockoutSeconds = 0.45f;

    // --- Animation cadence (frames per sprite swap) ---
    public const int RunFrameInterval = 6;
    public const int WingFrameInterval = 10;
}
