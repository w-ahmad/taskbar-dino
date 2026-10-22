namespace TaskbarDino.Core.Settings;

/// <summary>
/// Built-in dino colour themes. The sprite art is authored in greyscale and tinted at render time,
/// so adding a theme is just adding an entry here plus a tint colour — the foundation for the
/// future downloadable-skins system.
/// </summary>
public enum DinoTheme
{
    /// <summary>Follows the current Windows accent colour (resolved at runtime). The default.</summary>
    Accent,
    ChromeGray,
    Green,
    Blue,
    Red,
    Purple,
    Gold,
}
