using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Graphics;

/// <summary>A named sub-rectangle of a texture atlas, in atlas pixels.</summary>
public readonly record struct AtlasRegion(PixelRect Source)
{
    public int Width => Source.Width;
    public int Height => Source.Height;
    public bool IsEmpty => Source.IsEmpty;
}

/// <summary>
/// A rasterised atlas: one pixel buffer plus named regions. Both built-in sheets are generated at
/// runtime from character grids, so there is no image decoding and no third-party art.
/// </summary>
public sealed class SpriteSheet(int width, int height, Bgra32[] pixels, IReadOnlyDictionary<string, AtlasRegion> regions)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public Bgra32[] Pixels { get; } = pixels;

    public IReadOnlyDictionary<string, AtlasRegion> Regions { get; } = regions;

    public AtlasRegion this[string name] =>
        Regions.TryGetValue(name, out AtlasRegion region)
            ? region
            : throw new KeyNotFoundException($"Sprite sheet has no region named '{name}'.");

    /// <summary>Tight bounds of the opaque pixels inside <paramref name="region"/>, relative to the region.</summary>
    public PixelRect OpaqueBounds(AtlasRegion region)
    {
        PixelRect s = region.Source;
        int minX = s.Width, minY = s.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < s.Height; y++)
        {
            int row = ((s.Y + y) * Width) + s.X;
            for (int x = 0; x < s.Width; x++)
            {
                if (Pixels[row + x].IsOpaque)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return maxX < 0 ? default : PixelRect.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}
