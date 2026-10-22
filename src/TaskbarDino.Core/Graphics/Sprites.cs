using System.Numerics;

using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Core.Graphics;

/// <summary>An opaque handle to a GPU texture owned by an <see cref="IRenderer"/>.</summary>
public readonly record struct TextureHandle(int Id)
{
    public bool IsValid => Id > 0;

    public static TextureHandle None => default;
}

/// <summary>
/// A named sub-rectangle of a texture atlas, plus a pivot describing the sprite's anchor.
/// The pivot is expressed in [0, 1] across the source rectangle; the default (0.5, 1.0) is
/// bottom-centre — the character's feet — which is what we position at the Home Position.
/// </summary>
public readonly record struct AtlasRegion(PixelRect Source, Vector2 Pivot)
{
    public AtlasRegion(PixelRect source)
        : this(source, new Vector2(0.5f, 1f))
    {
    }

    public int Width => Source.Width;
    public int Height => Source.Height;
}

/// <summary>
/// A texture atlas: one GPU texture plus a lookup of named regions. Skins provide their own atlas,
/// which is why the animation layer references regions by name rather than by pixel coordinates.
/// </summary>
public sealed class SpriteAtlas(TextureHandle texture, int pixelWidth, int pixelHeight, IReadOnlyDictionary<string, AtlasRegion> regions)
{
    public TextureHandle Texture { get; } = texture;

    public int PixelWidth { get; } = pixelWidth;

    public int PixelHeight { get; } = pixelHeight;

    private readonly IReadOnlyDictionary<string, AtlasRegion> _regions = regions;

    public IReadOnlyCollection<string> RegionNames => (IReadOnlyCollection<string>)_regions.Keys;

    public AtlasRegion this[string name] =>
        _regions.TryGetValue(name, out AtlasRegion region)
            ? region
            : throw new KeyNotFoundException($"Sprite atlas has no region named '{name}'.");

    public bool TryGet(string name, out AtlasRegion region) => _regions.TryGetValue(name, out region);
}
