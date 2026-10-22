namespace TaskbarDino.Core.Platform;

/// <summary>
/// Keyboard input for Game Mode, delivered by a global low-level keyboard hook so the game never has
/// to steal focus (the overlay stays a non-activating, click-through window). It is only active while
/// a game is running; the handled keys (Space/↑/Esc) are swallowed so they don't leak to whatever app
/// is underneath. Jump is reported as a held state (press then release) so holding produces a higher,
/// longer hop.
/// </summary>
public interface IGameInput : IDisposable
{
    /// <summary>Installs the global keyboard hook. Call when entering Game Mode.</summary>
    void Enable();

    /// <summary>Removes the hook. Call when leaving Game Mode (keys pass through normally again).</summary>
    void Disable();

    /// <summary>Space or ↑ pressed (<see langword="true"/>) or released (<see langword="false"/>) — jump / hold to jump higher.</summary>
    event EventHandler<bool>? JumpChanged;

    /// <summary>Esc pressed — leave Game Mode and return to the companion.</summary>
    event EventHandler? ExitPressed;
}
