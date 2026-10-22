using System.Numerics;

using TaskbarDino.Core.Graphics;
using TaskbarDino.Core.Primitives;

namespace TaskbarDino.Tests.Graphics;

public class SpriteHitTestTests
{
    // A 2x2 atlas with opaque pixels on the main diagonal (top-left, bottom-right).
    private static readonly Bgra32[] Atlas =
    [
        new(0, 0, 0, 255), Bgra32.Transparent,
        Bgra32.Transparent, new(0, 0, 0, 255),
    ];

    private static readonly AtlasRegion Region = new(new PixelRect(0, 0, 2, 2));
    private static readonly RectF Dest = new(0, 0, 20, 20); // 10px per source texel

    [Fact]
    public void Hits_OpaqueTexels()
    {
        Assert.True(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(5, 5)));   // top-left
        Assert.True(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(15, 15))); // bottom-right
    }

    [Fact]
    public void Misses_TransparentTexels()
    {
        Assert.False(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(15, 5))); // top-right
        Assert.False(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(5, 15))); // bottom-left
    }

    [Fact]
    public void Misses_OutsideDestination()
    {
        Assert.False(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(-1, -1)));
        Assert.False(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: false, new Vector2(100, 100)));
    }

    [Fact]
    public void FlipX_MirrorsHorizontally()
    {
        // With flip, the top-left screen point samples the top-right texel (transparent).
        Assert.False(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: true, new Vector2(5, 5)));
        // ...and the top-right screen point samples the top-left texel (opaque).
        Assert.True(SpriteHitTest.IsOpaque(Atlas, 2, Region, Dest, flipX: true, new Vector2(15, 5)));
    }
}
