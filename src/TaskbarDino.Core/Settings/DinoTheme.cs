namespace TaskbarDino.Core.Settings;

/// <summary>
/// Built-in dino colours. The art is authored once as a three-tone body and re-rasterised with a
/// palette, so a theme is just a palette — no duplicated sprite sheets.
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
