namespace TaskbarDino.Core.Primitives;

/// <summary>
/// A floating-point rectangle used for render destinations and game geometry. Kept distinct from
/// <see cref="PixelRect"/> so the type system separates "device pixels from Win32" from
/// "sub-pixel positions being animated".
/// </summary>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + (Width / 2f);
    public float CenterY => Y + (Height / 2f);

    /// <summary>Shrinks (positive) or grows (negative) the rectangle on all sides.</summary>
    public RectF Deflate(float dx, float dy) => new(X + dx, Y + dy, Width - (2f * dx), Height - (2f * dy));

    /// <summary>True when this rectangle overlaps <paramref name="other"/> (edges touching does not count).</summary>
    public bool Intersects(RectF other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public override string ToString() => $"[{X:0.#},{Y:0.#} {Width:0.#}x{Height:0.#}]";
}
