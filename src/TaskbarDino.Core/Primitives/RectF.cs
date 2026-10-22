using System.Numerics;

namespace TaskbarDino.Core.Primitives;

/// <summary>
/// A floating-point rectangle used for render destinations and world-space geometry.
/// Kept distinct from <see cref="PixelRect"/> so the type system separates "device pixels I got
/// from Win32" from "sub-pixel positions I am animating".
/// </summary>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public Vector2 Location => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public Vector2 Center => new(X + (Width / 2f), Y + (Height / 2f));

    /// <summary>Creates a rectangle of the given size centred on <paramref name="center"/>.</summary>
    public static RectF FromCenter(Vector2 center, float width, float height) =>
        new(center.X - (width / 2f), center.Y - (height / 2f), width, height);

    /// <summary>Creates a rectangle anchored at a bottom-centre point (feet), growing upward.</summary>
    public static RectF FromBottomCenter(Vector2 bottomCenter, float width, float height) =>
        new(bottomCenter.X - (width / 2f), bottomCenter.Y - height, width, height);

    public RectF Offset(Vector2 delta) => new(X + delta.X, Y + delta.Y, Width, Height);

    /// <summary>Shrinks (positive) or grows (negative) the rectangle on all sides.</summary>
    public RectF Deflate(float dx, float dy) => new(X + dx, Y + dy, Width - (2f * dx), Height - (2f * dy));

    public bool Contains(Vector2 p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    /// <summary>True when this rectangle overlaps <paramref name="other"/> (edges touching does not count).</summary>
    public bool Intersects(RectF other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public override string ToString() => $"[{X:0.#},{Y:0.#} {Width:0.#}x{Height:0.#}]";
}
