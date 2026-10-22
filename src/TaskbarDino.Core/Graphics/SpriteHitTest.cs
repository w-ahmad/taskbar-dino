using System.Numerics;

using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Graphics;

/// <summary>
/// Per-pixel sprite hit-testing: maps a point inside a sprite's destination rectangle back to the
/// source texel and reports whether that texel is opaque. This is what makes clicks land on the dino
/// but fall through the (mostly transparent) overlay everywhere else. Pure and allocation-free.
/// </summary>
public static class SpriteHitTest
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="point"/> is over a non-transparent pixel of the
    /// sprite drawn from <paramref name="region"/> of <paramref name="atlasPixels"/> into <paramref name="destination"/>.
    /// </summary>
    public static bool IsOpaque(
        ReadOnlySpan<Bgra32> atlasPixels,
        int atlasWidth,
        in AtlasRegion region,
        in RectF destination,
        bool flipX,
        Vector2 point)
    {
        if (destination.Width <= 0f || destination.Height <= 0f || !destination.Contains(point))
        {
            return false;
        }

        float u = (point.X - destination.X) / destination.Width;
        float v = (point.Y - destination.Y) / destination.Height;
        if (flipX)
        {
            u = 1f - u;
        }

        PixelRect src = region.Source;
        int sx = src.X + Math.Clamp((int)(u * src.Width), 0, src.Width - 1);
        int sy = src.Y + Math.Clamp((int)(v * src.Height), 0, src.Height - 1);

        int index = (sy * atlasWidth) + sx;
        return (uint)index < (uint)atlasPixels.Length && atlasPixels[index].A > 0;
    }
}
