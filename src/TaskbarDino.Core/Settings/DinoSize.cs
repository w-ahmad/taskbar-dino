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
    /// <summary>Base sprite magnification at 100% DPI. The art is 48 px tall, so Medium is 1:1 (48 px) and Large 72 px.</summary>
    public static float ScaleFactor(this DinoSize size) => size switch
    {
        DinoSize.Tiny => 0.5f,
        DinoSize.Small => 0.75f,
        DinoSize.Medium => 1.0f,
        DinoSize.Large => 1.5f,
        _ => 1.0f,
    };
}
