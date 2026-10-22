namespace TaskbarDino.Core.Primitives;

/// <summary>An integer point in physical screen pixels.</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public static PixelPoint Zero => default;

    public static PixelPoint operator +(PixelPoint a, PixelPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static PixelPoint operator -(PixelPoint a, PixelPoint b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// An axis-aligned rectangle in physical screen pixels (origin top-left, Y grows downward),
/// matching the coordinate system of the Win32 APIs it is built from.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PixelPoint Location => new(X, Y);
    public PixelPoint Center => new(X + (Width / 2), Y + (Height / 2));
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Builds a rectangle from left/top/right/bottom edges (the shape of a Win32 <c>RECT</c>).</summary>
    public static PixelRect FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public bool Contains(PixelPoint p) => Contains(p.X, p.Y);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>Grows (or shrinks, for negative values) the rectangle by the given amount on each axis.</summary>
    public PixelRect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + (2 * dx), Height + (2 * dy));

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
