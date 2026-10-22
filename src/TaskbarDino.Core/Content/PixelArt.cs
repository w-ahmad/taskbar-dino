using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Content;

/// <summary>
/// Helpers for the runtime-generated pixel art: painting character grids into a BGRA buffer,
/// adding a one-pixel outline around opaque pixels, and shelf-packing named sprites into an atlas.
/// </summary>
internal static class PixelArt
{
    /// <summary>A character → colour mapping; unmapped characters are transparent.</summary>
    public readonly struct Ink(ReadOnlyMemory<(char Key, Bgra32 Color)> map)
    {
        private readonly ReadOnlyMemory<(char Key, Bgra32 Color)> _map = map;

        public bool TryGet(char c, out Bgra32 color)
        {
            foreach ((char key, Bgra32 value) in _map.Span)
            {
                if (key == c)
                {
                    color = value;
                    return true;
                }
            }

            color = default;
            return false;
        }
    }

    /// <summary>Paints <paramref name="rows"/> at (<paramref name="x"/>, <paramref name="y"/>), clipped to the given cell.</summary>
    public static void Paint(Bgra32[] buffer, int stride, PixelRect cell, int x, int y, string[] rows, in Ink ink)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            int py = y + r;
            if (py < 0 || py >= cell.Height)
            {
                continue;
            }

            string row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                int px = x + c;
                if (px < 0 || px >= cell.Width || !ink.TryGet(row[c], out Bgra32 color))
                {
                    continue;
                }

                buffer[((cell.Y + py) * stride) + cell.X + px] = color;
            }
        }
    }

    /// <summary>Colours every transparent pixel in the cell that touches an opaque one (4-neighbourhood).</summary>
    public static void Outline(Bgra32[] buffer, int stride, PixelRect cell, Bgra32 color)
    {
        int w = cell.Width;
        int h = cell.Height;
        Span<bool> body = w * h <= 4096 ? stackalloc bool[w * h] : new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                body[(y * w) + x] = buffer[((cell.Y + y) * stride) + cell.X + x].IsOpaque;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (body[(y * w) + x])
                {
                    continue;
                }

                bool touches =
                    (x > 0 && body[(y * w) + x - 1]) ||
                    (x < w - 1 && body[(y * w) + x + 1]) ||
                    (y > 0 && body[((y - 1) * w) + x]) ||
                    (y < h - 1 && body[((y + 1) * w) + x]);
                if (touches)
                {
                    buffer[((cell.Y + y) * stride) + cell.X + x] = color;
                }
            }
        }
    }

    /// <summary>Shelf-packs sprites into an atlas of fixed <paramref name="stride"/>; returns the regions and the height used.</summary>
    public static Dictionary<string, AtlasRegion> Pack(ReadOnlySpan<(string Name, int Width, int Height)> sprites, int stride, int gap, out int atlasHeight)
    {
        var regions = new Dictionary<string, AtlasRegion>(sprites.Length);
        int x = 0, y = 0, shelf = 0;
        foreach ((string name, int w, int h) in sprites)
        {
            if (x + w > stride)
            {
                x = 0;
                y += shelf + gap;
                shelf = 0;
            }

            regions[name] = new AtlasRegion(new PixelRect(x, y, w, h));
            x += w + gap;
            shelf = Math.Max(shelf, h);
        }

        atlasHeight = y + shelf;
        return regions;
    }
}
