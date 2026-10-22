namespace TaskbarDino.Core.Settings;

/// <summary>
/// How big the dino is drawn. Chosen by the user from the tray menu and persisted. The value maps to
/// a sprite scale factor (see <see cref="DinoSizes.ScaleFactor"/>) that is multiplied by the monitor's
/// DPI scale, so a size feels consistent across displays. The same factor scales the game playfield,
/// so a bigger dino means bigger obstacles too.
/// </summary>
public enum DinoSize
{
    Tiny,
    Small,
    Medium,
    Large,
}

/// <summary>Maps a <see cref="DinoSize"/> to the sprite scale factor used for rendering and physics.</summary>
public static class DinoSizes
{
    /// <summary>
    /// Base sprite magnification at 100% DPI. <see cref="DinoSize.Medium"/> (2×) is the default and
    /// matches the size the app shipped with. Tiny/Medium/Large are whole-number magnifications (crisp
    /// nearest-neighbour pixels); Small (1.5×) trades a little crispness for an in-between size.
    /// </summary>
    public static float ScaleFactor(this DinoSize size) => size switch
    {
        DinoSize.Tiny => 1.0f,
        DinoSize.Small => 1.5f,
        DinoSize.Medium => 2.0f,
        DinoSize.Large => 3.0f,
        _ => 2.0f,
    };
}
