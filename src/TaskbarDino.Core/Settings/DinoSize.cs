namespace TaskbarDino.Core.Settings;

/// <summary>
/// How big the dino is drawn. The value maps to a sprite magnification that is multiplied by the
/// monitor's DPI scale, so a size feels consistent across displays. The same factor scales the
/// game playfield, so a bigger dino means bigger obstacles too.
/// </summary>
public enum DinoSize
{
    Tiny,
    Small,
    Medium,
    Large,
}

public static class DinoSizes
{
    /// <summary>Base sprite magnification at 100% DPI (whole numbers stay crisp; Small is an in-between).</summary>
    public static float ScaleFactor(this DinoSize size) => size switch
    {
        DinoSize.Tiny => 1.0f,
        DinoSize.Small => 1.5f,
        DinoSize.Medium => 2.0f,
        DinoSize.Large => 3.0f,
        _ => 1.5f,
    };
}
