using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Game.Runner;

/// <summary>High-level state of a game session.</summary>
public enum GameState
{
    /// <summary>Actively running (the game starts here — no "press to start").</summary>
    Running,

    /// <summary>The dino just hit an obstacle; pausing briefly before the run auto-restarts.</summary>
    Crashed,
}

/// <summary>What the dino is doing this frame — maps to a sprite in the renderer.</summary>
public enum TrexPose
{
    Running,
    Jumping,
    Dead,
}

/// <summary>The kind of obstacle, which selects its sprite and collision size.</summary>
public enum ObstacleType
{
    CactusSmall,
    CactusLarge,
    Pterodactyl,
}

/// <summary>A single obstacle scrolling right-to-left. Cacti can cluster (<see cref="Count"/>).</summary>
public sealed class Obstacle
{
    public ObstacleType Type { get; init; }

    /// <summary>Left edge, logical units.</summary>
    public float X { get; set; }

    /// <summary>Top edge, logical units.</summary>
    public float Y { get; init; }

    /// <summary>Width of one cell (a whole cactus group is <see cref="Width"/> × <see cref="Count"/>).</summary>
    public float Width { get; init; }

    public float Height { get; init; }

    /// <summary>How many cacti are clustered side-by-side (1 for a pterodactyl).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Set once the obstacle has been counted as dodged (passed the dino), so it scores only once.</summary>
    public bool Counted { get; set; }

    /// <summary>Total on-screen width including any clustered cacti.</summary>
    public float TotalWidth => Width * Count;

    /// <summary>Axis-aligned bounds of the whole obstacle, logical units.</summary>
    public RectF Bounds => new(X, Y, TotalWidth, Height);
}
