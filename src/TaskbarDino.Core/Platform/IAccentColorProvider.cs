using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Core.Platform;

/// <summary>
/// Supplies the current Windows accent colour, used by the "Accent" dino theme so the dino matches
/// the system look. Implemented in the Windows layer (reads it from the OS); returns a sensible
/// fallback if it can't be determined.
/// </summary>
public interface IAccentColorProvider
{
    Rgba GetAccentColor();
}
